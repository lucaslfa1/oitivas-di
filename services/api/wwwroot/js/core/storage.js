/**
 * Helpers de storage (localStorage)
 * Mantém leitura/escrita de JSON em um único lugar.
 */

export function readJsonArray(key) {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function writeJsonArray(key, value) {
  const safe = Array.isArray(value) ? value : [];
  localStorage.setItem(key, JSON.stringify(safe));
}
