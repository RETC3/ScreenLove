// Download dei file di ScreenLove dal sito:
//   /download/latest                      -> rimanda all'installer dell'ultima versione
//   /download/ScreenLove-Setup-0.1.0.exe  -> il file
// I file stanno nel bucket R2 collegato come DOWNLOADS. Se il bucket non è configurato o il file
// non c'è, si inoltra la copia della release su GitHub: chi scarica vede comunque solo il sito.

const GITHUB_RELEASES = 'https://github.com/RETC3/ScreenLove/releases/download';
const FILE_NAME = /^ScreenLove-(?:Setup-(\d+\.\d+\.\d+)\.exe|(\d+\.\d+\.\d+)-win-(?:x64|arm64)\.zip)$/;

export async function onRequest({ request, env, params }) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response('Metodo non consentito', { status: 405, headers: { Allow: 'GET, HEAD' } });
  }

  const parts = Array.isArray(params.path) ? params.path : [params.path];
  const name = parts.join('/');

  if (name === 'latest') {
    const latest = await env.ASSETS.fetch(new URL('/latest.json', request.url));
    if (!latest.ok || !(latest.headers.get('Content-Type') || '').includes('json')) return notFound();
    let release;
    try { release = await latest.json(); } catch { return notFound(); }
    const setup = (release.assets || []).find(a => /^ScreenLove-Setup-.*\.exe$/.test(a.name));
    if (!setup) return notFound();
    return new Response(null, {
      status: 302,
      headers: { Location: `/download/${setup.name}`, 'Cache-Control': 'no-store' },
    });
  }

  const match = FILE_NAME.exec(name);
  if (!match) return notFound();
  const version = match[1] || match[2];

  const headers = new Headers({
    'Content-Type': name.endsWith('.zip') ? 'application/zip' : 'application/vnd.microsoft.portable-executable',
    'Content-Disposition': `attachment; filename="${name}"`,
    // i nomi contengono la versione: lo stesso indirizzo non cambia mai contenuto
    'Cache-Control': 'public, max-age=86400',
    'X-Content-Type-Options': 'nosniff',
  });

  if (env.DOWNLOADS) {
    const object = request.method === 'HEAD' ? await env.DOWNLOADS.head(name) : await env.DOWNLOADS.get(name);
    if (object) {
      headers.set('Content-Length', String(object.size));
      headers.set('ETag', object.httpEtag);
      return new Response(request.method === 'HEAD' ? null : object.body, { headers });
    }
  }

  // Riserva: la stessa release su GitHub.
  const upstream = await fetch(`${GITHUB_RELEASES}/v${version}/${name}`, { method: request.method, redirect: 'follow' });
  if (!upstream.ok) return notFound();
  const length = upstream.headers.get('Content-Length');
  if (length) headers.set('Content-Length', length);
  return new Response(request.method === 'HEAD' ? null : upstream.body, { headers });
}

function notFound() {
  return new Response('File non trovato', { status: 404, headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
}
