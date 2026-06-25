/**
 * Implementa atualização local de uma análise.
 *
 * Observação: o backend ainda não possui PUT/PATCH. Então este update
 * atua somente no fallback (localStorage).
 */

import { readJsonArray, writeJsonArray } from '../core/storage.js';
import { idsIguais } from '../core/ids.js';

const STORAGE_KEY = 'sinistroIA_analises';

export function updateAnaliseLocal(id, patch) {
  const items = readJsonArray(STORAGE_KEY);
  const idx = items.findIndex(a => idsIguais(a?.id, id));
  if (idx < 0) return null;

  const updated = { ...items[idx], ...patch };
  items[idx] = updated;
  writeJsonArray(STORAGE_KEY, items);
  return updated;
}
