export function getCoverageBorder(coverage: number, borderWidth: string = '2px'): string {
  if (coverage < 50) return `${borderWidth} solid red`;
  if (coverage < 70) return `${borderWidth} solid #FFA500`;
  if (coverage < 80) return `${borderWidth} solid #FEDE00`;
  if (coverage < 90) return `${borderWidth} solid #D4E157`;
  return `${borderWidth} solid #4CAF50`;
}
