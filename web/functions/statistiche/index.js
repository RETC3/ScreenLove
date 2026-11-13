// GET /statistiche: download e installazioni attive (solo numeri), riservata con Cloudflare Access.

import { isAuthorized } from '../../lib/access.js';
import { readStats } from '../../lib/stats.js';

export async function onRequestGet({ request, env }) {
  if (!(await isAuthorized(request, env))) {
    return new Response('Pagina riservata.', { status: 403, headers: baseHeaders('text/plain; charset=utf-8') });
  }
  if (!env.STATS) {
    return new Response('Statistiche non configurate: manca il database STATS.', { status: 503, headers: baseHeaders('text/plain; charset=utf-8') });
  }
  const stats = await readStats(env.STATS);
  return new Response(render(stats, new Date()), { headers: baseHeaders('text/html; charset=utf-8') });
}

function baseHeaders(type) {
  return {
    'Content-Type': type,
    'Cache-Control': 'no-store',
    'X-Robots-Tag': 'noindex, nofollow',
    'X-Frame-Options': 'DENY',
  };
}

const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const num = n => Number(n || 0).toLocaleString('it-IT');

/** Gli ultimi 30 giorni (UTC), dal più vecchio a oggi, con 0 dove non ci sono dati. */
function last30(rows, now) {
  const byDay = new Map(rows.map(r => [r.day, Number(r.n)]));
  const days = [];
  for (let i = 29; i >= 0; i--) {
    const d = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - i));
    const key = d.toISOString().slice(0, 10);
    days.push({ day: key, n: byDay.get(key) || 0 });
  }
  return days;
}

function chart(days, label) {
  const max = Math.max(1, ...days.map(d => d.n));
  const w = 600, h = 140, gap = 4, bw = (w - gap * (days.length - 1)) / days.length;
  const bars = days.map((d, i) => {
    const bh = d.n ? Math.max(3, (d.n / max) * (h - 18)) : 0;
    const x = i * (bw + gap), y = h - bh;
    const date = new Date(d.day + 'T00:00:00Z').toLocaleDateString('it-IT', { day: 'numeric', month: 'short', timeZone: 'UTC' });
    return `<rect x="${x.toFixed(1)}" y="${y.toFixed(1)}" width="${bw.toFixed(1)}" height="${bh.toFixed(1)}" rx="2"><title>${esc(date)}: ${num(d.n)}</title></rect>`;
  }).join('');
  const first = new Date(days[0].day + 'T00:00:00Z').toLocaleDateString('it-IT', { day: 'numeric', month: 'short', timeZone: 'UTC' });
  return `<figure class="chart">
    <figcaption>${esc(label)} <span>· massimo ${num(max)} al giorno</span></figcaption>
    <svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" role="img" aria-label="${esc(label)}">${bars}</svg>
    <div class="axis"><span>${esc(first)}</span><span>oggi</span></div>
  </figure>`;
}

function table(headers, rows) {
  if (!rows.length) return '<p class="empty">Nessun dato.</p>';
  return `<table><thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${
    rows.map(r => `<tr>${r.map((c, i) => `<td${i ? ' class="n"' : ''}>${c}</td>`).join('')}</tr>`).join('')
  }</tbody></table>`;
}

function compareVersions(a, b) {
  const pa = a.split('.').map(Number), pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) if (pa[i] !== pb[i]) return pb[i] - pa[i];
  return 0;
}

function render(stats, now) {
  const dl = stats.downloads, inst = stats.installs;
  const t = dl.total, a = inst.active;

  const versions = new Map();
  for (const r of inst.byVersion) {
    const v = versions.get(r.version) || { setup: 0, portable: 0 };
    v[r.kind] = (v[r.kind] || 0) + Number(r.n);
    versions.set(r.version, v);
  }
  const versionRows = [...versions.entries()].sort((x, y) => compareVersions(x[0], y[0]))
    .map(([v, c]) => [esc(v), num(c.setup + c.portable), num(c.setup), num(c.portable)]);

  const fileRows = dl.byFile.map(r => [esc(r.file), num(r.n), num(r.web), num(r.app)]);
  const archRows = inst.byArch.map(r => [esc(r.arch), num(r.n)]);

  const updated = now.toLocaleString('it-IT', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/Rome' });

  return `<!doctype html>
<html lang="it">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>Statistiche · ScreenLove</title>
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<style>
  @font-face { font-family: "Fraunces"; font-style: normal; font-weight: 400 700; font-display: swap; src: url(/fonts/fraunces.woff2) format("woff2"); }
  @font-face { font-family: "DM Sans"; font-style: normal; font-weight: 400 700; font-display: swap; src: url(/fonts/dm-sans.woff2) format("woff2"); }
  :root { --bg: #141518; --panel: #1E1F24; --line: #33363E; --fg: #ECECEF; --muted: #9A9CA5; --accent: #FF3D7F; }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--fg); font: 16px/1.55 "DM Sans", "Segoe UI", system-ui, sans-serif; }
  main { max-width: 1040px; margin: 0 auto; padding: 36px 20px 60px; }
  h1, h2 { font-family: "Fraunces", Georgia, serif; font-weight: 500; letter-spacing: -.01em; }
  h1 { font-size: 34px; margin: 0 0 4px; } h1 i { color: var(--accent); }
  h2 { font-size: 23px; margin: 40px 0 14px; }
  .sub { color: var(--muted); margin: 0 0 8px; font-size: 14.5px; }
  .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; }
  .card { background: var(--panel); border: 1px solid var(--line); border-radius: 14px; padding: 16px 18px; }
  .card b { display: block; font: 600 30px/1.1 "Fraunces", Georgia, serif; }
  .card span { color: var(--muted); font-size: 14px; }
  .card.main b { color: var(--accent); }
  .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
  .chart { margin: 18px 0 0; background: var(--panel); border: 1px solid var(--line); border-radius: 14px; padding: 16px 18px; }
  .chart figcaption { font-size: 14.5px; margin-bottom: 10px; } .chart figcaption span { color: var(--muted); }
  .chart svg { width: 100%; height: 140px; display: block; } .chart rect { fill: var(--accent); }
  .chart rect:hover { fill: #ff7aa8; }
  .axis { display: flex; justify-content: space-between; color: var(--muted); font-size: 12.5px; margin-top: 6px; }
  table { width: 100%; border-collapse: collapse; background: var(--panel); border: 1px solid var(--line); border-radius: 14px; overflow: hidden; font-size: 15px; }
  th, td { padding: 9px 14px; text-align: left; border-bottom: 1px solid var(--line); }
  th { color: var(--muted); font-weight: 500; font-size: 13.5px; }
  tr:last-child td { border-bottom: 0; }
  td.n, th:not(:first-child) { text-align: right; font-variant-numeric: tabular-nums; }
  td:first-child { word-break: break-all; }
  .empty { color: var(--muted); }
  .note { color: var(--muted); font-size: 13.5px; margin-top: 40px; border-top: 1px solid var(--line); padding-top: 16px; }
  a { color: inherit; }
  @media (max-width: 760px) { .grid { grid-template-columns: 1fr; } h1 { font-size: 28px; } }
</style>
</head>
<body>
<main>
  <h1>Statistiche di Screen<i>Love</i></h1>
  <p class="sub">Aggiornate al ${esc(updated)} · solo numeri anonimi · <a href="/">torna al sito</a></p>

  <h2>Installazioni attive</h2>
  <div class="cards">
    <div class="card main"><b>${num(a.d7)}</b><span>ultimi 7 giorni</span></div>
    <div class="card"><b>${num(a.d30)}</b><span>ultimi 30 giorni</span></div>
    <div class="card"><b>${num(a.today)}</b><span>oggi</span></div>
    <div class="card"><b>${num(a.ever)}</b><span>viste in totale</span></div>
  </div>
  ${chart(last30(inst.daily, now), 'App attive al giorno, ultimi 30 giorni')}
  <div class="grid">
    <div><h2>Per versione</h2>${table(['Versione', 'Totale', 'Installate', 'Portatili'], versionRows)}</div>
    <div><h2>Per architettura</h2>${table(['Architettura', 'Installazioni'], archRows)}</div>
  </div>

  <h2>Download</h2>
  <div class="cards">
    <div class="card main"><b>${num(t.all_time)}</b><span>in totale</span></div>
    <div class="card"><b>${num(t.d30)}</b><span>ultimi 30 giorni</span></div>
    <div class="card"><b>${num(t.d7)}</b><span>ultimi 7 giorni</span></div>
    <div class="card"><b>${num(t.today)}</b><span>oggi</span></div>
  </div>
  ${chart(last30(dl.daily, now), 'Download al giorno, ultimi 30 giorni')}
  <h2>Per file</h2>
  ${table(['File', 'Totale', 'Dal sito', 'Aggiornamenti app'], fileRows)}

  <p class="note">
    Un'installazione è "attiva" se l'app ha cercato aggiornamenti in quel periodo: succede al massimo una volta al giorno,
    solo con gli aggiornamenti automatici e le statistiche anonime attivi. Le copie 0.1.0 non vengono contate.
    I download contano i file scaricati per intero, dal sito o dall'aggiornamento automatico dell'app. Date in UTC.
  </p>
</main>
</body>
</html>`;
}
