export function formatPercentageApprox(value: float): string {
  if (value === 0) {
    return '0%';
  } else if (value <= 0.1) {
    return '<0.1%';
  } else if (value < 1) {
    return '<1%';
  } else if (value > 99 && value < 100) {
    return '>99%';
  } else {
    return `${Math.trunc(value)}%`;
  }
}
