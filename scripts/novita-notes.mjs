// Stampa le note di una versione (da web/lib/novita.js) in Markdown, per la release su GitHub.
//   node scripts/novita-notes.mjs 0.2.0
// Codice di uscita 1 (senza output) se la versione non c'è.
import { versioni } from '../web/lib/novita.js';

const TIPI = { nuovo: 'Nuovo', migliorato: 'Migliorato', corretto: 'Corretto' };
const v = (process.argv[2] || '').replace(/^v/, '');
const rel = versioni.find((r) => r.versione === v);
if (!rel) process.exit(1);

const righe = [];
if (rel.titolo) righe.push(`## ${rel.titolo}`, '');
for (const [tipo, testo] of rel.voci) righe.push(`- **${TIPI[tipo] ?? tipo}:** ${testo}`);
righe.push('', `Tutte le novità e quelle in arrivo: https://screenlove.it/novita`);
process.stdout.write(righe.join('\n') + '\n');
