/**
 * Fluxo: geração de laudo pericial a partir da transcrição
 */

import { gerarLaudoTecnicoAPI } from '../../api/sinistroApi.js?v=3';
import { getFile, getTranscricao, setLaudo } from '../../core/state.js';
import { refreshIcons, getMediaDuration } from '../../core/utils.js';
import { toast } from '../../ui/toast.js';
import { saveDraft } from '../../core/drafts.js';
import { renderMarkdown, renderErrorMessage, renderLoadingBar } from '../../core/render.js';
import { setLoadingButton } from '../../ui/loadingButton.js';

export async function gerarRelatorioPericial() {
  const transcricao = getTranscricao();
  if (!transcricao) {
    toast.warning('Por favor, gere a transcrição primeiro.');
    return;
  }

  const btn = document.getElementById('btnGerarRelatorio');
  const laudoContainer = document.getElementById('laudoContainer');
  const laudoView = document.getElementById('laudoView');
  const file = getFile('audio');
  const contextEl = document.getElementById('ctxAudio');

  const originalText = btn ? btn.innerHTML : 'GERAR LAUDO PERICIAL';

  setLoadingButton(btn, {
    loading: true,
    loadingHtml: '<i data-lucide="loader-2" class="spin"></i> ANALISANDO...'
  });

  if (laudoContainer) laudoContainer.classList.remove('hidden');
  renderLoadingBar(laudoView);

  try {
    let duracao = 'Não informada';
    if (file) {
      try {
        duracao = await getMediaDuration(file);
      } catch (e) {
        console.warn('Erro ao obter duração:', e);
      }
    }

    const contexto = contextEl ? contextEl.value : '';

    const data = await gerarLaudoTecnicoAPI(transcricao, duracao, contexto);

    setLaudo('audio', data.markdown);

    try {
      const fileName = file?.name || 'N/A';
      saveDraft('audio', data.markdown, { arquivo: fileName, titulo: 'Laudo (áudio)' });
    } catch (e) {
      console.warn('Falha ao salvar draft de laudo (áudio):', e);
    }

    renderizarLaudoComDados(data.markdown);

    const btnEdit = document.getElementById('btnEditLaudo');
    if (btnEdit) btnEdit.classList.remove('hidden');



  } catch (err) {
    console.error('Erro ao gerar relatório:', err);
    toast.error(`Erro ao gerar relatório: ${err.message}`);
    renderErrorMessage(laudoView, `Falha na análise: ${err.message}`);
  } finally {
    setLoadingButton(btn, { loading: false, defaultHtml: originalText });
  }
}

export function renderizarLaudoComDados(markdown) {
  // Separa dados identificados do corpo do laudo
  const partes = markdown.split('---SEPARADOR_DADOS---');
  let dadosIdentificados = '';
  let corpoLaudo = markdown;

  if (partes.length > 1) {
    dadosIdentificados = partes[0].trim();
    corpoLaudo = partes[1].trim();
  }

  // Renderiza tabela de dados (se houver)
  const dadosContainer = document.getElementById('dadosIdentificadosContainer');
  const dadosView = document.getElementById('dadosIdentificadosView');

  if (dadosIdentificados && dadosView) {
    if (dadosContainer) dadosContainer.classList.remove('hidden');
    renderMarkdown(dadosView, dadosIdentificados);
  } else if (dadosContainer) {
    dadosContainer.classList.add('hidden');
  }

  // Renderiza corpo do laudo
  const laudoView = document.getElementById('laudoView');
  if (laudoView) {
    renderMarkdown(laudoView, corpoLaudo);
  }

  // Mostrar botões de ação após gerar laudo
  const actionsLaudo = document.getElementById('actionsLaudo');
  if (actionsLaudo) {
    actionsLaudo.classList.remove('hidden');
    refreshIcons();
  }
}
