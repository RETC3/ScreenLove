# ScreenLove ♥

Strumento di cattura dello schermo per Windows (C# · .NET 8 · WPF), ispirato alle funzionalità di Screenpresso
ma scritto da zero. Analisi del prodotto di riferimento: [docs/ANALISI-SCREENPRESSO.md](docs/ANALISI-SCREENPRESSO.md).

## Funzioni (v0.1)

**Cattura**
- `Stamp`: overlay a schermo intero con evidenziazione automatica della finestra sotto il cursore,
  selezione area trascinando, lente di ingrandimento, coordinate e colore del pixel
  - clic = finestra · trascina = area · `F` = schermo intero · frecce = sposta di 1 px
  - `Ctrl+C` = copia il colore esadecimale (color picker) · `Esc` / clic destro = annulla
- `Ctrl+Stamp`: schermo intero (con cursore opzionale)
- `Ctrl+Maiusc+Stamp`: **cattura a scorrimento**. Si sceglie una finestra o una zona; ScreenLove la fa
  scorrere con la rotellina, cattura dopo ogni scatto e cuce i fotogrammi in un'unica immagine alta
  - riconosce intestazioni e piè di pagina fissi e li tiene una volta sola
  - tollera lo scorrimento animato e a frazioni di pixel dei browser
  - adatta da solo quanto scorrere per volta; si ferma a fine pagina, con `Esc` o a 40.000 px
  - l'indicatore di avanzamento è escluso dalle catture
- `Ctrl+Alt+Stamp`: **testo (OCR) e QR code**. Si seleziona una zona: il testo viene riconosciuto
  con l'OCR integrato in Windows (offline, lingue installate nel sistema) e copiato negli appunti;
  QR code, Data Matrix e codici a barre vengono letti con ZXing.Net (link apribili con un clic).
  Nella finestra del risultato si può cambiare lingua, modificare il testo e unire le righe.
  Disponibile anche come "Estrai testo" su una cattura del workspace o nell'editor.
- `Maiusc+Stamp`: cattura con conto alla rovescia (per menu contestuali)
- `Ctrl+Alt+R`: **registrazione video MP4** (avvia / ferma). Si sceglie finestra o zona, parte un
  conto alla rovescia di 3 s, poi si registra a 30 fps in H.264 con audio AAC
  - audio di sistema e/o microfono (scelta nel menu dell'icona), mescolati in un'unica traccia
    e sincronizzati col video (scarto misurato < 60 ms, anche dopo una pausa)
  - cornice attorno alla zona e barra con tempo, Pausa/Riprendi, Stop, Annulla: non compaiono nel video
  - codifica hardware quando disponibile (Media Foundation tramite il MediaTranscoder di Windows)
  - webcam opzionale: bolla rotonda trascinabile nell'angolo della zona, registrata nel video
- Tutti i tasti rapidi sono personalizzabili nelle Impostazioni; quelli già occupati da altre app vengono segnalati
- Multi-monitor e DPI per-monitor

**Video (workspace → Modifica, o Altro ▾)**
- **Taglia**: anteprima con riproduzione, `[ Inizio qui` / `Fine qui ]` (tasti `I` / `O`, frecce = un fotogramma),
  salva un nuovo video senza toccare l'originale
- **Unisci** più video selezionati (dal più vecchio al più recente); se il motore di montaggio di Windows
  non è disponibile (es. macchine virtuali) si usa automaticamente un metodo di riserva

**Documenti (workspace → Altro ▾ → Crea documento)**
- Da una o più immagini: titolo del documento, titolo e commento per ogni passaggio, ordine modificabile,
  numerazione automatica
- Formati: **PDF** (PDFsharp), **Word .docx** (Open XML SDK), **HTML** autonomo con immagini incorporate

**Workspace**
- Storico di tutte le catture (`Immagini\ScreenLove`), miniature, selezione multipla
- Doppio clic = editor (immagini) o lettore di Windows (video) · trascina verso altre app ·
  copia immagine/percorso · elimina nel Cestino
- I video mostrano anteprima, risoluzione e durata

**Editor**
- Freccia (`A`), Rettangolo (`R`), Ellisse (`E`), Evidenziatore (`H`), Testo (`T`), Step numerati (`N`),
  Sfocatura/pixel (`B`), Spotlight (`S`), Ritaglio (`C`), Seleziona/sposta (`V`)
- Colore e spessore modificabili anche sull'elemento selezionato, `Maiusc` per vincolare forme e angoli
- Annulla/Ripeti, zoom (`Ctrl`+rotellina), Copia, Salva (sovrascrive la cattura), Salva come PNG/JPG/BMP
- **Effetti** sull'intera immagine (annullabili): ombra, angoli arrotondati, bordo, bordo inferiore strappato,
  watermark, "stile documentazione" (angoli + ombra). La trasparenza resta nei PNG; in JPG diventa bianco.

**Impostazioni** (⚙ nel workspace o menu dell'icona): cartella delle catture, avvio con Windows, comportamento
dopo la cattura, ritardo, video (fps, audio di sistema, microfono, cursore, webcam), testo del watermark, tasti rapidi.

**Area di notifica**: menu con catture, opzioni rapide, impostazioni e avvio con Windows.

## Compilare ed eseguire

```bash
dotnet build ScreenLove.sln
```

```bash
dotnet run --project src/ScreenLove
```

L'eseguibile di sviluppo è in `src/ScreenLove/bin/Debug/net8.0-windows10.0.19041.0/ScreenLove.exe` e richiede il
.NET 8 Desktop Runtime.

### Eseguibile autonomo (da distribuire)

Un unico `ScreenLove.exe` che include .NET: parte con un doppio clic, senza installare nulla.

```bash
scripts\publish
```

(`publish.cmd` avvia `publish.ps1` aggirando solo per quel comando il blocco degli script PowerShell,
attivo di default su Windows. Si può anche fare doppio clic su `scripts\publish.cmd`.)

Risultato: `dist\win-x64\ScreenLove.exe` (~77 MB). Opzioni:
- `-Arm64`: anche per i PC ARM64 (`dist\win-arm64\`)
- `-Zip`: crea anche `dist\ScreenLove-<versione>-<piattaforma>.zip`
- `-Installer`: crea anche `dist\ScreenLove-Setup-<versione>.exe` (serve [Inno Setup 6](https://jrsoftware.org/isdl.php))
- `-Version 0.2.0`: usa questa versione invece di quella del `.csproj`

La cartella `dist\` non è nel repository.

### Installer

`installer\ScreenLove.iss` (Inno Setup) installa ScreenLove per l'utente corrente, senza permessi di
amministratore, in `%LOCALAPPDATA%\Programs\ScreenLove`:
- collegamento nel menu Start, icona sul desktop facoltativa, "Avvia con Windows" (attivo di default)
- disinstallazione da "App installate"; le catture e le impostazioni restano
- se ScreenLove è aperto, l'installer propone di chiuderlo e il disinstallatore chiede di uscire

### Aggiornamenti

All'avvio (al massimo una volta al giorno, disattivabile nelle Impostazioni) ScreenLove legge l'ultima
versione dal sito (`https://screenlove.pages.dev/latest.json`, con GitHub come riserva). Se è più recente mostra una notifica; da lì, dal menu dell'icona ("Cerca aggiornamenti…")
o dalle Impostazioni ("Cerca ora") si aprono le novità con il pulsante **Aggiorna ora**. Il pulsante:
1. scarica l'installer e ne controlla dimensione e impronta SHA-256;
2. chiude ScreenLove;
3. installa la nuova versione senza domande;
4. riapre l'app.

La versione portatile (exe senza installer) apre invece la pagina della release.
- Si scaricano solo file dal sito di ScreenLove o dalle release di questo repository.
- `ScreenLove.exe --check-updates` controlla subito e mostra sempre l'esito.
- Per provare senza GitHub: `SCREENLOVE_UPDATE_FEED` = percorso di un file JSON nel formato di `/releases/latest`.

### Sito e download

Il sito **https://screenlove.pages.dev** (Cloudflare Pages, cartella `web/`) presenta l'app e la fa scaricare
da `/download/latest`; i file stanno su Cloudflare R2. Configurazione e dettagli: [docs/CLOUDFLARE.md](docs/CLOUDFLARE.md).

### Pubblicare una release

```bash
git tag -a v0.2.0 -m "Novità: …"
```

```bash
git push origin v0.2.0
```

Il workflow `.github/workflows/release.yml` fa tutto da solo:
- esegue i controlli automatici;
- compila x64 e ARM64;
- crea installer e zip;
- carica installer e zip su Cloudflare R2;
- pubblica la release "ScreenLove 0.2.0" (novità = messaggio del tag, altrimenti note automatiche);
- aggiorna il sito https://screenlove.pages.dev (pagina e `latest.json`).

Le copie installate la trovano al controllo successivo. Dalla scheda Actions si può anche solo compilare
(senza pubblicare) con "Run workflow".

L'icona dell'eseguibile (`src/ScreenLove/app.ico`) si rigenera con `scripts\make-icon.ps1`.

### Controlli automatici

```bash
dotnet run --project tests/ScreenLove.Checks
```

Verificano tasti rapidi, impostazioni, effetti immagine, documenti PDF/Word/HTML (Word validato con l'Open XML SDK),
registrazione, taglio e unione video (anche con audio, tramite clip sintetiche). Con `--no-video` si salta la parte
video, che registra 3 secondi dell'angolo in alto a sinistra dello schermo. Codice di uscita = numero di errori.
Con `--online` si controlla anche l'ultima release vera su GitHub (lettura e download dell'installer).

Diagnostica della cattura a scorrimento: con la variabile d'ambiente `SCREENLOVE_SCROLL_DEBUG`
impostata a una cartella, ogni fotogramma e l'esito di ogni confronto vengono salvati lì.

## Struttura

| Cartella | Contenuto |
|---|---|
| `Capture/` | overlay di selezione, conto alla rovescia, orchestrazione della cattura |
| `Services/` | cattura GDI, ricerca finestre, tasti rapidi globali, storico, I/O immagini, icona |
| `Workspace/` | finestra dello storico |
| `Recording/` | registrazione video: cattura fotogrammi, mixer audio (NAudio), codifica MP4, cornice e barra di controllo |
| `Recording/` → anche `WebcamWindow` | bolla della webcam durante la registrazione |
| `Video/` | taglio e unione video, metodo di riserva per l'unione, finestra di taglio |
| `Documents/` | generatore di documenti PDF / Word / HTML e relativa finestra |
| `Options/` | finestra delle impostazioni |
| `Ocr/` | finestra del testo riconosciuto (OCR e codici QR/a barre) |
| `Editor/` | documento (immagine + annotazioni), annotazioni vettoriali, effetti, superficie di disegno, finestra editor |
| `Options/` → anche `UpdateWindow`, `Services/Updater` | aggiornamenti dalle release di GitHub |
| `installer/` | script Inno Setup dell'installer |
| `web/` | sito su Cloudflare Pages: pagina, download (Pages Functions + R2) |
| `.github/workflows/` | release automatica da tag, pubblicazione del sito |
| `tests/ScreenLove.Checks` | controlli automatici |

## Prossimi passi (roadmap)

1. Video: cattura con Windows.Graphics.Capture (meno CPU), velocità e muto nel taglio, sottotitoli
2. Scorrimento: ripulire la colonna della barra di scorrimento, scorrimento orizzontale
3. OCR: filtrare il "rumore" (icone lette come lettere), OCR su una selezione dentro l'editor
4. Condivisione (link, cloud, email), più lingue
5. Firma del codice (niente avviso SmartScreen al primo avvio)
