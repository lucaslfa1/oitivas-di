import { refreshIcons } from '../core/utils.js';

let wavesurfer = null;
let themeObserver = null;

export function initWaveform(url) {
    const container = document.getElementById('waveform');
    if (!container) return;

    if (wavesurfer) {
        wavesurfer.destroy();
    }

    // Detectar tema inicial
    const isDark = document.body.getAttribute('data-theme') === 'dark';
    const waveColor = isDark ? 'rgba(255, 255, 255, 0.2)' : '#cbd5e1';

    try {
        wavesurfer = WaveSurfer.create({
            container: '#waveform',
            waveColor: waveColor,
            progressColor: '#ed8936', // Orange
            cursorColor: '#ed8936',
            barWidth: 2,
            barRadius: 3,
            cursorWidth: 1,
            height: 80,
            barGap: 3,
            url: url
        });

        // Controls
        const btnPlayPause = document.getElementById('btnPlayPause');
        const timeCurrent = document.getElementById('timeCurrent');
        const timeTotal = document.getElementById('timeTotal');

        if (btnPlayPause) {
            btnPlayPause.onclick = () => wavesurfer.playPause();
        }

        wavesurfer.on('play', () => {
            if (btnPlayPause) btnPlayPause.innerHTML = '<i data-lucide="pause"></i>';
            refreshIcons();
        });

        wavesurfer.on('pause', () => {
            if (btnPlayPause) btnPlayPause.innerHTML = '<i data-lucide="play"></i>';
            refreshIcons();
        });

        wavesurfer.on('audioprocess', () => {
            if (timeCurrent) timeCurrent.innerText = formatTime(wavesurfer.getCurrentTime());
        });

        wavesurfer.on('ready', () => {
            if (timeTotal) timeTotal.innerText = formatTime(wavesurfer.getDuration());
        });

        wavesurfer.on('finish', () => {
            if (btnPlayPause) btnPlayPause.innerHTML = '<i data-lucide="play"></i>';
            refreshIcons();
        });

        // Observer para mudança de tema
        if (!themeObserver) {
            themeObserver = new MutationObserver((mutations) => {
                mutations.forEach((mutation) => {
                    if (mutation.type === 'attributes' && mutation.attributeName === 'data-theme') {
                        updateWaveformTheme();
                    }
                });
            });

            themeObserver.observe(document.body, {
                attributes: true,
                attributeFilter: ['data-theme']
            });
        }

    } catch (e) {
        console.error("Erro ao iniciar WaveSurfer:", e);
    }
}

function updateWaveformTheme() {
    if (!wavesurfer) return;

    const isDark = document.body.getAttribute('data-theme') === 'dark';
    const newColor = isDark ? 'rgba(255, 255, 255, 0.2)' : '#cbd5e1';

    // Verifica se o método setOptions existe (v7)
    if (wavesurfer.setOptions) {
        wavesurfer.setOptions({
            waveColor: newColor
        });
    }
}

function formatTime(seconds) {
    const min = Math.floor(seconds / 60);
    const sec = Math.floor(seconds % 60);
    return `${min}:${sec.toString().padStart(2, '0')}`;
}

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

// Sincronização externa (para clicar no texto e ir para o áudio)
export function seekToWaveform(seconds) {
    if (wavesurfer) {
        wavesurfer.setTime(seconds);
        wavesurfer.play();
    }
}
