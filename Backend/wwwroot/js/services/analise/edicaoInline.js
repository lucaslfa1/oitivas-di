/**
 * Edição inline (WYSIWYG) para transcrição/laudo/foto/vídeo.
 */

import { setTranscricao, setTranscricaoValidada, setLaudo, getLaudo } from '../../core/state.js';
import { refreshIcons } from '../../core/utils.js';
import { toast } from '../../ui/toast.js';

import { renderizarLaudoComDados } from './relatorio.js';

let transcricaoBackup = '';
let laudoBackup = '';
let fotoBackup = '';
let videoBackup = '';

export function toggleEditTranscricao() {
  const viewArea = document.getElementById('transcricaoView');
  const editArea = document.getElementById('transcricaoEdit');
  const editableContent = document.getElementById('transcricaoEditableContent');
  const btnEdit = document.getElementById('btnEditTranscricao');

  if (!editableContent || !viewArea || !editArea || !btnEdit) return;

  if (editArea.classList.contains('hidden')) {
    transcricaoBackup = viewArea.innerHTML;
    editableContent.innerHTML = viewArea.innerHTML;
    viewArea.classList.add('hidden');
    editArea.classList.remove('hidden');
    btnEdit.innerHTML = '<i data-lucide="x"></i>';
    editableContent.focus();
  } else {
    viewArea.classList.remove('hidden');
    editArea.classList.add('hidden');
    btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';
  }
  refreshIcons();
}

export function salvarEdicaoTranscricao() {
  const editableContent = document.getElementById('transcricaoEditableContent');
  const viewArea = document.getElementById('transcricaoView');
  const editArea = document.getElementById('transcricaoEdit');
  const btnEdit = document.getElementById('btnEditTranscricao');

  if (!editableContent || !viewArea || !editArea || !btnEdit) return;

  const novoConteudo = editableContent.innerHTML;
  setTranscricao(editableContent.innerText);
  setTranscricaoValidada(true);

  viewArea.innerHTML = novoConteudo;

  viewArea.classList.remove('hidden');
  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';

  toast.success('Transcrição atualizada!');
  refreshIcons();
}

export function cancelarEdicaoTranscricao() {
  const viewArea = document.getElementById('transcricaoView');
  const editArea = document.getElementById('transcricaoEdit');
  const btnEdit = document.getElementById('btnEditTranscricao');

  if (!viewArea || !editArea || !btnEdit) return;

  // Mantém o conteúdo atual; apenas sai do modo edição
  viewArea.classList.remove('hidden');
  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';
  refreshIcons();
}

export function toggleEditLaudo() {
  const viewArea = document.getElementById('laudoView');
  const dadosContainer = document.getElementById('dadosIdentificadosContainer');
  const editArea = document.getElementById('laudoEdit');
  const editableContent = document.getElementById('laudoEditableContent');
  const btnEdit = document.getElementById('btnEditLaudo');

  if (!editableContent || !viewArea || !editArea || !btnEdit) return;

  if (editArea.classList.contains('hidden')) {
    // Entrar no modo edição: Carrega o Markdown original
    let markdown = getLaudo('audio') || '';
    laudoBackup = markdown;

    editableContent.innerText = markdown; // Exibe Markdown puro

    viewArea.classList.add('hidden');
    if (dadosContainer) dadosContainer.classList.add('hidden'); // Esconde tabela separada

    editArea.classList.remove('hidden');
    btnEdit.innerHTML = '<i data-lucide="x"></i>';

    // Estilo monospace para facilitar edição de tabelas
    editableContent.style.fontFamily = 'Consolas, monospace';
    editableContent.title = "Edite o Markdown diretamente";
    editableContent.focus();
  } else {
    // Cancelar
    cancelarEdicaoLaudo();
  }
  refreshIcons();
}

export function salvarEdicaoLaudo() {
  const editableContent = document.getElementById('laudoEditableContent');
  const viewArea = document.getElementById('laudoView');
  const editArea = document.getElementById('laudoEdit');
  const btnEdit = document.getElementById('btnEditLaudo');

  if (!editableContent || !viewArea || !editArea || !btnEdit) return;

  const novoMarkdown = editableContent.innerText;
  setLaudo('audio', novoMarkdown);

  // Renderiza novamente (vai separar a tabela se o separador existir)
  renderizarLaudoComDados(novoMarkdown);

  viewArea.classList.remove('hidden');
  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';

  toast.success('Laudo atualizado!');
  refreshIcons();
}

export function cancelarEdicaoLaudo() {
  const viewArea = document.getElementById('laudoView');
  const dadosContainer = document.getElementById('dadosIdentificadosContainer');
  const editArea = document.getElementById('laudoEdit');
  const btnEdit = document.getElementById('btnEditLaudo');

  if (!viewArea || !editArea || !btnEdit) return;

  // Reverte visualização
  const markdown = getLaudo('audio') || laudoBackup;
  renderizarLaudoComDados(markdown);

  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';
  refreshIcons();
}

export function toggleEditFoto() {
  const viewArea = document.getElementById('fotoViewArea');
  const editArea = document.getElementById('fotoEditArea');
  const editableContent = document.getElementById('fotoEditableContent');
  const outputFoto = document.getElementById('outputFoto');
  const btnEdit = document.getElementById('btnEditFoto');

  if (!viewArea || !editArea || !editableContent || !outputFoto || !btnEdit) return;

  const isEditing = !editArea.classList.contains('hidden');

  if (isEditing) {
    cancelarEdicaoFoto();
  } else {
    fotoBackup = outputFoto.innerHTML;
    editableContent.innerHTML = outputFoto.innerHTML;

    viewArea.classList.add('hidden');
    editArea.classList.remove('hidden');
    btnEdit.innerHTML = '<i data-lucide="x"></i>';
    editableContent.focus();
    refreshIcons();
  }
}

export function salvarEdicaoFoto() {
  const editableContent = document.getElementById('fotoEditableContent');
  const viewArea = document.getElementById('fotoViewArea');
  const editArea = document.getElementById('fotoEditArea');
  const btnEdit = document.getElementById('btnEditFoto');
  const output = document.getElementById('outputFoto');

  if (!editableContent || !viewArea || !editArea || !btnEdit || !output) return;

  const novoConteudo = editableContent.innerHTML;
  setLaudo('foto', editableContent.innerText);

  output.innerHTML = novoConteudo;

  viewArea.classList.remove('hidden');
  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';

  toast.success('Laudo de foto atualizado!');
  refreshIcons();
}

export function cancelarEdicaoFoto() {
  const viewArea = document.getElementById('fotoViewArea');
  const editArea = document.getElementById('fotoEditArea');
  const btnEdit = document.getElementById('btnEditFoto');

  if (!viewArea || !editArea || !btnEdit) return;

  viewArea.classList.remove('hidden');
  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';
  refreshIcons();
}

export function toggleEditVideo() {
  const viewArea = document.getElementById('videoViewArea');
  const editArea = document.getElementById('videoEditArea');
  const editableContent = document.getElementById('videoEditableContent');
  const outputVideo = document.getElementById('outputVideo');
  const btnEdit = document.getElementById('btnEditVideo');

  if (!viewArea || !editArea || !editableContent || !outputVideo || !btnEdit) return;

  const isEditing = !editArea.classList.contains('hidden');

  if (isEditing) {
    cancelarEdicaoVideo();
  } else {
    videoBackup = outputVideo.innerHTML;
    editableContent.innerHTML = outputVideo.innerHTML;

    viewArea.classList.add('hidden');
    editArea.classList.remove('hidden');
    btnEdit.innerHTML = '<i data-lucide="x"></i>';
    editableContent.focus();
    refreshIcons();
  }
}

export function salvarEdicaoVideo() {
  const editableContent = document.getElementById('videoEditableContent');
  const viewArea = document.getElementById('videoViewArea');
  const editArea = document.getElementById('videoEditArea');
  const btnEdit = document.getElementById('btnEditVideo');
  const output = document.getElementById('outputVideo');

  if (!editableContent || !viewArea || !editArea || !btnEdit || !output) return;

  const novoConteudo = editableContent.innerHTML;
  setLaudo('video', editableContent.innerText);

  output.innerHTML = novoConteudo;

  viewArea.classList.remove('hidden');
  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';

  toast.success('Laudo de vídeo atualizado!');
  refreshIcons();
}

export function cancelarEdicaoVideo() {
  const viewArea = document.getElementById('videoViewArea');
  const editArea = document.getElementById('videoEditArea');
  const btnEdit = document.getElementById('btnEditVideo');

  if (!viewArea || !editArea || !btnEdit) return;

  viewArea.classList.remove('hidden');
  editArea.classList.add('hidden');
  btnEdit.innerHTML = '<i data-lucide="edit-3"></i>';
  refreshIcons();
}
