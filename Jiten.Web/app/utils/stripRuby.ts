export function stripRuby(text: string): string {
  return text.replace(/\[.*?\]/g, '');
}
