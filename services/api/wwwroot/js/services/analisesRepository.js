/**
 * Repositório de Análises
 * Responsável por ler/salvar/excluir análises priorizando o backend,
 * com fallback para localStorage.
 */

import { buscarAnalisesAPI, buscarAnaliseAPI, salvarAnaliseAPI, excluirAnaliseAPI } from '../api/sinistroApi.js';
import { readJsonArray, writeJsonArray } from '../core/storage.js';
import { idsIguais } from '../core/ids.js';

const STORAGE_KEY = 'sinistroIA_analises';

function readLocal() {
  return readJsonArray(STORAGE_KEY);
}

function writeLocal(items) {
  writeJsonArray(STORAGE_KEY, items);
}

export async function listAnalises() {
  try {
    return await buscarAnalisesAPI();
  } catch {
    return readLocal();
  }
}

export async function getAnalise(id) {
  try {
    return await buscarAnaliseAPI(id);
  } catch {
    const items = readLocal();
    return items.find(a => idsIguais(a?.id, id)) || null;
  }
}

export async function saveAnalise(dados) {
  try {
    return await salvarAnaliseAPI(dados);
  } catch {
    const items = readLocal();
    const item = { id: Date.now(), ...dados };
    items.push(item);
    writeLocal(items);
    return item;
  }
}

export async function deleteAnalise(id) {
  try {
    const ok = await excluirAnaliseAPI(id);
    if (ok) return true;
  } catch {
    // ignore
  }

  // fallback local
  const items = readLocal();
  const next = items.filter(a => !idsIguais(a?.id, id));
  writeLocal(next);
  return true;
}
