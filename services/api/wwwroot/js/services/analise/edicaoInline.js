/**
 * Edição inline (WYSIWYG) para transcrição/laudo/foto/vídeo.
 *
 * Módulo do Sentinel (análise forense de sinistros) responsável por permitir que o
 * operador edite, diretamente na interface, o conteúdo já produzido pela análise de IA
 * (Azure) para cada uma das quatro modalidades: transcrição de áudio, laudo de áudio,
 * laudo de foto e laudo de vídeo.
 *
 * PADRÃO TOGGLE / SAVE / CANCEL
 * Cada modalidade expõe três funções que operam sobre o mesmo trio de elementos do DOM:
 *   - `toggle...`  : alterna entre o modo de VISUALIZAÇÃO (viewArea) e o modo de EDIÇÃO
 *                    (editArea). Ao entrar em edição, copia o conteúdo atual para a área
 *                    editável e guarda uma cópia de segurança (backup); ao sair sem salvar,
 *                    delega para a função `cancelar...`.
 *   - `salvar...`  : persiste o conteúdo editado no estado global (state.js), atualiza a
 *                    área de visualização, volta para o modo de visualização e exibe um toast.
 *   - `cancelar...`: descarta as alterações feitas na área editável e restaura a
 *                    visualização original, sem persistir nada no estado.
 *
 * O botão de cada modalidade (`btnEdit...`) é reaproveitado como toggle: quando em edição
 * exibe o ícone "x" (sair/cancelar) e quando em visualização exibe o ícone "edit-3" (editar).
 * `refreshIcons()` é chamado ao final para que a biblioteca de ícones (Lucide) re-renderize
 * os ícones recém-inseridos via innerHTML.
 *
 * A visibilidade de cada área é controlada pela classe CSS utilitária `hidden`.
 */

import { setTranscricao, setTranscricaoValidada, setLaudo, getLaudo } from '../../core/state.js';
import { refreshIcons } from '../../core/utils.js';
import { toast } from '../../ui/toast.js';

import { renderizarLaudoComDados } from './relatorio.js';

// Cópias de segurança do conteúdo capturadas no instante em que cada modalidade entra em
// modo de edição. Servem como referência para restaurar a visualização caso o usuário
// cancele. Observação: para transcrição/foto/vídeo o "undo" efetivo é feito reexibindo a
// viewArea original (que nunca é alterada antes do salvar), então estes backups funcionam
// principalmente como registro do estado pré-edição; para o laudo, `laudoBackup` é usado
// como fallback do Markdown caso o estado global não tenha o laudo de áudio.
let transcricaoBackup = '';
let laudoBackup = '';
let fotoBackup = '';
let videoBackup = '';

/**
 * Alterna a transcrição entre os modos de visualização e edição (WYSIWYG sobre HTML).
 *
 * COMO FUNCIONA:
 * - Decide o sentido da alternância pela presença da classe `hidden` na editArea: se a
 *   área de edição está oculta, significa que estamos visualizando e devemos ENTRAR em edição;
 *   caso contrário, SAIR da edição (sem salvar).
 * - Ao entrar: faz backup do HTML atual da viewArea, espelha esse HTML na área editável
 *   (edição rica/WYSIWYG), oculta a viewArea, mostra a editArea, troca o ícone do botão
 *   para "x" (sair) e foca o campo para o cursor já ficar pronto para digitação.
 * - Ao sair: apenas reexibe a viewArea (que ainda contém o conteúdo original, pois nada
 *   foi persistido) e restaura o ícone "edit-3" (editar).
 *
 * Efeitos colaterais: muta o DOM (innerHTML/classes/ícone), atribui `transcricaoBackup`,
 * move o foco e dispara `refreshIcons()`. Não persiste nada no estado.
 * @returns {void} Retorna cedo (sem efeito) se algum elemento do DOM não for encontrado.
 */
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

/**
 * Persiste a edição da transcrição e retorna ao modo de visualização.
 *
 * COMO FUNCIONA:
 * - Lê o conteúdo editado em duas representações: `innerHTML` (rico, para reexibir na
 *   viewArea preservando formatação) e `innerText` (texto puro, para o estado global).
 * - Salva o texto puro via `setTranscricao` e marca a transcrição como validada pelo
 *   operador via `setTranscricaoValidada(true)` — sinaliza ao restante do sistema que
 *   este conteúdo passou por revisão humana e não é mais apenas a saída bruta da IA.
 * - Copia o HTML editado de volta para a viewArea, reexibe a visualização, oculta a edição,
 *   restaura o ícone "edit-3" e confirma com um toast de sucesso.
 *
 * Efeitos colaterais: escreve no estado global (transcrição + flag de validação), muta o
 * DOM, exibe toast e chama `refreshIcons()`.
 * @returns {void} Retorna cedo (sem salvar) se algum elemento do DOM não for encontrado.
 */
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

/**
 * Cancela a edição da transcrição, descartando alterações.
 *
 * COMO FUNCIONA: como o salvar é o único ponto que sobrescreve a viewArea, basta reexibir
 * a viewArea (que ainda carrega o conteúdo pré-edição) e ocultar a editArea. O que foi
 * digitado na área editável é simplesmente abandonado, sem tocar no estado global.
 *
 * Efeitos colaterais: muta o DOM (classes/ícone) e chama `refreshIcons()`.
 * @returns {void} Retorna cedo se algum elemento do DOM não for encontrado.
 */
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

/**
 * Alterna o laudo de áudio entre visualização e edição de Markdown bruto.
 *
 * DIFERENÇA EM RELAÇÃO ÀS OUTRAS MODALIDADES: o laudo não é editado como HTML rico, e sim
 * como Markdown puro. Em visualização o laudo é renderizado (HTML + possível tabela de
 * "dados identificados" separada); ao editar, expomos o Markdown de origem para que o
 * operador ajuste texto e tabelas com fidelidade, sem o ruído da conversão para HTML.
 *
 * COMO FUNCIONA:
 * - Ao entrar em edição: recupera o Markdown original do estado global via
 *   `getLaudo('audio')` (string vazia se ausente), guarda-o em `laudoBackup`, e o joga na
 *   área editável como texto puro (`innerText`, não innerHTML — evita interpretar o Markdown
 *   como HTML). Oculta a viewArea e também o container da tabela de dados identificados
 *   (`dadosIdentificadosContainer`), que durante a edição é representada pelo próprio
 *   Markdown e não deve aparecer duplicada. Aplica fonte monospace (Consolas) para alinhar
 *   colunas de tabelas Markdown e facilitar a edição, define um `title` de ajuda e foca o campo.
 * - Ao sair sem salvar: delega para `cancelarEdicaoLaudo()`, que reverte a renderização.
 *
 * Efeitos colaterais: muta o DOM (conteúdo/classes/estilo/title/ícone), atribui
 * `laudoBackup`, move o foco e chama `refreshIcons()`. Não persiste no estado.
 * @returns {void} Retorna cedo se algum elemento essencial do DOM não for encontrado.
 */
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

/**
 * Persiste a edição do laudo de áudio (Markdown) e volta ao modo de visualização.
 *
 * COMO FUNCIONA:
 * - Lê o Markdown editado de `innerText` (texto puro — o que o operador realmente digitou).
 * - Persiste no estado global via `setLaudo('audio', novoMarkdown)`.
 * - Re-renderiza o laudo a partir do Markdown via `renderizarLaudoComDados`, que reconstrói
 *   o HTML e, se houver o separador esperado no conteúdo, extrai novamente a tabela de
 *   "dados identificados" para o container próprio.
 * - Reexibe a viewArea, oculta a editArea, restaura o ícone "edit-3" e confirma com toast.
 *
 * Efeitos colaterais: escreve o laudo de áudio no estado global, re-renderiza o DOM,
 * exibe toast e chama `refreshIcons()`.
 * @returns {void} Retorna cedo (sem salvar) se algum elemento do DOM não for encontrado.
 */
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

/**
 * Cancela a edição do laudo de áudio, descartando alterações e reconstruindo a visualização.
 *
 * COMO FUNCIONA: diferente das demais modalidades, aqui a viewArea precisa ser RE-RENDERIZADA
 * a partir do Markdown, pois durante a edição ela foi apenas ocultada (e a tabela de dados
 * separada também). O Markdown usado é o do estado global (`getLaudo('audio')`); se o estado
 * estiver vazio, recorre-se a `laudoBackup` capturado no início da edição como fallback.
 * `renderizarLaudoComDados` reexibe a viewArea (e a tabela de dados, quando aplicável), por
 * isso aqui só é necessário ocultar a editArea e restaurar o ícone "edit-3".
 *
 * Efeitos colaterais: re-renderiza o DOM do laudo e chama `refreshIcons()`. Não escreve no estado.
 * @returns {void} Retorna cedo se algum elemento do DOM não for encontrado.
 */
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

/**
 * Alterna o laudo de foto entre visualização e edição (WYSIWYG sobre HTML).
 *
 * Segue o mesmo padrão da transcrição (edição de HTML rico), mas a fonte/destino do
 * conteúdo é o elemento `outputFoto` (onde o laudo de foto fica renderizado), enquanto a
 * `viewArea` (`fotoViewArea`) é o invólucro mostrado/ocultado.
 *
 * COMO FUNCIONA:
 * - `isEditing` é derivado da ausência da classe `hidden` na editArea (true = já estamos
 *   editando). Se já editando, delega para `cancelarEdicaoFoto()` (sai descartando).
 * - Caso contrário, entra em edição: faz backup do HTML de `outputFoto`, espelha-o na área
 *   editável, oculta a viewArea, mostra a editArea, troca o ícone para "x", foca o campo e
 *   re-renderiza os ícones.
 *
 * Efeitos colaterais: muta o DOM, atribui `fotoBackup`, move o foco e chama `refreshIcons()`
 * (no ramo de entrada; no ramo de saída, `cancelarEdicaoFoto` cuida do refresh). Não persiste no estado.
 * @returns {void} Retorna cedo se algum elemento do DOM não for encontrado.
 */
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

/**
 * Persiste a edição do laudo de foto e retorna ao modo de visualização.
 *
 * COMO FUNCIONA:
 * - Captura o HTML editado (`innerHTML`) para reexibir com formatação no `outputFoto`, e o
 *   texto puro (`innerText`) para persistir no estado via `setLaudo('foto', ...)`.
 * - Reescreve o `outputFoto` com o HTML editado, reexibe a viewArea, oculta a editArea,
 *   restaura o ícone "edit-3" e confirma com toast.
 *
 * Efeitos colaterais: escreve o laudo de foto no estado global, muta o DOM, exibe toast e
 * chama `refreshIcons()`.
 * @returns {void} Retorna cedo (sem salvar) se algum elemento do DOM não for encontrado.
 */
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

/**
 * Cancela a edição do laudo de foto, descartando alterações.
 *
 * COMO FUNCIONA: o `outputFoto` só é sobrescrito ao salvar, então cancelar apenas reexibe a
 * viewArea original e oculta a editArea; o conteúdo digitado na área editável é descartado
 * sem tocar no estado global. É também o ramo "sair da edição" invocado por `toggleEditFoto`.
 *
 * Efeitos colaterais: muta o DOM (classes/ícone) e chama `refreshIcons()`.
 * @returns {void} Retorna cedo se algum elemento do DOM não for encontrado.
 */
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

/**
 * Alterna o laudo de vídeo entre visualização e edição (WYSIWYG sobre HTML).
 *
 * Espelha exatamente `toggleEditFoto`, operando sobre os elementos da modalidade de vídeo
 * (`videoViewArea`, `videoEditArea`, `videoEditableContent`, `outputVideo`, `btnEditVideo`).
 *
 * COMO FUNCIONA:
 * - `isEditing` indica se já estamos em edição (editArea sem `hidden`). Se sim, delega para
 *   `cancelarEdicaoVideo()`.
 * - Caso contrário, entra em edição: backup do HTML de `outputVideo`, espelha na área
 *   editável, oculta a viewArea, mostra a editArea, troca o ícone para "x", foca e re-renderiza ícones.
 *
 * Efeitos colaterais: muta o DOM, atribui `videoBackup`, move o foco e chama `refreshIcons()`
 * (no ramo de entrada). Não persiste no estado.
 * @returns {void} Retorna cedo se algum elemento do DOM não for encontrado.
 */
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

/**
 * Persiste a edição do laudo de vídeo e retorna ao modo de visualização.
 *
 * Espelho de `salvarEdicaoFoto` para a modalidade de vídeo.
 *
 * COMO FUNCIONA:
 * - Captura `innerHTML` (para reexibir formatado em `outputVideo`) e `innerText` (texto puro
 *   para o estado, via `setLaudo('video', ...)`).
 * - Reescreve o `outputVideo`, reexibe a viewArea, oculta a editArea, restaura o ícone
 *   "edit-3" e confirma com toast.
 *
 * Efeitos colaterais: escreve o laudo de vídeo no estado global, muta o DOM, exibe toast e
 * chama `refreshIcons()`.
 * @returns {void} Retorna cedo (sem salvar) se algum elemento do DOM não for encontrado.
 */
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

/**
 * Cancela a edição do laudo de vídeo, descartando alterações.
 *
 * Espelho de `cancelarEdicaoFoto`: como `outputVideo` só muda ao salvar, basta reexibir a
 * viewArea original e ocultar a editArea; o que foi digitado é descartado sem tocar no
 * estado. É também o ramo "sair da edição" chamado por `toggleEditVideo`.
 *
 * Efeitos colaterais: muta o DOM (classes/ícone) e chama `refreshIcons()`.
 * @returns {void} Retorna cedo se algum elemento do DOM não for encontrado.
 */
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
