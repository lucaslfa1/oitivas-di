"""
SentimentAnalyzer - Análise acústica para estimar emoção/tom de voz.

Baseado em características físicas do áudio (energia, variação, silêncio).
Não usa modelos pesados de ML para manter performance.
"""

import numpy as np
from pydub import AudioSegment
import logging

logger = logging.getLogger(__name__)


class SentimentAnalyzer:
    """
    Analisa características acústicas para estimar o tom de voz.
    """
    
    def analyze(self, audio: AudioSegment) -> dict:
        """
        Analisa o áudio e retorna métricas de sentimento/tom.
        
        Args:
            audio: AudioSegment
            
        Returns:
            Dicionário com métricas e classificação estimada.
        """
        try:
            # Converter para array numpy
            samples = np.array(audio.get_array_of_samples()).astype(np.float32)
            
            # Normalizar
            max_val = 2**15 if audio.sample_width == 2 else 2**7
            samples = samples / max_val
            
            # 1. Energia (RMS) - Intensidade/Volume
            rms = np.sqrt(np.mean(samples**2))
            
            # 2. Zero Crossing Rate - "Aspereza" / Agitação
            zcr = ((samples[:-1] * samples[1:]) < 0).sum() / len(samples)
            
            # 3. Taxa de Silêncio
            # Considera silêncio amplitude < 1%
            silence_mask = np.abs(samples) < 0.01
            silence_ratio = np.sum(silence_mask) / len(samples)
            
            # 4. Variabilidade de Energia (Dinâmica)
            # Divide em chunks de 100ms
            chunk_size = int(audio.frame_rate * 0.1)
            num_chunks = len(samples) // chunk_size
            if num_chunks > 0:
                chunks = np.array_split(samples[:num_chunks*chunk_size], num_chunks)
                chunk_rms = [np.sqrt(np.mean(c**2)) for c in chunks]
                energy_variability = np.std(chunk_rms)
            else:
                energy_variability = 0
            
            # Classificação Heurística
            classification = self._classify(rms, zcr, silence_ratio, energy_variability)
            
            return {
                "metrics": {
                    "rms_energy": round(float(rms), 4),
                    "zero_crossing_rate": round(float(zcr), 4),
                    "silence_ratio": round(float(silence_ratio), 4),
                    "energy_variability": round(float(energy_variability), 4)
                },
                "classification": classification,
                "description": self._get_description(classification)
            }
            
        except Exception as e:
            logger.error(f"Erro na análise de sentimento: {e}")
            return {"error": str(e)}
    
    def _classify(self, rms, zcr, silence, variability) -> str:
        """Classifica o tom de voz baseado nas métricas."""
        
        # Heurísticas baseadas em estudos de prosódia
        
        if silence > 0.6:
            return "hesitante"  # Muito silêncio
            
        if rms > 0.15 and variability > 0.05:
            return "agitado"   # Alto e variável
            
        if rms > 0.2:
            return "agressivo" # Muito alto constante
            
        if zcr > 0.1:
            return "tenso"     # Muita "aspereza" (frequência alta/ruído)
            
        if rms < 0.05:
            return "calmo"     # Baixo volume
            
        return "neutro"

    def _get_description(self, classification: str) -> str:
        mapa = {
            "hesitante": "Muitas pausas, possível insegurança ou busca por respostas.",
            "agitado": "Variação brusca de tom, possível nervosismo ou excitação.",
            "agressivo": "Volume elevado e constante, possível raiva ou imposição.",
            "tenso": "Voz tesa ou com ruído, possível estresse.",
            "calmo": "Volume baixo e controlado, aparente tranquilidade.",
            "neutro": "Padrão de fala normal sem desvios significativos."
        }
        return mapa.get(classification, "Padrão indefinido.")
