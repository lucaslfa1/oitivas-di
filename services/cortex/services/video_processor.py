"""
VideoProcessor - Processamento de vídeo para análise forense de sinistros (Sentinel).

Este módulo é responsável por transformar um arquivo de vídeo bruto (bytes) em
artefatos prontos para análise pelos modelos de IA do pipeline Sentinel (Azure-only):
keyframes em base64 (imagens estáticas representativas) e a trilha de áudio em WAV
(para transcrição). Também expõe metadados técnicos do vídeo (resolução, fps, duração,
codec) usados para auditoria e diagnóstico.

Decisões de projeto relevantes:
- OpenCV (cv2) e NumPy são dependências PESADAS e opcionais. São importadas sob
  demanda (lazy import) em ``_ensure_dependencies`` para que o serviço suba mesmo
  em ambientes onde elas não estejam instaladas; nesse caso o processamento de
  keyframes degrada graciosamente retornando lista vazia em vez de quebrar.
- A escrita do vídeo em arquivo temporário é necessária porque ``cv2.VideoCapture``
  e o ffmpeg (via pydub) trabalham com caminhos de arquivo, não com buffers em memória.

Pipeline de alto nível:
1. Extração de keyframes (amostragem uniforme ao longo do vídeo) -> ``process``
2. Extração da trilha de áudio em WAV -> ``extract_audio``
3. Leitura de metadados técnicos do contêiner -> ``get_video_info``
"""

import logging
import tempfile
import os
import base64
from typing import List, Optional

logger = logging.getLogger(__name__)


class VideoProcessor:
    """Processador de vídeo para extração de keyframes, áudio e metadados.

    A instância é leve e segura para reutilização: as dependências pesadas (OpenCV
    e NumPy) só são carregadas na primeira operação que realmente precisa delas,
    ficando em cache nos atributos ``_cv2``/``_numpy`` para chamadas seguintes.
    """

    def __init__(self, keyframe_quality: int = 85):
        """Inicializa o processador definindo a qualidade de compressão dos keyframes.

        Args:
            keyframe_quality: Qualidade JPEG dos keyframes (0-100, default: 85).
                85 é o ponto de equilíbrio entre fidelidade visual suficiente para a
                análise da IA e tamanho do base64 enviado (valores acima inflam o
                payload sem ganho perceptível para o modelo).

        Como funciona:
            Apenas guarda a configuração e zera os caches de dependência (``_cv2`` e
            ``_numpy`` iniciam em ``None``). Nenhum import pesado acontece aqui — isso
            é adiado para ``_ensure_dependencies`` (lazy loading), mantendo a
            construção barata e sem efeitos colaterais.
        """
        self.keyframe_quality = keyframe_quality
        self._cv2 = None
        self._numpy = None
    
    def _ensure_dependencies(self):
        """Carrega OpenCV e NumPy sob demanda (lazy import) e indica disponibilidade.

        Returns:
            bool: ``True`` se cv2/NumPy estão carregados e prontos para uso; ``False``
            se o OpenCV não está instalado no ambiente (degradação graciosa — o
            chamador deve abortar a operação dependente sem lançar exceção).

        Como funciona:
            1. Curto-circuito de cache: se ``self._cv2`` já foi resolvido em uma
               chamada anterior, retorna ``True`` imediatamente sem reimportar.
            2. Na primeira chamada, tenta ``import cv2``/``import numpy``. Esses
               imports são caros e nem sempre disponíveis, por isso ficam aqui e não
               no topo do módulo.
            3. Em sucesso, memoiza os módulos em ``self._cv2``/``self._numpy`` para
               que toda operação posterior reaproveite o mesmo handle.
            4. Em ``ImportError`` (biblioteca ausente), apenas loga um aviso com a
               instrução de instalação e retorna ``False`` — não propaga a exceção,
               permitindo que o serviço continue operando sem capacidade de vídeo.
        """
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
        """Processa o vídeo e extrai keyframes representativos em JPEG/base64.

        Args:
            video_bytes: Bytes do arquivo de vídeo já carregado em memória.
            filename: Nome do arquivo original (usado apenas para logging/diagnóstico).
            extract_keyframes: Se ``False``, pula todo o trabalho e retorna lista vazia
                (default: True).
            max_keyframes: Número máximo de keyframes a extrair (default: 10). Funciona
                como teto: o vídeo é amostrado uniformemente para render no máximo essa
                quantidade de imagens, limitando o custo/tamanho enviado à IA.

        Returns:
            Lista de strings base64 (cada uma um quadro JPEG). Vazia se a extração foi
            desabilitada, se o OpenCV não está disponível, se o vídeo não pôde ser
            aberto, ou em caso de qualquer erro (a função nunca propaga exceção).

        Como funciona (pipeline de amostragem de keyframes):
            1. Curto-circuitos: respeita ``extract_keyframes=False`` e a ausência de
               OpenCV, retornando ``[]`` em ambos os casos.
            2. Persiste os bytes em um arquivo temporário ``.mp4`` porque
               ``cv2.VideoCapture`` exige um caminho de arquivo, não um buffer.
            3. Abre o vídeo e lê os metadados (fps, total de frames, resolução) para
               calcular a amostragem e para logging.
            4. Calcula o passo de amostragem: ``interval = total_frames // max_keyframes``
               (com piso 1). Isso distribui os keyframes UNIFORMEMENTE ao longo de todo
               o vídeo — ex.: 300 frames / 10 keyframes => pega 1 a cada 30 frames.
               O ``max(1, ...)`` evita intervalo zero quando há menos frames que
               ``max_keyframes`` (aí cada frame lido vira candidato).
            5. Percorre os frames sequencialmente; seleciona apenas aqueles cujo índice
               é múltiplo de ``interval`` (``frame_idx % interval == 0``). O loop também
               para assim que ``max_keyframes`` é atingido, evitando ler o vídeo inteiro
               à toa.
            6. Para cada frame selecionado: reduz a largura para no máximo 1280px
               (mantendo proporção) e o codifica em JPEG -> base64.
            7. Libera o ``VideoCapture`` e, no ``finally``, remove o arquivo temporário.
        """
        if not extract_keyframes:
            return []
        
        if not self._ensure_dependencies():
            logger.warning("Retornando lista vazia - OpenCV não disponível")
            return []
        
        cv2 = self._cv2
        np = self._numpy
        
        logger.info(f"Processando vídeo: {filename}, max_keyframes={max_keyframes}")
        
        # Persiste o vídeo em disco: cv2.VideoCapture decodifica a partir de um caminho
        # de arquivo, não aceita os bytes em memória diretamente. delete=False permite
        # fechar o handle antes do OpenCV abrir; a remoção é garantida no finally.
        tmp_path = None
        try:
            with tempfile.NamedTemporaryFile(suffix='.mp4', delete=False) as f:
                f.write(video_bytes)
                tmp_path = f.name

            cap = cv2.VideoCapture(tmp_path)

            # Container corrompido, codec ausente ou caminho inválido: aborta sem exceção.
            if not cap.isOpened():
                logger.error(f"Não foi possível abrir o vídeo: {filename}")
                return []

            # Metadados do contêiner, usados tanto para o cálculo da amostragem
            # (total_frames) quanto para logging/diagnóstico (resolução, fps, duração).
            fps = cap.get(cv2.CAP_PROP_FPS)
            total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
            width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
            height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
            duration = total_frames / fps if fps > 0 else 0  # guarda contra fps=0 (metadado ausente)

            logger.info(f"Vídeo: {width}x{height}, {fps:.1f}fps, {duration:.1f}s, {total_frames} frames")

            # Passo de amostragem uniforme: pega 1 keyframe a cada `interval` frames,
            # espalhando-os por todo o vídeo. Piso de 1 (max(1, ...)) evita divisão que
            # resultaria em 0 quando total_frames < max_keyframes (nesse caso amostra
            # todo frame lido, até esgotar o vídeo ou atingir max_keyframes).
            interval = max(1, total_frames // max_keyframes)

            keyframes = []
            frame_idx = 0

            # Lê o vídeo quadro a quadro. Para assim que coletar max_keyframes — não é
            # preciso decodificar até o fim, o que economiza tempo em vídeos longos.
            while cap.isOpened() and len(keyframes) < max_keyframes:
                ret, frame = cap.read()
                if not ret:
                    break  # fim do stream ou falha de leitura

                # Seleciona somente os frames nos múltiplos do passo de amostragem.
                if frame_idx % interval == 0:
                    # Downscale para no máx. 1280px de largura: reduz o tamanho do base64
                    # e padroniza a entrada da IA. 1280 (HD 720p de largura) preserva
                    # detalhe suficiente para a análise forense. scale mantém o aspect
                    # ratio; vídeos já <= 1280px passam intactos (sem upscale).
                    if width > 1280:
                        scale = 1280 / width
                        new_width = 1280
                        new_height = int(height * scale)
                        frame = cv2.resize(frame, (new_width, new_height))

                    # Codifica o frame (matriz BGR do OpenCV) em JPEG na qualidade
                    # configurada e converte o buffer binário para base64 (texto),
                    # formato exigido pelo transporte para os modelos Azure.
                    success, buffer = cv2.imencode(
                        '.jpg',
                        frame,
                        [cv2.IMWRITE_JPEG_QUALITY, self.keyframe_quality]
                    )

                    if success:
                        b64 = base64.b64encode(buffer).decode('utf-8')
                        keyframes.append(b64)
                        logger.debug(f"Keyframe {len(keyframes)} extraído do frame {frame_idx}")

                frame_idx += 1  # avança o índice de TODO frame lido (não só os amostrados)

            cap.release()  # libera o handle/recursos nativos do decodificador
            
            logger.info(f"Extraídos {len(keyframes)} keyframes de {filename}")
            return keyframes
            
        except Exception as e:
            # Qualquer falha de decodificação/IO vira lista vazia: o pipeline trata
            # vídeo como sinal opcional e não deve abortar a análise do sinistro.
            logger.error(f"Erro ao processar vídeo: {e}")
            return []

        finally:
            # Garante a remoção do arquivo temporário em qualquer caminho de saída
            # (sucesso, return antecipado ou exceção). A falha ao apagar é silenciada
            # de propósito para não mascarar o resultado real do processamento.
            if tmp_path and os.path.exists(tmp_path):
                try:
                    os.unlink(tmp_path)
                except Exception:
                    pass
    
    async def extract_audio(self, video_bytes: bytes, filename: str) -> Optional[bytes]:
        """Extrai a trilha de áudio do vídeo e a devolve como WAV em memória.

        Args:
            video_bytes: Bytes do arquivo de vídeo.
            filename: Nome do arquivo (apenas para logging).

        Returns:
            Bytes do áudio em formato WAV, ou ``None`` se o vídeo não tiver áudio
            extraível, se faltar ffmpeg/pydub, ou em qualquer erro (não propaga exceção).

        Como funciona:
            1. Importa ``pydub``/``io`` sob demanda — pydub depende do ffmpeg no host e é
               opcional, então o import fica dentro do try para degradar com ``None``.
            2. Grava os bytes em um ``.mp4`` temporário, pois ``AudioSegment.from_file``
               lê de um caminho de arquivo (e delega a decodificação ao ffmpeg).
            3. Decodifica e re-exporta a faixa em WAV (PCM não comprimido). WAV é o
               formato escolhido por ser o mais interoperável/seguro para o serviço de
               transcrição do pipeline (Azure) — evita problemas de codec de formatos
               comprimidos. A exportação vai para um ``BytesIO``, sem tocar o disco.
            4. O ``finally`` interno remove o temporário mesmo se a exportação falhar.
        """
        try:
            from pydub import AudioSegment
            import io

            # Salva em disco: pydub/ffmpeg operam sobre um caminho de arquivo.
            with tempfile.NamedTemporaryFile(suffix='.mp4', delete=False) as f:
                f.write(video_bytes)
                tmp_path = f.name

            try:
                # ffmpeg (via pydub) demultiplexa o container e decodifica a faixa de áudio.
                audio = AudioSegment.from_file(tmp_path)

                # Re-encoda como WAV em memória. WAV (PCM) maximiza compatibilidade com
                # o serviço de transcrição Azure e elimina riscos de codec comprimido.
                output = io.BytesIO()
                audio.export(output, format="wav")

                logger.info(f"Áudio extraído de {filename} (WAV): {len(output.getvalue())} bytes")
                return output.getvalue()

            finally:
                # Remove o temporário tanto em sucesso quanto em falha da exportação.
                if os.path.exists(tmp_path):
                    os.unlink(tmp_path)

        except Exception as e:
            # Áudio é opcional: falhas (sem faixa de áudio, sem ffmpeg, etc.) viram None.
            logger.error(f"Erro ao extrair áudio do vídeo: {e}")
            return None
    
    def get_video_info(self, video_bytes: bytes) -> dict:
        """Lê metadados técnicos do vídeo (resolução, fps, duração, frames, codec).

        Args:
            video_bytes: Bytes do arquivo de vídeo.

        Returns:
            Dicionário com ``width``, ``height``, ``fps``, ``total_frames``,
            ``duration_seconds`` e ``codec``. Em caso de OpenCV ausente ou erro,
            retorna ``{"error": <mensagem>}`` (nunca lança exceção).

        Como funciona:
            1. Exige OpenCV (``_ensure_dependencies``); sem ele, devolve dict de erro.
            2. Grava o vídeo num ``.mp4`` temporário e abre com ``cv2.VideoCapture``.
            3. Lê as propriedades do container (largura, altura, fps, contagem de frames).
            4. Calcula a duração como ``total_frames / fps`` (somente se fps > 0, para
               evitar divisão por zero quando o metadado de fps está ausente).
            5. Decodifica o codec a partir do FOURCC: o OpenCV expõe o código de 4
               caracteres empacotado num inteiro de 32 bits. O loop extrai cada byte
               (8 bits por caractere) com ``(fourcc >> 8*i) & 0xFF`` para i=0..3 e
               converte de volta para caractere, remontando a string ex.: "avc1"/"mp4v".
            6. Libera o capture; o ``finally`` remove o arquivo temporário.
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

                # Duração só é confiável com fps > 0; caso contrário mantém o default 0.
                if info["fps"] > 0:
                    info["duration_seconds"] = info["total_frames"] / info["fps"]

                # FOURCC: inteiro 32 bits com 4 chars ASCII empacotados (8 bits cada).
                # Desempacota byte a byte (deslocando 0/8/16/24 bits e mascarando 0xFF)
                # para reconstruir a sigla do codec, ex.: "mp4v", "avc1".
                fourcc = int(cap.get(cv2.CAP_PROP_FOURCC))
                info["codec"] = "".join([chr((fourcc >> 8 * i) & 0xFF) for i in range(4)])

                cap.release()
                return info

            finally:
                # Limpeza do temporário (executada mesmo se a leitura acima falhar).
                os.unlink(tmp_path)

        except Exception as e:
            logger.error(f"Erro ao obter info do vídeo: {e}")
            return {"error": str(e)}
