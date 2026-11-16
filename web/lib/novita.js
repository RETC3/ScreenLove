// Novità di ScreenLove: unica fonte per la pagina https://screenlove.it/novita e per le note delle
// release su GitHub (scripts/novita-notes.mjs, usato dal workflow Release).
//
// Prima di pubblicare una versione, aggiungila IN CIMA a `versioni`. Ogni voce ha un tipo:
//   nuovo · migliorato · corretto
// Nel testo si possono usare **grassetto** e `tasti` (per esempio `Ctrl+Stamp`).
//
// `inArrivo` è l'elenco di quello che stiamo preparando. Stato:
//   sviluppo (ci stiamo lavorando) · programma (in programma per le prossime versioni)
// Quando una novità esce, toglila da qui e mettila nella versione.

export const versioni = [
  {
    versione: '0.3.0',
    data: '2026-11-14',
    titolo: 'Più leggero',
    voci: [
      ['corretto', "Memoria: dopo catture e finestre chiuse ScreenLove restituisce la RAM a Windows. Prima poteva restare a centinaia di MB, o superare 1 GB, anche ferma nell'area di notifica."],
      ['migliorato', 'Selezione della zona più fluida e leggera sulla scheda grafica, anche con più monitor ad alta risoluzione.'],
      ['migliorato', 'Miniature dello storico più leggere, soprattutto per le catture a scorrimento molto lunghe.'],
    ],
  },
  {
    versione: '0.2.0',
    data: '2026-11-13',
    titolo: 'Un sito tutto nuovo',
    voci: [
      ['nuovo', 'Sito ufficiale **screenlove.it**: download e aggiornamenti passano da lì (GitHub resta come riserva).'],
      ['migliorato', "Aggiornamenti più sicuri: prima di avviarlo, l'installer scaricato viene controllato con la sua impronta SHA-256."],
      ['nuovo', "Statistiche anonime d'uso: una volta al giorno l'app comunica solo un codice casuale, la versione e il tipo di Windows, per contare le copie in uso. Si disattivano in **Impostazioni → Aggiornamenti**."],
    ],
  },
  {
    versione: '0.1.0',
    data: '2026-11-13',
    titolo: 'La prima versione',
    voci: [
      ['nuovo', 'Cattura di finestre, zone e schermo intero con `Stamp`, lente di ingrandimento e selettore di colore.'],
      ['nuovo', 'Cattura a scorrimento (`Ctrl+Maiusc+Stamp`): pagine intere cucite in una sola immagine, fino a 40.000 pixel.'],
      ['nuovo', 'Testo (OCR) e QR code (`Ctrl+Alt+Stamp`): il testo di una zona copiato negli appunti, senza internet.'],
      ['nuovo', 'Registrazione video MP4 (`Ctrl+Alt+R`) con audio di sistema, microfono e webcam; taglio e unione dei video.'],
      ['nuovo', 'Editor con frecce, forme, testo, passaggi numerati, sfocatura, spotlight ed effetti (ombra, bordi, watermark).'],
      ['nuovo', 'Documenti PDF, Word e HTML da una o più catture, con titoli e commenti.'],
      ['nuovo', 'Storico delle catture, tasti rapidi personalizzabili, installer e versione portatile per x64 e ARM64.'],
    ],
  },
];

export const inArrivo = [
  { stato: 'programma', titolo: 'Niente più avviso di Windows', testo: "Installer con firma digitale: al primo avvio SmartScreen non mostrerà più \"App non riconosciuta\"." },
  { stato: 'programma', titolo: 'Si apre anche quando è già attivo', testo: "Doppio clic sull'icona del desktop con ScreenLove già nell'area di notifica: si apre la finestra delle catture invece di non succedere nulla." },
  { stato: 'programma', titolo: 'Scegli lo schermo', testo: 'Con più monitor, la cattura dello schermo intero chiede se prenderli tutti o solo uno.' },
  { stato: 'programma', titolo: 'Ritaglio con le maniglie', testo: "Premi **Ritaglia** e compare una cornice con le maniglie su angoli e lati da trascinare, come nelle foto dello smartphone." },
  { stato: 'programma', titolo: 'Annotazioni modificabili', testo: 'Frecce, forme, sfocature e le altre annotazioni si spostano e si ridimensionano con le maniglie, senza cancellarle e rifarle.' },
  { stato: 'programma', titolo: 'Più spotlight insieme', testo: "Si possono evidenziare più zone nella stessa immagine: il resto si scurisce una volta sola, non di più a ogni spotlight." },
  { stato: 'programma', titolo: 'Scegli microfono, audio e webcam', testo: 'Nelle impostazioni si sceglie quale microfono, quale uscita audio e quale webcam usare. Di base restano quelli predefiniti di Windows.' },
  { stato: 'programma', titolo: 'Video più leggeri', testo: 'Registrazione con la cattura moderna di Windows: meno lavoro per il processore. Nel taglio, velocità e audio muto.' },
  { stato: 'programma', titolo: 'Scorrimento ancora più pulito', testo: 'Cattura a scorrimento senza la barra laterale e anche in orizzontale.' },
  { stato: 'programma', titolo: 'OCR più preciso', testo: "Meno simboli letti per sbaglio e lettura del testo direttamente da una selezione nell'editor." },
  { stato: 'programma', titolo: 'Condivisione', testo: 'Inviare una cattura con un link, sul cloud o per email, in un clic.' },
  { stato: 'programma', titolo: 'Più lingue', testo: "ScreenLove anche in inglese e in altre lingue." },
];
