// POST /api/ping: l'app installata segnala di essere in uso (al massimo una volta al giorno, solo se
// l'utente non ha disattivato le statistiche anonime nelle impostazioni).
// Corpo JSON: { "id": "<32 cifre esadecimali casuali>", "version": "0.2.0", "arch": "x64", "kind": "setup" }
// Si salvano solo questi quattro campi e il giorno: niente IP né altri dati (web/lib/stats.js).

import { parsePing, savePing } from '../../lib/stats.js';

const MAX_BODY = 1024;

export async function onRequestPost({ request, env }) {
  const length = Number(request.headers.get('Content-Length') || 0);
  if (length > MAX_BODY) return reply(413);

  let body;
  try {
    const text = await request.text();
    if (text.length > MAX_BODY) return reply(413);
    body = JSON.parse(text);
  } catch {
    return reply(400);
  }

  const ping = parsePing(body);
  if (!ping) return reply(400);
  if (!env.STATS) return reply(204); // statistiche non configurate: l'app non deve accorgersene

  try {
    await savePing(env.STATS, ping);
  } catch (err) {
    console.error('statistiche: ping non salvato', err);
    return reply(503);
  }
  return reply(204);
}

export function onRequest() {
  return new Response('Metodo non consentito', { status: 405, headers: { Allow: 'POST' } });
}

function reply(status) {
  return new Response(null, { status, headers: { 'Cache-Control': 'no-store' } });
}
