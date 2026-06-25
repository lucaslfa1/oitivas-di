/**
 * Helpers de renderização no DOM.
 */

function escapeHtml(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

/**
 * Renderiza markdown usando `marked` quando disponível, com fallback seguro em <pre>.
 */
export function renderMarkdown(targetEl, markdown) {
  if (!targetEl) return;
  const md = markdown ?? '';

  if (typeof marked !== 'undefined' && typeof marked?.parse === 'function') {
    targetEl.innerHTML = marked.parse(md);
    return;
  }

  targetEl.innerHTML = `<pre style="white-space: pre-wrap;">${escapeHtml(md)}</pre>`;
}

export function renderErrorMessage(targetEl, message) {
  if (!targetEl) return;
  const msg = escapeHtml(message ?? 'Erro desconhecido');
  targetEl.innerHTML = `<p style="color:red; text-align:center;">❌ ${msg}</p>`;
}

export function renderLoadingBar(targetEl) {
  if (!targetEl) return;
  targetEl.innerHTML = '<div class="loading-bar-container"><div class="loading-bar-progress"></div></div>';
}
