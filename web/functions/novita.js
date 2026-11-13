// Pagina https://screenlove.it/novita: versioni pubblicate e novità in arrivo, da web/lib/novita.js.
import { versioni, inArrivo } from '../lib/novita.js';

const MESI = ['gennaio', 'febbraio', 'marzo', 'aprile', 'maggio', 'giugno', 'luglio', 'agosto',
  'settembre', 'ottobre', 'novembre', 'dicembre'];

const TIPI = {
  nuovo: 'Nuovo',
  migliorato: 'Migliorato',
  corretto: 'Corretto',
};

const STATI = {
  sviluppo: 'In sviluppo',
  programma: 'In programma',
};

function esc(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// **grassetto** e `tasti`, dopo l'escape.
function testo(s) {
  return esc(s)
    .replace(/\*\*(.+?)\*\*/g, '<b>$1</b>')
    .replace(/`(.+?)`/g, '<kbd>$1</kbd>');
}

function data(iso) {
  const [y, m, d] = iso.split('-').map(Number);
  return `${d} ${MESI[m - 1]} ${y}`;
}

const HEART = '<svg viewBox="0 0 24 24"><path d="M12 20.5C6.6 16.7 4.5 14 4.5 10.9a3.7 3.7 0 0 1 7.5-1.4 3.7 3.7 0 0 1 7.5 1.4c0 3.1-2.1 5.8-7.5 9.6Z" fill="#fff"/></svg>';

export function renderNovita() {
  const arrivo = inArrivo.map((n) => `
      <li class="soon ${esc(n.stato)}">
        <span class="state">${esc(STATI[n.stato] ?? n.stato)}</span>
        <h3>${esc(n.titolo)}</h3>
        <p>${testo(n.testo)}</p>
      </li>`).join('');

  const elenco = versioni.map((v, i) => `
      <li class="rel" id="v${esc(v.versione)}">
        <div class="meta">
          <a class="ver" href="#v${esc(v.versione)}">${esc(v.versione)}</a>
          <time datetime="${esc(v.data)}">${esc(data(v.data))}</time>
          ${i === 0 ? '<span class="latest">Ultima versione</span>' : ''}
        </div>
        <div class="card">
          ${v.titolo ? `<h3>${esc(v.titolo)}</h3>` : ''}
          <ul class="items">
            ${v.voci.map(([tipo, t]) => `<li><span class="tag ${esc(tipo)}">${esc(TIPI[tipo] ?? tipo)}</span><span>${testo(t)}</span></li>`).join('\n            ')}
          </ul>
          ${i === 0 ? `<a class="btn primary small" href="/download/latest">Scarica la ${esc(v.versione)}</a>` : ''}
        </div>
      </li>`).join('');

  return `<!doctype html>
<html lang="it">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Novità · ScreenLove</title>
<meta name="description" content="Tutte le versioni di ScreenLove con le loro novità, e quello che stiamo preparando per le prossime.">
<meta name="theme-color" content="#141518">
<link rel="icon" href="/favicon.ico" sizes="48x48">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<link rel="manifest" href="/site.webmanifest">
<link rel="canonical" href="https://screenlove.it/novita">
<meta property="og:type" content="website">
<meta property="og:site_name" content="ScreenLove">
<meta property="og:locale" content="it_IT">
<meta property="og:url" content="https://screenlove.it/novita">
<meta property="og:title" content="Novità · ScreenLove">
<meta property="og:description" content="Le versioni di ScreenLove e le novità in arrivo.">
<meta property="og:image" content="https://screenlove.it/og-image.png">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">
<meta name="twitter:card" content="summary_large_image">
<style>
  @font-face { font-family: "Fraunces"; font-style: normal; font-weight: 400 700; font-display: swap; src: url(/fonts/fraunces.woff2) format("woff2"); }
  @font-face { font-family: "Fraunces"; font-style: italic; font-weight: 400 700; font-display: swap; src: url(/fonts/fraunces-italic.woff2) format("woff2"); }
  @font-face { font-family: "DM Sans"; font-style: normal; font-weight: 400 700; font-display: swap; src: url(/fonts/dm-sans.woff2) format("woff2"); }
  :root { --bg: #141518; --panel: #1E1F24; --panel2: #26282E; --line: #33363E; --fg: #ECECEF; --muted: #9A9CA5;
    --accent: #FF3D7F; --accent-soft: rgba(255,61,127,.14); --green: #3DDC97; --blue: #6CB4FF; --amber: #FFB547; }
  * { box-sizing: border-box; }
  body { margin: 0; color: var(--fg); font: 17px/1.65 "DM Sans", "Segoe UI", system-ui, sans-serif;
    background: radial-gradient(800px 480px at 80% -80px, rgba(255,61,127,.12), transparent 70%), var(--bg); }
  a { color: inherit; }
  .wrap { max-width: 980px; margin: 0 auto; padding: 0 24px; }
  .top .wrap { display: flex; align-items: center; gap: 24px; height: 76px; }
  .brand { display: inline-flex; align-items: center; gap: 12px; text-decoration: none; font: 600 22px/1 "Fraunces", Georgia, serif; }
  .brand .logo { width: 40px; height: 40px; border-radius: 50%; background: var(--accent); display: grid; place-items: center; }
  .brand .logo svg { width: 21px; height: 21px; }
  .top nav { margin-left: auto; display: flex; gap: 26px; }
  .top nav a { text-decoration: none; color: var(--muted); font-size: 15.5px; }
  .top nav a:hover, .top nav a[aria-current] { color: var(--fg); }

  h1, h2, h3 { font-family: "Fraunces", Georgia, serif; font-weight: 500; letter-spacing: -.01em; }
  header.intro { padding: 50px 0 20px; }
  .eyebrow { font-size: 13px; letter-spacing: .16em; text-transform: uppercase; color: var(--accent); font-weight: 600; }
  h1 { font-size: clamp(42px, 7vw, 68px); line-height: 1.04; margin: 14px 0 18px; }
  h1 em { color: var(--accent); }
  .lead { color: var(--muted); font-size: 19px; max-width: 34em; margin: 0; }

  section { padding: 46px 0 10px; }
  h2 { font-size: 32px; margin: 0 0 6px; }
  .sub { color: var(--muted); margin: 0 0 26px; }

  .soon-list { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }
  .soon { background: var(--panel); border: 1px dashed #4A4D57; border-radius: 18px; padding: 20px 22px 18px; }
  .soon.sviluppo { border: 1px solid rgba(255,61,127,.5); background: linear-gradient(160deg, rgba(255,61,127,.08), var(--panel) 60%); }
  .soon h3 { font-size: 21px; margin: 12px 0 6px; line-height: 1.25; }
  .soon p { margin: 0; color: var(--muted); font-size: 15.5px; line-height: 1.55; }
  .state { display: inline-flex; align-items: center; gap: 8px; font-size: 12px; font-weight: 700; letter-spacing: .12em; text-transform: uppercase; color: var(--muted); }
  .state::before { content: ""; width: 8px; height: 8px; border-radius: 50%; background: var(--muted); }
  .sviluppo .state { color: var(--accent); }
  .sviluppo .state::before { background: var(--accent); box-shadow: 0 0 0 0 rgba(255,61,127,.6); animation: pulse 1.8s infinite; }
  @keyframes pulse { 70% { box-shadow: 0 0 0 8px rgba(255,61,127,0); } 100% { box-shadow: 0 0 0 0 rgba(255,61,127,0); } }
  @media (prefers-reduced-motion: reduce) { .sviluppo .state::before { animation: none; } }

  .releases { list-style: none; margin: 0; padding: 0; position: relative; }
  .rel { display: grid; grid-template-columns: 170px 1fr; gap: 28px; position: relative; padding-bottom: 34px; }
  .rel::before { content: ""; position: absolute; left: 170px; margin-left: 13px; top: 14px; bottom: 0; width: 2px; background: var(--line); }
  .rel:last-child::before { display: none; }
  .meta { text-align: right; padding: 2px 8px 0 0; position: relative; }
  .meta::after { content: ""; position: absolute; right: -21px; top: 10px; width: 12px; height: 12px; border-radius: 50%; background: var(--bg); border: 2px solid var(--muted); }
  .rel:first-child .meta::after { border-color: var(--accent); background: var(--accent); box-shadow: 0 0 0 5px var(--accent-soft); }
  .ver { display: block; font: 500 34px/1 "Fraunces", Georgia, serif; text-decoration: none; }
  .ver:hover { color: var(--accent); }
  time { display: block; color: var(--muted); font-size: 14.5px; margin-top: 8px; }
  .latest { display: inline-block; margin-top: 10px; padding: 3px 10px; border-radius: 999px; background: var(--accent-soft); color: var(--accent); font-size: 12.5px; font-weight: 700; }
  .card { background: var(--panel); border: 1px solid var(--line); border-radius: 20px; padding: 22px 26px 24px; }
  .card h3 { font-size: 24px; margin: 0 0 12px; }
  .items { list-style: none; margin: 0; padding: 0; }
  .items li { display: flex; gap: 12px; align-items: baseline; padding: 9px 0; border-top: 1px solid rgba(255,255,255,.05); line-height: 1.55; }
  .items li:first-child { border-top: 0; }
  .tag { flex: none; width: 92px; text-align: center; padding: 2px 0; border-radius: 6px; font-size: 12px; font-weight: 700; letter-spacing: .04em; }
  .tag.nuovo { background: rgba(61,220,151,.13); color: var(--green); }
  .tag.migliorato { background: rgba(108,180,255,.13); color: var(--blue); }
  .tag.corretto { background: rgba(255,181,71,.13); color: var(--amber); }
  kbd { font: 600 14px/1 "DM Sans", sans-serif; background: var(--panel2); border: 1px solid var(--line); border-bottom-width: 2px; border-radius: 6px; padding: 2px 7px; white-space: nowrap; }
  .btn { display: inline-flex; align-items: center; gap: 10px; padding: 11px 22px; border-radius: 999px; font-weight: 600; font-size: 15px; text-decoration: none; margin-top: 16px; }
  .btn.primary { background: var(--accent); color: #fff; box-shadow: 0 10px 30px rgba(255,61,127,.3); }
  .btn.primary:hover { background: #ff5590; }

  footer { margin-top: 40px; padding: 22px 0 34px; border-top: 1px solid var(--line); color: var(--muted); font-size: 14.5px; }
  footer .wrap { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 10px; }

  @media (max-width: 720px) {
    .top nav { gap: 18px; }
    .top nav a.hide-sm { display: none; }
    header.intro { padding-top: 30px; }
    .rel { grid-template-columns: 1fr; gap: 12px; padding-left: 26px; }
    .rel::before { left: 5px; margin-left: 0; top: 16px; }
    .meta { text-align: left; padding: 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 14px; }
    .meta::after { left: -26px; right: auto; top: 11px; }
    .ver { font-size: 28px; }
    time { margin-top: 0; }
    .latest { margin-top: 0; }
    .card { padding: 18px 18px 20px; }
    .items li { flex-direction: column; gap: 6px; }
  }
</style>
</head>
<body>
<div class="top"><div class="wrap">
  <a class="brand" href="/"><span class="logo">${HEART}</span>ScreenLove</a>
  <nav>
    <a class="hide-sm" href="/#funzioni">Funzioni</a>
    <a href="/novita" aria-current="page">Novità</a>
    <a href="/#scarica">Scarica</a>
  </nav>
</div></div>

<main class="wrap">
  <header class="intro">
    <div class="eyebrow">Novità</div>
    <h1>Cosa c'è di <em>nuovo</em>.</h1>
    <p class="lead">Ogni versione di ScreenLove con le sue novità, e quello che stiamo preparando per le prossime.
      Le copie installate si aggiornano da sole.</p>
  </header>

  <section id="in-arrivo">
    <h2>In arrivo</h2>
    <p class="sub">Ci stiamo lavorando per le prossime versioni.</p>
    <ul class="soon-list">${arrivo}
    </ul>
  </section>

  <section id="versioni">
    <h2>Versioni</h2>
    <p class="sub">Dalla più recente.</p>
    <ol class="releases">${elenco}
    </ol>
  </section>
</main>

<footer><div class="wrap">
  <span>ScreenLove · cattura dello schermo per Windows</span>
  <span><a href="/privacy">Privacy</a> · <a href="mailto:info@screenlove.it">info@screenlove.it</a></span>
</div></footer>
</body>
</html>`;
}

export function onRequestGet() {
  return new Response(renderNovita(), {
    headers: {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'public, max-age=300',
    },
  });
}
