export function applySSRProxyHeaders(headers: Headers): void {
  if (!import.meta.server) return;

  const proxyHeaders = useRequestHeaders(['x-forwarded-for', 'cf-connecting-ip', 'user-agent']);

  if (proxyHeaders['x-forwarded-for']) {
    headers.set('X-Forwarded-For', proxyHeaders['x-forwarded-for']);
  }
  if (proxyHeaders['cf-connecting-ip']) {
    headers.set('CF-Connecting-IP', proxyHeaders['cf-connecting-ip']);
  }
  if (proxyHeaders['user-agent']) {
    headers.set('User-Agent', proxyHeaders['user-agent']);
  }
}
