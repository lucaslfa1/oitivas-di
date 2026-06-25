"""
SENTINEL Cortex - Intelligence Routes
Endpoints para análise cognitiva e inteligência.
"""

from fastapi import APIRouter, UploadFile, File, HTTPException

from models.responses import SentimentResponse
from services.audio_processor import AudioProcessor
from services.sentiment_analyzer import SentimentAnalyzer
from core.config import logger

router = APIRouter()

# Instanciar serviços
audio_proc = AudioProcessor()
sentiment_analyzer = SentimentAnalyzer()


@router.post("/analyze/sentiment", response_model=SentimentResponse)
async def analyze_sentiment(file: UploadFile = File(...)):
    """
    Analisa tom de voz/sentimento do áudio.
    
    Baseado em métricas acústicas (energia, variação, silêncio).
    Retorna classificação: calmo, agitado, agressivo, tenso, hesitante, neutro.
    """
    try:
        logger.info(f"[SENTINEL] Analisando sentimento: {file.filename}")
        content = await file.read()
        
        # Carregar áudio usando helper do audio_proc
        audio = audio_proc._load_audio(
            content, 
            file.filename or "audio.mp3", 
            file.content_type or "audio/mpeg"
        )
        
        result = sentiment_analyzer.analyze(audio)
        
        if "error" in result:
            raise Exception(result["error"])
            
        return SentimentResponse(
            success=True,
            classification=result["classification"],
            description=result["description"],
            metrics=result["metrics"]
        )
        
    except Exception as e:
        logger.error(f"[SENTINEL] Erro na análise de sentimento: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))
