/**
 * @file waveform.js
 * @module ui/waveform
 *
 * Modulo de UI responsavel por renderizar e controlar o player de forma de onda
 * (waveform) do audio analisado no Sentinel. Usa a biblioteca WaveSurfer.js v7
 * carregada globalmente (via <script>, por isso e referenciada como `WaveSurfer`
 * sem import). Expoe funcoes para inicializar o player a partir de uma URL de
 * audio, reagir a troca de tema claro/escuro, destruir o player liberando
 * recursos, e fazer "seek" (pular para um instante) a partir de cliques na
 * transcricao/linha do tempo.
 *
 * Estado de modulo (singletons): mantemos uma unica instancia de WaveSurfer e um
 * unico MutationObserver de tema por carregamento de pagina. Eles sao guardados
 * em variaveis de modulo para que `destroyWaveform` possa libera-los e evitar
 * vazamento de memoria / observers duplicados ao reabrir o player.
 *
 * @requires WaveSurfer - biblioteca global WaveSurfer.js v7 (objeto no escopo window).
 */
import { refreshIcons } from '../core/utils.js';

/**
 * Instancia ativa do WaveSurfer, ou `null` quando nenhum player esta carregado.
 * Singleton de modulo: garante que exista no maximo um player por vez.
 * @type {(import('wavesurfer.js').default|null)}
 */
let wavesurfer = null;

/**
 * Observer que monitora o atributo `data-theme` em <body> para repintar a onda
 * quando o usuario alterna entre tema claro e escuro. `null` enquanto nenhum
 * observer estiver ativo. Singleton de modulo para evitar criar varios observers.
 * @type {(MutationObserver|null)}
 */
let themeObserver = null;

/**
 * Inicializa (ou reinicializa) o player de forma de onda apontando para a URL de
 * audio informada e conecta todos os controles e eventos de UI.
 *
 * COMO FUNCIONA:
 * 1. Localiza o container `#waveform` no DOM; se nao existir, encerra sem efeito.
 * 2. Se ja houver um player ativo, destroi a instancia anterior antes de criar a
 *    nova (evita players empilhados quando o usuario abre outro audio).
 * 3. Detecta o tema atual lendo `data-theme` do <body> para escolher a cor da
 *    onda nao-tocada (`waveColor`): em tema escuro usa branco translucido
 *    (rgba 255,255,255,0.2) para contraste sobre fundo escuro; em tema claro usa
 *    cinza-azulado `#cbd5e1`. A cor de progresso/cursor e sempre laranja (#ed8936),
 *    cor de destaque da marca, independente do tema.
 * 4. Cria a instancia WaveSurfer v7 com parametros visuais fixos (barras de 2px,
 *    raio 3px, espacamento 3px, altura 80px) e ja passa a `url` para carregamento.
 * 5. Liga os controles de UI: botao play/pause e os displays de tempo atual/total.
 * 6. Registra os eventos do WaveSurfer (play, pause, audioprocess, ready, finish)
 *    para manter icone do botao e tempos sincronizados com o estado do audio.
 * 7. Cria (uma unica vez) um MutationObserver sobre o <body> para repintar a onda
 *    quando o tema mudar.
 *
 * EFEITOS COLATERAIS: muta o DOM (innerHTML do botao, innerText dos tempos),
 * altera o estado de modulo `wavesurfer` e `themeObserver`, e registra handlers
 * de evento. Erros na criacao do WaveSurfer sao capturados e apenas logados no
 * console (a funcao nao relanca), para nao quebrar o restante da UI.
 *
 * @param {string} url - URL do arquivo de audio a ser carregado e renderizado.
 * @returns {void}
 */
export function initWaveform(url) {
    // Guard: sem o container no DOM nao ha onde renderizar a onda; aborta silenciosamente.
    const container = document.getElementById('waveform');
    if (!container) return;

    // Destroi qualquer player anterior para nao acumular instancias ao trocar de audio.
    if (wavesurfer) {
        wavesurfer.destroy();
    }

    // Detectar tema inicial: define a cor da parte ainda nao tocada da onda conforme o tema.
    // Tema escuro -> branco translucido (contraste sobre fundo escuro); tema claro -> cinza #cbd5e1.
    const isDark = document.body.getAttribute('data-theme') === 'dark';
    const waveColor = isDark ? 'rgba(255, 255, 255, 0.2)' : '#cbd5e1';

    try {
        // Cria o player WaveSurfer v7. Os valores numericos sao puramente esteticos:
        //   barWidth 2 / barRadius 3 / barGap 3 -> barras finas e arredondadas com folga;
        //   cursorWidth 1 -> linha fina do cursor de reproducao;
        //   height 80 -> altura fixa do canvas em pixels.
        // progressColor/cursorColor laranja (#ed8936) sao a cor de destaque da marca e nao mudam com o tema.
        wavesurfer = WaveSurfer.create({
            container: '#waveform',
            waveColor: waveColor,
            progressColor: '#ed8936', // Laranja (cor de destaque da marca)
            cursorColor: '#ed8936',
            barWidth: 2,
            barRadius: 3,
            cursorWidth: 1,
            height: 80,
            barGap: 3,
            url: url
        });

        // Controles de UI: botao play/pause e os dois displays de tempo (atual e total).
        const btnPlayPause = document.getElementById('btnPlayPause');
        const timeCurrent = document.getElementById('timeCurrent');
        const timeTotal = document.getElementById('timeTotal');

        // Clique no botao alterna entre tocar e pausar (delega ao proprio WaveSurfer).
        if (btnPlayPause) {
            btnPlayPause.onclick = () => wavesurfer.playPause();
        }

        // Ao iniciar a reproducao, troca o icone do botao para "pause" e re-renderiza os
        // icones Lucide (refreshIcons reprocessa os <i data-lucide=...> recem-inseridos).
        wavesurfer.on('play', () => {
            if (btnPlayPause) btnPlayPause.innerHTML = '<i data-lucide="pause"></i>';
            refreshIcons();
        });

        // Ao pausar, volta o icone para "play".
        wavesurfer.on('pause', () => {
            if (btnPlayPause) btnPlayPause.innerHTML = '<i data-lucide="play"></i>';
            refreshIcons();
        });

        // Durante a reproducao (disparado continuamente), atualiza o display de tempo decorrido.
        wavesurfer.on('audioprocess', () => {
            if (timeCurrent) timeCurrent.innerText = formatTime(wavesurfer.getCurrentTime());
        });

        // Quando o audio termina de carregar/decodificar, ja conhecemos a duracao total: exibe-a.
        wavesurfer.on('ready', () => {
            if (timeTotal) timeTotal.innerText = formatTime(wavesurfer.getDuration());
        });

        // Ao chegar ao fim do audio, restaura o icone "play" (o player fica pronto para recomecar).
        wavesurfer.on('finish', () => {
            if (btnPlayPause) btnPlayPause.innerHTML = '<i data-lucide="play"></i>';
            refreshIcons();
        });

        // Observer para mudanca de tema: registrado uma unica vez (guard `!themeObserver`).
        // Observa apenas o atributo `data-theme` do <body> e, quando ele muda, repinta a onda.
        if (!themeObserver) {
            themeObserver = new MutationObserver((mutations) => {
                mutations.forEach((mutation) => {
                    if (mutation.type === 'attributes' && mutation.attributeName === 'data-theme') {
                        updateWaveformTheme();
                    }
                });
            });

            // attributeFilter restringe o observer a `data-theme`, evitando callbacks desnecessarios
            // para qualquer outra mutacao de atributo do <body>.
            themeObserver.observe(document.body, {
                attributes: true,
                attributeFilter: ['data-theme']
            });
        }

    } catch (e) {
        // Falhas na criacao do WaveSurfer sao apenas logadas (nao relancadas) para nao
        // derrubar o restante da UI caso a biblioteca/URL apresente problema.
        console.error("Erro ao iniciar WaveSurfer:", e);
    }
}

/**
 * Repinta a cor da onda nao-tocada (`waveColor`) de acordo com o tema atual,
 * sem recriar o player. Acionada pelo MutationObserver de `data-theme`.
 *
 * COMO FUNCIONA: se nao houver player ativo, encerra. Caso contrario, le o tema
 * atual do <body> e escolhe a mesma paleta usada em `initWaveform` (branco
 * translucido no escuro, `#cbd5e1` no claro). Aplica via `setOptions`, API
 * disponivel apenas no WaveSurfer v7 — o guard `if (wavesurfer.setOptions)`
 * evita erro caso uma versao anterior esteja carregada. As cores de
 * progresso/cursor (laranja) nao mudam, entao nao sao tocadas aqui.
 *
 * EFEITOS COLATERAIS: altera as opcoes de renderizacao do player ativo, fazendo
 * o WaveSurfer redesenhar a onda.
 *
 * @returns {void}
 */
function updateWaveformTheme() {
    if (!wavesurfer) return;

    const isDark = document.body.getAttribute('data-theme') === 'dark';
    const newColor = isDark ? 'rgba(255, 255, 255, 0.2)' : '#cbd5e1';

    // setOptions so existe no WaveSurfer v7; o guard evita erro em versoes anteriores.
    if (wavesurfer.setOptions) {
        wavesurfer.setOptions({
            waveColor: newColor
        });
    }
}

/**
 * Formata uma duracao em segundos para a string "M:SS" exibida nos displays de tempo.
 *
 * COMO FUNCIONA: extrai os minutos inteiros (`Math.floor(seconds / 60)`) e os
 * segundos restantes (`Math.floor(seconds % 60)`); o `% 60` descarta os minutos
 * ja contabilizados. `padStart(2, '0')` garante dois digitos nos segundos
 * (ex.: 65s -> "1:05" em vez de "1:5"). Os minutos NAO sao preenchidos com zero
 * a esquerda, entao o formato e "M:SS" (ex.: "12:09", "0:07").
 *
 * @param {number} seconds - Duracao em segundos (tipicamente fracionaria, vinda do WaveSurfer).
 * @returns {string} Tempo formatado como "M:SS" (segundos sempre com 2 digitos).
 */
function formatTime(seconds) {
    const min = Math.floor(seconds / 60);
    const sec = Math.floor(seconds % 60);
    return `${min}:${sec.toString().padStart(2, '0')}`;
}

/**
 * Destroi o player e desconecta o observer de tema, liberando todos os recursos.
 *
 * COMO FUNCIONA: se houver player ativo, chama `wavesurfer.destroy()` (libera o
 * canvas/eventos internos da lib) e zera o singleton `wavesurfer`. Em seguida, se
 * houver observer ativo, chama `themeObserver.disconnect()` e zera o singleton
 * `themeObserver`. Zerar ambas as variaveis e essencial: evita vazamento de
 * memoria e impede que um observer orfao continue disparando `updateWaveformTheme`
 * sobre um player ja destruido. Deve ser chamada ao fechar/descartar a tela do player.
 *
 * EFEITOS COLATERAIS: destroi a instancia WaveSurfer, desconecta o MutationObserver
 * e reseta o estado de modulo (`wavesurfer` e `themeObserver` voltam a `null`).
 *
 * @returns {void}
 */
export function destroyWaveform() {
    if (wavesurfer) {
        wavesurfer.destroy();
        wavesurfer = null;
    }
    if (themeObserver) {
        themeObserver.disconnect();
        themeObserver = null;
    }
}

/**
 * Sincronizacao externa: posiciona o player em um instante especifico e inicia a
 * reproducao. Usada quando o usuario clica em um timestamp na transcricao/linha do
 * tempo para "pular" ate aquele trecho do audio.
 *
 * COMO FUNCIONA: se houver player ativo, move o cursor para `seconds` via
 * `setTime` (API do WaveSurfer v7) e imediatamente chama `play()` para tocar a
 * partir dali. Sem player ativo, e um no-op silencioso.
 *
 * EFEITOS COLATERAIS: altera a posicao de reproducao e inicia a tocar o audio.
 *
 * @param {number} seconds - Instante de destino, em segundos, para onde mover o cursor.
 * @returns {void}
 */
// Sincronização externa (para clicar no texto e ir para o áudio)
export function seekToWaveform(seconds) {
    if (wavesurfer) {
        wavesurfer.setTime(seconds);
        wavesurfer.play();
    }
}
