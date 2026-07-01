/**
 * Submódulo: editor/edição inline de item salvo.
 */

import { toast } from '../../ui/toast.js';
import { refreshIcons } from '../../core/utils.js';
import { getItemAtual, setItemAtual, getConteudoOriginal, setIsEditando, getIsEditando } from './salvosState.js';
import { updateAnaliseLocal } from '../../services/analisesRepositoryUpdate.js';

export function toggleEditSalvo() {
  const itemAtual = getItemAtual();
  if (!itemAtual) {
    toast.error('Nenhum documento selecionado.');
    return;
  }

  const viewArea = document.getElementById('salvoViewArea');
  const editArea = document.getElementById('salvoEditArea');
  const editContent = document.getElementById('salvoEditableContent');
  const btnEdit = document.getElementById('btnEditSalvo');

  if (!viewArea || !editArea || !editContent) return;

  const nextIsEditando = !getIsEditando();
  setIsEditando(nextIsEditando);

  if (nextIsEditando) {
    editContent.innerHTML = (itemAtual.conteudo || '').replace(/\n/g, '<br>');
    viewArea.classList.add('hidden');
    editArea.classList.remove('hidden');
    if (btnEdit) btnEdit.classList.add('active');
    editContent.focus();
  } else {
    viewArea.classList.remove('hidden');
    editArea.classList.add('hidden');
    if (btnEdit) btnEdit.classList.remove('active');
  }

  refreshIcons();
}

export async function salvarEdicaoSalvo() {
  const itemAtual = getItemAtual();
  if (!itemAtual) {
    toast.error('Nenhum documento para salvar.');
    return;
  }

  const editContent = document.getElementById('salvoEditableContent');
  if (!editContent) return;

  const novoConteudo = editContent.innerText;

  if (!novoConteudo.trim()) {
    toast.error('O conteúdo não pode estar vazio.');
    return;
  }

  const updated = updateAnaliseLocal(itemAtual.id, { conteudo: novoConteudo });
  if (!updated) {
    toast.error('Não foi possível salvar (item não encontrado no armazenamento local).');
    return;
  }

  setItemAtual(updated);
  toast.success('Documento atualizado!');

  const viewContent = document.getElementById('salvoViewContent');
  if (viewContent) {
    if (typeof marked !== 'undefined') {
      viewContent.innerHTML = marked.parse(novoConteudo);
    } else {
      viewContent.innerHTML = `<pre style="white-space: pre-wrap;">${novoConteudo}</pre>`;
    }
  }

  // Sai do modo de edição
  setIsEditando(false);
  const viewArea = document.getElementById('salvoViewArea');
  const editArea = document.getElementById('salvoEditArea');
  const btnEdit = document.getElementById('btnEditSalvo');

  if (viewArea) viewArea.classList.remove('hidden');
  if (editArea) editArea.classList.add('hidden');
  if (btnEdit) btnEdit.classList.remove('active');

  refreshIcons();
}

export function cancelarEdicaoSalvo() {
  const itemAtual = getItemAtual();
  if (!itemAtual) return;

  itemAtual.conteudo = getConteudoOriginal();

  setIsEditando(false);
  const viewArea = document.getElementById('salvoViewArea');
  const editArea = document.getElementById('salvoEditArea');
  const btnEdit = document.getElementById('btnEditSalvo');

  if (viewArea) viewArea.classList.remove('hidden');
  if (editArea) editArea.classList.add('hidden');
  if (btnEdit) btnEdit.classList.remove('active');

  refreshIcons();
}
