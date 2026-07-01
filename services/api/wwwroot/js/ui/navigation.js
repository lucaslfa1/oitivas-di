/**
 * Navegação e Controle de Abas
 */

// Callback para quando salvos for aberto (evita importação circular)
let onSalvosOpenCallback = null;

import { getLatestDraft } from '../core/drafts.js';
import { refreshIcons } from '../core/utils.js';

/**
 * Define callback para quando aba salvos for aberta
 * @param {Function} callback - Função a ser chamada
 */
export function setOnSalvosOpen(callback) {
    onSalvosOpenCallback = callback;
}

/**
 * Navega para um painel específico
 * @param {string} mode - ID do painel (audio, foto, video, salvos)
 */
export function setMode(mode) {
    console.log("📂 Navegando para:", mode);

    // Esconde todos os painéis
    document.querySelectorAll('.panel').forEach(p => {
        p.classList.remove('active');
        p.classList.add('hidden');
    });

    // Remove active de todos os botões do menu
    document.querySelectorAll('.menu-item').forEach(b => b.classList.remove('active'));

    // Mostra o painel selecionado
    const panel = document.getElementById(`panel-${mode}`);
    if (panel) {
        panel.classList.remove('hidden');
        panel.classList.add('active');
        console.log(`✅ Painel panel-${mode} ativado`);

        // Carrega arquivos salvos quando abrir a aba
        if (mode === 'salvos' && onSalvosOpenCallback) {
            onSalvosOpenCallback();
        }
    } else {
        console.error(`❌ Painel não encontrado: panel-${mode}`);
    }

    // Ao trocar de painel, tenta restaurar o último draft daquele tipo
    /* 
    // DESATIVADO: Causava confusão ao manter oitivas antigas. O usuário prefere iniciar limpo.
    try {
        if (mode === 'audio') {
            const d = getLatestDraft('transcricao');
            if (d && d.conteudo) {
                // somente restaura se a UI ainda estiver vazia
                const transView = document.getElementById('transcricaoView');
                const transContainer = document.getElementById('transcricaoContainer');
                const emptyState = document.getElementById('audioEmptyState');
                if (transView && transContainer && emptyState && !transView.innerText.trim()) {
                    emptyState.classList.add('hidden');
                    transContainer.classList.remove('hidden');
                    // mantém o formato simples (sem reparse complexo)
                    transView.innerText = d.conteudo;
                    refreshIcons();
                }
            }
        }

        if (mode === 'foto' || mode === 'video') {
            const d = getLatestDraft(mode);
            const out = document.getElementById(`output${mode === 'foto' ? 'Foto' : 'Video'}`);
            if (d && d.conteudo && out && !out.innerText.trim()) {
                if (typeof marked !== 'undefined') {
                    out.innerHTML = marked.parse(d.conteudo);
                } else {
                    out.innerHTML = `<pre>${d.conteudo}</pre>`;
                }

                const actions = document.getElementById(`actions${mode === 'foto' ? 'Foto' : 'Video'}`);
                if (actions) actions.classList.remove('hidden');

                const btnEdit = document.getElementById(`btnEdit${mode === 'foto' ? 'Foto' : 'Video'}`);
                if (btnEdit) btnEdit.classList.remove('hidden');

                refreshIcons();
            }
        }
    } catch (e) {
        console.warn('Falha ao restaurar draft:', e);
    }
    */

    // Ativa o botão do menu correspondente
    const navBtn = document.getElementById(`nav-${mode}`);
    if (navBtn) {
        navBtn.classList.add('active');
    }
}

/**
 * Inicializa a navegação
 */
export function initNavigation() {
    // A navegação é controlada pelos botões do menu via onclick
    // Esta função pode ser expandida para adicionar mais lógica se necessário
    console.log("✅ Navegação inicializada");
}
