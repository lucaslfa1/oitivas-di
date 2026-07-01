/**
 * Funções utilitárias compartilhadas
 */

/**
 * Capitaliza a primeira letra de uma string
 * @param {string} s - String a capitalizar
 * @returns {string}
 */
export function capitalize(s) {
    return s.charAt(0).toUpperCase() + s.slice(1);
}

/**
 * Formata data para exibição pt-BR
 * @param {string|Date} date - Data a formatar
 * @returns {string}
 */
export function formatarData(date) {
    const d = new Date(date);
    return d.toLocaleDateString('pt-BR');
}

/**
 * Formata hora para exibição pt-BR
 * @param {string|Date} date - Data a formatar
 * @returns {string}
 */
export function formatarHora(date) {
    const d = new Date(date);
    return d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}

/**
 * Formata data e hora completa pt-BR
 * @param {string|Date} date - Data a formatar
 * @returns {string}
 */
export function formatarDataHora(date) {
    const d = new Date(date);
    return d.toLocaleString('pt-BR');
}

/**
 * Debounce para evitar múltiplas chamadas
 * @param {Function} func - Função a ser executada
 * @param {number} wait - Tempo de espera em ms
 * @returns {Function}
 */
export function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

/**
 * Atualiza ícones do Lucide de forma segura
 */
export function refreshIcons() {
    if (typeof lucide !== 'undefined') {
        lucide.createIcons();
    }
}

/**
 * Gera hash SHA-256 de um arquivo
 * Útil para identificar duplicatas
 * @param {File} file - Arquivo para gerar hash
 * @returns {Promise<string>} Hash hexadecimal
 */
export async function generateFileHash(file) {
    const buffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest('SHA-256', buffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
}

/**
 * Torna timestamps [M:SS] / [MM:SS] / [H:MM:SS] clicáveis no laudo.
 * Ao clicar, chama window.seekTo() que pula para o momento no vídeo/áudio.
 * @param {string} html - HTML já renderizado (ex.: saída do marked.parse)
 * @returns {string} HTML com os timestamps envoltos em <a class="timestamp-link">
 */
export function linkifyTimestamps(html) {
    if (!html) return html;
    return html.replace(/\[(\d{1,3}:\d{2}(?::\d{2})?)\]/g,
        (_m, t) => `<a href="#" class="timestamp-link" onclick="seekTo('${t}'); return false;">[${t}]</a>`);
}

/**
 * Obtém a duração de um arquivo de mídia (áudio ou vídeo)
 * @param {File} file - Arquivo de mídia
 * @returns {Promise<string>} Duração formatada (MM:SS ou HH:MM:SS)
 */
export async function getMediaDuration(file) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(file);
        const media = file.type.startsWith('audio/') 
            ? new Audio() 
            : document.createElement('video');
        
        media.preload = 'metadata';
        
        media.onloadedmetadata = () => {
            URL.revokeObjectURL(url);
            const duration = media.duration;
            
            if (isNaN(duration) || !isFinite(duration)) {
                resolve('Não determinada');
                return;
            }
            
            const hours = Math.floor(duration / 3600);
            const minutes = Math.floor((duration % 3600) / 60);
            const seconds = Math.floor(duration % 60);
            
            if (hours > 0) {
                resolve(`${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`);
            } else {
                resolve(`${minutes}:${seconds.toString().padStart(2, '0')}`);
            }
        };
        
        media.onerror = () => {
            URL.revokeObjectURL(url);
            resolve('Não determinada');
        };
        
        media.src = url;
    });
}

/**
 * Formata duração em segundos para MM:SS ou HH:MM:SS
 * @param {number} seconds - Duração em segundos
 * @returns {string}
 */
export function formatDuration(seconds) {
    if (isNaN(seconds) || !isFinite(seconds)) return 'Não determinada';
    
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    
    if (hours > 0) {
        return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }
    return `${minutes}:${secs.toString().padStart(2, '0')}`;
}
