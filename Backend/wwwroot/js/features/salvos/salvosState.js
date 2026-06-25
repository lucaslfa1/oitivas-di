/**
 * Estado do viewer/edição de itens salvos.
 */

let itemAtual = null;
let conteudoOriginal = '';
let isEditando = false;

export function getItemAtual() {
  return itemAtual;
}

export function setItemAtual(item) {
  itemAtual = item;
}

export function getConteudoOriginal() {
  return conteudoOriginal;
}

export function setConteudoOriginal(conteudo) {
  conteudoOriginal = conteudo;
}

export function getIsEditando() {
  return isEditando;
}

export function setIsEditando(value) {
  isEditando = Boolean(value);
}

export function resetSalvosState() {
  itemAtual = null;
  conteudoOriginal = '';
  isEditando = false;
}
