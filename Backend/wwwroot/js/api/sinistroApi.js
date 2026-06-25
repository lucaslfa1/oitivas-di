/**
 * API Client para comunicação com o backend
 */

import { API_URL, API_TRANSCREVER, API_SALVAR, API_ANALISES, MODO_BACKEND } from '../config/constants.js';

/**
 * Envia arquivo para análise
 * @param {File} file - Arquivo a analisar
 * @param {string} type - Tipo: audio, foto, video
 * @param {string} context - Contexto adicional
 * @param {string} duracao - Duração do arquivo (opcional, para áudio/vídeo)
 * @param {string} transcricao - Transcrição prévia (opcional, para two-step)
 * @returns {Promise<Object>}
 */
export async function enviarParaAnalise(file, type, context = '', duracao = '', transcricao = '') {
    const formData = new FormData();
    formData.append("Arquivo", file);
    formData.append("Contexto", context);
    formData.append("Modo", MODO_BACKEND[type] || 'vistoria');

    // Adiciona duração se fornecida
    if (duracao) {
        formData.append("Duracao", duracao);
    }

    // Adiciona transcrição prévia para two-step approach
    if (transcricao) {
        formData.append("Transcricao", transcricao);
    }

    // Determinar endpoint correto baseado no tipo
    const API_BASE = API_URL.replace('/analisar', '');
    let endpoint;

    switch (type) {
        case 'foto':
            endpoint = `${API_BASE}/analisar/imagem`;
            break;
        case 'video':
            endpoint = `${API_BASE}/analisar/video`;
            break;
        case 'audio':
            endpoint = `${API_BASE}/analisar/oitiva`;
            break;
        default:
            endpoint = API_URL;
    }

    console.log(`📤 Enviando para API: ${endpoint}`);

    const res = await fetch(endpoint, {
        method: "POST",
        body: formData
    });

    if (!res.ok) {
        const errorText = await res.text();
        throw new Error(errorText);
    }

    return await res.json();
}

/**
 * Gera transcricao de audio
 * @param {File} file - Arquivo de áudio
 * @returns {Promise<Object>} - { transcricao: string, fonte: string, dadosOitiva: Object }
 */
export async function gerarTranscricaoAPI(file, connectionId = null) {
    const formData = new FormData();
    formData.append("Arquivo", file);

    console.log(`Gerando transcricao de audio...`);

    const headers = {};
    if (connectionId) {
        headers['X-Connection-Id'] = connectionId;
    }

    const res = await fetch(API_TRANSCREVER, {
        method: "POST",
        headers: headers,
        body: formData
    });

    if (!res.ok) {
        const errorData = await res.json().catch(() => ({ error: 'Erro desconhecido' }));
        throw new Error(errorData.message || errorData.error || 'Erro na transcrição');
    }

    const data = await res.json();
    console.log('Transcricao gerada com sucesso');

    // Log dos dados de oitiva extraídos
    if (data.dadosOitiva && Object.keys(data.dadosOitiva).length > 0) {
        console.log('📋 Dados de Oitiva extraídos:', Object.keys(data.dadosOitiva).length, 'campos');
    }

    return {
        transcricao: data.transcricao,
        fonte: data.fonte || 'transcricao',
        dadosOitiva: data.dadosOitiva || {}
    };
}

/**
 * Salva análise no servidor
 * @param {Object} dados - Dados da análise
 * @returns {Promise<Object>}
 */
export async function salvarAnaliseAPI(dados) {
    const res = await fetch(API_SALVAR, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dados)
    });

    if (!res.ok) {
        throw new Error('Erro ao salvar no servidor');
    }

    return await res.json();
}

/**
 * Busca todas as análises salvas
 * @returns {Promise<Array>}
 */
export async function buscarAnalisesAPI() {
    const res = await fetch(API_ANALISES);

    if (!res.ok) {
        throw new Error('Erro ao buscar análises');
    }

    return await res.json();
}

/**
 * Busca uma análise específica por ID
 * @param {number|string} id - ID da análise
 * @returns {Promise<Object>}
 */
export async function buscarAnaliseAPI(id) {
    const res = await fetch(`${API_ANALISES}/${id}`);

    if (!res.ok) {
        throw new Error('Análise não encontrada');
    }

    return await res.json();
}

/**
 * Exclui uma análise
 * @param {number|string} id - ID da análise
 * @returns {Promise<boolean>}
 */
export async function excluirAnaliseAPI(id) {
    const res = await fetch(`${API_ANALISES}/${id}`, {
        method: 'DELETE'
    });

    return res.ok;
}

/**
 * Gera laudo técnico usando GPT-4 (Azure OpenAI)
 * @param {string} transcricao - Transcrição completa
 * @param {string} duracao - Duração do áudio
 * @param {string} contexto - Contexto adicional
 * @returns {Promise<{markdown: string}>}
 */
export async function gerarLaudoTecnicoAPI(transcricao, duracao, contexto) {
    const API_BASE = API_URL.replace('/analisar', '');
    const response = await fetch(`${API_BASE}/analisar/laudo`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            Transcricao: transcricao,
            Duracao: duracao,
            Contexto: contexto
        })
    });

    if (!response.ok) {
        const errorData = await response.json().catch(() => ({ error: 'Erro desconhecido' }));
        throw new Error(errorData.error || errorData.detail || 'Erro ao gerar laudo técnico');
    }

    return response.json();
}

/**
 * Extrai dados estruturados da transcrição
 * @param {string} transcricao - Texto da transcrição
 * @returns {Promise<Object>} - JSON com dados extraídos
 */
export async function extrairDadosAPI(transcricao) {
    const API_BASE = API_URL.replace('/analisar', '');
    const response = await fetch(`${API_BASE}/extrair-dados`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Transcricao: transcricao })
    });

    if (!response.ok) {
        console.warn("Falha na extração de dados");
        return {};
    }

    return await response.json();
}

/**
 * Faz upload de um arquivo para o storage local
 * @param {File} file - Arquivo a ser salvo
 * @returns {Promise<{url: string, fileName: string}>}
 */
export async function uploadFileAPI(file) {
    const formData = new FormData();
    formData.append("file", file);

    const response = await fetch(`${API_URL.replace('/api/analisar', '')}/api/storage/upload`, {
        method: 'POST',
        body: formData
    });

    if (!response.ok) {
        throw new Error('Falha no upload do arquivo');
    }

    return await response.json();
}

