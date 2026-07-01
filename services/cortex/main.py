"""
SENTINEL Cortex - API Gateway Central
Ponto de entrada da aplicação.

Para análise forense e processamento de mídia do ecossistema SENTINEL.

Como funciona (visão geral do módulo):
    Este módulo monta a instância FastAPI que serve como gateway central do
    SENTINEL Cortex. Toda a inteligência (análise de mídia, transcrição, etc.)
    vive nos routers e serviços importados; aqui ficam apenas a configuração da
    aplicação, o registro das rotas e o ciclo de vida (startup/shutdown).

    Pipeline de inicialização do módulo (executado uma vez, no import):
        1. Carrega as configurações via `get_settings()` (cacheadas) e inicializa
           o logging com o nível definido em `settings.log_level`.
        2. Cria a aplicação FastAPI com metadados (nome/versão/descrição) e expõe
           a documentação interativa em /docs, /redoc e /openapi.json.
        3. Habilita CORS a partir das origens configuradas, permitindo que os
           frontends do ecossistema chamem a API a partir do navegador.
        4. Registra o roteador da API duas vezes: uma sob o prefixo /api/v1
           (contrato atual) e outra sem prefixo (compatibilidade legada, a ser
           removida após a migração dos frontends).
        5. Registra a rota auxiliar /converter e os handlers de ciclo de vida.

    Observação de arquitetura: o pipeline de análise é Azure-only. Integrações
    antigas (Gemini/Vertex/Central) foram removidas do projeto; qualquer resíduo
    desses nomes em comentários é histórico e não reflete o código atual.
"""

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from core.config import get_settings, setup_logging
from routers.api import api_router

# Configurações
settings = get_settings()
logger = setup_logging(settings.log_level)

# Criar aplicação FastAPI
app = FastAPI(
    title=settings.app_name,
    description=settings.app_description,
    version=settings.app_version,
    docs_url="/docs",
    redoc_url="/redoc",
    openapi_url="/openapi.json"
)

# CORS Middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Incluir rotas da API v1
app.include_router(api_router, prefix="/api/v1")

# Incluir rotas legadas (sem prefixo) para compatibilidade
# TODO: Remover após migração completa dos frontends
app.include_router(api_router)

from fastapi.responses import FileResponse
import os

@app.get("/converter")
async def get_converter():
    """Serve a página estática converter.html que vive FORA do diretório do serviço.

    Returns:
        FileResponse: a resposta com o conteúdo de ``converter.html`` quando o
            arquivo existe no disco.
        dict: um payload de erro ``{"error": ..., "path": ...}`` quando o arquivo
            não é encontrado (note que, neste caso, o FastAPI ainda responde
            HTTP 200 com o JSON do erro — não é um 404 propriamente dito).

    Como funciona:
        1. Resolve o caminho do HTML a partir da localização DESTE arquivo
           (``__file__``): sobe um nível com ``".."`` e aponta para
           ``converter.html``. ``os.path.abspath`` normaliza o caminho relativo
           em absoluto, evitando ambiguidade conforme o diretório de execução.
        2. Verifica a existência do arquivo com ``os.path.exists``.
        3. Se existir, devolve-o via ``FileResponse`` (streaming do arquivo com os
           cabeçalhos de Content-Type inferidos pela extensão). Caso contrário,
           retorna o dicionário de erro com o caminho calculado para diagnóstico.

    Acoplamento (IMPORTANTE para o time):
        Este endpoint quebra o isolamento do serviço: ``converter.html`` NÃO mora
        dentro de ``sentinel-cortex/``, e sim no diretório-pai (um nível acima).
        Consequências práticas:
          - O serviço depende do layout de pastas do repositório/deploy. Se o
            ``main.py`` for movido, ou o serviço empacotado/conteinerizado sem o
            diretório-pai, o arquivo deixará de ser encontrado e a rota passará a
            responder o payload de erro.
          - O caminho é fixo via ``".."``; não há configuração para sobrescrevê-lo.
          - Por servir um arquivo de fora da árvore do serviço, vale revisar em
            deploy se esse asset realmente acompanha o pacote do gateway.
    """
    # Caminho para o arquivo converter.html (um nível acima do sentinel-cortex)
    file_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "converter.html"))
    if os.path.exists(file_path):
        return FileResponse(file_path)
    return {"error": "File not found", "path": file_path}


@app.on_event("startup")
async def startup_event():
    """Handler de ciclo de vida disparado quando a aplicação termina de subir.

    Como funciona:
        O FastAPI/Starlette invoca esta corrotina UMA vez, após o servidor ASGI
        estar pronto para aceitar conexões e ANTES de atender a primeira
        requisição. Aqui o handler apenas registra um banner de boas-vindas no
        log (nome e versão da app, confirmação de que o gateway iniciou e a URL
        da documentação interativa montada em ``settings.host:settings.port``).

        É puramente observabilidade: não há efeitos colaterais nem inicialização
        de recursos (conexões, pools, clientes Azure). Os emojis e as linhas de
        ``"=" * 50`` servem só para destacar visualmente o evento no log.

        Nota: ``@app.on_event("startup")`` é a API clássica do FastAPI; em
        versões mais novas o padrão recomendado é o context manager ``lifespan``.
        Mantido como está para não alterar o comportamento atual.
    """
    logger.info("=" * 50)
    logger.info(f"🚀 {settings.app_name} v{settings.app_version}")
    logger.info(f"📡 API Gateway iniciado")
    logger.info(f"📚 Docs: http://{settings.host}:{settings.port}/docs")
    logger.info("=" * 50)


@app.on_event("shutdown")
async def shutdown_event():
    """Handler de ciclo de vida disparado durante o encerramento gracioso da app.

    Como funciona:
        O FastAPI/Starlette invoca esta corrotina UMA vez, quando o processo
        recebe o sinal de parada (ex.: SIGTERM/SIGINT) e antes do servidor ASGI
        finalizar. Aqui o handler apenas registra no log que o SENTINEL Cortex
        foi encerrado, servindo como marcador de fim de execução.

        Não há liberação explícita de recursos: como o startup não abre conexões
        persistentes, não há nada para fechar neste ponto. Caso futuramente sejam
        adicionados pools ou clientes de longa duração, é aqui que devem ser
        finalizados.
    """
    logger.info("🛑 SENTINEL Cortex encerrado")


# Ponto de entrada para execução direta
#
# Como funciona:
#   Este bloco só roda quando o arquivo é executado diretamente (`python main.py`),
#   e não quando é importado por um servidor ASGI externo (ex.: `uvicorn main:app`,
#   gunicorn). Nesse modo de execução direta, sobe-se o Uvicorn programaticamente:
#     - "main:app" é passado como string (e não o objeto `app`) justamente para
#       habilitar o reload por arquivo: o Uvicorn precisa do caminho de import
#       para recarregar o módulo a cada alteração.
#     - host/port vêm das configurações centralizadas (`settings`).
#     - reload=settings.debug liga o auto-reload APENAS em ambiente de
#       desenvolvimento (debug=True); em produção deve permanecer desligado.
if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "main:app",
        host=settings.host,
        port=settings.port,
        reload=settings.debug
    )
