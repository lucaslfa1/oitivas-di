/**
 * Gerenciamento de Modais
 */

import { refreshIcons } from '../core/utils.js';

// Callback armazenado para ação do botão principal
let currentModalCallback = null;
let currentModalType = null;
let originalContent = '';

/**
 * Abre o modal de transcrição com conteúdo HTML
 * @param {string} htmlContent - Conteúdo HTML para exibir
 * @param {string} [titulo] - Título opcional do modal
 */
export function abrirModalTranscricao(htmlContent, titulo = null) {
    const modal = document.getElementById('modalTranscricao');
    const output = document.getElementById('outputTranscricao');
    const header = modal?.querySelector('.modal-header h2');

    if (!modal || !output) {
        console.error('❌ Modal de transcrição não encontrado');
        return;
    }

    if (titulo && header) {
        header.innerHTML = titulo;
    }

    output.innerHTML = htmlContent;
    modal.classList.remove('hidden');
    document.body.style.overflow = 'hidden';

    // Esconder área de edição se existir
    const editArea = document.getElementById('modalEditArea');
    if (editArea) editArea.classList.add('hidden');
    const viewArea = document.getElementById('outputTranscricao');
    if (viewArea) viewArea.classList.remove('hidden');

    refreshIcons();
    console.log('📜 Modal aberto');
}

/**
 * Abre modal editável para transcrição ou laudo
 * @param {string} titulo - Título do modal
 * @param {string} conteudo - Conteúdo inicial (markdown)
 * @param {string} tipo - 'transcricao' | 'laudo'
 * @param {function} onSave - Callback ao salvar (recebe texto editado)
 * @param {function} [onAction] - Callback opcional para ação principal (ex: "Gerar Laudo")
 * @param {string} [actionLabel] - Label do botão de ação
 */
export function abrirModalEditavel(titulo, conteudo, tipo, onSave, onAction = null, actionLabel = null) {
    const modal = document.getElementById('modalTranscricao');
    const header = modal?.querySelector('.modal-header h2');

    if (!modal) {
        console.error('❌ Modal não encontrado');
        return;
    }

    // Salvar estado
    currentModalCallback = onSave;
    currentModalType = tipo;
    originalContent = conteudo;

    // Atualizar título
    if (header) {
        header.innerHTML = `<i data-lucide="edit-3"></i> ${titulo}`;
    }

    // Criar área de edição se não existir
    let editArea = document.getElementById('modalEditArea');
    if (!editArea) {
        editArea = document.createElement('div');
        editArea.id = 'modalEditArea';
        editArea.className = 'modal-edit-area';
        modal.querySelector('.modal-body').appendChild(editArea);
    }

    // Esconder área de visualização normal
    const viewArea = document.getElementById('outputTranscricao');
    if (viewArea) viewArea.classList.add('hidden');

    // Configurar área de edição
    editArea.innerHTML = `
        <div class="edit-container">
            <div class="edit-panel">
                <div class="edit-panel-header">
                    <span><i data-lucide="code"></i> Editor</span>
                    <small class="text-muted">Edite o texto abaixo</small>
                </div>
                <textarea id="modalEditorTextarea" class="modal-editor">${escapeHtml(conteudo)}</textarea>
            </div>
            <div class="preview-panel">
                <div class="edit-panel-header">
                    <span><i data-lucide="eye"></i> Preview</span>
                    <small class="text-muted">Visualização em tempo real</small>
                </div>
                <div id="modalEditorPreview" class="modal-preview">${renderMarkdown(conteudo)}</div>
            </div>
        </div>
        <div class="edit-actions">
            <button id="btnModalCancelar" class="btn-secondary">
                <i data-lucide="x"></i> Cancelar
            </button>
            <button id="btnModalSalvar" class="btn-primary">
                <i data-lucide="save"></i> Salvar Alterações
            </button>
            ${onAction ? `
                <button id="btnModalAction" class="btn-success">
                    <i data-lucide="sparkles"></i> ${actionLabel || 'Continuar'}
                </button>
            ` : ''}
        </div>
    `;

    editArea.classList.remove('hidden');
    modal.classList.remove('hidden');
    document.body.style.overflow = 'hidden';

    // Event listeners
    const textarea = document.getElementById('modalEditorTextarea');
    const preview = document.getElementById('modalEditorPreview');

    // Preview em tempo real
    textarea.addEventListener('input', () => {
        preview.innerHTML = renderMarkdown(textarea.value);
    });

    // Botão cancelar
    document.getElementById('btnModalCancelar').onclick = () => {
        if (textarea.value !== originalContent) {
            if (!confirm('Você tem alterações não salvas. Deseja sair mesmo assim?')) {
                return;
            }
        }
        fecharModalTranscricao();
    };

    // Botão salvar
    document.getElementById('btnModalSalvar').onclick = () => {
        const textoEditado = textarea.value;
        if (currentModalCallback) {
            currentModalCallback(textoEditado);
        }
        fecharModalTranscricao();
    };

    // Botão ação (opcional)
    if (onAction) {
        document.getElementById('btnModalAction').onclick = () => {
            const textoEditado = textarea.value;
            if (currentModalCallback) {
                currentModalCallback(textoEditado);
            }
            onAction(textoEditado);
            fecharModalTranscricao();
        };
    }

    refreshIcons();
    console.log(`📝 Modal editável aberto [${tipo}]`);
}

/**
 * Renderiza markdown para HTML
 * @param {string} markdown - Texto markdown
 * @returns {string} HTML renderizado
 */
function renderMarkdown(markdown) {
    if (!markdown) return '<p class="text-muted">Nenhum conteúdo</p>';

    if (typeof marked !== 'undefined') {
        return marked.parse(markdown);
    }

    // Fallback básico se marked não estiver disponível
    return `<pre style="white-space:pre-wrap">${escapeHtml(markdown)}</pre>`;
}

/**
 * Escapa HTML para exibição segura
 * @param {string} text - Texto a escapar
 * @returns {string} Texto escapado
 */
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

/**
 * Fecha o modal de transcrição
 */
export function fecharModalTranscricao() {
    const modal = document.getElementById('modalTranscricao');
    if (modal) {
        modal.classList.add('hidden');
        document.body.style.overflow = '';

        // Limpar estado
        currentModalCallback = null;
        currentModalType = null;
        originalContent = '';

        // Esconder área de edição
        const editArea = document.getElementById('modalEditArea');
        if (editArea) editArea.classList.add('hidden');

        console.log('📜 Modal fechado');
    }
}

/**
 * Fecha modal ao clicar no overlay
 * @param {Event} event - Evento de clique
 */
export function fecharModalOverlay(event) {
    if (event.target.id === 'modalTranscricao') {
        // Verificar se há alterações não salvas
        const textarea = document.getElementById('modalEditorTextarea');
        if (textarea && textarea.value !== originalContent) {
            if (!confirm('Você tem alterações não salvas. Deseja sair mesmo assim?')) {
                return;
            }
        }
        fecharModalTranscricao();
    }
}

/**
 * Gera HTML do loader animado para transcrição
 * @returns {string} HTML do loader
 */
export function getLoaderHTML(mensagem = 'Processando...') {
    return `
        <div class="loading-container">
            <div class="loading-bar-container">
                <div class="loading-bar-progress"></div>
            </div>
            <div class="loading-text" id="loadingText">
                ${mensagem}
            </div>
        </div>
    `;
}

/**
 * Gera HTML de erro para o modal
 * @param {string} message - Mensagem de erro
 * @returns {string} HTML de erro
 */
export function getErrorHTML(message) {
    // Detecta se é manutenção
    const isMaintenance = message.includes('Manutenção') || message.includes('manutenção') || message.includes('maintenance');
    const titulo = isMaintenance ? '🔧 Em Manutenção' : 'Erro no Processamento';
    const cor = isMaintenance ? 'var(--warning)' : 'var(--danger)';
    const icone = isMaintenance ? 'wrench' : 'alert-circle';

    return `
        <div class="loading-container" style="color: ${cor};">
            <i data-lucide="${icone}" style="width: 48px; height: 48px;"></i>
            <div class="loading-text">
                <h4 style="color: ${cor};">${titulo}</h4>
                <p>${message}</p>
            </div>
        </div>
    `;
}

/**
 * Atualiza etapa visual do loader
 * @param {number} etapaAtual - Número da etapa atual (1-4)
 * @param {number} proximaEtapa - Número da próxima etapa (2-4)
 */
export function atualizarEtapaLoader(etapaAtual, proximaEtapa) {
    const loadingText = document.getElementById('loadingText');
    if (!loadingText) return;

    const mensagens = {
        1: 'Enviando áudio...',
        2: 'Processando arquivo...',
        3: 'IA analisando conteúdo...',
        4: 'Formatando transcrição...'
    };

    if (mensagens[proximaEtapa]) {
        loadingText.innerText = mensagens[proximaEtapa];
    }
}

export function initModal() {
    // Fecha modal com ESC
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            const textarea = document.getElementById('modalEditorTextarea');
            if (textarea && textarea.value !== originalContent) {
                if (!confirm('Você tem alterações não salvas. Deseja sair mesmo assim?')) {
                    return;
                }
            }
            fecharModalTranscricao();
        }
    });

    console.log("✅ Modal inicializado");
}

