// Controllo di Cloudflare Access per le pagine riservate (la pagina delle statistiche).
// Access mette davanti al sito una schermata di accesso e passa alla funzione un token firmato
// (header Cf-Access-Jwt-Assertion). Qui si verifica la firma, così la pagina resta chiusa anche se
// qualcuno la raggiunge da un indirizzo non protetto (per esempio le anteprime di Cloudflare Pages).
// Configurazione in wrangler.toml: ACCESS_TEAM_DOMAIN (es. "screenlove.cloudflareaccess.com") e ACCESS_AUD.

let certsCache = null; // { url, keys, expires }

function base64UrlDecode(s) {
  const b64 = s.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(s.length / 4) * 4, '=');
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

const decodeJson = part => JSON.parse(new TextDecoder().decode(base64UrlDecode(part)));

async function signingKeys(team) {
  const url = `https://${team}/cdn-cgi/access/certs`;
  if (certsCache && certsCache.url === url && certsCache.expires > Date.now()) return certsCache.keys;
  const resp = await fetch(url);
  if (!resp.ok) throw new Error(`certificati di Access non disponibili (${resp.status})`);
  const { keys } = await resp.json();
  certsCache = { url, keys: keys || [], expires: Date.now() + 60 * 60 * 1000 };
  return certsCache.keys;
}

function tokenFrom(request) {
  const header = request.headers.get('Cf-Access-Jwt-Assertion');
  if (header) return header;
  const cookie = request.headers.get('Cookie') || '';
  const m = /(?:^|;\s*)CF_Authorization=([^;]+)/.exec(cookie);
  return m ? m[1] : null;
}

/**
 * True se la richiesta arriva da un utente autorizzato da Cloudflare Access.
 * In locale (wrangler pages dev, STATS_DEV_OPEN = "1" in .dev.vars) la pagina è aperta.
 */
export async function isAuthorized(request, env) {
  const host = new URL(request.url).hostname;
  if (env.STATS_DEV_OPEN === '1' && (host === 'localhost' || host === '127.0.0.1')) return true;

  const team = env.ACCESS_TEAM_DOMAIN;
  const aud = env.ACCESS_AUD;
  if (!team || !aud) return false;

  const token = tokenFrom(request);
  if (!token) return false;
  const parts = token.split('.');
  if (parts.length !== 3) return false;

  try {
    const header = decodeJson(parts[0]);
    const payload = decodeJson(parts[1]);
    if (header.alg !== 'RS256') return false;

    const now = Math.floor(Date.now() / 1000);
    const audiences = Array.isArray(payload.aud) ? payload.aud : [payload.aud];
    if (!audiences.includes(aud)) return false;
    if (payload.iss !== `https://${team}`) return false;
    if (typeof payload.exp !== 'number' || payload.exp < now) return false;
    if (typeof payload.nbf === 'number' && payload.nbf > now + 60) return false;

    const jwk = (await signingKeys(team)).find(k => k.kid === header.kid);
    if (!jwk) return false;
    const key = await crypto.subtle.importKey('jwk', jwk, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify']);
    return await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, base64UrlDecode(parts[2]),
      new TextEncoder().encode(`${parts[0]}.${parts[1]}`));
  } catch (err) {
    console.error('Access: token non valido', err);
    return false;
  }
}
