/**
 * Formats a JavaScript Date object into a 'YYYY-MM-DD' string.
 * This correctly uses the date's local year, month, and day,
 * avoiding all timezone conversion issues.
 * @param {Date} date The date object to format.
 * @returns {string} The formatted date string.
 */
export function formatDateAsYyyyMmDd(date: Date) {
  const year = date.getFullYear();
  const month = (date.getMonth() + 1).toString().padStart(2, '0');
  const day = date.getDate().toString().padStart(2, '0');

  return `${year}-${month}-${day}`;
}
