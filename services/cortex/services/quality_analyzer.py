"""
QualityAnalyzer - Análise de qualidade de áudio e vídeo.

Fornece métricas e score de confiança para laudos.

Este módulo roda 100% local (offline), apoiado apenas em pydub/numpy. Não há
chamada a serviço externo: o objetivo é dar um "selo de qualidade" ao material
ANTES de enviá-lo para a etapa de transcrição/análise forense (Azure), evitando
gastar processamento em áudios inaudíveis, curtos demais ou distorcidos.

Como funciona (visão geral do pipeline):
    1. Decodifica os bytes recebidos para um objeto `AudioSegment` (pydub).
    2. Extrai métricas acústicas objetivas (volume médio, picos, silêncio,
       duração, taxa de amostragem).
    3. Compara cada métrica contra limiares heurísticos calibrados para fala
       humana captada por microfone comum.
    4. Converte os desvios em penalidades sobre um `score` que começa em 1.0
       (perfeito) e nunca fica negativo, acumulando "notas" legíveis para o laudo.
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

    Sobre a unidade dBFS (decibéis relativos a full scale):
        Toda métrica de volume usa dBFS, escala em que 0 dB é o teto absoluto do
        formato digital (não dá para ficar mais alto sem cortar a onda). Por isso
        os valores de fala são sempre NEGATIVOS: quanto mais negativo, mais baixo
        o som. Ex.: -3 dBFS é alto; -40 dBFS é praticamente silêncio. Os limiares
        nos métodos abaixo são interpretados sempre nessa escala.

    A classe é stateless (não guarda estado entre chamadas), então uma única
    instância pode ser reutilizada com segurança para analisar vários arquivos.
    """
    
    def analyze_audio(self, audio_bytes: bytes) -> dict:
        """
        Analisa qualidade do áudio e retorna score + notas.

        Args:
            audio_bytes: Bytes do arquivo de áudio (qualquer formato que o
                ffmpeg/pydub consiga decodificar; o sufixo .mp3 do arquivo
                temporário é só uma dica, não força o formato real).

        Returns:
            Dicionário com:
              - 'score' (float 0.0 a 1.0): confiança na qualidade; 1.0 = ideal.
              - 'notes' (list[str]): mensagens com emoji para o laudo (⚠️/ℹ️/✅).
              - 'details' (dict, só no caminho de sucesso): métricas brutas já
                arredondadas, para auditoria e exibição.
            Em caso de falha de decodificação, retorna score neutro 0.5 e uma
            nota de erro (sem a chave 'details').

        Como funciona:
            1. Persistência temporária: pydub/ffmpeg precisa de um caminho de
               arquivo, então os bytes são gravados em um NamedTemporaryFile.
               No Windows um arquivo aberto fica travado (lock), por isso o
               `with` é FECHADO antes de chamar `AudioSegment.from_file`; o
               `finally` apaga o temporário sempre, engolindo erro de unlink
               (no Windows a deleção pode falhar se o ffmpeg ainda segura o
               handle por um instante — não é fatal para a análise).
            2. Cinco verificações independentes (volume, duração, silêncio,
               clipping, taxa de amostragem). Cada uma adiciona nota(s) e, quando
               há problema, SUBTRAI uma penalidade do `score`. As penalidades são
               cumulativas e propositalmente "soft" (um único defeito não zera a
               nota; vários defeitos juntos é que derrubam a confiança).
            3. Pós-processamento das notas: se nada foi sinalizado, emite mensagem
               de aprovação; se houve só ressalvas leves (score >= 0.8), insere um
               selo "boa" no topo para contextualizar o laudo.
            4. Clamp final: o score é grampeado em [0.0, 1.0] com
               `max(0.0, min(1.0, score))` para nunca extrapolar mesmo que a soma
               das penalidades passe de 1.0.
        """
        notes = []
        # Começa "perfeito" (1.0) e só perde pontos conforme detecta defeitos.
        score = 1.0
        
        try:
            # Carregar áudio - precisamos fechar o arquivo antes de usar pydub no Windows.
            # delete=False: não deixamos o context manager apagar; controlamos a
            # remoção manualmente no finally para garantir que o caminho exista
            # enquanto o pydub ainda não terminou de ler.
            tmp_path = None
            try:
                with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as tmp:
                    tmp.write(audio_bytes)
                    tmp_path = tmp.name
                # Arquivo fechado aqui (saída do `with` libera o lock do Windows),
                # agora pydub pode abrir o caminho sem conflito de handle.
                audio = AudioSegment.from_file(tmp_path)
            finally:
                # Limpeza garantida do temporário mesmo se from_file lançar exceção.
                if tmp_path and os.path.exists(tmp_path):
                    try:
                        os.unlink(tmp_path)
                    except Exception:
                        # No Windows o ffmpeg pode ainda segurar o handle por um
                        # instante; falhar ao deletar não invalida a análise.
                        pass  # Ignorar erro de deleção no Windows
            
            # 1. Verificar volume médio (dBFS).
            # `audio.dBFS` é o nível RMS médio do clipe inteiro. Para fala captada
            # de perto o ideal fica em torno de -16 a -20 dBFS. A escada de limiares
            # vai do pior ao menos grave (if/elif para classificar em UMA faixa só):
            #   < -40 dBFS: quase inaudível, alto risco de perder palavras  -> -0.25
            #   < -30 dBFS: baixo, pede aproximar o microfone               -> -0.15
            #   < -20 dBFS: levemente baixo, ressalva informativa            -> -0.05
            if audio.dBFS < -40:
                notes.append("⚠️ Volume muito baixo - pode haver perda de fala")
                score -= 0.25
            elif audio.dBFS < -30:
                notes.append("⚠️ Volume abaixo do ideal - recomenda-se falar mais perto do microfone")
                score -= 0.15
            elif audio.dBFS < -20:
                notes.append("ℹ️ Volume ligeiramente baixo")
                score -= 0.05
            
            # 2. Verificar duração.
            # `len(audio)` em pydub retorna a duração em MILISSEGUNDOS; dividir por
            # 1000 dá segundos e por 60 dá minutos.
            #   > 120 min e > 60 min: apenas AVISOS de tempo (sem penalidade) para
            #       gerenciar a expectativa de quanto a transcrição vai demorar.
            #   < 0.5 min (30 s): curto demais para render conteúdo útil           -> -0.1
            duration_min = len(audio) / 1000 / 60
            if duration_min > 120:
                notes.append(f"⏱️ Áudio muito longo ({duration_min:.0f} min) - transcrição pode demorar significativamente")
            elif duration_min > 60:
                notes.append(f"⏱️ Áudio longo ({duration_min:.0f} min) - transcrição pode demorar")
            elif duration_min < 0.5:
                notes.append("⚠️ Áudio muito curto - pode não conter conteúdo suficiente")
                score -= 0.1
            
            # 3. Verificar silêncio excessivo.
            # `_detect_silence_ratio` devolve a fração do clipe (0.0-1.0) abaixo do
            # limiar de silêncio. Muita pausa indica gravação vazia, microfone
            # mudo ou conteúdo escasso.
            #   > 0.70 (70% mudo): forte sinal de problema -> -0.25
            #   > 0.50 (50% mudo): apenas observação        -> -0.10
            silent_ratio = self._detect_silence_ratio(audio)
            if silent_ratio > 0.7:
                notes.append(f"⚠️ {silent_ratio*100:.0f}% de silêncio - verifique se há conteúdo")
                score -= 0.25
            elif silent_ratio > 0.5:
                notes.append(f"ℹ️ {silent_ratio*100:.0f}% de silêncio detectado")
                score -= 0.1
            
            # 4. Verificar clipping (distorção).
            # `audio.max_dBFS` é o pico mais alto do clipe. Como 0 dBFS é o teto
            # digital, picos colados nele significam que a onda provavelmente foi
            # "ceifada" (clipping), gerando distorção que atrapalha a transcrição.
            #   > -0.5 dBFS: praticamente no teto, clipping provável -> -0.15
            #   > -1.0 dBFS: picos perigosamente altos, só ressalva  -> -0.05
            if audio.max_dBFS > -0.5:
                notes.append("⚠️ Possível distorção por volume excessivo (clipping)")
                score -= 0.15
            elif audio.max_dBFS > -1.0:
                notes.append("ℹ️ Picos de volume próximos ao limite")
                score -= 0.05
            
            # 5. Verificar taxa de amostragem.
            # `audio.frame_rate` é o sample rate em Hz. 16000 Hz (16 kHz) é o piso
            # padrão dos modelos de ASR (speech-to-text), incluindo o Azure: abaixo
            # disso as frequências da fala começam a ser cortadas e a transcrição
            # perde acurácia.                                                   -> -0.1
            if audio.frame_rate < 16000:
                notes.append("⚠️ Taxa de amostragem baixa - qualidade de transcrição pode ser afetada")
                score -= 0.1

            # Pós-processamento das notas (resumo amigável para o laudo):
            #   - Nenhuma nota acumulada => tudo passou: emite selo de aprovação.
            #   - Houve apenas ressalvas leves (score ainda >= 0.8) => insere no
            #     TOPO um selo "boa" para que o leitor do laudo veja o veredito
            #     geral antes da lista de detalhes.
            if not notes:
                notes.append("✅ Qualidade de áudio adequada para transcrição")
            elif score >= 0.8:
                notes.insert(0, "✅ Qualidade geral boa")
            
            return {
                # Clamp em [0.0, 1.0]: protege contra penalidades somadas > 1.0.
                "score": max(0.0, min(1.0, score)),
                "notes": notes,
                # `details` expõe as métricas brutas (já arredondadas) usadas nas
                # decisões acima, para auditoria humana e exibição no laudo.
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
            # Falha de decodificação (formato inválido, ffmpeg ausente, bytes
            # corrompidos). Em vez de propagar o erro e travar o laudo, retorna
            # score NEUTRO 0.5 (nem aprova nem reprova) com a causa registrada.
            # Note que neste caminho NÃO há a chave 'details'.
            logger.error(f"Erro ao analisar qualidade de áudio: {e}")
            return {
                "score": 0.5,
                "notes": [f"⚠️ Não foi possível analisar qualidade completamente: {str(e)}"]
            }
    
    def analyze_video(self, video_bytes: bytes) -> dict:
        """
        Analisa qualidade do vídeo (placeholder - foco é áudio por enquanto).

        Args:
            video_bytes: Bytes do arquivo de vídeo (atualmente IGNORADOS).

        Returns:
            Dicionário fixo com 'score' 0.8 e nota informativa.

        Como funciona:
            Stub intencional. A análise de imagem ainda não foi implementada, mas
            o método já existe para manter a interface estável com os chamadores.
            Devolve um score otimista de 0.8 (não penaliza por algo que não foi
            medido) e sinaliza, via nota, que o recurso virá em versão futura.
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
            Float de 0.0 a 1.0 representando proporção de silêncio (ex.: 0.3 = 30%
            do áudio considerado silêncio).

        Como funciona:
            1. Fatia o áudio em janelas fixas de 500 ms (`chunk_ms`). 500 ms é um
               meio-termo: curto o bastante para captar pausas reais da fala, longo
               o bastante para não explodir o custo de iteração em áudios longos.
            2. `total_chunks` usa divisão inteira (//), então uma sobra final menor
               que 500 ms é descartada de propósito (não vira janela parcial).
            3. Guarda de borda: se o áudio é mais curto que uma janela, não há o que
               medir e retorna 0.0 (evita divisão por zero no final).
            4. Para cada janela, mede o `dBFS` local e a classifica como silêncio se
               ficar abaixo de -40 dBFS (`silence_threshold`) — mesmo piso usado na
               checagem de "volume muito baixo", ou seja, "abaixo disso é ruído de
               fundo, não fala".
            5. Retorna a razão janelas_silenciosas / janelas_totais.
        """
        # Dividir em chunks de 500ms (granularidade da varredura).
        chunk_ms = 500
        silent_chunks = 0
        # Divisão inteira: descarta a sobra final < 500 ms.
        total_chunks = len(audio) // chunk_ms

        # Áudio menor que uma janela: nada a medir, evita divisão por zero abaixo.
        if total_chunks == 0:
            return 0.0

        # -40 dBFS: piso de "som útil"; abaixo disso tratamos como silêncio/ruído.
        silence_threshold = -40  # dBFS

        for i in range(total_chunks):
            # Slicing por milissegundos (API do pydub) recorta a janela [i, i+1).
            chunk = audio[i * chunk_ms:(i + 1) * chunk_ms]
            if chunk.dBFS < silence_threshold:
                silent_chunks += 1

        return silent_chunks / total_chunks
