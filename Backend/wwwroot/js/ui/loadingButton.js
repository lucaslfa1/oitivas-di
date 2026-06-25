/**
 * Utilitário simples para estado de loading em botões.
 */

import { refreshIcons } from '../core/utils.js';

export function setLoadingButton(buttonEl, { loading, loadingHtml, defaultHtml } = {}) {
  if (!buttonEl) return;

  if (loading) {
    buttonEl.disabled = true;
    if (loadingHtml != null) buttonEl.innerHTML = loadingHtml;
    refreshIcons();
    return;
  }

  buttonEl.disabled = false;
  if (defaultHtml != null) buttonEl.innerHTML = defaultHtml;
  refreshIcons();
}
