"""
SENTINEL Cortex - Media Processing Routes
Endpoints para processamento de mídia (áudio, vídeo, extração).
"""

import base64
import shutil
import tempfile
import os
from fastapi import APIRouter, UploadFile, File, HTTPException
from fastapi.responses import Response

from models.responses import (
    ProcessedAudioResponse,
    ProcessedVideoResponse,
    ExtractedAudioResponse,
    AnnotateImageRequest,
    AnnotateImageResponse,
)
from services.audio_processor import AudioProcessor
from services.video_processor import VideoProcessor
from services.quality_analyzer import QualityAnalyzer
from services.cache_service import get_cache
from core.config import logger

router = APIRouter()

# Instanciar serviços
audio_proc = AudioProcessor()
video_proc = VideoProcessor()
quality_analyzer = QualityAnalyzer()
cache = get_cache()


@router.post("/process/audio", response_model=ProcessedAudioResponse)
async def process_audio(file: UploadFile = File(...), use_cache: bool = True):
    """
    Processa áudio: normaliza volume, reduz ruído, converte para formato otimizado.
    
    - **file**: Arquivo de áudio (MP3, WAV, M4A, OGG, etc.)
    - **use_cache**: Se deve usar cache (default: True)
    
    Retorna:
    - Áudio processado em base64
    - Score de qualidade (0.0 a 1.0)
    - Notas sobre a qualidade
    """
    try:
        logger.info(f"[SENTINEL] Processando áudio: {file.filename} ({file.content_type})")
        
        # Criar arquivo temporário para o upload
        tmp_path = None
        try:
            suffix = os.path.splitext(file.filename)[1] if file.filename else ""
            with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
                shutil.copyfileobj(file.file, tmp)
                tmp_path = tmp.name
            
            # Verificar cache (usando tamanho do arquivo como proxy simples por enquanto, 
            # já que hash de arquivo grande é custoso)
            # TODO: Implementar hash eficiente de stream se necessário
            
            # Processar áudio passando o path
            processed, metadata = await audio_proc.process(
                None, # audio_bytes
                file.filename or "audio.mp3",
                file.content_type or "audio/mpeg",
                input_path=tmp_path
            )
            
            # Analisar qualidade do áudio processado
            quality = quality_analyzer.analyze_audio(processed)
            
            # Converter para base64
            processed_b64 = base64.b64encode(processed).decode('utf-8')
            
            logger.info(f"[SENTINEL] Áudio processado: score: {quality['score']}")
            
            return ProcessedAudioResponse(
                success=True,
                original_size_bytes=os.path.getsize(tmp_path),
                processed_size_bytes=len(processed),
                processed_file_base64=processed_b64,
                metadata=metadata,
                cached=False
            )
            
        finally:
            # Limpar arquivo temporário do upload
            if tmp_path and os.path.exists(tmp_path):
                try: os.unlink(tmp_path)
                except: pass
        
    except Exception as e:
        logger.error(f"[SENTINEL] Erro ao processar áudio: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))



@router.post("/process/merge-audio")
async def merge_audio(files: list[UploadFile] = File(...)):
    """
    Recebe múltiplos arquivos de áudio (parts) e os une em um único arquivo MP3 (otimizado).
    Útil para juntar gravações de ligações que caíram e voltaram.
    
    Retorna o áudio diretamente como binary stream (não base64) para evitar
    limites de tamanho de resposta do Cloud Run.
    """
    try:
        logger.info(f"[SENTINEL] Iniciando merge de {len(files)} arquivos")
        
        audio_files_data = []
        total_original_size = 0
        
        for file in files:
            content = await file.read()
            total_original_size += len(content)
            audio_files_data.append((content, file.content_type or ""))
            
        merged, metadata = await audio_proc.merge_audios(audio_files_data)
        
        logger.info(f"[SENTINEL] Merge concluído: {total_original_size / 1024:.0f} KB -> {len(merged) / 1024:.0f} KB")
        
        # Retorna binary direto (sem base64) para evitar "Response size was too large"
        return Response(
            content=merged, 
            media_type="audio/mpeg",
            headers={
                "Content-Disposition": "attachment; filename=merged_audio.mp3",
                "X-Original-Size": str(total_original_size),
                "X-Processed-Size": str(len(merged)),
                "X-Merged-Count": str(len(audio_files_data)),
            }
        )
        
    except Exception as e:
        logger.error(f"[SENTINEL] Erro ao fazer merge de áudios: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/process/video", response_model=ProcessedVideoResponse)

async def process_video(
    file: UploadFile = File(...),
    extract_keyframes: bool = True,
    max_keyframes: int = 10
):
    """
    Processa vídeo: extrai keyframes, analisa qualidade.
    
    - **file**: Arquivo de vídeo (MP4, AVI, MOV, etc.)
    - **extract_keyframes**: Se deve extrair frames-chave (default: True)
    - **max_keyframes**: Número máximo de keyframes (default: 10)
    
    Retorna:
    - Lista de keyframes em base64 (JPEG)
    """
    try:
        logger.info(f"[SENTINEL] Processando vídeo: {file.filename} ({file.content_type})")
        
        # TODO: Implementar streaming para vídeo também se video_proc suportar
        # Por enquanto mantendo read() pois video_proc.process espera bytes
        # Idealmente refatorar video_processor.py também
        content = await file.read()
        
        # Processar vídeo e extrair keyframes
        keyframes = await video_proc.process(
            content,
            file.filename or "video.mp4",
            extract_keyframes=extract_keyframes,
            max_keyframes=max_keyframes
        )
        
        logger.info(f"[SENTINEL] Vídeo processado: {len(keyframes)} keyframes")
        
        return ProcessedVideoResponse(
            success=True,
            original_size_bytes=len(content),
            keyframes_base64=keyframes,
            keyframes_count=len(keyframes)
        )
        
    except Exception as e:
        logger.error(f"[SENTINEL] Erro ao processar vídeo: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/extract/audio", response_model=ExtractedAudioResponse)
async def extract_audio_from_video(file: UploadFile = File(...)):
    """
    Extrai a trilha de áudio de um arquivo de vídeo.
    
    - **file**: Arquivo de vídeo (MP4, AVI, MOV, MKV, WEBM, etc.)
    
    Retorna:
    - Áudio extraído em base64 (MP3, 128kbps)
    - Metadados do vídeo original
    
    Uso típico: Auditoria de vídeos de atendimento onde apenas o áudio é necessário.
    """
    try:
        logger.info(f"[SENTINEL] Extraindo áudio de: {file.filename} ({file.content_type})")
        
        # TODO: Refatorar video_proc para aceitar path também para evitar OOM aqui
        content = await file.read()
        original_size = len(content)
        
        # Validar que é um vídeo ou arquivo de mídia suportado
        content_type = file.content_type or ""
        # Permitir video/* e também audio/mpeg que as vezes é detectado para .mpeg
        if not content_type.startswith("video/") and not file.filename.lower().endswith(
            ('.mp4', '.avi', '.mov', '.mkv', '.webm', '.flv', '.wmv', '.m4v', '.mpeg', '.mpg')
        ):
            raise HTTPException(
                status_code=400, 
                detail="Arquivo não é um vídeo válido. Formatos aceitos: MP4, AVI, MOV, MKV, WEBM, FLV, WMV, M4V, MPEG"
            )
        
        # Obter info do vídeo
        video_info = video_proc.get_video_info(content)
        duration = video_info.get("duration_seconds", 0)
        
        # Extrair áudio usando VideoProcessor
        audio_bytes = await video_proc.extract_audio(content, file.filename or "video.mp4")
        
        if audio_bytes is None:
            raise HTTPException(
                status_code=500,
                detail="Falha ao extrair áudio do vídeo. Verifique se o vídeo possui trilha de áudio."
            )
        
        # Converter para base64
        audio_b64 = base64.b64encode(audio_bytes).decode('utf-8')
        
        logger.info(f"[SENTINEL] Áudio extraído: {original_size} bytes (video) -> {len(audio_bytes)} bytes (audio)")
        
        return ExtractedAudioResponse(
            success=True,
            original_video_size_bytes=original_size,
            extracted_audio_size_bytes=len(audio_bytes),
            audio_file_base64=audio_b64,
            audio_format="wav",
            audio_bitrate="pcm_s16le",
            video_duration_seconds=duration,
            message=f"Áudio extraído com sucesso de '{file.filename}'"
        )
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[SENTINEL] Erro ao extrair áudio: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))

from fastapi.responses import Response

@router.post("/tools/convert-to-wav")
async def convert_to_wav_direct(file: UploadFile = File(...)):
    """
    Ferramenta de Debug: Converte qualquer áudio/vídeo para WAV e retorna o arquivo para download.
    """
    try:
        logger.info(f"Convertendo manualmente: {file.filename}")
        
        tmp_path = None
        try:
            suffix = os.path.splitext(file.filename)[1] if file.filename else ""
            with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
                shutil.copyfileobj(file.file, tmp)
                tmp_path = tmp.name

            # Processar
            processed, _ = await audio_proc.process(
                None, 
                file.filename, 
                file.content_type or "",
                input_path=tmp_path
            )
            
            # Retornar binário direto
            return Response(content=processed, media_type="audio/wav", headers={
                "Content-Disposition": f"attachment; filename={file.filename}.wav"
            })
        finally:
             if tmp_path and os.path.exists(tmp_path):
                try: os.unlink(tmp_path)
                except: pass
                
    except Exception as e:
        logger.error(f"Erro na conversão manual: {e}")
        raise HTTPException(status_code=500, detail=str(e))

@router.post("/process/annotate", response_model=AnnotateImageResponse)
async def annotate_image(payload: AnnotateImageRequest):
    """
    Desenha setas, retângulos ou círculos em uma imagem base64 e retorna a imagem anotada em base64.
    """
    try:
        if not video_proc._ensure_dependencies():
            raise HTTPException(status_code=500, detail="Dependências de processamento de imagem (OpenCV) não disponíveis.")

        cv2 = video_proc._cv2
        np = video_proc._numpy

        try:
            img_data = base64.b64decode(payload.image_base64)
            np_arr = np.frombuffer(img_data, np.uint8)
            image = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)
        except Exception as e:
            raise HTTPException(status_code=400, detail=f"Erro ao decodificar imagem base64: {str(e)}")

        if image is None:
            raise HTTPException(status_code=400, detail="Imagem inválida ou corrompida.")

        h, w, _ = image.shape

        for ann in payload.annotations:
            t = ann.type.lower()
            coords = ann.coordinates
            if len(coords) < 4:
                continue

            ymin, xmin, ymax, xmax = coords
            y1 = int(ymin * h / 1000.0)
            x1 = int(xmin * w / 1000.0)
            y2 = int(ymax * h / 1000.0)
            x2 = int(xmax * w / 1000.0)

            color = (0, 107, 255)
            thickness = max(2, int(min(w, h) / 300.0))

            if t in ["retangulo", "rectangle", "rect"]:
                cv2.rectangle(image, (x1, y1), (x2, y2), color, thickness)
            elif t in ["circulo", "circle", "circ"]:
                center_x = (x1 + x2) // 2
                center_y = (y1 + y2) // 2
                radius = int(np.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2) / 2.0)
                cv2.circle(image, (center_x, center_y), radius, color, thickness)
            elif t in ["seta", "arrow"]:
                cv2.arrowedLine(image, (x1, y1), (x2, y2), color, thickness, tipLength=0.2)

            label = ann.label
            if label:
                font = cv2.FONT_HERSHEY_SIMPLEX
                font_scale = max(0.4, min(w, h) / 1000.0)
                (text_w, text_h), _ = cv2.getTextSize(label, font, font_scale, 1)
                ly = max(text_h + 10, y1 - 5)
                lx = max(5, x1)

                cv2.rectangle(image, (lx, ly - text_h - 5), (lx + text_w + 5, ly + 5), (0, 0, 0), -1)
                cv2.putText(image, label, (lx + 2, ly - 2), font, font_scale, (255, 255, 255), 1, cv2.LINE_AA)

        success, buffer = cv2.imencode('.jpg', image, [cv2.IMWRITE_JPEG_QUALITY, 85])
        if not success:
            raise HTTPException(status_code=500, detail="Erro ao codificar imagem processada.")

        annotated_b64 = base64.b64encode(buffer).decode('utf-8')
        return AnnotateImageResponse(success=True, annotated_image_base64=annotated_b64)

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[SENTINEL] Erro ao anotar imagem: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))
