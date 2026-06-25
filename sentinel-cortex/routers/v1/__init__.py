# V1 Routers
from .media import router as media_router
from .intelligence import router as intelligence_router
from .system import router as system_router
from .auth import router as auth_router

__all__ = ["media_router", "intelligence_router", "system_router", "auth_router"]
