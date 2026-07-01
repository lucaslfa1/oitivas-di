/**
 * Serviço de Salvamento
 * Salva análises no servidor e localStorage
 */

import { salvarAnaliseAPI } from '../api/sinistroApi.js';
import { getFile, getLaudo, getTranscricao } from '../core/state.js';
import { TIPO_NOMES } from '../config/constants.js';
import { capitalize, refreshIcons } from '../core/utils.js';
import { toast } from '../ui/toast.js';
import { clearDrafts } from '../core/drafts.js';

/**
 * Salva análise no sistema
 * @param {string} type - Tipo: audio, foto, video
 */
export async function salvarAnalise(type) {
    let laudo = getLaudo(type);

    if (!laudo) {
        toast.warning('Nenhuma análise para salvar. Execute a análise primeiro.');
        return;
    }

    const btn = document.querySelector(`#actions${capitalize(type)} [onclick*="salvarAnalise"]`);
    const originalText = btn ? btn.innerHTML : '';

    // Estado de loading
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i data-lucide="loader-2" class="spin"></i> Salvando...';
        refreshIcons();
    }

    const file = getFile(type);
    const dados = {
        tipo: TIPO_NOMES[type] || type,
        conteudo: laudo,
        arquivo: file ? file.name : 'N/A',
        dataAnalise: new Date().toISOString()
    };
    try {
        const result = await salvarAnaliseAPI(dados);
        console.log('💾 Análise salva:', result);
        toast.success('Análise salva com sucesso!');

        // Se salvou no servidor, remove drafts desse tipo
        try { clearDrafts(type); } catch { }

        // Feedback de sucesso
        if (btn) {
            btn.innerHTML = '<i data-lucide="check"></i> Salvo!';
            btn.style.background = '#16a34a';
            refreshIcons();

            setTimeout(() => {
                btn.innerHTML = originalText;
                btn.style.background = '';
                refreshIcons();
            }, 2000);
        }
    } catch (err) {
        console.error('❌ Erro ao salvar:', err);

        // Salva localmente como fallback
        salvarLocal(type, dados);
        toast.info('Salvo localmente (servidor indisponível)', { title: 'Backup Local' });

        if (btn) {
            btn.innerHTML = '<i data-lucide="check"></i> Salvo Local!';
            refreshIcons();

            setTimeout(() => {
                btn.innerHTML = originalText;
                refreshIcons();
            }, 2000);
        }
    } finally {
        if (btn) {
            btn.disabled = false;
        }
    }
}

/**
 * Salva transcrição no sistema
 */
export async function salvarTranscricao() {
    const transcricao = getTranscricao();

    if (!transcricao) {
        toast.warning('Nenhuma transcrição para salvar.');
        return;
    }

    const btn = document.querySelector('.modal-footer .btn-salvar');
    const originalText = btn ? btn.innerHTML : '';

    // Estado de loading
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i data-lucide="loader-2" class="spin"></i> Salvando...';
        refreshIcons();
    }

    const file = getFile('audio');
    const dados = {
        tipo: 'Transcrição',
        conteudo: transcricao,
        arquivo: file ? file.name : 'N/A',
        dataAnalise: new Date().toISOString()
    };
    try {
        const result = await salvarAnaliseAPI(dados);
        console.log('💾 Transcrição salva:', result);
        toast.success('Transcrição salva com sucesso!');

        // Se salvou no servidor, remove drafts de transcrição
        try { clearDrafts('transcricao'); } catch { }

        // Feedback de sucesso
        if (btn) {
            btn.innerHTML = '<i data-lucide="check"></i> Salvo!';
            btn.style.background = '#16a34a';
            refreshIcons();

            setTimeout(() => {
                btn.innerHTML = originalText;
                btn.style.background = '';
                refreshIcons();
            }, 2000);
        }
    } catch (err) {
        console.error('❌ Erro ao salvar transcrição:', err);

        // Salva localmente como fallback
        salvarLocal('transcricao', dados);
        toast.info('Salvo localmente (servidor indisponível)', { title: 'Backup Local' });

        if (btn) {
            btn.innerHTML = '<i data-lucide="check"></i> Salvo Local!';
            refreshIcons();

            setTimeout(() => {
                btn.innerHTML = originalText;
                refreshIcons();
            }, 2000);
        }
    } finally {
        if (btn) {
            btn.disabled = false;
        }
    }
}

/**
 * Salva localmente no localStorage (fallback)
 * @param {string} type - Tipo da análise
 * @param {Object} dados - Dados a salvar
 */
export function salvarLocal(type, dados) {
    try {
        // Recupera análises existentes
        const analises = JSON.parse(localStorage.getItem('sinistroIA_analises') || '[]');

        // Adiciona nova análise
        analises.push({
            id: Date.now(),
            ...dados
        });

        // Salva no localStorage
        localStorage.setItem('sinistroIA_analises', JSON.stringify(analises));

        console.log('💾 Salvo localmente:', dados.tipo);
    } catch (err) {
        console.error('❌ Erro ao salvar localmente:', err);
    }
}
