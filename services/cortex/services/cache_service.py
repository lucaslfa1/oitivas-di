"""
CacheService - Cache inteligente para transcrições.

Evita reprocessamento de arquivos idênticos usando hash SHA-256.
Suporta in-memory (desenvolvimento) ou Redis (produção).

Como funciona (visão geral do módulo):
    1. O conteúdo binário de um áudio é reduzido a uma chave determinística
       (hash SHA-256). Áudios idênticos byte a byte geram a MESMA chave, então
       qualquer reenvio do mesmo arquivo atinge o cache sem reprocessar.
    2. A chave indexa um dicionário em memória (`_cache`), onde cada entrada
       guarda o resultado da transcrição, o instante de expiração (TTL) e o
       carimbo de criação.
    3. Duas políticas de descarte convivem:
       - TTL (time-to-live): toda entrada vence após `ttl_hours`; entradas
         vencidas são tratadas como inexistentes e removidas na leitura.
       - LRU (least-recently-used): quando o cache atinge `max_entries`, as
         entradas menos recentemente acessadas são despejadas para abrir espaço.
    4. A recência é mantida por `_access_order`, uma lista que funciona como
         fila: o item no índice 0 é o "mais antigo" (candidato a despejo) e o
         final da lista é o "mais recente".

    Observação: a implementação atual é puramente in-memory (um dicionário
    Python no processo). A menção a Redis descreve o destino de produção
    pretendido, mas este módulo não fala com Redis — ele é o backend de
    desenvolvimento/processo único. Por ser in-memory, o cache não é
    compartilhado entre processos/workers nem sobrevive a reinícios.
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

    Combina duas políticas de descarte sobre um dicionário em memória:
    TTL (expiração por tempo) e LRU (despejo do menos recentemente usado
    quando o limite de capacidade é atingido).

    Estado interno:
        _cache (dict[str, dict]): mapa chave_hash -> entrada. Cada entrada é
            um dict com 'data' (resultado da transcrição), 'expires' (datetime
            de vencimento do TTL) e 'created' (ISO-8601 do momento de gravação).
        _access_order (list[str]): ordem de recência de acesso. O índice 0 é o
            menos recentemente usado (primeiro a ser despejado pelo LRU); o
            final da lista é o mais recentemente usado. É reordenada a cada
            leitura/gravação bem-sucedida.

    Invariante esperada: todo hash presente em `_cache` deveria estar também em
    `_access_order`. Os métodos usam checagens defensivas (`if x in lista`)
    antes de remover para tolerar eventuais divergências sem lançar exceção.

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
            ttl_hours: Tempo de vida (TTL) padrão das entradas, em horas.
                Default 24h: equilíbrio entre reaproveitar transcrições recentes
                e não servir resultados velhos indefinidamente. Pode ser
                sobrescrito por entrada em `set(...)`.
            max_entries: Capacidade máxima de entradas simultâneas. Default 1000:
                teto de memória — ao atingir esse limite, o LRU despeja as
                entradas mais antigas para abrir espaço a novas.

        Como funciona:
            1. Guarda os parâmetros de política (`ttl_hours`, `max_entries`).
            2. Cria o dicionário de armazenamento `_cache` vazio (o backend real).
            3. Cria a lista `_access_order` vazia, que rastreia a recência de
               acesso usada pelo despejo LRU.
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
            data: Bytes do arquivo (ex.: conteúdo bruto do áudio).

        Returns:
            Hash hexadecimal de 64 caracteres usado como chave de cache.

        Como funciona:
            1. Aplica SHA-256 sobre o conteúdo binário inteiro.
            2. Converte o digest para hexadecimal (`hexdigest`), produzindo uma
               string ASCII estável de 64 caracteres.
            Por ser determinístico, o mesmo conteúdo sempre gera a mesma chave,
            garantindo o deduplicação: dois envios idênticos colidem na mesma
            entrada. Pequenas alterações nos bytes mudam o hash por completo
            (efeito avalanche), evitando colisões acidentais entre áudios
            diferentes.
        """
        return hashlib.sha256(data).hexdigest()
    
    def get(self, file_hash: str) -> Optional[dict]:
        """
        Recupera item do cache se existir e não estiver expirado.

        Args:
            file_hash: Hash do arquivo (chave) gerado por `get_hash`.

        Returns:
            Os dados cacheados (`entrada['data']`) em caso de acerto válido, ou
            None se a chave não existir (MISS) ou se a entrada já venceu o TTL
            (EXPIRED). Note que a expiração também é tratada como "ausência".

        Como funciona:
            1. Busca a entrada por chave (`dict.get`, sem lançar KeyError).
            2. MISS: se não há entrada, registra e retorna None imediatamente.
            3. Checagem de TTL: compara o instante de expiração com o relógio
               atual. Se já passou, a entrada é considerada inválida — ela é
               removida de `_cache` e de `_access_order` (limpeza preguiçosa,
               "lazy expiration": só expira de fato no momento em que é lida) e
               retorna None.
            4. Atualização LRU: em um acerto válido, a chave é movida para o FIM
               de `_access_order` (remove-e-reanexa), marcando-a como a mais
               recentemente usada e, portanto, a última a ser despejada.
            5. Retorna apenas o payload `data`, ocultando os metadados internos
               ('expires'/'created') do chamador.

        Observações:
            - O slice `file_hash[:16]` nos logs mostra só o prefixo do hash
              (16 caracteres) para identificar a entrada sem poluir o log com
              os 64 caracteres completos.
            - A remoção em `_access_order` é guardada por `if ... in ...` para
              tolerar o caso (anômalo) de a chave estar no `_cache` mas não na
              lista de recência.
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
            file_hash: Hash do arquivo (chave) gerado por `get_hash`.
            data: Dados a cachear (tipicamente o resultado da transcrição).
            ttl_hours: TTL customizado para ESTA entrada, em horas. Quando
                omitido (None), cai no TTL padrão da instância (`self.ttl_hours`).

        Como funciona:
            1. Resolve o TTL efetivo: `ttl_hours or self.ttl_hours`. Cuidado com
               o número mágico — como `0` é falsy em Python, passar
               `ttl_hours=0` NÃO força expiração imediata; ele recai no default.
            2. Despejo LRU (antes de inserir): enquanto o cache estiver cheio
               (`len >= max_entries`) e houver itens rastreados, remove o item
               mais antigo. `_access_order.pop(0)` retira o início da fila (o
               menos recentemente usado) e a entrada correspondente é apagada de
               `_cache`. O laço é `while` (não `if`) para cobrir casos em que o
               cache esteja acima do teto por mais de uma unidade.
            3. Grava a nova entrada com três campos:
               - 'data': o payload do chamador;
               - 'expires': agora + TTL, o instante de vencimento usado em `get`
                 e `cleanup_expired`;
               - 'created': timestamp ISO-8601 do momento da gravação, usado
                 apenas para diagnóstico/estatística.
            4. Anexa a chave ao FIM de `_access_order`, marcando-a como a mais
               recentemente usada.

        Atenção:
            - Não há deduplicação de `_access_order` ao re-gravar uma chave já
              existente: um `set` repetido pode deixar a chave duplicada na
              lista de recência (o `get`, ao contrário, faz remove-e-reanexa).
              Isso é benigno para a correção, mas pode inflar levemente a lista.
            - O despejo ocorre ANTES da inserção, então logo após o `set` o
              tamanho pode atingir exatamente `max_entries`.
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
            Número de entradas que existiam antes da limpeza (quantas foram
            removidas).

        Como funciona:
            1. Captura o tamanho atual ANTES de esvaziar (para reportá-lo).
            2. Zera as duas estruturas em conjunto — `_cache` (armazenamento) e
               `_access_order` (recência LRU) — mantendo-as consistentes.
            3. Retorna a contagem capturada no passo 1.
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
            Número de entradas vencidas que foram removidas.

        Como funciona:
            1. Fixa um único "agora" (`now`) para comparar todas as entradas com
               o mesmo instante de referência, evitando inconsistências durante
               a varredura.
            2. Coleta primeiro as chaves vencidas (`expires < now`) numa lista
               separada — não se pode apagar do dicionário enquanto se itera
               sobre ele, então a coleta antecede a remoção.
            3. Remove cada chave vencida de `_cache` e, defensivamente, de
               `_access_order`.

        Diferença em relação a `get`:
            `get` expira de forma preguiçosa (apenas a chave consultada, no
            momento do acesso). `cleanup_expired` é a varredura proativa de TODAS
            as entradas — útil para ser chamada periodicamente (job/scheduler) e
            liberar memória de itens que ninguém mais consulta.
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
            Dicionário com métricas operacionais:
              - total_entries: quantas entradas há no `_cache` (inclui vencidas
                ainda não coletadas);
              - active_entries: quantas ainda estão dentro do TTL;
              - expired_entries: quantas já venceram mas continuam ocupando
                memória (aguardando `get` ou `cleanup_expired`);
              - max_entries / ttl_hours: a configuração de política vigente;
              - memory_usage_approx_kb: estimativa grosseira de memória (ver
                `_estimate_memory`).

        Como funciona:
            1. Fixa um `now` único como referência temporal.
            2. Conta as ativas comparando 'expires' >= now (note que aqui a
               fronteira é inclusiva: uma entrada que vence exatamente agora
               ainda conta como ativa).
            3. Deriva as expiradas por subtração (total - ativas), evitando uma
               segunda varredura.
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
        """Estima uso de memória em bytes.

        Returns:
            Tamanho aproximado, em bytes, da serialização JSON do cache; 0 caso
            a serialização falhe.

        Como funciona:
            1. Monta uma cópia rasa de cada entrada substituindo o campo
               'expires' (um datetime, que não é serializável em JSON) pela sua
               representação ISO-8601 via `isoformat()`.
            2. Serializa o dicionário inteiro com `json.dumps` e usa o
               comprimento da string resultante como proxy do consumo de
               memória.
            Trata-se de uma APROXIMAÇÃO grosseira: mede bytes de texto JSON, não
            o footprint real dos objetos Python em heap (que costuma ser maior).
            Serve apenas para dar ordem de grandeza nas estatísticas.
            3. Qualquer erro de serialização (ex.: um `data` não serializável) é
               capturado e convertido em 0, para que a coleta de estatísticas
               nunca quebre por causa de uma estimativa de memória.
        """
        try:
            return len(json.dumps({
                h: {**v, 'expires': v['expires'].isoformat()}
                for h, v in self._cache.items()
            }))
        except Exception:
            return 0


# Instância global do cache.
# Mantém um único TranscriptionCache compartilhado por todo o processo, para que
# o cache (in-memory) seja realmente reaproveitado entre chamadas. Começa como
# None e é criado sob demanda em `get_cache` (lazy initialization).
_cache_instance: Optional[TranscriptionCache] = None


def get_cache() -> TranscriptionCache:
    """Retorna instância singleton do cache.

    Returns:
        A instância única e compartilhada de `TranscriptionCache` do processo.

    Como funciona:
        1. Acessa a variável de módulo `_cache_instance` via `global`.
        2. Na primeira chamada (`is None`), instancia o cache com os parâmetros
           padrão (TTL=24h, max_entries=1000) e o memoriza.
        3. Nas chamadas seguintes, devolve a mesma instância — garantindo que
           todos os chamadores compartilhem o mesmo armazenamento.

    Atenção: não é thread-safe. Sob concorrência real (múltiplas threads na
    primeira chamada simultânea), duas instâncias poderiam ser criadas em corrida.
    Como o backend é in-memory e por processo, cada worker/processo terá seu
    próprio singleton — o cache não é compartilhado entre processos.
    """
    global _cache_instance
    if _cache_instance is None:
        _cache_instance = TranscriptionCache()
    return _cache_instance
