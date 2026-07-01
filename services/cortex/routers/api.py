"""
SENTINEL Cortex - API Router Aggregator
Centraliza todas as rotas da API v1.
"""

from fastapi import APIRouter

from routers.v1 import media_router, intelligence_router, system_router, auth_router

api_router = APIRouter()

# Incluir routers com tags para documentação organizada
api_router.include_router(
    auth_router,
    tags=["Authentication"]
)

api_router.include_router(
    media_router, 
    tags=["Media Processing"]
)

api_router.include_router(
    intelligence_router, 
    tags=["Intelligence"]
)

api_router.include_router(
    system_router, 
    tags=["System"]
)
