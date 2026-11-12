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
- Se un tasto è già occupato (es. da un'altra app di cattura) si usano `Ctrl+Alt+A` / `Ctrl+Alt+F` / `Ctrl+Alt+S` / `Ctrl+Alt+T`
- Multi-monitor e DPI per-monitor

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

**Area di notifica**: menu con catture, opzioni (appunti, editor/workspace dopo la cattura, cursore) e avvio con Windows.

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

Risultato: `dist\win-x64\ScreenLove.exe` (~75 MB). Opzioni: `-Arm64` per i PC ARM64
(`dist\win-arm64\`), `-Zip` per creare anche `dist\ScreenLove-<versione>-<piattaforma>.zip`.
La cartella `dist\` non è nel repository.

L'icona dell'eseguibile (`src/ScreenLove/app.ico`) si rigenera con `scripts\make-icon.ps1`.

Diagnostica della cattura a scorrimento: con la variabile d'ambiente `SCREENLOVE_SCROLL_DEBUG`
impostata a una cartella, ogni fotogramma e l'esito di ogni confronto vengono salvati lì.

## Struttura

| Cartella | Contenuto |
|---|---|
| `Capture/` | overlay di selezione, conto alla rovescia, orchestrazione della cattura |
| `Services/` | cattura GDI, ricerca finestre, tasti rapidi globali, storico, I/O immagini, icona |
| `Workspace/` | finestra dello storico |
| `Recording/` | registrazione video: cattura fotogrammi, mixer audio (NAudio), codifica MP4, cornice e barra di controllo |
| `Ocr/` | finestra del testo riconosciuto (OCR e codici QR/a barre) |
| `Editor/` | documento (immagine + annotazioni), annotazioni vettoriali, superficie di disegno, finestra editor |

## Prossimi passi (roadmap)

1. Video: taglio inizio/fine, unione, webcam, cattura con Windows.Graphics.Capture (meno CPU)
2. Scorrimento: ripulire la colonna della barra di scorrimento, scorrimento orizzontale
3. OCR: filtrare il "rumore" (icone lette come lettere), OCR su una selezione dentro l'editor
4. Effetti bordo: ombra, angoli arrotondati, bordi strappati, watermark
5. Generatore documenti (PDF/HTML/DOCX) dalle catture
6. Condivisione (link, cloud, email), impostazioni con interfaccia, più lingue
