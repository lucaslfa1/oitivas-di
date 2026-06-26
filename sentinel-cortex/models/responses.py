"""
SENTINEL Cortex - Response Models
Todos os modelos de resposta da API.
"""

from pydantic import BaseModel


class ProcessedAudioResponse(BaseModel):
    """Resposta para processamento de áudio."""
    success: bool
    original_size_bytes: int
    processed_size_bytes: int
    processed_file_base64: str | None = None
    metadata: dict | None = None
    cached: bool = False


class ProcessedVideoResponse(BaseModel):
    """Resposta para processamento de vídeo."""
    success: bool
    original_size_bytes: int
    keyframes_base64: list[str] | None = None
    keyframes_count: int = 0


class ExtractedAudioResponse(BaseModel):
    """Resposta para extração de áudio de vídeo."""
    success: bool
    original_video_size_bytes: int
    extracted_audio_size_bytes: int
    audio_file_base64: str | None = None
    audio_format: str = "mp3"
    audio_bitrate: str = "128k"
    video_duration_seconds: float | None = None
    message: str | None = None


class CacheStatsResponse(BaseModel):
    """Estatísticas do cache."""
    total_entries: int
    active_entries: int
    expired_entries: int
    max_entries: int
    ttl_hours: int
    memory_usage_approx_kb: int


class SentimentResponse(BaseModel):
    """Resposta da análise de sentimento."""
    success: bool
    classification: str
    description: str
    metrics: dict


class AnnotationItem(BaseModel):
    """Item de anotação visual."""
    type: str
    coordinates: list[int]
    label: str | None = None


class AnnotateImageRequest(BaseModel):
    """Requisição para anotação de imagem."""
    image_base64: str
    annotations: list[AnnotationItem]


class AnnotateImageResponse(BaseModel):
    """Resposta com imagem anotada."""
    success: bool
    annotated_image_base64: str | None = None
