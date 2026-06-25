/**
 * Fluxo: processar análise de foto/vídeo
 */

import { enviarParaAnalise } from '../../api/sinistroApi.js?v=3';
import { getFile, setLaudo } from '../../core/state.js';
import { capitalize, refreshIcons, getMediaDuration } from '../../core/utils.js';
import { getLoaderHTML, getErrorHTML } from '../../ui/modal.js';
import { toast } from '../../ui/toast.js';
import { saveDraft } from '../../core/drafts.js';

export async function processar(tipo) {
  const file = getFile(tipo);
  if (!file) {
    toast.warning(`Por favor, selecione um arquivo de ${tipo} primeiro.`);
    return;
  }

  const btn = document.getElementById(`btn${capitalize(tipo)}`);
  const output = document.getElementById(`output${capitalize(tipo)}`);
  const contextEl = document.getElementById(`ctx${capitalize(tipo)}`);

  const originalText = btn?.innerHTML ?? '';
  if (btn) {
    btn.disabled = true;
    btn.innerHTML = '<i data-lucide="loader-2" class="spin"></i> PROCESSANDO...';
  }
  refreshIcons();

  if (output) output.innerHTML = getLoaderHTML();

  try {
    let duracao = 'Não informada';
    if (tipo === 'video') {
      try { duracao = await getMediaDuration(file); } catch (e) { console.warn('Erro duração:', e); }
    }

    const contexto = contextEl ? contextEl.value : '';
    const data = await enviarParaAnalise(file, tipo, contexto, duracao);
    const markdown = data.markdown || '';

    setLaudo(tipo, markdown);

    try {
      const fileName = file?.name || 'N/A';
      saveDraft(tipo, markdown, { arquivo: fileName, titulo: `Laudo (${tipo})` });
    } catch (e) {
      console.warn('Falha ao salvar draft de laudo:', e);
    }

    if (output) {
      if (typeof marked !== 'undefined') output.innerHTML = marked.parse(markdown);
      else output.innerHTML = `<pre>${markdown}</pre>`;
    }

    const actions = document.getElementById(`actions${capitalize(tipo)}`);
    if (actions) {
      actions.classList.remove('hidden');
      refreshIcons();
    }

    const btnEdit = document.getElementById(`btnEdit${capitalize(tipo)}`);
    if (btnEdit) btnEdit.classList.remove('hidden');



  } catch (err) {
    console.error('Erro no processamento:', err);
    if (output) output.innerHTML = getErrorHTML(err.message);
    toast.error('Erro ao processar arquivo.');
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.innerHTML = originalText;
    }
    refreshIcons();
  }
}
