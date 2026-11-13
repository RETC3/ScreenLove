// Un solo indirizzo ufficiale: www.screenlove.it e screenlove.pages.dev rimandano a https://screenlove.it.
// Le anteprime di Cloudflare Pages (<id>.screenlove.pages.dev) restano raggiungibili.
const CANONICAL = 'screenlove.it';
const REDIRECT_HOSTS = new Set(['www.screenlove.it', 'screenlove.pages.dev']);

export async function onRequest({ request, next }) {
  const url = new URL(request.url);
  if (REDIRECT_HOSTS.has(url.hostname)) {
    url.hostname = CANONICAL;
    url.protocol = 'https:';
    url.port = '';
    return Response.redirect(url.toString(), 301);
  }
  return next();
}
