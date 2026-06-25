"""
CacheService - Cache inteligente para transcrições.

Evita reprocessamento de arquivos idênticos usando hash SHA-256.
Suporta in-memory (desenvolvimento) ou Redis (produção).
"""

import hashlib
from datetime import datetime, timedelta
from typing import Optional, Any
import logging
import json

logger = logging.getLogger(__name__)


class TranscriptionCache:
    """
    Cache de transcrições baseado em hash do arquivo.
    
    Uso:
        cache = TranscriptionCache(ttl_hours=24)
        
        # Verificar cache
        file_hash = cache.get_hash(audio_bytes)
        cached = cache.get(file_hash)
        if cached:
            return cached
            
        # Processar e cachear
        result = process_audio(audio_bytes)
        cache.set(file_hash, result)
    """
    
    def __init__(self, ttl_hours: int = 24, max_entries: int = 1000):
        """
        Inicializa o cache.
        
        Args:
            ttl_hours: Tempo de vida das entradas em horas (default: 24)
            max_entries: Número máximo de entradas no cache (default: 1000)
        """
        self.ttl_hours = ttl_hours
        self.max_entries = max_entries
        self._cache: dict[str, dict] = {}
        self._access_order: list[str] = []  # LRU tracking
        logger.info(f"Cache inicializado: TTL={ttl_hours}h, max_entries={max_entries}")
    
    def get_hash(self, data: bytes) -> str:
        """
        Calcula hash SHA-256 dos dados.
        
        Args:
            data: Bytes do arquivo
            
        Returns:
            Hash hexadecimal de 64 caracteres
        """
        return hashlib.sha256(data).hexdigest()
    
    def get(self, file_hash: str) -> Optional[dict]:
        """
        Recupera item do cache se existir e não estiver expirado.
        
        Args:
            file_hash: Hash do arquivo
            
        Returns:
            Dados cacheados ou None se não encontrado/expirado
        """
        cached = self._cache.get(file_hash)
        
        if cached is None:
            logger.debug(f"Cache MISS: {file_hash[:16]}...")
            return None
        
        # Verificar expiração
        if cached['expires'] < datetime.now():
            logger.info(f"Cache EXPIRED: {file_hash[:16]}...")
            del self._cache[file_hash]
            if file_hash in self._access_order:
                self._access_order.remove(file_hash)
            return None
        
        # Atualizar ordem de acesso (LRU)
        if file_hash in self._access_order:
            self._access_order.remove(file_hash)
        self._access_order.append(file_hash)
        
        logger.info(f"Cache HIT: {file_hash[:16]}...")
        return cached['data']
    
    def set(self, file_hash: str, data: dict, ttl_hours: Optional[int] = None) -> None:
        """
        Armazena item no cache.
        
        Args:
            file_hash: Hash do arquivo
            data: Dados a cachear
            ttl_hours: TTL customizado (opcional, usa default se não informado)
        """
        ttl = ttl_hours or self.ttl_hours
        
        # Limpar entradas antigas se necessário (LRU eviction)
        while len(self._cache) >= self.max_entries and self._access_order:
            oldest_hash = self._access_order.pop(0)
            if oldest_hash in self._cache:
                del self._cache[oldest_hash]
                logger.debug(f"Cache EVICTED (LRU): {oldest_hash[:16]}...")
        
        self._cache[file_hash] = {
            'data': data,
            'expires': datetime.now() + timedelta(hours=ttl),
            'created': datetime.now().isoformat()
        }
        self._access_order.append(file_hash)
        
        logger.info(f"Cache SET: {file_hash[:16]}... (TTL={ttl}h)")
    
    def clear(self) -> int:
        """
        Limpa todo o cache.
        
        Returns:
            Número de entradas removidas
        """
        count = len(self._cache)
        self._cache.clear()
        self._access_order.clear()
        logger.info(f"Cache CLEARED: {count} entradas removidas")
        return count
    
    def cleanup_expired(self) -> int:
        """
        Remove entradas expiradas do cache.
        
        Returns:
            Número de entradas removidas
        """
        now = datetime.now()
        expired = [
            h for h, v in self._cache.items() 
            if v['expires'] < now
        ]
        
        for file_hash in expired:
            del self._cache[file_hash]
            if file_hash in self._access_order:
                self._access_order.remove(file_hash)
        
        if expired:
            logger.info(f"Cache CLEANUP: {len(expired)} entradas expiradas removidas")
        
        return len(expired)
    
    def stats(self) -> dict:
        """
        Retorna estatísticas do cache.
        
        Returns:
            Dicionário com estatísticas
        """
        now = datetime.now()
        active = sum(1 for v in self._cache.values() if v['expires'] >= now)
        expired = len(self._cache) - active
        
        return {
            "total_entries": len(self._cache),
            "active_entries": active,
            "expired_entries": expired,
            "max_entries": self.max_entries,
            "ttl_hours": self.ttl_hours,
            "memory_usage_approx_kb": self._estimate_memory() // 1024
        }
    
    def _estimate_memory(self) -> int:
        """Estima uso de memória em bytes."""
        try:
            return len(json.dumps({
                h: {**v, 'expires': v['expires'].isoformat()}
                for h, v in self._cache.items()
            }))
        except Exception:
            return 0


# Instância global do cache
_cache_instance: Optional[TranscriptionCache] = None


def get_cache() -> TranscriptionCache:
    """Retorna instância singleton do cache."""
    global _cache_instance
    if _cache_instance is None:
        _cache_instance = TranscriptionCache()
    return _cache_instance
