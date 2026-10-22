function input(value: string): string {
  // Deliberately reject control characters rather than silently trimming them.
  // eslint-disable-next-line no-control-regex
  if (typeof value !== "string" || /[\u0000-\u001f\u007f]/u.test(value))
    throw new Error("Invalid compatibility value");
  return value.trim();
}
export function normalizeTitleId(value: string): string {
  const normalized = input(value).toUpperCase();
  if (!/^CUSA[0-9]{5}$/u.test(normalized)) throw new Error("Invalid title ID");
  return normalized;
}
export function normalizeGameVersion(value: string): string {
  const match = /^([0-9]{1,3})\.([0-9]{1,3})$/u.exec(input(value));
  if (!match) throw new Error("Invalid game version");
  return `${Number(match[1])}.${String(Number(match[2])).padStart(2, "0")}`;
}
export function compatibilityKey(titleId: string, gameVersion: string): string {
  return `${normalizeTitleId(titleId)}@${normalizeGameVersion(gameVersion)}`;
}
