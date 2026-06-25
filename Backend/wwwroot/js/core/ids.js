/**
 * Utilitário para comparar IDs vindos de fontes diferentes.
 * (backend pode retornar number; local pode ter number/string)
 */

export function idsIguais(a, b) {
  if (a === b) return true;
  if (a == null || b == null) return false;
  return String(a) === String(b);
}
