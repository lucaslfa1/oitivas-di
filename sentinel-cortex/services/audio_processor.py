from pydub import AudioSegment
from pydub.effects import normalize, compress_dynamic_range
import noisereduce as nr
import numpy as np
import io
import tempfile
import os
import logging

logger = logging.getLogger(__name__)

class AudioProcessor:
    def __init__(self, target_dbfs: float = -16.0):
        self.target_dbfs = target_dbfs
    
    async def process(self, audio_bytes: bytes | None, filename: str, mime_type: str, input_path: str | None = None) -> tuple[bytes, dict]:
        logger.info(f"Processando áudio: {filename} ({mime_type})")
        
        # 1. CARREGAR (Lógica flexível para aceitar MPEG/MP3/WAV)
        # Se input_path for fornecido, audio_bytes pode ser None
        audio = self._load_audio(audio_bytes, filename, mime_type, input_path)
        
        # 2. TRATAR (Pipeline Otimizado para Voz)
        if audio.channels > 1: audio = audio.set_channels(1)
        
        # A. Reduzir ruído PRIMEIRO (ajuda a identificar o silêncio real)
        # Desabilitado: Redução de ruído clássica degrada o sinal para IA (Azure e Whisper)
        # audio = self._reduce_noise(audio)
        
        # B. Trim Inteligente (Corta até achar voz)
        # Desabilitado na Fase 8: Cortar silêncio inicial destrói o alinhamento de 
        # timestamps retornado pela Azure Cloud (causando dessincronização da legenda no Frontend)
        
        # C. Normalizar volume final
        audio = normalize(audio, headroom=abs(self.target_dbfs))
        
        # 3. EXPORTAR COMO MP3 (Aumentado para 128k para não perder fonemas de alta frequência)
        output = io.BytesIO()
        audio.export(output, format="mp3", bitrate="128k")
        processed_bytes = output.getvalue()
        
        metadata = {
            "format": "mp3",
            "original_duration": (len(audio_bytes) if audio_bytes else os.path.getsize(input_path) if input_path else 0)/1000, # Estimado
            "processed_duration": len(audio)/1000,
            "start_trim_seconds": start_trim/1000
        }
        return processed_bytes, metadata

    async def merge_audios(self, audio_files_data: list[tuple[bytes, str]]) -> tuple[bytes, dict]:
        """
        Recebe uma lista de tuplas (bytes, mime_type) e retorna um único WAV concatenado.
        """
        if not audio_files_data:
            raise ValueError("Nenhum áudio fornecido para merge")

        logger.info(f"Iniciando merge de {len(audio_files_data)} arquivos")
        
        combined_audio: AudioSegment = None
        
        # Processar cada arquivo
        for i, (data, mime) in enumerate(audio_files_data):
            try:
                # Carregar usando o método interno (que já lida com formatos variados)
                # Passamos um nome fictício baseada no índice para ajudar na detecção de extensão se mime falhar
                dummy_ext = self._get_ext_from_mime(mime)
                dummy_name = f"part_{i}{dummy_ext}"
                
                logger.info(f"Carregando parte {i+1}/{len(audio_files_data)} ({len(data)} bytes, {mime})")
                
                segment = self._load_audio(data, dummy_name, mime)
                
                # Normalizar canais para mono (para garantir compatibilidade na junção)
                if segment.channels > 1:
                    segment = segment.set_channels(1)
                
                # Normalizar taxa de amostragem (opcional, mas recomendado para evitar glitches)
                segment = segment.set_frame_rate(24000) # Padronizando para 24kHz que é bom para voz
                
                if combined_audio is None:
                    combined_audio = segment
                else:
                    combined_audio += segment
                    
            except Exception as e:
                logger.warning(f"Falha ao processar parte {i} ({mime}): {e}")
                # Continua para tentar salvar o que der, ou lança erro? 
                # Decisão: Ignorar arquivo corrompido e tentar juntar os outros
                continue
        
        if combined_audio is None:
            raise ValueError("Não foi possível processar nenhum dos áudios fornecidos")

        # Exportar
        output = io.BytesIO()
        combined_audio.export(output, format="mp3", bitrate="128k")
        merged_bytes = output.getvalue()
        
        metadata = {
            "format": "mp3",
            "merged_count": len(audio_files_data),
            "total_duration_seconds": len(combined_audio) / 1000.0
        }
        
        return merged_bytes, metadata
    

    def _get_ext_from_mime(self, mime_type: str) -> str:
            format_map = {
                "audio/mpeg": ".mp3", "video/mpeg": ".mpeg", "audio/wav": ".wav",
                "audio/mp3": ".mp3", "audio/ogg": ".ogg", "audio/m4a": ".m4a",
                "audio/x-m4a": ".m4a", "audio/mp4": ".m4a"
            }
            return format_map.get(mime_type, ".mp3")

    def _load_audio(self, audio_bytes: bytes | None, filename: str, mime_type: str, input_path: str | None = None) -> AudioSegment:
        # 1. Tentar usar a extensão original do arquivo (Mais seguro para ffmpeg)
        ext = os.path.splitext(filename)[1].lower() if filename else ""
        
        # 2. Se não tiver extensão, tentar deduzir pelo mime_type
        if not ext or len(ext) < 2:
            ext = self._get_ext_from_mime(mime_type)

        # Se já temos um caminho de arquivo (do upload streamado), usamos ele direto
        if input_path and os.path.exists(input_path):
            logger.info(f"Carregando direto do disco: {input_path}")
            try:
                # Carrega direto do path fornecido
                audio = AudioSegment.from_file(input_path)
                
                # Força limpeza interna e conversão para WAV na memória
                buffer = io.BytesIO()
                audio.export(buffer, format="wav")
                buffer.seek(0)
                return AudioSegment.from_file(buffer, format="wav")
            except Exception as e:
                logger.error(f"Erro ao carregar áudio do path {input_path}: {e}")
                raise

        # Fallback: Cria temporário a partir dos bytes (comportamento antigo)
        if audio_bytes is None:
            raise ValueError("Nem input_path nem audio_bytes foram fornecidos")

        logger.info(f"Salvando temporário como: temp{ext}")

        tmp_path = None
        try:
            with tempfile.NamedTemporaryFile(suffix=ext, delete=False) as tmp:
                tmp.write(audio_bytes)
                tmp_path = tmp.name
            
            # Deixa o ffmpeg/pydub detectar o formato automaticamente
            audio = AudioSegment.from_file(tmp_path)
            
            # Força limpeza interna e conversão para WAV na memória
            buffer = io.BytesIO()
            audio.export(buffer, format="wav")
            buffer.seek(0)
            return AudioSegment.from_file(buffer, format="wav")
            
        except Exception as e:
            logger.error(f"Erro ao carregar áudio (ext={ext}): {e}")
            raise
            
        finally:
            if tmp_path and os.path.exists(tmp_path):
                try: os.unlink(tmp_path) 
                except: pass

    def _detect_leading_silence(self, sound, silence_threshold=-40.0, chunk_size=10):
        """
        Detecta quantos ms de silêncio existem no início do áudio.
        Itera em chunks pequenos até achar algo mais alto que o threshold.
        """
        trim_ms = 0
        while trim_ms < len(sound) and sound[trim_ms:trim_ms+chunk_size].dBFS < silence_threshold:
            trim_ms += chunk_size
        return trim_ms

    def _reduce_noise(self, audio):
        try:
            # Converte para array numpy para processamento
            samples = np.array(audio.get_array_of_samples())
            
            # Redução de ruído estacionário mais suave para não abafar a voz
            reduced_noise = nr.reduce_noise(
                y=samples, 
                sr=audio.frame_rate,
                prop_decrease=0.40, # Preserva mais as altas frequências da voz humana
                stationary=True
            )
            
            # Reconstrói áudio
            return audio._spawn(reduced_noise.tobytes())
        except Exception as e:
            logger.warning(f"Erro no reduce_noise: {e}")
            return audio
