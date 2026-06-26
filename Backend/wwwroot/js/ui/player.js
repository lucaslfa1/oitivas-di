/**
 * Controle de Player de Mídia
 * Permite sincronização entre texto (timestamps) e áudio/vídeo
 */

export function seekTo(timeString) {
    console.log("Seek to:", timeString);

    // Limpa o timestamp de possíveis caracteres extras
    const cleanTime = timeString.replace(/[^\d:]/g, '').trim();

    // timeString format: M:SS, MM:SS or HH:MM:SS
    const parts = cleanTime.split(':').map(Number);
    let seconds = 0;

    if (parts.length === 2) {
        // MM:SS ou M:SS
        seconds = (parts[0] || 0) * 60 + (parts[1] || 0);
    } else if (parts.length === 3) {
        // HH:MM:SS
        seconds = (parts[0] || 0) * 3600 + (parts[1] || 0) * 60 + (parts[2] || 0);
    } else if (parts.length === 1 && parts[0] > 0) {
        // Apenas segundos
        seconds = parts[0];
    }

    // Validação básica
    if (isNaN(seconds) || seconds < 0) {
        console.warn("Timestamp inválido:", timeString);
        return;
    }

    console.log("Seeking to seconds:", seconds);

    const audio = document.getElementById('audioPreview');
    const video = document.getElementById('videoPreview');

    // Tenta usar Waveform primeiro para áudio
    import('./waveform.js').then(module => {
        if (module.seekToWaveform) {
            module.seekToWaveform(seconds);
            console.log("Waveform seek executado");
        }
    }).catch(() => {
        // Fallback para player nativo de áudio
        if (audio && audio.src) {
            audio.currentTime = seconds;
            audio.play().catch(e => console.warn("Auto-play blocked:", e));
            console.log("Audio nativo seek executado");
        }
    });

    // Se for vídeo, também ajusta
    if (video && video.src) {
        video.currentTime = seconds;
        video.pause();
        console.log("Video seek executado e pausado");
    }
}
