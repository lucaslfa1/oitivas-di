import dash
from dash import dcc, html, Input, Output, State
import pandas as pd
import plotly.express as px
import plotly.graph_objects as go
import os
import json
from flask import Flask, session, redirect, request

# ============ CONFIGURAÇÃO FLASK ============
server = Flask(__name__)
server.secret_key = os.urandom(24)  # Necessário para sessão

# ============ CARREGAR DADOS ============
if os.path.exists('editados/Base_BI_Sinistros_Escalas_v3.xlsx'):
    EXCEL_PATH = 'editados/Base_BI_Sinistros_Escalas_v3.xlsx'
    DIM_OPERADOR_PATH = 'Dim_Operador_Inferida.csv'
else:
    # Fallback para caminho absoluto se necessário (ajustado para nova estrutura)
    EXCEL_PATH = os.path.join(os.getcwd(), 'editados', 'Base_BI_Sinistros_Escalas_v3.xlsx')
    DIM_OPERADOR_PATH = os.path.join(os.getcwd(), 'Dim_Operador_Inferida.csv')

# Carregar tabelas
try:
    fato_sinistro = pd.read_excel(EXCEL_PATH, sheet_name='Fato_Sinistro')
    dim_escala = pd.read_excel(EXCEL_PATH, sheet_name='Dim_Escala')
    dim_situacao = pd.read_excel(EXCEL_PATH, sheet_name='Dim_Situacao')
    dim_motivo = pd.read_excel(EXCEL_PATH, sheet_name='Dim_Motivo')

    # Carregar Dimensão Operador
    if os.path.exists(DIM_OPERADOR_PATH):
        dim_operador = pd.read_csv(DIM_OPERADOR_PATH)
        fato_sinistro = fato_sinistro.merge(
            dim_operador[['Operador', 'Escala_Predominante']], 
            left_on='Operador_Inclusao', 
            right_on='Operador', 
            how='left'
        )
        fato_sinistro['Escala_Original'] = fato_sinistro['Escala']
        fato_sinistro['Escala'] = fato_sinistro['Escala_Predominante'].fillna('Desconhecida')
    else:
        print("⚠️ Dim_Operador_Inferida.csv não encontrado.")

    fato_sinistro['dtIncl'] = pd.to_datetime(fato_sinistro['dtIncl'], errors='coerce')
    fato_sinistro['dtIni'] = pd.to_datetime(fato_sinistro['dtIni'], errors='coerce')
    fato_sinistro['dtFim'] = pd.to_datetime(fato_sinistro['dtFim'], errors='coerce')

    # Calcular tempo de atendimento em horas
    fato_sinistro['Tempo_Atendimento'] = (fato_sinistro['dtFim'] - fato_sinistro['dtIni']).dt.total_seconds() / 3600

except Exception as e:
    print(f"Erro ao carregar dados: {e}")
    # Criar DataFrames vazios para evitar crash na inicialização se arquivos faltarem
    fato_sinistro = pd.DataFrame(columns=['Operador_Inclusao', 'Escala', 'Descricao_Situacao', 'Descricao_Motivo', 'dtIncl', 'dtIni', 'dtFim', 'Tempo_Atendimento'])
    dim_situacao = pd.DataFrame(columns=['Descricao_Situacao'])

import requests

# ============ CONFIGURAÇÃO API CENTRALIZADA ============
BACKEND_AUTH_URL = os.environ.get('BACKEND_AUTH_URL', 'http://localhost:5252/api/auth/login')
ALLOWED_DASHBOARD_ROLES = ['Admin', 'Coordenador', 'Supervisor', 'Analista']

# Rota de Login (Recebe POST do Dashboard ou Sentinel)
@server.route('/login', methods=['POST'])
def login_route():
    username = request.form.get('username')
    password = request.form.get('password')
    
    try:
        response = requests.post(
            BACKEND_AUTH_URL, 
            json={'username': username, 'password': password},
            timeout=5
        )
        
        if response.status_code == 200:
            result = response.json()
            if result.get('success'):
                user_role = result.get('role')
                
                # Verificação de Permissão específica para Dashboard
                if user_role in ALLOWED_DASHBOARD_ROLES:
                    session['user'] = result.get('username')
                    session['role'] = user_role
                    return redirect('/')
                else:
                    return f"Acesso Negado: O perfil {user_role} não possui permissão para acessar o Dashboard.", 403
            
        return "Usuário ou senha incorretos", 401
    except Exception as e:
        print(f"Erro ao conectar com serviço de autenticação: {e}")
        return "Serviço de autenticação indisponível", 503

# ============ APP DASH ============
app = dash.Dash(__name__, server=server, title='Sentinel - Dashboard KPI', suppress_callback_exceptions=True)

# Cores
CORES_ESCALA = {'Azul': '#3b82f6', 'Amarela': '#eab308', 'Verde': '#22c55e', 'Cinza': '#94a3b8', 'Desconhecida': '#64748b'}

# Layout de Login (Formulário HTML Puro)
def layout_login():
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
    try:
        # Filtrar dados
        df = fato_sinistro.copy()
        if escala != 'Todas': df = df[df['Escala'] == escala]
        # Filtro de situação removido
        
        # KPIs (Sem emojis, textos ajustados)
        def create_kpi(value, label, color, description):
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
        qtd_operadores = df["Operador_Inclusao"].nunique()
        media_por_operador = len(df) / qtd_operadores if qtd_operadores > 0 else 0
        
        # Cálculo Escala Mais Produtiva
        if not df.empty:
            top_escala = df['Escala'].value_counts().idxmax()
            qtd_top = df['Escala'].value_counts().max()
            cor_top = CORES_ESCALA.get(top_escala, '#64748b')
        else:
            top_escala = "-"
            qtd_top = 0
            cor_top = '#64748b'

        # Gerar Lista de KPIs (sem ícones)
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
        fig_escala.update_traces(textinfo='percent', textfont_size=14, marker=dict(line=dict(color='#0f172a', width=2)))
        
        # Gráfico Barras
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
        df_time['Semana_Ano'] = df_time['dtIncl'].dt.to_period('W').astype(str)
        
        # Criar label amigável: "Sem X (dd/mm)"
        def format_week_label(dt):
            week_num = dt.isocalendar()[1]
            start_of_week = dt - pd.Timedelta(days=dt.weekday())
            return f"Sem {week_num} ({start_of_week.strftime('%d/%m')})"

        df_time['Semana_Label'] = df_time['dtIncl'].apply(format_week_label)

        # Agrupar
        df_grouped = df_time.groupby(['Semana_Ano', 'Semana_Label', 'Escala']).size().reset_index(name='count')
        
        # Ordenar cronologicamente
        df_grouped = df_grouped.sort_values('Semana_Ano')

        # Garantir ordem das escalas na legenda
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
        fig_timeline.update_layout(**layout_config, hovermode='x unified')
        fig_timeline.update_traces(line=dict(width=3), marker=dict(size=8))

        # Tabela Operadores (Ranking Completo)
        # Agrupar por operador e contar envios
        df_ops = df.groupby('Operador_Inclusao').size().reset_index(name='Qtd')
        
        # Filtrar por busca (se houver)
        if busca:
            df_ops = df_ops[df_ops['Operador_Inclusao'].str.contains(busca, case=False, na=False)]
            
        # Ordenar
        ascending = True if ordem == 'asc' else False
        df_ops = df_ops.sort_values('Qtd', ascending=ascending)
        
        # Adicionar ranking (posição)
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

        return kpis, fig_escala, fig_motivos, fig_timeline, tabela

    except Exception as e:
        print(f"Erro no callback: {e}")
        return [], {}, {}, {}, html.Div("Erro ao carregar dados")

print("--- INICIANDO SERVIDOR DASH ---")

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 8051))
    app.run(debug=True, host='0.0.0.0', port=port)

