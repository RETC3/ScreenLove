# Sito e download su Cloudflare

Il sito **https://screenlove.it** (Cloudflare Pages, progetto `screenlove`; `www.screenlove.it` e `screenlove.pages.dev` rimandano lì) mostra ScreenLove e permette di scaricarlo:
- **la pagina:** `web/public/index.html`;
- **i download:** passano da `/download/...` (`web/functions/download/[[path]].js`);
- **i file** stanno in un bucket **Cloudflare R2** (`screenlove-downloads`). Se R2 non è attivo, il sito
  inoltra i file della release su GitHub, e chi scarica vede comunque solo il sito;
- **`latest.json`** descrive l'ultima versione. Lo generano i workflow con `scripts/make-latest-json.ps1`.
  L'app lo legge per gli aggiornamenti (con GitHub come riserva) e controlla l'impronta SHA-256 dell'installer.

Tutto viene pubblicato dai workflow di GitHub:
- **`Release`** (con un tag `vX.Y.Z`) carica i file su R2, crea la release e aggiorna il sito;
- **`Sito`** ripubblica la pagina quando cambia qualcosa in `web/`.

## Configurazione (una volta sola)

### 1. Attiva R2 (consigliato)

1. Pannello Cloudflare → **R2 Object Storage**.
2. Clic su **Purchase R2 Plan / Attiva** e scegli il piano **gratuito**: 10 GB di spazio e download senza
   costi di traffico. Cloudflare chiede un metodo di pagamento anche per il piano gratuito, ma non addebita
   nulla entro i limiti.
3. Il bucket non va creato: lo crea il workflow **Sito**. Il workflow carica anche i file dell'ultima release
   che mancano, quindi basta rilanciarlo (Actions → Sito → Run workflow) dopo l'attivazione.

Se preferisci non attivare R2, salta questo passo: il sito funziona lo stesso, con i file presi dalla release
su GitHub.

### 2. Copia l'Account ID

Nel pannello Cloudflare, pagina **Panoramica dell'account** (o qualsiasi pagina di Workers & Pages): nella
colonna di destra c'è **Account ID**. Copialo.

### 3. Crea il token API

1. In alto a destra: icona del profilo → **My Profile** → **API Tokens** → **Create Token**.
2. In fondo, **Custom token** → **Get started**.
3. Nome: `ScreenLove GitHub`.
4. Permissions, due righe:
   - **Account** · **Cloudflare Pages** · **Edit**
   - **Account** · **Workers R2 Storage** · **Edit**
5. Account Resources: **Include** · il tuo account.
6. **Continue to summary** → **Create Token** e copia il token. Viene mostrato una volta sola.

### 4. Salva i due valori su GitHub

Repository → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**:

| Name | Secret |
|---|---|
| `CLOUDFLARE_ACCOUNT_ID` | l'Account ID del passo 2 |
| `CLOUDFLARE_API_TOKEN` | il token del passo 3 |

I valori restano cifrati su GitHub e nessuno li può rileggere, nemmeno tu.

### 5. Prima pubblicazione

- **Il sito:** scheda **Actions** → **Sito** → **Run workflow**. Il workflow crea il progetto Pages
  `screenlove` e pubblica la pagina.
- **I file su R2** arrivano con la prossima release (`git tag -a v0.1.1 -m "Novità…"` e push del tag).
  Fino ad allora i download del sito passano dalla release su GitHub.

## Controlli

- `https://screenlove.it/latest.json`: ultima versione, dimensioni e impronte.
- `https://screenlove.it/download/latest`: scarica l'installer più recente.
- In locale: `cd web`, poi `npx wrangler@4 pages dev` e apri http://localhost:8788. Serve
  `web/public/latest.json`, che si crea con `scripts/make-latest-json.ps1`.
