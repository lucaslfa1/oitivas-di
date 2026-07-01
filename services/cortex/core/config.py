"""
SENTINEL Cortex - Core Configuration
Configurações centralizadas e setup de logging.
"""

import logging
from functools import lru_cache
from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    """Configurações da aplicação."""
    
    # API Info
    app_name: str = "SENTINEL Cortex"
    app_version: str = "2.0.0"
    app_description: str = "API Gateway Central - Processamento de mídia e orquestração para análise forense"
    
    # Server
    host: str = "0.0.0.0"
    port: int = 8000
    debug: bool = False
    
    # CORS
    cors_origins: list[str] = ["*"]
    
    # Logging
    log_level: str = "INFO"

    # Security
    # IMPORTANTE: defina a chave via variável de ambiente SENTINEL_SECRET_KEY.
    # Nunca versione um secret real aqui (ver docs/PLANO_MELHORIA_HANDOFF.md, Fase 0).
    secret_key: str = ""
    algorithm: str = "HS256"
    access_token_expire_minutes: int = 30
    
    class Config:
        env_prefix = "SENTINEL_"
        env_file = ".env"


@lru_cache()
def get_settings() -> Settings:
    """Retorna configurações cacheadas."""
    return Settings()


def setup_logging(level: str = "INFO") -> logging.Logger:
    """Configura logging centralizado."""
    logging.basicConfig(
        level=getattr(logging, level.upper()),
        format="%(asctime)s | %(levelname)s | %(name)s | %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S"
    )
    return logging.getLogger("sentinel")


# Logger global
logger = setup_logging()
