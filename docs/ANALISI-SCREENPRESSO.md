# Analisi di Screenpresso (riferimento per ScreenLove)

Data analisi: 2026-11-12 · Versione installata: **Screenpresso 2.4.1.0** (Learnpulse)
Percorso: `C:\Users\Stefano\AppData\Local\Learnpulse\Screenpresso`

> Scopo: capire *cosa fa* e *con quali tecnologie*, per costruire un'app nostra.
> ScreenLove va scritta da zero: niente codice decompilato, icone, testi o nome/brand di Screenpresso.

## 1. Stack tecnico (dai file installati)

| Area | Cosa usa Screenpresso | Evidenza |
|---|---|---|
| Runtime | .NET Framework (assembly managed, ~158 MB con risorse incorporate) | `Screenpresso.exe` è un assembly .NET, contiene `.NETFramework,Version` |
| UI | WPF (+ parti WinForms) | `PresentationFramework`, `WindowsBase`, `System.Windows.Forms` |
| Cattura schermo | Windows.Graphics.Capture + DXGI/Direct3D11 | stringa `GraphicsCapture`, `SharpDX.DXGI/Direct3D11` |
| Rendering editor | Direct2D (SharpDX) + SkiaSharp/HarfBuzz per testo | `SharpDX.Direct2D1`, `SkiaSharp`, `HarfBuzzSharp` |
| Video | Media Foundation + ffmpeg + codec proprio | `MediaFoundation`, `ffmpeg`, `ScreenpressoCodec.dll` |
| OCR | OCR nativo di Windows | `Windows.Media.Ocr` |
| Visione / blur automatico | OpenCV (Emgu CV) | `Emgu.CV.dll`, `cvextern.dll` |
| Rimozione sfondo | ONNX Runtime con modelli BiRefNet / U²-Net (rembg) | `onnxruntime.dll`, `licenses/BackgroundRemoval` |
| AI generativa | Google Gemini (`Google.GenAI`), `Microsoft.Extensions.AI` | DLL omonime |
| Sottotitoli / traduzione | Speech-to-text + Google Translate v2 | `Google.Apis.Translate.v2` |
| Documenti | GemBox.Document (PDF/DOCX/HTML) — libreria commerciale | `GemBox.Document.dll` |
| Android | ADB via USB | `libusb-1.0.dll` |
| Condivisione | AWS S3, Azure, Google Drive, YouTube, OneDrive (MSAL), SFTP (SSH.NET) | DLL SDK corrispondenti |
| Web view | WebView2 (login OAuth, pagine interne) | `Microsoft.Web.WebView2.*` |
| Estensione Chrome | Native messaging host `com.screenpresso.api` → `ScreenpressoBrowserNative.exe` (stdio) | `.manifest` |

## 2. Funzionalità (dal sito, pagine /features e /pricing)

**Cattura**
- Hotkey PrintScreen unico: area / finestra / schermo intero, con evidenziazione automatica di finestre ed elementi (edge snapping) e lente di ingrandimento
- Cattura con scorrimento (stitching), cursore incluso, ritardo (per menu contestuali)
- Registrazione video MP4 HD/4K, audio di sistema + microfono, webcam, schermi HDR
- Color picker, OCR, lettura QR code, cattura dispositivi Android (immagini e video)

**Editor immagini (vettoriale)**
- Frecce, rettangoli, ellissi, testo, fumetti/callout, spotlight, strumento "step" numerato
- Crop, ombra, angoli arrotondati, riflesso, bordi strappati, watermark
- Blur/pixelate, blur automatico dati sensibili (email, password…)
- Modifica immagini con AI generativa, rimozione sfondo

**Video**
- Taglio inizio/fine, merge, crop, muto, velocità, cambio formato, export frame
- Sottotitoli automatici da voce, blur automatico aree sensibili

**Workspace**
- Pannello con storico di tutte le catture (immagini, video, documenti), più workspace
- Drag & drop verso altre app, copia, stampa, organizzazione

**Condivisione / output**
- Cloud proprio con URL breve, email, Google Drive, YouTube, OneDrive, OneNote, Dropbox, Imgur, Linear, Cloudinary, SFTP, S3, script personalizzato
- Generatore documenti PDF / DOCX / HTML da immagini + commenti

**Altro**
- 22 lingue, portable (senza installazione), policy aziendali / MSI, estensione Chrome per pagine intere

## 3. Modello commerciale

| Piano | Prezzo | Note |
|---|---|---|
| Free | 0 € | cattura immagini, video HD con branding, editor limitato |
| Pro | 29,99 € una tantum / utente | tutto il resto (4K, OCR, Android, AI, documenti, blur…) |
| Enterprise | 2190 € una tantum, utenti illimitati | VDI/Citrix/RDS, attivazione offline |

Licenza perpetua con 1 anno di aggiornamenti; requisiti Windows 11 / Server 2022, x64 o ARM64.
