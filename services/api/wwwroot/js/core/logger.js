/**
 * Logger centralizado
 * Controla logs por ambiente e permite desativar em produção
 */

const isProd = window.location.hostname !== 'localhost' && !window.location.hostname.includes('127.0.0.1');

/**
 * Logger com níveis
 */
export const logger = {
    /**
     * Log de debug - apenas em desenvolvimento
     */
    debug: (...args) => {
        if (!isProd) {
            console.log(...args);
        }
    },

    /**
     * Log de informação - apenas em desenvolvimento
     */
    info: (...args) => {
        if (!isProd) {
            console.log(...args);
        }
    },

    /**
     * Avisos - sempre exibe
     */
    warn: (...args) => {
        console.warn(...args);
    },

    /**
     * Erros - sempre exibe
     */
    error: (...args) => {
        console.error(...args);
    }
};

/**
 * Helper para verificar se está em produção
 */
export const isProduction = () => isProd;
