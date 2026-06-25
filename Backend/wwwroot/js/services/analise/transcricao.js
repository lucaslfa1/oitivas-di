/**
 * Fluxo: transcrição (áudio)
 */

import { gerarTranscricaoAPI } from '../../api/sinistroApi.js?v=3';
import { initSignalR } from '../../services/signalrService.js';
import { getFile, setTranscricao, setTranscricaoValidada, getTranscricao } from '../../core/state.js';
import { refreshIcons } from '../../core/utils.js';
import { formatarTranscricao } from '../transcricao.js';
import { getLoaderHTML, getErrorHTML } from '../../ui/modal.js';
import { toast } from '../../ui/toast.js';
import { saveDraft } from '../../core/drafts.js';

export async function gerarRelatorioPericial() {
  const transcricao = getTranscricao();
  if (!transcricao) {
    toast.warning('Gere a transcrição primeiro.');
    return;
  }

  const btn = document.getElementById('btnGerarRelatorio');
  const originalText = btn?.innerHTML ?? '';
  const laudoView = document.getElementById('laudoView');
  const laudoContainer = document.getElementById('laudoContainer');

  try {
    if (btn) {
      btn.disabled = true;
      btn.innerHTML = '<i data-lucide="loader-2" class="spin"></i> GERANDO LAUDO...';
    }

    if (laudoView) laudoView.innerHTML = getLoaderHTML();
    if (laudoContainer) laudoContainer.classList.remove('hidden');

    // Rola até o laudo
    laudoContainer.scrollIntoView({ behavior: 'smooth' });

    // Import dinâmico para evitar dependência circular
    const { gerarLaudoAPI } = await import('../../api/sinistroApi.js?v=3');

    // Payload simplificado sem tipoOperacao
    const data = await gerarLaudoAPI({
      transcricao: transcricao,
      duracao: document.getElementById('timeTotal')?.innerText || "Não informada",
      contexto: "" // Contexto manual se houver campo futuro
    });

    const { renderizarLaudoComDados } = await import('./relatorio.js');
    renderizarLaudoComDados(data.laudo);

    const btnEdit = document.getElementById('btnEditLaudo');
    if (btnEdit) btnEdit.classList.remove('hidden');

    const actionsLaudo = document.getElementById('actionsLaudo');
    if (actionsLaudo) actionsLaudo.classList.remove('hidden');

    refreshIcons();
    toast.success('Laudo gerado com sucesso!');

  } catch (err) {
    console.error('Erro ao gerar laudo:', err);
    if (laudoView) laudoView.innerHTML = getErrorHTML(err.message);
    toast.error('Falha ao gerar laudo: ' + err.message);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.innerHTML = originalText;
    }
    refreshIcons();
  }
}

export async function gerarTranscricao() {
  const file = getFile('audio');
  if (!file) {
    toast.warning('Selecione um arquivo de áudio primeiro.');
    return;
  }

  const btn = document.getElementById('btnGerarTranscricao');
  const transcricaoContainer = document.getElementById('transcricaoContainer');
  const transcricaoView = document.getElementById('transcricaoView');
  const originalText = btn?.innerHTML ?? '';

  try {
    if (btn) {
      btn.disabled = true;
      btn.innerHTML = '<i data-lucide="loader-2" class="spin"></i> TRANSCREVENDO...';
    }
    refreshIcons();

    const emptyState = document.getElementById('audioEmptyState');
    if (emptyState) emptyState.classList.add('hidden');
    if (transcricaoContainer) transcricaoContainer.classList.remove('hidden');

    if (transcricaoView) transcricaoView.innerHTML = getLoaderHTML();

    const progressContainer = document.getElementById('progressContainer');
    const progressContainerMerge = document.getElementById('progressContainerMerge');

    // Atualiza ambas as barras (simples e robusto)
    const updateProgress = (_msg, pct) => {
      const els = [
        { status: 'progressStatus', percent: 'progressPercent', bar: 'progressBar' },
        { status: 'progressStatusMerge', percent: 'progressPercentMerge', bar: 'progressBarMerge' }
      ];

      els.forEach(el => {
        const s = document.getElementById(el.status);
        const p = document.getElementById(el.percent);
        const b = document.getElementById(el.bar);
        if (s) s.innerText = '';
        if (p) p.innerText = `${pct}%`;
        if (b) b.style.width = `${pct}%`;
      });
    };

    if (progressContainer) progressContainer.classList.remove('hidden');
    const tabMerge = document.getElementById('tab-merge');
    if (progressContainerMerge && tabMerge && !tabMerge.classList.contains('hidden')) {
      progressContainerMerge.classList.remove('hidden');
    }

    updateProgress("", 0);

    // Conectar ao SignalR
    const connectionId = await initSignalR(updateProgress);

    const data = await gerarTranscricaoAPI(file, connectionId);

    setTranscricao(data.transcricao);
    setTranscricaoValidada(true);

    try {
      const fileName = file?.name || 'N/A';
      saveDraft('transcricao', data.transcricao, { arquivo: fileName, titulo: 'Transcrição (rascunho)' });
    } catch (e) {
      console.warn('Falha ao salvar draft de transcrição:', e);
    }

    const htmlFormatado = formatarTranscricao(data.transcricao);
    if (transcricaoView) transcricaoView.innerHTML = htmlFormatado;

    const btnEdit = document.getElementById('btnEditTranscricao');
    if (btnEdit) btnEdit.classList.remove('hidden');

    // Mostrar botões de ação após gerar transcrição
    const actionsTranscricao = document.getElementById('actionsTranscricao');
    if (actionsTranscricao) actionsTranscricao.classList.remove('hidden');

    refreshIcons();

    // --- EXTRAÇÃO AUTOMÁTICA DE DADOS (Background) ---
    try {
      const insightsContainer = document.getElementById('insightsContainer');
      const insightsContent = document.getElementById('insightsContent');

      if (insightsContainer && insightsContent) {
        // Mostra loading discreto ou apenas inicia
        console.log("🔍 Iniciando extração de dados em background...");

        // Chama API
        import('../../api/sinistroApi.js?v=3').then(async ({ extrairDadosAPI }) => {
          const dados = await extrairDadosAPI(data.transcricao);

          if (dados && Object.keys(dados).length > 0) {
            insightsContent.innerHTML = ''; // Limpa

            for (const [key, value] of Object.entries(dados)) {
              if (value && value !== 'null' && value !== 'Não mencionado') {
                const label = key.replace(/_/g, ' ');
                const div = document.createElement('div');
                div.className = 'insight-item';
                div.innerHTML = `
                                <span class="insight-label">${label}</span>
                                <span class="insight-value">${value}</span>
                            `;
                insightsContent.appendChild(div);
              }
            }

            // Só mostra se tiver dados
            if (insightsContent.children.length > 0) {
              insightsContainer.classList.remove('hidden');
              refreshIcons();
              toast.success("Insights extraídos com sucesso!");
            }
          }
        });
      }
    } catch (e) {
      console.warn("Erro na extração automática:", e);
    }

  } catch (err) {
    console.error('Erro na transcrição:', err);
    if (transcricaoView) transcricaoView.innerHTML = getErrorHTML(err.message);
    toast.error('Falha ao gerar transcrição: ' + err.message);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.innerHTML = originalText;
    }

    if (progressContainer) {
      setTimeout(() => progressContainer.classList.add('hidden'), 3000);
    }
    const progressContainerMerge = document.getElementById('progressContainerMerge');
    if (progressContainerMerge) {
      setTimeout(() => progressContainerMerge.classList.add('hidden'), 3000);
    }
    refreshIcons();
  }
}

export function abrirTranscricao() {
  const transcricao = getTranscricao();
  if (!transcricao) {
    toast.info('Nenhuma transcrição disponível. Clique em "Gerar Transcrição" primeiro.');
    return;
  }

  const transcricaoContainer = document.getElementById('transcricaoContainer');
  if (transcricaoContainer) {
    transcricaoContainer.scrollIntoView({ behavior: 'smooth' });
  }
}
