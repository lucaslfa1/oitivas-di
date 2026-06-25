"""
SENTINEL Cortex - API Gateway Central
Ponto de entrada da aplicação.

Para análise forense e processamento de mídia do ecossistema SENTINEL.
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
    # Caminho para o arquivo converter.html (um nível acima do sentinel-cortex)
    file_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "converter.html"))
    if os.path.exists(file_path):
        return FileResponse(file_path)
    return {"error": "File not found", "path": file_path}


@app.on_event("startup")
async def startup_event():
    """Evento de inicialização."""
    logger.info("=" * 50)
    logger.info(f"🚀 {settings.app_name} v{settings.app_version}")
    logger.info(f"📡 API Gateway iniciado")
    logger.info(f"📚 Docs: http://{settings.host}:{settings.port}/docs")
    logger.info("=" * 50)


@app.on_event("shutdown")
async def shutdown_event():
    """Evento de encerramento."""
    logger.info("🛑 SENTINEL Cortex encerrado")


# Ponto de entrada para execução direta
if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "main:app",
        host=settings.host,
        port=settings.port,
        reload=settings.debug
    )
