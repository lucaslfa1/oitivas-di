"""
SentimentAnalyzer - Análise acústica para estimar emoção/tom de voz.

Baseado em características físicas do áudio (energia, variação, silêncio).
Não usa modelos pesados de ML para manter performance.

Contexto (Sentinel / análise forense de sinistros):
    Este módulo é a base prosódica do Sentinel. A premissa é estimar o estado
    emocional de um locutor a partir de *traços físicos do sinal de áudio*, sem
    transcrição e sem inferência de linguagem natural. Por isso roda 100% local
    (NumPy puro), é determinístico e barato — não há chamada a serviço externo
    de IA. A análise semântica/textual fica a cargo de outras camadas (Azure);
    aqui tratamos apenas do "como" foi dito, não do "o que" foi dito.

Base prosódica (as 4 features extraídas do sinal):
    - RMS (Root Mean Square): energia média do sinal => proxy de volume/intensidade.
    - ZCR (Zero Crossing Rate): nº de vezes que a onda cruza o zero => proxy de
      conteúdo de alta frequência / "aspereza" / ruído / tensão na voz.
    - Silence ratio: fração de amostras quase-mudas => proxy de pausas/hesitação.
    - Energy variability: desvio-padrão da energia ao longo do tempo => proxy de
      oscilação de tom (fala constante vs. fala com picos/altos e baixos).

Importante: as classes emocionais retornadas são ESTIMATIVAS heurísticas, não
diagnóstico. Servem como indício/sinalização para revisão humana.
"""

import numpy as np
from pydub import AudioSegment
import logging

logger = logging.getLogger(__name__)


class SentimentAnalyzer:
    """
    Analisa características acústicas para estimar o tom de voz.

    A classe é stateless (não guarda estado entre chamadas): cada áudio é
    processado de forma independente em `analyze`, o que a torna segura para
    reúso e para execução concorrente. Toda a lógica de extração de features
    vive em `analyze`; a interpretação dessas features (rótulo emocional e
    texto explicativo) fica isolada em `_classify` e `_get_description`.
    """

    def analyze(self, audio: AudioSegment) -> dict:
        """
        Analisa o áudio e retorna métricas de sentimento/tom.

        Args:
            audio: AudioSegment (objeto pydub já carregado em memória; pode ser
                mono ou estéreo — os canais são tratados como uma sequência única
                de amostras intercaladas, o que é suficiente para métricas globais
                de energia/silêncio).

        Returns:
            Dicionário com:
              - "metrics": as 4 features prosódicas (rms_energy, zero_crossing_rate,
                silence_ratio, energy_variability), arredondadas a 4 casas.
              - "classification": rótulo emocional estimado (str).
              - "description": frase humana explicando o rótulo.
            Em caso de falha, retorna {"error": <mensagem>} em vez de propagar
            a exceção — assim uma análise problemática não derruba o pipeline maior.

        Como funciona:
            1. Converte as amostras do AudioSegment em um array NumPy float32.
            2. Normaliza para a faixa [-1, 1] dividindo pelo valor de fundo de escala
               do formato: 2^15 (32768) para 16 bits, 2^7 (128) para 8 bits. Isso
               torna os limiares de `_classify` independentes da profundidade de bits.
            3. Calcula as 4 features prosódicas (RMS, ZCR, silêncio, variabilidade).
            4. Delega a `_classify` a conversão das features em um rótulo emocional.
            5. Monta o dicionário de retorno, anexando a descrição via `_get_description`.
            Qualquer erro no caminho é capturado, logado e devolvido como {"error": ...}.
        """
        try:
            # Converter para array numpy.
            # get_array_of_samples() devolve as amostras PCM inteiras (ex.: int16);
            # promovemos a float32 para permitir operações vetoriais e evitar
            # overflow/truncamento ao elevar ao quadrado mais adiante.
            samples = np.array(audio.get_array_of_samples()).astype(np.float32)

            # Normalizar para a faixa [-1, 1].
            # O fundo de escala depende da profundidade de bits: áudio de 16 bits
            # (sample_width == 2) varia em [-32768, 32767] -> divide por 2^15;
            # caso contrário assume 8 bits -> divide por 2^7 (128). Normalizar aqui
            # é o que permite usar limiares fixos (0.15, 0.2, ...) em `_classify`
            # sem depender do formato de origem do arquivo.
            max_val = 2**15 if audio.sample_width == 2 else 2**7
            samples = samples / max_val

            # 1. Energia (RMS) - Intensidade/Volume.
            # Raiz da média dos quadrados: estima a amplitude "típica" do sinal.
            # Em [-1,1], RMS alto = voz alta/forte; RMS baixo = voz baixa/sussurrada.
            rms = np.sqrt(np.mean(samples**2))

            # 2. Zero Crossing Rate - "Aspereza" / Agitação.
            # Conta quantos pares de amostras consecutivas têm sinais opostos
            # (produto < 0 => cruzou o zero), normalizado pelo total de amostras.
            # Muitos cruzamentos => energia em alta frequência (ruído, voz tensa,
            # consoantes fricativas); poucos => sinal mais grave/suave.
            zcr = ((samples[:-1] * samples[1:]) < 0).sum() / len(samples)

            # 3. Taxa de Silêncio.
            # Considera "silêncio" toda amostra com amplitude < 0.01 (1% do fundo
            # de escala), tolerância que absorve ruído de fundo baixo. A fração de
            # amostras silenciosas indica volume de pausas: muita pausa pode sinalizar
            # hesitação/insegurança.
            silence_mask = np.abs(samples) < 0.01
            silence_ratio = np.sum(silence_mask) / len(samples)

            # 4. Variabilidade de Energia (Dinâmica).
            # Mede o quanto o volume oscila ao longo do tempo. O sinal é fatiado em
            # janelas de 100 ms (chunk_size = frame_rate * 0.1 amostras); calcula-se
            # o RMS de cada janela e o desvio-padrão desses RMS. Desvio alto = tom
            # com picos e quedas (fala agitada); desvio baixo = fala uniforme.
            chunk_size = int(audio.frame_rate * 0.1)
            num_chunks = len(samples) // chunk_size
            if num_chunks > 0:
                # Descarta a "sobra" final (< 100 ms) com o fatiamento [:num_chunks*chunk_size]
                # para que todas as janelas tenham o mesmo tamanho antes do split.
                chunks = np.array_split(samples[:num_chunks*chunk_size], num_chunks)
                chunk_rms = [np.sqrt(np.mean(c**2)) for c in chunks]
                energy_variability = np.std(chunk_rms)
            else:
                # Áudio mais curto que uma janela de 100 ms: não há como medir
                # variação temporal, então assume-se 0 (sem dinâmica detectável).
                energy_variability = 0

            # Classificação Heurística.
            # Converte as 4 features numéricas em um único rótulo emocional.
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
            # Falha de análise não deve quebrar o pipeline: loga e devolve um
            # dicionário de erro que o chamador pode inspecionar/ignorar.
            logger.error(f"Erro na análise de sentimento: {e}")
            return {"error": str(e)}

    def _classify(self, rms, zcr, silence, variability) -> str:
        """Classifica o tom de voz a partir das 4 features prosódicas.

        Args:
            rms: energia média do sinal normalizado (volume); faixa típica ~[0, 0.5].
            zcr: zero crossing rate (aspereza / alta frequência); faixa ~[0, 0.5].
            silence: fração de amostras quase-mudas (pausas); faixa [0, 1].
            variability: desvio-padrão do RMS por janela de 100 ms (dinâmica).

        Returns:
            Um rótulo emocional estimado: "hesitante", "agitado", "agressivo",
            "tenso", "calmo" ou "neutro" (fallback).

        Como funciona:
            Cascata de regras heurísticas avaliadas em ordem de prioridade — o
            PRIMEIRO limiar satisfeito vence e retorna imediatamente, então a
            ordem importa (regras mais "fortes"/específicas vêm antes). Os números
            são limiares calibrados sobre o sinal normalizado em [-1, 1]; cada um
            traduz uma intuição prosódica:

              1. silence > 0.6  -> "hesitante": mais de 60% do áudio é pausa/silêncio,
                 indício de fala entrecortada, insegurança ou busca por respostas.
              2. rms > 0.15 E variability > 0.05 -> "agitado": volume alto E oscilando
                 bastante entre janelas — picos e quedas típicos de nervosismo/excitação.
                 Exige as duas condições para distinguir de um volume alto porém estável.
              3. rms > 0.2 -> "agressivo": volume muito alto e (já não sendo "agitado")
                 relativamente constante — padrão de imposição/raiva sustentada.
              4. zcr > 0.1 -> "tenso": muita energia de alta frequência / "aspereza"
                 na voz, associada a estresse, mesmo sem volume elevado.
              5. rms < 0.05 -> "calmo": volume bem baixo e controlado, voz tranquila.
              6. fallback -> "neutro": nenhum extremo disparado; fala dentro do padrão.

            Observação sobre a ordem: "agitado" (regra 2) é testado antes de
            "agressivo" (regra 3) de propósito — um áudio alto E variável é
            classificado como agitado; só cai em agressivo se for alto sem a
            variabilidade que caracteriza o agitado.
        """

        # Heurísticas baseadas em estudos de prosódia.
        # (Limiares empíricos; ver docstring para o significado de cada valor.)

        if silence > 0.6:
            return "hesitante"  # >60% de pausa/silêncio => fala entrecortada

        if rms > 0.15 and variability > 0.05:
            return "agitado"   # Volume alto E oscilando muito entre janelas

        if rms > 0.2:
            return "agressivo" # Volume muito alto e relativamente constante

        if zcr > 0.1:
            return "tenso"     # Muita "aspereza" (energia de alta frequência/ruído)

        if rms < 0.05:
            return "calmo"     # Volume bem baixo e controlado

        return "neutro"

    def _get_description(self, classification: str) -> str:
        """Traduz o rótulo de `_classify` em uma frase explicativa legível.

        Args:
            classification: rótulo emocional produzido por `_classify`.

        Returns:
            Texto descritivo, voltado ao analista humano, ligando a feature
            prosódica dominante à hipótese de estado emocional. Para rótulos
            desconhecidos retorna "Padrão indefinido." (fallback defensivo, caso
            o conjunto de rótulos de `_classify` mude e este mapa fique defasado).

        Como funciona:
            Simples look-up em um dicionário fixo (rótulo -> frase). Mantém o texto
            de apresentação separado da lógica de classificação, para que ajustes
            de redação não toquem nos limiares de `_classify`.
        """
        mapa = {
            "hesitante": "Muitas pausas, possível insegurança ou busca por respostas.",
            "agitado": "Variação brusca de tom, possível nervosismo ou excitação.",
            "agressivo": "Volume elevado e constante, possível raiva ou imposição.",
            "tenso": "Voz tesa ou com ruído, possível estresse.",
            "calmo": "Volume baixo e controlado, aparente tranquilidade.",
            "neutro": "Padrão de fala normal sem desvios significativos."
        }
        return mapa.get(classification, "Padrão indefinido.")
