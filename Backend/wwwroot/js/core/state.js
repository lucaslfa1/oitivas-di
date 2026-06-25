/**
 * Estado Global da Aplicação
 * Gerencia o estado compartilhado entre módulos
 */

// Estado reativo dos arquivos carregados
export const state = {
    currentFiles: {
        audio: null,
        foto: null,
        video: null
    },
    transcricaoAtual: null,
    transcricaoOriginal: null,      // Backup antes de edições
    transcricaoValidada: false,     // Flag: usuário revisou a transcrição
    laudoAtual: {
        audio: null,
        foto: null,
        video: null
    },
    laudoOriginal: {                // Backup antes de edições
        audio: null,
        foto: null,
        video: null
    },
    filtroSalvos: 'todos'
};

// Getters
export const getFile = (type) => state.currentFiles[type];
export const getTranscricao = () => state.transcricaoAtual;
export const getTranscricaoOriginal = () => state.transcricaoOriginal;
export const isTranscricaoValidada = () => state.transcricaoValidada;
export const getLaudo = (type) => state.laudoAtual[type];
export const getLaudoOriginal = (type) => state.laudoOriginal[type];
export const getFiltroSalvos = () => state.filtroSalvos;

// Setters
export const setFile = (type, file) => {
    state.currentFiles[type] = file;
    // Reset transcrição quando novo arquivo é carregado
    if (type === 'audio') {
        state.transcricaoAtual = null;
        state.transcricaoOriginal = null;
        state.transcricaoValidada = false;
    }
    console.log(`📎 Arquivo atualizado [${type}]:`, file?.name || 'null');
};

export const setTranscricao = (transcricao) => {
    // Salva original apenas na primeira vez
    if (!state.transcricaoOriginal) {
        state.transcricaoOriginal = transcricao;
    }
    state.transcricaoAtual = transcricao;
    console.log(`📜 Transcrição atualizada (${transcricao?.length || 0} chars)`);
};

export const setTranscricaoValidada = (validada) => {
    state.transcricaoValidada = validada;
    console.log(`✅ Transcrição validada: ${validada}`);
};

export const setLaudo = (type, laudo) => {
    // Salva original apenas na primeira vez
    if (!state.laudoOriginal[type]) {
        state.laudoOriginal[type] = laudo;
    }
    state.laudoAtual[type] = laudo;
    console.log(`📋 Laudo atualizado [${type}]`);
};

export const setFiltroSalvos = (filtro) => {
    state.filtroSalvos = filtro;
};

// Limpar arquivo específico
export const clearFile = (type) => {
    state.currentFiles[type] = null;
    if (type === 'audio') {
        state.transcricaoAtual = null;
        state.transcricaoOriginal = null;
        state.transcricaoValidada = false;
        state.laudoAtual.audio = null;
        state.laudoOriginal.audio = null;
    }
    console.log(`🗑️ Arquivo removido [${type}]`);
};

// Reset completo
export const resetState = () => {
    state.currentFiles = { audio: null, foto: null, video: null };
    state.transcricaoAtual = null;
    state.transcricaoOriginal = null;
    state.transcricaoValidada = false;
    state.laudoAtual = { audio: null, foto: null, video: null };
    state.laudoOriginal = { audio: null, foto: null, video: null };
    state.filtroSalvos = 'todos';
    console.log('🔄 Estado resetado');
};

// Verificar se pode gerar laudo (transcrição existe e foi validada)
export const canGenerateLaudo = () => {
    return state.transcricaoAtual && state.transcricaoValidada;
};
