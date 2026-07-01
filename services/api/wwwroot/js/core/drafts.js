/**
 * Drafts (rascunhos)
 * Mantém um histórico local (localStorage) das últimas transcrições/laudos gerados
 * para evitar perda de conteúdo quando o usuário navega sem salvar.
 */

const STORAGE_KEY = 'sinistroIA_drafts_v1';
const MAX_PER_TYPE = 10;

function safeParse(json, fallback) {
    try {
        const parsed = JSON.parse(json);
        // Alguns cenários: string "null" -> null; ou formato antigo sem items
        if (!parsed || typeof parsed !== 'object') return fallback;
        return parsed;
    } catch {
        return fallback;
    }
}

function normalizeDraftStore(data) {
    if (!data || typeof data !== 'object') return { items: [] };
    if (!Array.isArray(data.items)) data.items = [];
    return data;
}

export function getAllDrafts() {
    const raw = localStorage.getItem(STORAGE_KEY);
    return normalizeDraftStore(safeParse(raw, { items: [] }));
}

function persist(data) {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(normalizeDraftStore(data)));
}

/**
 * Salva um rascunho (sempre adiciona no topo).
 * @param {'transcricao'|'audio'|'foto'|'video'} type
 * @param {string} conteudo
 * @param {{ arquivo?: string, titulo?: string, contexto?: string }} meta
 */
export function saveDraft(type, conteudo, meta = {}) {
    if (!conteudo || !conteudo.trim()) return;

    const data = normalizeDraftStore(getAllDrafts());
    const item = {
        id: Date.now(),
        type,
        conteudo,
        meta: {
            arquivo: meta.arquivo || 'N/A',
            titulo: meta.titulo || '',
            contexto: meta.contexto || ''
        },
        createdAt: new Date().toISOString()
    };

    data.items = [item, ...(data.items || [])];

    // limita por tipo
    const byType = {};
    const filtered = [];
    for (const it of data.items) {
        byType[it.type] = (byType[it.type] || 0) + 1;
        if (byType[it.type] <= MAX_PER_TYPE) filtered.push(it);
    }
    data.items = filtered;

    persist(data);
}

export function getLatestDraft(type) {
    const data = normalizeDraftStore(getAllDrafts());
    return (data.items || []).find(i => i.type === type) || null;
}

export function clearDrafts(type = null) {
    if (!type) {
        localStorage.removeItem(STORAGE_KEY);
        return;
    }

    const data = normalizeDraftStore(getAllDrafts());
    data.items = (data.items || []).filter(i => i.type !== type);
    persist(data);
}
