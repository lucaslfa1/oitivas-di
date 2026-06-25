/**
 * Feature: Arquivos Salvos
 * Gerencia listagem, visualização e exclusão de análises salvas
 * Visualização inline com suporte a edição
 */

import { listAnalises, getAnalise, deleteAnalise } from '../services/analisesRepository.js';
import { TIPO_ICONS, TIPO_CLASSES } from '../config/constants.js';
import { formatarData, formatarHora, refreshIcons } from '../core/utils.js';
import { setFiltroSalvos, getFiltroSalvos } from '../core/state.js';
import { exportarItemPDF, exportarItemWord } from '../services/export.js';
import { toast } from '../ui/toast.js';

import {
    getItemAtual,
    setItemAtual,
    setConteudoOriginal,
    resetSalvosState
} from './salvos/salvosState.js';

export { toggleEditSalvo, salvarEdicaoSalvo, cancelarEdicaoSalvo } from './salvos/salvosEditor.js';

// Estado do item atualmente visualizado
let itemAtual = null;
let conteudoOriginal = '';
let isEditando = false;

/**
 * Atualiza o badge de contagem no menu lateral
 * @param {number} total - Total de arquivos salvos
 */
export function atualizarBadgeSalvos(total) {
    const badge = document.getElementById('salvosCountBadge');
    if (!badge) return;

    if (total > 0) {
        badge.textContent = total > 99 ? '99+' : total;
        badge.classList.remove('hidden');
    } else {
        badge.classList.add('hidden');
    }
}

/**
 * Carrega arquivos salvos do servidor ou localStorage
 */
export async function carregarSalvos() {
    const lista = document.getElementById('savedFilesList');
    if (!lista) return;

    const dados = await listAnalises();
    renderizarListaSalvos(dados);
    atualizarBadgeSalvos(dados.length);
}

/**
 * Renderiza lista de análises salvas
 * @param {Array} analises - Lista de análises
 * @param {string} filtro - Filtro atual ('todos', 'oitiva', etc)
 */
export function renderizarListaSalvos(analises, filtro = null) {
    const lista = document.getElementById('savedFilesList');
    const count = document.getElementById('savedCount');

    if (!lista) return;

    // Usa filtro passado ou o do estado
    const filtroAtual = filtro || getFiltroSalvos();

    // Filtra se necessário
    let analisesFiltradas = analises;
    if (filtroAtual !== 'todos') {
        analisesFiltradas = analises.filter(a => {
            const tipo = (a.tipo || '').toLowerCase();
            // Mapeamento de filtros para tipos
            if (filtroAtual === 'audio') {
                return tipo.includes('audio') || tipo.includes('oitiva') || tipo.includes('laudo');
            }
            if (filtroAtual === 'foto') {
                return tipo.includes('foto') || tipo.includes('imagem') || tipo.includes('vistoria');
            }
            if (filtroAtual === 'video') {
                return tipo.includes('video') || tipo.includes('vídeo');
            }
            if (filtroAtual === 'transcricao') {
                return tipo.includes('transcri');
            }
            return tipo.includes(filtroAtual.toLowerCase());
        });
    }

    // Atualiza contador
    if (count) count.textContent = analisesFiltradas.length;

    // Se não houver análises
    if (analisesFiltradas.length === 0) {
        lista.innerHTML = `
            <div class="saved-files-empty">
                <i data-lucide="folder-x"></i>
                <p>Nenhum arquivo ${filtroAtual !== 'todos' ? 'deste tipo ' : ''}salvo ainda.</p>
            </div>
        `;
        refreshIcons();
        return;
    }

    // Ordena por data (mais recente primeiro)
    analisesFiltradas.sort((a, b) =>
        new Date(b.dataAnalise || b.data) - new Date(a.dataAnalise || a.data)
    );

    // Renderiza itens - layout compacto estilo Windows Details
    lista.innerHTML = analisesFiltradas.map(item => {
        const tipoIcon = TIPO_ICONS[item.tipo] || TIPO_ICONS[item.tipo?.toLowerCase()] || 'file';
        const tipoClass = TIPO_CLASSES[item.tipo] || TIPO_CLASSES[item.tipo?.toLowerCase()] || '';
        const data = new Date(item.dataAnalise || item.data);
        const tipoLabel = item.tipo || 'Análise';
        const nomeArquivo = item.arquivo || 'Documento sem nome';

        return `
            <div class="saved-file-item" data-id="${item.id}" onclick="visualizarSalvo(${item.id})">
                <div class="saved-file-icon ${tipoClass}">
                    <i data-lucide="${tipoIcon}"></i>
                </div>
                <div class="saved-file-details">
                    <span class="saved-file-name" title="${nomeArquivo}">${nomeArquivo}</span>
                </div>
                <span class="saved-file-type-badge ${tipoClass}">${tipoLabel}</span>
                <div class="saved-file-meta">
                    <span>${formatarData(data)}</span>
                    <span>${formatarHora(data)}</span>
                </div>
                <div class="saved-file-actions" onclick="event.stopPropagation()">
                    <button onclick="exportarSalvo(${item.id})" title="Exportar PDF">
                        <i data-lucide="file-text"></i>
                    </button>
                    <button onclick="exportarSalvoWord(${item.id})" title="Exportar Word">
                        <i data-lucide="file"></i>
                    </button>
                    <button class="btn-delete" onclick="excluirSalvo(${item.id})" title="Excluir">
                        <i data-lucide="trash-2"></i>
                    </button>
                </div>
            </div>
        `;
    }).join('');

    refreshIcons();
}

/**
 * Filtra arquivos salvos por tipo
 * @param {string} filtro - Filtro: 'todos', 'audio', 'foto', 'video', 'transcricao'
 */
export function filtrarSalvos(filtro) {
    setFiltroSalvos(filtro);

    // Atualiza botões de filtro usando data-filter
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.classList.remove('active');
        const btnFilter = btn.dataset.filter || btn.textContent.toLowerCase();
        if (btnFilter === filtro || (filtro === 'todos' && btnFilter === 'todos')) {
            btn.classList.add('active');
        }
    });

    // Recarrega com filtro
    carregarSalvosComFiltro(filtro);
}

/**
 * Carrega salvos aplicando filtro
 * @param {string} filtro - Filtro a aplicar
 */
async function carregarSalvosComFiltro(filtro) {
    const dados = await listAnalises();
    renderizarListaSalvos(dados, filtro);
}

/**
 * Atualiza lista se painel estiver ativo
 */
export function atualizarListaSalvos() {
    if (document.getElementById('panel-salvos')?.classList.contains('active')) {
        carregarSalvosComFiltro(getFiltroSalvos());
    }
}

/**
 * Visualiza um arquivo salvo inline (sem modal)
 * @param {number|string} id - ID do arquivo
 */
export async function visualizarSalvo(id) {
    const item = await getAnalise(id);
    if (!item) {
        toast.error('Arquivo não encontrado.');
        return;
    }

    // Guarda item atual para edição
    setItemAtual(item);
    setConteudoOriginal(item.conteudo);

    // Elementos do viewer inline
    const emptyState = document.getElementById('salvoEmptyState');
    const viewContainer = document.getElementById('salvoViewContainer');
    const viewContent = document.getElementById('salvoViewContent');
    const viewerTitle = document.getElementById('salvoViewerTitle');
    const btnEdit = document.getElementById('btnEditSalvo');
    const viewArea = document.getElementById('salvoViewArea');
    const editArea = document.getElementById('salvoEditArea');

    if (!viewContainer || !viewContent) {
        toast.error('Erro: Elementos de visualização não encontrados');
        return;
    }

    // Renderiza conteúdo markdown com formatação de speaker
    let htmlContent;
    let conteudo = item.conteudo || '';

    // Formata speakers se for transcrição (padrão "Nome:" -> quebra de linha)
    if (item.tipo?.toLowerCase().includes('transcri')) {
        // Adiciona quebras de linha antes de cada speaker
        conteudo = conteudo.replace(/\*\*([^*]+):\*\*/g, '\n\n**$1:**');
        // Se não tiver markdown, formata manualmente
        if (!conteudo.includes('**')) {
            conteudo = conteudo.replace(/(Operador\s*\w*|Motorista|Cliente|Atendente):/gi, '\n\n**$1:**');
        }
    }

    if (typeof marked !== 'undefined') {
        htmlContent = marked.parse(conteudo);
    } else {
        // Fallback: formata básico
        htmlContent = conteudo
            .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
            .replace(/\n/g, '<br>');
        htmlContent = `<div style="white-space: pre-wrap;">${htmlContent}</div>`;
    }

    // Atualiza título
    if (viewerTitle) {
        viewerTitle.innerHTML = `<i data-lucide="file-text"></i> ${item.tipo} - ${item.arquivo || 'Documento'}`;
    }

    // Exibe conteúdo
    viewContent.innerHTML = htmlContent;

    // --- DETECTOR DE ÁUDIO NO CONTEÚDO ---
    const audioRegex = /Arquivo salvo em: \[(.*?)\]\((.*?)\)/;
    const match = conteudo.match(audioRegex);

    // Se encontrar link de áudio e for do tipo Oitiva, adiciona player
    if (match && item.tipo === 'Oitiva') {
        const audioUrl = match[2];
        const playerHtml = `
            <div class="audio-player-container" style="margin-bottom: 20px; padding: 15px; background: rgba(0,0,0,0.05); border-radius: 8px;">
                <p style="margin-bottom: 10px; font-weight: bold; font-size: 0.9em;"><i data-lucide="headphones"></i> Áudio Arquivado</p>
                <audio controls style="width: 100%;">
                    <source src="${audioUrl}" type="audio/wav">
                    <source src="${audioUrl}" type="audio/mpeg">
                    Seu navegador não suporta áudio.
                </audio>
            </div>
        `;
        viewContent.innerHTML = playerHtml + viewContent.innerHTML;
    }
    // -------------------------------------

    // Mostra container e esconde empty state
    if (emptyState) emptyState.classList.add('hidden');
    viewContainer.classList.remove('hidden');
    if (viewArea) viewArea.classList.remove('hidden');
    if (editArea) editArea.classList.add('hidden');

    // Mostra botão de edição
    if (btnEdit) btnEdit.classList.remove('hidden');

    // Destaca o item selecionado na lista
    document.querySelectorAll('.saved-file-item').forEach(el => {
        el.classList.remove('selected');
        if (el.dataset.id == id) {
            el.classList.add('selected');
        }
    });

    refreshIcons();
}

// ============================================
// FUNÇÕES DE EDIÇÃO - importadas de salvosEditor.js
// ============================================
// toggleEditSalvo, salvarEdicaoSalvo, cancelarEdicaoSalvo
// são exportadas via re-export no topo do arquivo

/**
 * Fecha a visualização inline
 */
export function fecharVisualizacaoSalvo() {
    resetSalvosState();

    const emptyState = document.getElementById('salvoEmptyState');
    const viewContainer = document.getElementById('salvoViewContainer');
    const btnEdit = document.getElementById('btnEditSalvo');

    if (emptyState) emptyState.classList.remove('hidden');
    if (viewContainer) viewContainer.classList.add('hidden');
    if (btnEdit) btnEdit.classList.add('hidden');

    // Remove seleção da lista
    document.querySelectorAll('.saved-file-item').forEach(el => {
        el.classList.remove('selected');
    });

    refreshIcons();
}

/**
 * Copia texto do documento atual
 */
export function copiarTextoSalvo() {
    const itemAtual = getItemAtual();
    if (!itemAtual) {
        toast.error('Nenhum documento selecionado.');
        return;
    }

    navigator.clipboard.writeText(itemAtual.conteudo)
        .then(() => toast.success('Texto copiado!'))
        .catch(() => toast.error('Erro ao copiar texto.'));
}

/**
 * Exporta documento atual para PDF
 */
export function exportarSalvoAtual() {
    const itemAtual = getItemAtual();
    if (!itemAtual) {
        toast.error('Nenhum documento selecionado.');
        return;
    }
    exportarItemPDF(itemAtual);
}

/**
 * Exporta documento atual para Word
 */
export function exportarSalvoAtualWord() {
    const itemAtual = getItemAtual();
    if (!itemAtual) {
        toast.error('Nenhum documento selecionado.');
        return;
    }
    exportarItemWord(itemAtual);
}

/**
 * Exporta arquivo salvo para PDF
 * @param {number|string} id - ID do arquivo
 */
export async function exportarSalvo(id) {
    const item = await getAnalise(id);
    if (!item) {
        toast.error('Arquivo não encontrado.');
        return;
    }

    exportarItemPDF(item);
}

/**
 * Exporta arquivo salvo para Word
 * @param {number|string} id - ID do arquivo
 */
export async function exportarSalvoWord(id) {
    const item = await getAnalise(id);
    if (!item) {
        toast.error('Arquivo não encontrado.');
        return;
    }

    exportarItemWord(item);
}

/**
 * Exclui arquivo salvo
 * @param {number|string} id - ID do arquivo
 */
export async function excluirSalvo(id) {
    if (!confirm('Tem certeza que deseja excluir este arquivo?')) return;

    // Se estiver visualizando este item, fecha a visualização
    const itemAtual = getItemAtual();
    if (itemAtual && itemAtual.id === id) {
        fecharVisualizacaoSalvo();
    }

    await deleteAnalise(id);
    toast.success('Excluído');
    atualizarListaSalvos();
}
