"""
QualityAnalyzer - Análise de qualidade de áudio e vídeo.

Fornece métricas e score de confiança para laudos.
"""

from pydub import AudioSegment
import numpy as np
import tempfile
import os
import logging

logger = logging.getLogger(__name__)


class QualityAnalyzer:
    """
    Analisa qualidade de áudio/vídeo e fornece score de confiança.
    
    Métricas de Áudio:
    - Volume médio (dBFS)
    - Proporção de silêncio
    - Detecção de clipping/distorção
    - Duração
    """
    
    def analyze_audio(self, audio_bytes: bytes) -> dict:
        """
        Analisa qualidade do áudio e retorna score + notas.
        
        Args:
            audio_bytes: Bytes do arquivo de áudio
            
        Returns:
            Dicionário com 'score' (0.0 a 1.0) e 'notes' (lista de strings)
        """
        notes = []
        score = 1.0
        
        try:
            # Carregar áudio - precisamos fechar o arquivo antes de usar pydub no Windows
            tmp_path = None
            try:
                with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as tmp:
                    tmp.write(audio_bytes)
                    tmp_path = tmp.name
                # Arquivo fechado aqui, agora pydub pode abrir
                audio = AudioSegment.from_file(tmp_path)
            finally:
                if tmp_path and os.path.exists(tmp_path):
                    try:
                        os.unlink(tmp_path)
                    except Exception:
                        pass  # Ignorar erro de deleção no Windows
            
            # 1. Verificar volume médio (dBFS)
            if audio.dBFS < -40:
                notes.append("⚠️ Volume muito baixo - pode haver perda de fala")
                score -= 0.25
            elif audio.dBFS < -30:
                notes.append("⚠️ Volume abaixo do ideal - recomenda-se falar mais perto do microfone")
                score -= 0.15
            elif audio.dBFS < -20:
                notes.append("ℹ️ Volume ligeiramente baixo")
                score -= 0.05
            
            # 2. Verificar duração
            duration_min = len(audio) / 1000 / 60
            if duration_min > 120:
                notes.append(f"⏱️ Áudio muito longo ({duration_min:.0f} min) - transcrição pode demorar significativamente")
            elif duration_min > 60:
                notes.append(f"⏱️ Áudio longo ({duration_min:.0f} min) - transcrição pode demorar")
            elif duration_min < 0.5:
                notes.append("⚠️ Áudio muito curto - pode não conter conteúdo suficiente")
                score -= 0.1
            
            # 3. Verificar silêncio excessivo
            silent_ratio = self._detect_silence_ratio(audio)
            if silent_ratio > 0.7:
                notes.append(f"⚠️ {silent_ratio*100:.0f}% de silêncio - verifique se há conteúdo")
                score -= 0.25
            elif silent_ratio > 0.5:
                notes.append(f"ℹ️ {silent_ratio*100:.0f}% de silêncio detectado")
                score -= 0.1
            
            # 4. Verificar clipping (distorção)
            if audio.max_dBFS > -0.5:
                notes.append("⚠️ Possível distorção por volume excessivo (clipping)")
                score -= 0.15
            elif audio.max_dBFS > -1.0:
                notes.append("ℹ️ Picos de volume próximos ao limite")
                score -= 0.05
            
            # 5. Verificar taxa de amostragem
            if audio.frame_rate < 16000:
                notes.append("⚠️ Taxa de amostragem baixa - qualidade de transcrição pode ser afetada")
                score -= 0.1
            
            # Mensagem positiva se tudo OK
            if not notes:
                notes.append("✅ Qualidade de áudio adequada para transcrição")
            elif score >= 0.8:
                notes.insert(0, "✅ Qualidade geral boa")
            
            return {
                "score": max(0.0, min(1.0, score)),
                "notes": notes,
                "details": {
                    "duration_seconds": round(len(audio) / 1000, 2),
                    "average_dbfs": round(audio.dBFS, 2),
                    "max_dbfs": round(audio.max_dBFS, 2),
                    "sample_rate": audio.frame_rate,
                    "channels": audio.channels,
                    "silence_ratio": round(silent_ratio, 2)
                }
            }
            
        except Exception as e:
            logger.error(f"Erro ao analisar qualidade de áudio: {e}")
            return {
                "score": 0.5,
                "notes": [f"⚠️ Não foi possível analisar qualidade completamente: {str(e)}"]
            }
    
    def analyze_video(self, video_bytes: bytes) -> dict:
        """
        Analisa qualidade do vídeo (placeholder - foco é áudio por enquanto).
        """
        return {
            "score": 0.8,
            "notes": ["ℹ️ Análise de vídeo disponível em versão futura"]
        }
    
    def _detect_silence_ratio(self, audio: AudioSegment) -> float:
        """
        Detecta proporção de silêncio no áudio.
        
        Args:
            audio: AudioSegment para analisar
            
        Returns:
            Float de 0.0 a 1.0 representando proporção de silêncio
        """
        # Dividir em chunks de 500ms
        chunk_ms = 500
        silent_chunks = 0
        total_chunks = len(audio) // chunk_ms
        
        if total_chunks == 0:
            return 0.0
        
        silence_threshold = -40  # dBFS
        
        for i in range(total_chunks):
            chunk = audio[i * chunk_ms:(i + 1) * chunk_ms]
            if chunk.dBFS < silence_threshold:
                silent_chunks += 1
        
        return silent_chunks / total_chunks
