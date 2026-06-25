"""
VideoProcessor - Processamento de vídeo para análise forense.

Pipeline:
1. Extração de keyframes
2. Análise de qualidade de imagem
3. Detecção de cenas importantes
"""

import logging
import tempfile
import os
import base64
from typing import List, Optional

logger = logging.getLogger(__name__)


class VideoProcessor:
    """
    Processador de vídeo para extração de keyframes e análise.
    """
    
    def __init__(self, keyframe_quality: int = 85):
        """
        Inicializa o processador.
        
        Args:
            keyframe_quality: Qualidade JPEG dos keyframes (0-100, default: 85)
        """
        self.keyframe_quality = keyframe_quality
        self._cv2 = None
        self._numpy = None
    
    def _ensure_dependencies(self):
        """Carrega dependências sob demanda."""
        if self._cv2 is None:
            try:
                import cv2
                import numpy as np
                self._cv2 = cv2
                self._numpy = np
                logger.info("OpenCV carregado com sucesso")
            except ImportError:
                logger.warning("OpenCV não disponível - instale com: pip install opencv-python")
                return False
        return True
    
    async def process(
        self,
        video_bytes: bytes,
        filename: str,
        extract_keyframes: bool = True,
        max_keyframes: int = 10
    ) -> List[str]:
        """
        Processa vídeo e extrai keyframes.
        
        Args:
            video_bytes: Bytes do arquivo de vídeo
            filename: Nome do arquivo original
            extract_keyframes: Se deve extrair frames-chave (default: True)
            max_keyframes: Número máximo de keyframes (default: 10)
            
        Returns:
            Lista de keyframes em base64 (JPEG)
        """
        if not extract_keyframes:
            return []
        
        if not self._ensure_dependencies():
            logger.warning("Retornando lista vazia - OpenCV não disponível")
            return []
        
        cv2 = self._cv2
        np = self._numpy
        
        logger.info(f"Processando vídeo: {filename}, max_keyframes={max_keyframes}")
        
        # Salvar temporariamente
        tmp_path = None
        try:
            with tempfile.NamedTemporaryFile(suffix='.mp4', delete=False) as f:
                f.write(video_bytes)
                tmp_path = f.name
            
            cap = cv2.VideoCapture(tmp_path)
            
            if not cap.isOpened():
                logger.error(f"Não foi possível abrir o vídeo: {filename}")
                return []
            
            fps = cap.get(cv2.CAP_PROP_FPS)
            total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
            width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
            height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
            duration = total_frames / fps if fps > 0 else 0
            
            logger.info(f"Vídeo: {width}x{height}, {fps:.1f}fps, {duration:.1f}s, {total_frames} frames")
            
            # Calcular intervalo entre keyframes
            interval = max(1, total_frames // max_keyframes)
            
            keyframes = []
            frame_idx = 0
            
            while cap.isOpened() and len(keyframes) < max_keyframes:
                ret, frame = cap.read()
                if not ret:
                    break
                
                if frame_idx % interval == 0:
                    # Redimensionar se muito grande
                    if width > 1280:
                        scale = 1280 / width
                        new_width = 1280
                        new_height = int(height * scale)
                        frame = cv2.resize(frame, (new_width, new_height))
                    
                    # Converter para JPEG base64
                    success, buffer = cv2.imencode(
                        '.jpg', 
                        frame, 
                        [cv2.IMWRITE_JPEG_QUALITY, self.keyframe_quality]
                    )
                    
                    if success:
                        b64 = base64.b64encode(buffer).decode('utf-8')
                        keyframes.append(b64)
                        logger.debug(f"Keyframe {len(keyframes)} extraído do frame {frame_idx}")
                
                frame_idx += 1
            
            cap.release()
            
            logger.info(f"Extraídos {len(keyframes)} keyframes de {filename}")
            return keyframes
            
        except Exception as e:
            logger.error(f"Erro ao processar vídeo: {e}")
            return []
            
        finally:
            if tmp_path and os.path.exists(tmp_path):
                try:
                    os.unlink(tmp_path)
                except Exception:
                    pass
    
    async def extract_audio(self, video_bytes: bytes, filename: str) -> Optional[bytes]:
        """
        Extrai trilha de áudio do vídeo.
        
        Args:
            video_bytes: Bytes do vídeo
            filename: Nome do arquivo
            
        Returns:
            Bytes do áudio extraído ou None se falhar
        """
        try:
            from pydub import AudioSegment
            import io
            
            # Salvar temporariamente
            with tempfile.NamedTemporaryFile(suffix='.mp4', delete=False) as f:
                f.write(video_bytes)
                tmp_path = f.name
            
            try:
                # Extrair áudio usando pydub/ffmpeg
                audio = AudioSegment.from_file(tmp_path)
                
                # Exportar como WAV (mais seguro para Gemini e igual Oitiva)
                output = io.BytesIO()
                audio.export(output, format="wav")
                
                logger.info(f"Áudio extraído de {filename} (WAV): {len(output.getvalue())} bytes")
                return output.getvalue()
                
            finally:
                if os.path.exists(tmp_path):
                    os.unlink(tmp_path)
                
        except Exception as e:
            logger.error(f"Erro ao extrair áudio do vídeo: {e}")
            return None
    
    def get_video_info(self, video_bytes: bytes) -> dict:
        """
        Obtém informações básicas do vídeo.
        
        Args:
            video_bytes: Bytes do vídeo
            
        Returns:
            Dicionário com informações do vídeo
        """
        if not self._ensure_dependencies():
            return {"error": "OpenCV não disponível"}
        
        cv2 = self._cv2
        
        try:
            with tempfile.NamedTemporaryFile(suffix='.mp4', delete=False) as f:
                f.write(video_bytes)
                tmp_path = f.name
            
            try:
                cap = cv2.VideoCapture(tmp_path)
                
                info = {
                    "width": int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
                    "height": int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)),
                    "fps": cap.get(cv2.CAP_PROP_FPS),
                    "total_frames": int(cap.get(cv2.CAP_PROP_FRAME_COUNT)),
                    "duration_seconds": 0,
                    "codec": "",
                }
                
                if info["fps"] > 0:
                    info["duration_seconds"] = info["total_frames"] / info["fps"]
                
                fourcc = int(cap.get(cv2.CAP_PROP_FOURCC))
                info["codec"] = "".join([chr((fourcc >> 8 * i) & 0xFF) for i in range(4)])
                
                cap.release()
                return info
                
            finally:
                os.unlink(tmp_path)
                
        except Exception as e:
            logger.error(f"Erro ao obter info do vídeo: {e}")
            return {"error": str(e)}
