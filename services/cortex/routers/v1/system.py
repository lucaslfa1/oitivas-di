"""
SENTINEL Cortex - System Routes
Endpoints de sistema: health check, cache, métricas.
"""

from fastapi import APIRouter

from models.responses import CacheStatsResponse
from services.cache_service import get_cache
from core.config import get_settings

router = APIRouter()

# Obter configurações e cache
settings = get_settings()
cache = get_cache()


@router.get("/health")
async def health_check():
    """Health check endpoint."""
    return {
        "status": "healthy",
        "service": "sentinel-cortex",
        "version": settings.app_version,
        "cache_entries": cache.stats()["active_entries"]
    }


@router.get("/cache/stats", response_model=CacheStatsResponse)
async def cache_stats():
    """Retorna estatísticas do cache."""
    return cache.stats()


@router.post("/cache/clear")
async def cache_clear():
    """Limpa todo o cache."""
    count = cache.clear()
    return {"cleared": count}


@router.post("/cache/cleanup")
async def cache_cleanup():
    """Remove entradas expiradas do cache."""
    count = cache.cleanup_expired()
    return {"removed": count}
