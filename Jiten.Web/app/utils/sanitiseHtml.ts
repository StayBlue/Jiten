import DOMPurify from 'dompurify';

/**
 * Sanitises HTML content to prevent XSS attacks.
 * Allows basic formatting tags used for ruby text and styling.
 * Client-only — SSR returns raw HTML since it comes from our own API.
 */
export function sanitiseHtml(html: string): string {
  if (!html) return '';
  if (import.meta.server) return html;

  return DOMPurify.sanitize(html, {
    ALLOWED_TAGS: ['ruby', 'rt', 'rp', 'span', 'strong', 'em', 'br', 'a', 'b', 'i'],
    ALLOWED_ATTR: ['href', 'target', 'rel', 'class', 'style', 'lang'],
    ALLOW_DATA_ATTR: false,
  });
}
