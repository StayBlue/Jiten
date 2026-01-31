export const toNumOrNull = (v: unknown): number | null => {
  if (v === undefined || v === null || v === '' || (Array.isArray(v) && v.length === 0)) return null;
  const s = Array.isArray(v) ? v[0] : v;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
};

export const toBooleanOrNull = (v: unknown): boolean | null => {
  if (v === undefined || v === null || v === '' || (Array.isArray(v) && v.length === 0)) return null;
  const s = Array.isArray(v) ? v[0] : v;
  return s === 'true' ? true : s === 'false' ? false : null;
};

export const parseNumberArray = (v: unknown): number[] => {
  if (!v) return [];
  const str = Array.isArray(v) ? v[0] : v;
  if (typeof str !== 'string') return [];
  return str
    .split(',')
    .map((s) => Number(s.trim()))
    .filter((n) => Number.isFinite(n) && n > 0);
};
