"""Pré-processamento de áudio do Sentinel (análise forense de sinistros).

Este módulo prepara os áudios de oitivas/declarações antes de enviá-los à
transcrição na nuvem (Azure Speech). O foco é entregar um sinal limpo e
padronizado, mas SEM aplicar transformações que destroem o alinhamento temporal:
a Azure devolve timestamps por palavra que o Frontend usa para sincronizar a
legenda com o player. Por isso etapas como corte de silêncio inicial e redução
de ruído clássica estão propositalmente desativadas (ver `process`).

Dependências externas:
    - pydub/ffmpeg: decodificação e re-encode dos formatos de entrada.
    - noisereduce/numpy: redução de ruído (mantida no código, porém desativada).
"""

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
    """Encapsula o pipeline de normalização e merge de áudios para transcrição.

    A instância guarda apenas o alvo de volume (`target_dbfs`); todo o resto é
    derivado por chamada. Métodos públicos (`process`, `merge_audios`) recebem
    bytes/caminhos de arquivo e devolvem áudio MP3 pronto para a Azure, junto de
    um dicionário de metadados. Métodos privados (`_load_audio`,
    `_get_ext_from_mime`, `_detect_leading_silence`, `_reduce_noise`) são
    auxiliares internos.
    """

    def __init__(self, target_dbfs: float = -16.0):
        """Inicializa o processador com o nível de volume alvo.

        Args:
            target_dbfs: Volume alvo em dBFS (negativo, pois 0 dBFS é o teto
                digital). O padrão -16.0 é um meio-termo para voz: alto o
                suficiente para a IA, com folga (headroom) para evitar clipping.

        Como funciona:
            Apenas memoriza o alvo. O valor é usado em `process` como `headroom`
            da normalização (via `abs(self.target_dbfs)`), ou seja, quantos dB
            abaixo do pico o áudio deve ficar.
        """
        self.target_dbfs = target_dbfs
    
    async def process(self, audio_bytes: bytes | None, filename: str, mime_type: str, input_path: str | None = None) -> tuple[bytes, dict]:
        """Padroniza um único áudio para transcrição na Azure (mono + volume + MP3 128k).

        Recebe o áudio por bytes em memória ou por caminho em disco (upload
        streamado), converte para mono, normaliza o volume e re-encoda como MP3
        a 128 kbps, devolvendo os bytes prontos e metadados de duração.

        Args:
            audio_bytes: Conteúdo binário do áudio. Pode ser None quando o áudio
                já está em disco e `input_path` é informado.
            filename: Nome original do arquivo; usado para deduzir a extensão e,
                por consequência, ajudar o ffmpeg/pydub a decodificar o formato.
            mime_type: MIME do upload; usado como fallback de extensão quando
                `filename` não tem sufixo reconhecível.
            input_path: Caminho opcional no disco. Quando presente e existente,
                tem prioridade sobre `audio_bytes` (evita reescrever o arquivo).

        Returns:
            Tupla (bytes_mp3, metadata), onde `metadata` traz format, durações
            estimada/processada (em segundos) e o trim inicial aplicado.

        Raises:
            ValueError: Propagado de `_load_audio` quando nem `input_path` nem
                `audio_bytes` foram fornecidos.
            Exception: Erros de decodificação/encode do ffmpeg/pydub são
                propagados a partir de `_load_audio`/`export`.

        Como funciona:
            1. CARREGAR: delega a `_load_audio`, que normaliza qualquer formato de
               entrada (MPEG/MP3/WAV/M4A/OGG) re-decodificando para WAV em memória.
            2. MONO: se o sinal vier estéreo (`channels > 1`), reduz para 1 canal
               — voz não precisa de estéreo e isso reduz o custo da transcrição.
            3. Redução de ruído e trim de silêncio inicial estão DESABILITADOS de
               propósito (ver comentários inline): ambos alteram o alinhamento
               temporal que a Azure devolve por palavra, dessincronizando a
               legenda no Frontend.
            4. NORMALIZAR: aplica `normalize` com `headroom=abs(self.target_dbfs)`,
               ou seja, deixa o pico do áudio `target_dbfs` dB abaixo de 0 dBFS.
            5. EXPORTAR: re-encoda em MP3 a 128 kbps — bitrate alto o bastante para
               preservar fonemas de alta frequência sem inflar o arquivo.
            6. METADATA: monta o dicionário de retorno. Observação: a duração
               original é apenas estimada a partir do tamanho em bytes dividido por
               1000, e não corresponde à duração real em segundos.
        """
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
            # original_duration é uma ESTIMATIVA grosseira: tamanho em bytes / 1000.
            # Não é a duração real em segundos (depende de bitrate/codec).
            "original_duration": (len(audio_bytes) if audio_bytes else os.path.getsize(input_path) if input_path else 0)/1000, # Estimado
            # processed_duration vem em ms (len(AudioSegment)); /1000 converte para segundos.
            "processed_duration": len(audio)/1000
            # NOTA: a chave 'start_trim_seconds' foi removida — o trim de silêncio inicial
            # está desabilitado de propósito (preserva os timestamps por palavra da Azure),
            # então não havia valor real a reportar. Removê-la corrige um NameError latente.
        }
        return processed_bytes, metadata

    async def merge_audios(self, audio_files_data: list[tuple[bytes, str]]) -> tuple[bytes, dict]:
        """Concatena vários áudios em sequência num único MP3 (mono, 24 kHz).

        Usado quando uma oitiva é enviada em partes: junta os trechos na ordem
        recebida, padronizando canal e taxa de amostragem para evitar glitches na
        emenda. Partes corrompidas são puladas para não derrubar o lote inteiro.

        Args:
            audio_files_data: Lista de tuplas (bytes, mime_type), uma por trecho,
                na ordem em que devem ser concatenados.

        Returns:
            Tupla (bytes_mp3, metadata), onde `metadata` traz format, a quantidade
            de partes recebidas e a duração total do resultado em segundos.

        Raises:
            ValueError: Se a lista vier vazia, ou se nenhuma das partes pôde ser
                decodificada (todas falharam e foram puladas).

        Como funciona:
            1. Valida que há pelo menos um arquivo; caso contrário, levanta erro.
            2. Itera sobre as partes na ordem dada, mantendo um acumulador
               `combined_audio`:
               a. Deriva um nome fictício (`part_{i}<ext>`) a partir do MIME via
                  `_get_ext_from_mime`, para ajudar a detecção de formato caso o
                  MIME não baste.
               b. Decodifica o trecho com `_load_audio`.
               c. Força mono (`set_channels(1)`) para compatibilidade na junção.
               d. Reamostra para 24000 Hz (24 kHz) — taxa suficiente para voz e
                  uniforme entre as partes, evitando estalos na emenda.
               e. Concatena: o primeiro trecho inicializa o acumulador; os
                  seguintes são anexados com `+=` (que une os AudioSegments).
            3. Qualquer exceção numa parte é logada como warning e a parte é
               IGNORADA (`continue`) — preferimos salvar o que der a abortar tudo.
            4. Se nenhuma parte sobreviveu (`combined_audio is None`), levanta erro.
            5. Exporta o resultado como MP3 a 128 kbps e monta os metadados.
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
            """Mapeia um MIME type de áudio para a extensão de arquivo correspondente.

            Args:
                mime_type: MIME informado pelo upload (ex.: "audio/mpeg").

            Returns:
                Extensão com ponto (ex.: ".mp3"). Para MIMEs desconhecidos retorna
                ".mp3" como padrão seguro, já que o ffmpeg costuma decodificar bem
                contêineres MPEG.

            Como funciona:
                Consulta um dicionário fixo de MIMEs suportados (mpeg, wav, ogg,
                m4a/mp4 etc.) e usa `dict.get` com fallback ".mp3" quando o MIME
                não está mapeado. A extensão resultante serve só para nomear o
                arquivo temporário e orientar a detecção de formato do ffmpeg.
            """
            format_map = {
                "audio/mpeg": ".mp3", "video/mpeg": ".mpeg", "audio/wav": ".wav",
                "audio/mp3": ".mp3", "audio/ogg": ".ogg", "audio/m4a": ".m4a",
                "audio/x-m4a": ".m4a", "audio/mp4": ".m4a"
            }
            return format_map.get(mime_type, ".mp3")

    def _load_audio(self, audio_bytes: bytes | None, filename: str, mime_type: str, input_path: str | None = None) -> AudioSegment:
        """Decodifica áudio de qualquer formato suportado e o normaliza para WAV em memória.

        Aceita o áudio vindo de um caminho em disco (preferencial, upload
        streamado) ou de bytes em memória, e sempre devolve um AudioSegment
        re-decodificado a partir de WAV — uma "limpeza" que padroniza o objeto
        interno do pydub e evita inconsistências de contêiner.

        Args:
            audio_bytes: Conteúdo binário do áudio. Pode ser None se `input_path`
                for fornecido e existir.
            filename: Nome original; sua extensão é a primeira pista de formato.
            mime_type: MIME usado para deduzir a extensão quando `filename` não tem
                sufixo utilizável.
            input_path: Caminho no disco; quando presente e existente, é lido
                diretamente e tem prioridade sobre `audio_bytes`.

        Returns:
            AudioSegment decodificado e re-exportado como WAV em memória.

        Raises:
            ValueError: Se nem `input_path` (existente) nem `audio_bytes` forem
                fornecidos.
            Exception: Erros de leitura/decodificação do ffmpeg/pydub são logados
                e re-levantados.

        Como funciona:
            1. Determina a extensão: usa o sufixo de `filename` em minúsculas; se
               estiver ausente ou curto demais (< 2 chars), cai para
               `_get_ext_from_mime(mime_type)`. A extensão orienta o ffmpeg.
            2. Caminho em disco: se `input_path` existe, lê com
               `AudioSegment.from_file`, re-exporta para um buffer WAV em memória e
               relê a partir dele (passo de saneamento), retornando esse segmento.
            3. Fallback por bytes: se não há caminho, exige `audio_bytes` (senão
               ValueError). Grava os bytes num arquivo temporário com a extensão
               deduzida (`delete=False` para poder reabrir pelo ffmpeg), deixa o
               pydub/ffmpeg autodetectar o formato e aplica o mesmo saneamento
               via WAV em memória.
            4. `finally`: remove o arquivo temporário se ele foi criado, ignorando
               silenciosamente erros de unlink (limpeza best-effort).
        """
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
        """Mede quantos milissegundos de silêncio existem no início do áudio.

        Auxiliar de trim (atualmente não usado no pipeline ativo, já que o corte
        de silêncio inicial está desabilitado em `process` para preservar os
        timestamps da Azure). Mantido para uso futuro/diagnóstico.

        Args:
            sound: AudioSegment a inspecionar.
            silence_threshold: Limite em dBFS abaixo do qual um trecho é
                considerado silêncio. O padrão -40.0 dBFS é bem baixo, capturando
                apenas silêncio real/ruído de fundo fraco (valores maiores, mais
                próximos de 0, cortariam também fala baixa).
            chunk_size: Tamanho do passo em ms a cada iteração. O padrão 10 ms dá
                granularidade fina sem custo relevante.

        Returns:
            Quantidade de milissegundos de silêncio no início (0 se o áudio já
            começa acima do threshold).

        Como funciona:
            Caminha do início para o fim em saltos de `chunk_size` ms, avançando
            `trim_ms` enquanto o trecho `sound[trim_ms:trim_ms+chunk_size]` tiver
            volume (dBFS) abaixo de `silence_threshold`. Para no primeiro trecho
            audível ou ao chegar no fim do áudio, e devolve `trim_ms`.
        """
        trim_ms = 0
        while trim_ms < len(sound) and sound[trim_ms:trim_ms+chunk_size].dBFS < silence_threshold:
            trim_ms += chunk_size
        return trim_ms

    def _reduce_noise(self, audio):
        """Aplica redução de ruído estacionário ao áudio (DESATIVADO no pipeline).

        IMPORTANTE: este método NÃO é chamado por `process` — a chamada está
        comentada de propósito. A redução de ruído clássica degrada o sinal para
        os motores de transcrição (Azure e Whisper), removendo informação útil de
        alta frequência. O código é mantido como referência/experimento e pode
        ser reativado se o trade-off mudar.

        Args:
            audio: AudioSegment de entrada.

        Returns:
            Novo AudioSegment com o ruído reduzido. Em caso de qualquer falha no
            processamento, retorna o `audio` original inalterado (degradação
            graciosa).

        Como funciona:
            1. Converte as amostras do AudioSegment para um array numpy.
            2. Chama `noisereduce.reduce_noise` em modo estacionário
               (`stationary=True`), assumindo um perfil de ruído constante, com
               `prop_decrease=0.40` — atenua apenas ~40% do ruído estimado, o que
               preserva mais as altas frequências da voz humana do que uma
               redução agressiva.
            3. Reconstrói o AudioSegment a partir dos bytes processados via
               `_spawn`, mantendo os parâmetros (taxa, largura) do original.
            4. Qualquer exceção é logada como warning e o áudio original é
               devolvido sem alterações.
        """
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
