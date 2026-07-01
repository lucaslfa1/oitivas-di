"""Dashboard de KPIs do Sentinel (sistema de análise forense de sinistros).

Este módulo é uma aplicação Dash (front-end) montada sobre um servidor Flask
(back-end de sessão/autenticação). Ele apresenta indicadores de produtividade
de envios de sinistros, segmentados por "Escala" (turno/equipe de operadores).

Arquitetura geral:
    * Flask (``server``) cuida da sessão HTTP e da rota ``/login`` (RBAC).
    * Dash (``app``) monta a SPA: roteia entre tela de login e dashboard com base
      no estado da sessão e reage aos filtros via callbacks.
    * Os dados são carregados UMA única vez na importação do módulo (escopo global)
      a partir de uma planilha Excel + um CSV de dimensão de operador, e ficam em
      memória no DataFrame ``fato_sinistro`` durante todo o ciclo de vida do processo.

Autenticação: este dashboard NÃO valida senha localmente. Ele delega a verificação
de credenciais ao back-end Azure-only via HTTP (``BACKEND_AUTH_URL``) e apenas
aplica a regra de autorização (RBAC) sobre o papel retornado.
"""

import dash
from dash import dcc, html, Input, Output, State
import pandas as pd
import plotly.express as px
import plotly.graph_objects as go
import os
import json
from flask import Flask, session, redirect, request

# ============ CONFIGURAÇÃO FLASK ============
# O servidor Flask subjacente hospeda o app Dash e também expõe a rota /login.
server = Flask(__name__)
# secret_key aleatória por processo: assina o cookie de sessão. Como é gerada com
# os.urandom a cada boot, reiniciar o servidor invalida todas as sessões ativas
# (e, em deploy multi-instância, cada réplica teria uma chave diferente).
server.secret_key = os.urandom(24)  # Necessário para sessão

# ============ CARREGAR DADOS ============
# BLOCO DE CARREGAMENTO DE DADOS (executa na importação do módulo).
#
# Como funciona:
#   1. Resolve os caminhos da base. Primeiro tenta o caminho relativo
#      ('editados/...'), que funciona quando o processo é iniciado a partir da
#      raiz do projeto. Se não existir, cai no fallback absoluto baseado em
#      os.getcwd() — útil quando o app é iniciado de outro diretório de trabalho.
#   2. Lê as 4 abas do Excel (fato + 3 dimensões) e, opcionalmente, o CSV de
#      dimensão de operador para REESCREVER a coluna 'Escala'.
#   3. Normaliza tipos de data e deriva o tempo de atendimento.
#   4. Em caso de qualquer falha de I/O, substitui tudo por DataFrames vazios
#      para que o servidor suba mesmo sem os arquivos (degradação graciosa).
#
# Importante: este bloco roda UMA vez por processo; o dashboard inteiro opera
# sobre o DataFrame global 'fato_sinistro' resultante.
if os.path.exists('editados/Base_BI_Sinistros_Escalas_v3.xlsx'):
    EXCEL_PATH = 'editados/Base_BI_Sinistros_Escalas_v3.xlsx'
    DIM_OPERADOR_PATH = 'Dim_Operador_Inferida.csv'
else:
    # Fallback para caminho absoluto se necessário (ajustado para nova estrutura)
    EXCEL_PATH = os.path.join(os.getcwd(), 'editados', 'Base_BI_Sinistros_Escalas_v3.xlsx')
    DIM_OPERADOR_PATH = os.path.join(os.getcwd(), 'Dim_Operador_Inferida.csv')

# Carregar tabelas
try:
    # Tabela-fato (uma linha por envio/sinistro) + dimensões auxiliares.
    fato_sinistro = pd.read_excel(EXCEL_PATH, sheet_name='Fato_Sinistro')
    dim_escala = pd.read_excel(EXCEL_PATH, sheet_name='Dim_Escala')
    dim_situacao = pd.read_excel(EXCEL_PATH, sheet_name='Dim_Situacao')
    dim_motivo = pd.read_excel(EXCEL_PATH, sheet_name='Dim_Motivo')

    # Carregar Dimensão Operador
    # A escala que vem na fato é a escala registrada no envio individual; aqui ela é
    # SOBRESCRITA pela "Escala_Predominante" do operador (a escala em que ele mais
    # trabalha, inferida no CSV). Assim os KPIs/gráficos agrupam o operador por sua
    # escala de fato, e não pela escala pontual de cada envio.
    if os.path.exists(DIM_OPERADOR_PATH):
        dim_operador = pd.read_csv(DIM_OPERADOR_PATH)
        # LEFT JOIN: preserva todas as linhas da fato; operadores sem correspondência
        # no CSV ficam com Escala_Predominante = NaN.
        fato_sinistro = fato_sinistro.merge(
            dim_operador[['Operador', 'Escala_Predominante']],
            left_on='Operador_Inclusao',
            right_on='Operador',
            how='left'
        )
        # Guarda a escala original do envio antes de substituí-la (auditoria/debug).
        fato_sinistro['Escala_Original'] = fato_sinistro['Escala']
        # Operadores não mapeados caem no bucket 'Desconhecida' (tem cor própria em CORES_ESCALA).
        fato_sinistro['Escala'] = fato_sinistro['Escala_Predominante'].fillna('Desconhecida')
    else:
        print("⚠️ Dim_Operador_Inferida.csv não encontrado.")

    # Normaliza colunas de data; errors='coerce' transforma valores inválidos em NaT
    # em vez de levantar exceção, evitando que uma data malformada derrube a carga.
    fato_sinistro['dtIncl'] = pd.to_datetime(fato_sinistro['dtIncl'], errors='coerce')
    fato_sinistro['dtIni'] = pd.to_datetime(fato_sinistro['dtIni'], errors='coerce')
    fato_sinistro['dtFim'] = pd.to_datetime(fato_sinistro['dtFim'], errors='coerce')

    # Calcular tempo de atendimento em horas
    # Diferença (fim - início) em segundos, convertida para HORAS dividindo por 3600.
    fato_sinistro['Tempo_Atendimento'] = (fato_sinistro['dtFim'] - fato_sinistro['dtIni']).dt.total_seconds() / 3600

except Exception as e:
    # Degradação graciosa: se qualquer leitura falhar, o app ainda sobe, mas com base
    # vazia. Os callbacks renderizam tudo zerado em vez de quebrar na inicialização.
    print(f"Erro ao carregar dados: {e}")
    # Criar DataFrames vazios para evitar crash na inicialização se arquivos faltarem
    fato_sinistro = pd.DataFrame(columns=['Operador_Inclusao', 'Escala', 'Descricao_Situacao', 'Descricao_Motivo', 'dtIncl', 'dtIni', 'dtFim', 'Tempo_Atendimento'])
    dim_situacao = pd.DataFrame(columns=['Descricao_Situacao'])

import requests

# ============ CONFIGURAÇÃO API DE AUTENTICAÇÃO ============
# Endpoint do back-end Azure responsável por validar credenciais. Configurável por
# variável de ambiente para apontar para o serviço real em produção; o default
# localhost:5252 serve apenas para desenvolvimento local.
BACKEND_AUTH_URL = os.environ.get('BACKEND_AUTH_URL', 'http://localhost:5252/api/auth/login')
# Allowlist de RBAC: somente estes papéis podem abrir o Dashboard. Um usuário pode
# autenticar com sucesso no back-end e ainda assim ser barrado aqui (ex.: papel
# operacional sem direito de visão gerencial).
ALLOWED_DASHBOARD_ROLES = ['Admin', 'Coordenador', 'Supervisor', 'Analista']

# Rota de Login (Recebe POST do formulário HTML da tela de login)
@server.route('/login', methods=['POST'])
def login_route():
    """Autentica o usuário no back-end Azure e aplica o RBAC do Dashboard.

    Recebe um POST de formulário (campos ``username`` e ``password``) vindo do
    ``layout_login``, delega a verificação de credenciais ao serviço de
    autenticação e, em caso de sucesso, popula a sessão Flask.

    Returns:
        Em sucesso: ``redirect('/')`` (302) — sessão preenchida, leva ao dashboard.
        Em falha, uma tupla ``(mensagem, status_http)``:
            * 403 — credenciais válidas, mas o papel não está na allowlist (RBAC nega).
            * 401 — usuário/senha incorretos (back-end respondeu sem ``success``).
            * 503 — serviço de autenticação inacessível (timeout/erro de conexão).

    Como funciona:
        1. Extrai usuário e senha do ``request.form`` (envio via HTML form).
        2. Faz POST JSON ao ``BACKEND_AUTH_URL`` com timeout de 5s para não pendurar
           a requisição caso o serviço esteja lento/fora do ar.
        3. Se o back-end responder 200 E ``success=True``, lê o papel retornado.
        4. Aplica o RBAC: se o papel está em ``ALLOWED_DASHBOARD_ROLES``, grava
           ``user``/``role`` na sessão e redireciona para a raiz; senão, retorna 403.
        5. Qualquer outro caso (200 sem success, status != 200) cai no 401 genérico.
        6. Exceções de rede/JSON são capturadas e viram 503 (indisponibilidade).
    """
    username = request.form.get('username')
    password = request.form.get('password')

    try:
        # timeout=5: limite máximo de espera pelo serviço de auth; estouro vira 503 abaixo.
        response = requests.post(
            BACKEND_AUTH_URL,
            json={'username': username, 'password': password},
            timeout=5
        )

        if response.status_code == 200:
            result = response.json()
            if result.get('success'):
                user_role = result.get('role')

                # Verificação de Permissão específica para Dashboard (camada de RBAC).
                if user_role in ALLOWED_DASHBOARD_ROLES:
                    # Sessão preenchida = usuário autenticado; display_page passa a liberar o dashboard.
                    session['user'] = result.get('username')
                    session['role'] = user_role
                    return redirect('/')
                else:
                    # Autenticou, mas o papel não tem direito de ver o dashboard.
                    return f"Acesso Negado: O perfil {user_role} não possui permissão para acessar o Dashboard.", 403

        # Back-end recusou (success ausente/False) ou status diferente de 200.
        return "Usuário ou senha incorretos", 401
    except Exception as e:
        # Falha de rede/timeout/JSON inválido: o serviço de auth está indisponível.
        print(f"Erro ao conectar com serviço de autenticação: {e}")
        return "Serviço de autenticação indisponível", 503

# ============ APP DASH ============
# App Dash montado SOBRE o Flask existente (server=server), compartilhando o mesmo
# processo e sessão. suppress_callback_exceptions=True é necessário porque os
# componentes-alvo dos callbacks (ids do dashboard) nem sempre existem no layout
# inicial — eles só aparecem após o roteador renderizar a tela autenticada.
app = dash.Dash(__name__, server=server, title='Sentinel - Dashboard KPI', suppress_callback_exceptions=True)

# Cores
# Mapa fixo escala -> cor (hex). Usado em TODOS os gráficos e nas bordas dos KPIs
# para manter a mesma cor por escala em toda a tela. 'Desconhecida' (cinza) é o
# fallback de operadores não mapeados na dimensão.
CORES_ESCALA = {'Azul': '#3b82f6', 'Amarela': '#eab308', 'Verde': '#22c55e', 'Cinza': '#94a3b8', 'Desconhecida': '#64748b'}

# Layout de Login (Formulário HTML Puro)
def layout_login():
    """Constrói a tela de login (formulário HTML puro com POST para /login).

    Returns:
        html.Div: árvore de componentes Dash com o formulário de login.

    Como funciona:
        Renderiza um ``html.Form`` nativo (action='/login', method='POST') em vez de
        usar callbacks Dash. Isso é proposital: o submit dispara um POST HTTP normal
        diretamente para ``login_route`` no Flask, que cuida da sessão e do redirect.
        Os atributos ``name='username'``/``name='password'`` são essenciais — é por
        eles que o Flask recupera os valores via ``request.form``. Estilos são inline
        (tema escuro com acento laranja da marca Sentinel).
    """
    return html.Div([
        html.Div([
            html.H2('Login Sentinel', style={'color': '#f97316', 'marginBottom': '30px', 'fontWeight': '700'}),
            
            html.Form([
                dcc.Input(
                    name='username', # Importante para o Flask pegar o valor
                    type='text', 
                    placeholder='Usuário', 
                    style={
                        'width': '100%', 'padding': '12px', 'marginBottom': '15px', 'borderRadius': '8px',
                        'border': '1px solid rgba(255,255,255,0.1)', 'background': 'rgba(2, 6, 23, 0.5)', 'color': '#fff'
                    }
                ),
                
                dcc.Input(
                    name='password', # Importante para o Flask pegar o valor
                    type='password', 
                    placeholder='Senha', 
                    style={
                        'width': '100%', 'padding': '12px', 'marginBottom': '25px', 'borderRadius': '8px',
                        'border': '1px solid rgba(255,255,255,0.1)', 'background': 'rgba(2, 6, 23, 0.5)', 'color': '#fff'
                    }
                ),
                
                html.Button('Entrar', type='submit', style={
                    'width': '100%', 'padding': '12px', 'background': '#f97316', 'color': '#fff',
                    'border': 'none', 'borderRadius': '8px', 'fontWeight': 'bold', 'cursor': 'pointer',
                    'fontSize': '16px', 'transition': 'all 0.3s'
                })
            ], action='/login', method='POST')
            
        ], className='login-box')
    ], className='login-container')

def layout_dashboard():
    """Constrói o layout completo do dashboard autenticado.

    Returns:
        html.Div: árvore de componentes com header, filtros, gráficos, KPIs e ranking.

    Como funciona:
        Monta a estrutura visual (esqueleto) que será preenchida dinamicamente pelo
        callback ``update_dashboard``. Aqui ficam definidos os IDs que servem de
        ponte entre layout e callbacks:
            * Entradas (filtros): 'filtro-escala', 'busca-operador', 'ordem-ranking'.
            * Saídas (preenchidas pelo callback): 'kpi-cards', 'grafico-escala',
              'grafico-motivos', 'grafico-timeline', 'tabela-operadores'.
        As opções do slicer de escala são geradas em tempo de render a partir dos
        valores DISTINTOS já presentes em ``fato_sinistro['Escala']`` (mais a opção
        fixa 'Todas'). O nome do usuário logado vem de ``session.get('user')``.
        Observação: o filtro de Situação foi removido do produto (mantido só o de Escala).
    """
    return html.Div([
        # Header
        html.Div([
            html.Div([
                # Logo NStech
                html.Img(src='/assets/logo-nstech.png', style={'height': '36px'}),
            ]),
            html.Div([
                html.P(f'Bem-vindo, {session.get("user", "Usuário")}', className='header-subtitle'),
                html.Div(style={'width': '8px', 'height': '8px', 'background': '#22c55e', 'borderRadius': '50%', 'display': 'inline-block', 'marginRight': '6px'}),
                html.Span("Online", style={'color': '#22c55e', 'fontSize': '12px', 'fontWeight': '600'})
            ], style={'textAlign': 'right'})
        ], className='header-container'),
        
        # Filtros (Estilo Slicer Power BI)
        html.Div([
            html.Div([
                html.Label('FILTRAR POR ESCALA', className='filter-label'),
                dcc.RadioItems(
                    id='filtro-escala',
                    options=[{'label': 'Todas', 'value': 'Todas'}] + 
                            [{'label': e, 'value': e} for e in sorted(fato_sinistro['Escala'].unique()) if e != 'Todas'],
                    value='Todas',
                    className='slicer-container',
                    inputClassName='slicer-input',
                    labelClassName='slicer-label'
                )
            ], style={'flex': '1'}),
            
            # Filtro de Situação removido conforme solicitado
        ], className='filter-container'),
        
        # Gráficos Row 1 (Percentual + Top 10 Motivos)
        html.Div([
            html.Div([dcc.Graph(id='grafico-escala')], className='chart-card'),
            html.Div([dcc.Graph(id='grafico-motivos')], className='chart-card')
        ], className='chart-row'),
        
        # KPIs + Ranking (abaixo dos gráficos)
        html.Div([
            # Coluna KPIs
            html.Div([
                html.Div(id='kpi-cards', className='kpi-container-vertical')
            ], style={'flex': '1', 'minWidth': '300px'}),
            
            # Coluna Ranking
            html.Div([
                html.Div([
                    html.H3('Ranking de Envios', style={'color': '#e2e8f0', 'marginBottom': '15px', 'fontWeight': '700'}),
                    
                    # Filtros do Ranking
                    html.Div([
                        html.Div([
                            html.Label('Buscar Operador:', style={'color': '#94a3b8', 'fontSize': '12px', 'marginBottom': '5px', 'display': 'block'}),
                            dcc.Input(
                                id='busca-operador',
                                type='text',
                                placeholder='Digite o nome...',
                                style={
                                    'width': '100%',
                                    'padding': '10px 15px',
                                    'borderRadius': '8px',
                                    'border': '1px solid rgba(255,255,255,0.1)',
                                    'background': 'rgba(2, 6, 23, 0.5)',
                                    'color': '#fff',
                                    'fontSize': '14px'
                                }
                            )
                        ], style={'flex': '2'}),
                        
                        html.Div([
                            html.Label('Ordenar:', style={'color': '#94a3b8', 'fontSize': '12px', 'marginBottom': '5px', 'display': 'block'}),
                            dcc.Dropdown(
                                id='ordem-ranking',
                                options=[
                                    {'label': 'Maior para Menor', 'value': 'desc'},
                                    {'label': 'Menor para Maior', 'value': 'asc'}
                                ],
                                value='desc',
                                clearable=False,
                                style={'width': '180px'}
                            )
                        ])
                    ], style={'display': 'flex', 'gap': '20px', 'marginBottom': '15px', 'alignItems': 'flex-end'}),
                    
                    # Tabela
                    html.Div(id='tabela-operadores', style={'maxHeight': '500px', 'overflowY': 'auto'})
                ], className='ranking-card')
            ], style={'flex': '2', 'minWidth': '500px'})
        ], className='kpi-ranking-row'),
        
        # Gráficos Row 2 (Timeline)
        html.Div([
            html.Div([dcc.Graph(id='grafico-timeline')], className='chart-card')
        ], className='chart-row'),
        
        # Footer
        html.Div([
            html.P([
                'Powered by ', 
                html.Span('Sentinel', className='footer-brand'),
                ' • © 2026 Sentinel'
            ], className='footer-text')
        ], className='footer')
    ])

# ============ LAYOUT PRINCIPAL (ROTEADOR) ============
# Layout raiz fixo do app. É o único layout estático; o conteúdo real
# (login ou dashboard) é injetado em 'page-content' pelo callback display_page.
# O dcc.Location 'url' observa o caminho da barra de endereço e dispara o roteamento.
app.layout = html.Div([
    dcc.Location(id='url', refresh=True), # URL principal
    dcc.Location(id='url-login', refresh=True), # URL auxiliar para login (se necessário)
    html.Div(id='page-content') # Conteúdo dinâmico
])

# Callback de Roteamento
@app.callback(
    Output('page-content', 'children'),
    [Input('url', 'pathname')]
)
def display_page(pathname):
    """Roteia entre tela de login e dashboard conforme o estado da sessão (gate de sessão).

    Args:
        pathname (str): caminho atual da URL (vem do dcc.Location 'url'). Não é usado
            para decidir a rota — o gate é puramente baseado em sessão —, mas serve
            como gatilho do callback a cada navegação.

    Returns:
        html.Div: ``layout_dashboard()`` se houver usuário autenticado na sessão;
        caso contrário, ``layout_login()``.

    Como funciona:
        Funciona como guard de autenticação no front-end: se ``session['user']``
        existe (preenchido por ``login_route`` após autenticar), libera o dashboard;
        senão, devolve a tela de login. A proteção real depende da sessão Flask
        assinada — não há checagem de papel aqui (o RBAC já foi aplicado no login).
    """
    # Verificar sessão
    if session.get('user'):
        return layout_dashboard()
    else:
        return layout_login()

# ============ CALLBACKS ============
@app.callback(
    [Output('kpi-cards', 'children'), Output('grafico-escala', 'figure'), Output('grafico-motivos', 'figure'),
     Output('grafico-timeline', 'figure'), Output('tabela-operadores', 'children')],
    [Input('filtro-escala', 'value'), 
     Input('busca-operador', 'value'), Input('ordem-ranking', 'value')]
)
def update_dashboard(escala, busca, ordem):
    """Callback central: recalcula KPIs, 3 gráficos e o ranking a cada interação.

    É o único callback de dados do dashboard. Recebe os 3 filtros e produz as 5
    saídas registradas no decorator, na ordem exata abaixo.

    Args:
        escala (str): valor do slicer de escala ('Todas' ou uma escala específica).
        busca (str | None): texto de busca por nome de operador (filtra o ranking).
        ordem (str): 'desc' ou 'asc' — ordenação do ranking de operadores.

    Returns:
        tuple: ``(kpis, fig_escala, fig_motivos, fig_timeline, tabela)`` mapeando, na
        ordem, para os Outputs: 'kpi-cards', 'grafico-escala', 'grafico-motivos',
        'grafico-timeline', 'tabela-operadores'. Em erro, retorna
        ``([], {}, {}, {}, html.Div("Erro..."))`` para não quebrar a UI.

    Como funciona:
        1. Copia ``fato_sinistro`` e aplica o filtro de escala (exceto quando 'Todas').
           ATENÇÃO: o filtro de escala NÃO afeta o gráfico de pizza (ver passo 4).
        2. KPIs: total de envios (cdviag distintos), média por operador
           (linhas / operadores distintos) e a escala com mais envios no recorte.
        3. Gráfico de barras (Top 10 Motivos) e timeline semanal usam o df FILTRADO.
        4. Gráfico de pizza usa SEMPRE ``fato_sinistro`` completo (ignora ``escala``):
           é um panorama fixo do percentual por escala no total, por design — serve
           de referência estável mesmo quando o usuário restringe a um turno.
        5. Ranking: agrupa por operador, filtra por ``busca``, ordena por ``ordem`` e
           numera a posição. Tudo é envolto em try/except para degradar com elegância.
    """
    try:
        # Filtrar dados
        # Cópia defensiva: nunca mutar o DataFrame global; cada chamada parte do total.
        df = fato_sinistro.copy()
        # Recorte por escala. 'Todas' = sem filtro. Este recorte alimenta KPIs,
        # barras e timeline — mas NÃO o gráfico de pizza (que usa o total).
        if escala != 'Todas': df = df[df['Escala'] == escala]
        # Filtro de situação removido

        # KPIs (Sem emojis, textos ajustados)
        def create_kpi(value, label, color, description):
            """Monta um card de KPI (valor + rótulo + descrição) com borda colorida.

            Args:
                value: número/texto em destaque exibido no card.
                label (str): rótulo curto do indicador.
                color (str): cor hex da borda superior (faixa de 4px que identifica o KPI).
                description (str): texto auxiliar menor abaixo do valor.

            Returns:
                html.Div: o card pronto, com a classe 'kpi-card' e borda superior na cor dada.
            """
            return html.Div([
                html.Div([
                    html.Div([
                        html.P(value, className='kpi-value'),
                        html.P(label, className='kpi-label')
                    ]),
                    # Ícone removido conforme solicitado
                ], style={'display': 'flex', 'justifyContent': 'space-between', 'alignItems': 'flex-start'}),
                
                html.Div(description, style={'fontSize': '0.75rem', 'color': '#64748b', 'marginTop': '12px', 'lineHeight': '1.4', 'textAlign': 'left'})
            ], className='kpi-card', style={'borderTop': f'4px solid {color}'})

        # Cálculo da Média por Operador
        # Média = linhas (envios) / operadores distintos no recorte atual. Guarda
        # contra divisão por zero quando não há operadores (df vazio/filtro sem match).
        qtd_operadores = df["Operador_Inclusao"].nunique()
        media_por_operador = len(df) / qtd_operadores if qtd_operadores > 0 else 0

        # Cálculo Escala Mais Produtiva
        # Escala com maior número de envios no recorte: idxmax pega o rótulo e max a
        # contagem. cor_top busca a cor da escala (default cinza '#64748b' se ausente).
        if not df.empty:
            top_escala = df['Escala'].value_counts().idxmax()
            qtd_top = df['Escala'].value_counts().max()
            cor_top = CORES_ESCALA.get(top_escala, '#64748b')
        else:
            # Sem dados: placeholders neutros para os KPIs não quebrarem.
            top_escala = "-"
            qtd_top = 0
            cor_top = '#64748b'

        # Gerar Lista de KPIs (sem ícones)
        # Três cards: total de envios (contagem de viagens DISTINTAS via cdviag),
        # média por operador (1 casa decimal) e a escala campeã do recorte.
        kpis = [
            create_kpi(f"{df['cdviag'].nunique()}", 'Total de Envios', '#3b82f6', 'Total de envios BAS.'),
            create_kpi(f"{media_por_operador:.1f}", 'Média por Operador', '#eab308', 'Média de envios por operador.'),
            create_kpi(top_escala, 'Escala com mais envios totais', cor_top, f'Total de {qtd_top} envios.')
        ]

        # Configuração comum dos gráficos
        layout_config = {
            'paper_bgcolor': 'rgba(0,0,0,0)',
            'plot_bgcolor': 'rgba(0,0,0,0)',
            'font': {'color': '#94a3b8', 'family': 'Inter'},
            'margin': {'t': 50, 'b': 40, 'l': 40, 'r': 40}
        }
        
        # Gráfico Pizza (SEMPRE usa dados totais, sem filtro)
        # IMPORTANTE: usa 'fato_sinistro' (base completa), NÃO o 'df' filtrado. É uma
        # decisão de produto: a pizza é um panorama fixo da distribuição por escala no
        # total e deve permanecer estável mesmo quando o usuário seleciona uma escala
        # no slicer. hole=0.7 => formato donut (anel de 70% de raio interno).
        fig_escala = px.pie(
            fato_sinistro['Escala'].value_counts().reset_index(),
            values='count',
            names='Escala',
            color='Escala',
            color_discrete_map=CORES_ESCALA,
            title='Percentual de Envio por Escala (Total)',
            hole=0.7,
            template='plotly_dark'
        )
        fig_escala.update_layout(**layout_config)
        # Mostra apenas o percentual nas fatias; linha escura (#0f172a) separa os anéis.
        fig_escala.update_traces(textinfo='percent', textfont_size=14, marker=dict(line=dict(color='#0f172a', width=2)))

        # Gráfico Barras
        # Top 10 motivos do recorte FILTRADO (head(10) sobre a contagem já ordenada).
        # Barras horizontais; categoryorder='total ascending' coloca o maior no topo.
        fig_motivos = px.bar(
            df['Descricao_Motivo'].value_counts().head(10).reset_index(), 
            x='count', 
            y='Descricao_Motivo', 
            orientation='h', 
            title='Top 10 Motivos',
            template='plotly_dark',
            color='count',
            color_continuous_scale='Oranges'
        )
        fig_motivos.update_layout(**layout_config, yaxis={'categoryorder': 'total ascending'}, coloraxis_showscale=False)
        fig_motivos.update_traces(marker_color='#f97316', marker_line_width=0, opacity=0.9)
        
        # Gráfico Timeline (Semanal)
        # Agrupar por Semana
        df_time = df.copy()
        # Criar coluna Semana-Ano para ordenação correta
        # 'to_period('W')' colapsa a data de inclusão na semana-calendário; mantido como
        # string apenas para servir de CHAVE DE ORDENAÇÃO cronológica estável (ex.: "2026-01-05/2026-01-11").
        df_time['Semana_Ano'] = df_time['dtIncl'].dt.to_period('W').astype(str)

        # Criar label amigável: "Sem X (dd/mm)"
        def format_week_label(dt):
            """Gera o rótulo amigável de uma semana, ex.: "Sem 12 (16/03)".

            Args:
                dt (pd.Timestamp): data de inclusão de um envio.

            Returns:
                str: rótulo "Sem {número ISO} ({dd/mm da segunda-feira})".

            Como funciona:
                Usa o número de semana ISO (isocalendar()[1]) e calcula a segunda-feira
                daquela semana subtraindo ``dt.weekday()`` dias (weekday()==0 na segunda),
                exibindo só dia/mês. Esse rótulo é o que aparece no eixo X; a ordenação
                cronológica de fato vem da coluna 'Semana_Ano' (não deste texto).
            """
            week_num = dt.isocalendar()[1]
            start_of_week = dt - pd.Timedelta(days=dt.weekday())
            return f"Sem {week_num} ({start_of_week.strftime('%d/%m')})"

        df_time['Semana_Label'] = df_time['dtIncl'].apply(format_week_label)

        # Agrupar
        # Contagem de envios por (semana, rótulo, escala). 'Semana_Label' entra no
        # group-by só para ser carregado adiante; 'Semana_Ano' garante a ordenação.
        df_grouped = df_time.groupby(['Semana_Ano', 'Semana_Label', 'Escala']).size().reset_index(name='count')

        # Ordenar cronologicamente
        # Ordena pela chave ISO (string ordenável) para que o eixo X siga a ordem do tempo.
        df_grouped = df_grouped.sort_values('Semana_Ano')

        # Garantir ordem das escalas na legenda
        # Ordem fixa das séries na legenda/cores, independente da ordem de aparição nos dados.
        ordem_escala = ['Azul', 'Amarela', 'Verde', 'Cinza']

        fig_timeline = px.line(
            df_grouped, 
            x='Semana_Label', 
            y='count', 
            color='Escala', 
            color_discrete_map=CORES_ESCALA, 
            category_orders={'Escala': ordem_escala},
            title='Evolução Temporal (Volume por Semana)',
            markers=True,
            template='plotly_dark',
            line_shape='spline' # Linha suave
        )
        # hovermode='x unified': um único tooltip por semana comparando todas as escalas.
        fig_timeline.update_layout(**layout_config, hovermode='x unified')
        fig_timeline.update_traces(line=dict(width=3), marker=dict(size=8))

        # Tabela Operadores (Ranking Completo)
        # Agrupar por operador e contar envios
        # Uma linha por operador com a contagem de envios ('Qtd') no recorte filtrado.
        df_ops = df.groupby('Operador_Inclusao').size().reset_index(name='Qtd')

        # Filtrar por busca (se houver)
        # Busca por substring no nome, case-insensitive; na=False ignora nomes nulos.
        if busca:
            df_ops = df_ops[df_ops['Operador_Inclusao'].str.contains(busca, case=False, na=False)]

        # Ordenar
        # ordem='asc' => crescente; qualquer outro valor ('desc') => decrescente.
        ascending = True if ordem == 'asc' else False
        df_ops = df_ops.sort_values('Qtd', ascending=ascending)

        # Adicionar ranking (posição)
        # A posição #1 deve sempre representar o operador com MAIS envios. Como a tabela
        # pode estar ordenada ascendente, a numeração é invertida nesse caso (de N..1)
        # para que o topo lógico (#1) continue sendo o de maior Qtd, independentemente
        # da ordem de exibição escolhida.
        df_ops['Pos'] = range(1, len(df_ops) + 1) if not ascending else range(len(df_ops), 0, -1)

        tabela = html.Table([
            html.Thead(html.Tr([html.Th('Pos'), html.Th('Operador'), html.Th('Qtd')])),
            html.Tbody([
                html.Tr([
                    html.Td(f"#{row['Pos']}", style={'color': '#f97316', 'fontWeight': 'bold'}),
                    html.Td(row['Operador_Inclusao']),
                    html.Td(row['Qtd'], style={'fontWeight': 'bold'})
                ]) for _, row in df_ops.iterrows()
            ])
        ], className='custom-table')

        # Ordem do retorno casa exatamente com a lista de Outputs do decorator.
        return kpis, fig_escala, fig_motivos, fig_timeline, tabela

    except Exception as e:
        # Falha em qualquer etapa: registra e devolve saídas vazias/placeholder para
        # não derrubar a página inteira (figuras vazias = {}; tabela = mensagem de erro).
        print(f"Erro no callback: {e}")
        return [], {}, {}, {}, html.Div("Erro ao carregar dados")

print("--- INICIANDO SERVIDOR DASH ---")

# Ponto de entrada: só executa quando o arquivo é rodado diretamente (não em import).
# A porta vem da env PORT (default 8051) para compatibilidade com hosts/containers;
# host 0.0.0.0 expõe em todas as interfaces. debug=True NÃO deve ir para produção.
if __name__ == '__main__':
    port = int(os.environ.get('PORT', 8051))
    app.run(debug=True, host='0.0.0.0', port=port)

