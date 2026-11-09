# 🐭 TopoCursor

Il cursore del mouse diventa un topolino in pixel art:

| Cosa fai | Cosa fa il topo |
|---|---|
| muovi il mouse | **corre** (più vai veloce, più zampetta veloce) e si gira verso dove vai |
| ti fermi | **si riposa**: respira, muove i baffi, sbatte le palpebre |
| resti fermo ~8 s | **si addormenta** con le Z |
| clicchi | **addenta un pezzo di formaggio** (click ripetuti = sgranocchia) |
| trascini (tasto sinistro premuto + movimento) | **si aggrappa al formaggio** e si fa trasportare, zampe a penzoloni |
| ti fermi a metà trascinamento | **abbraccia il formaggio** (con cuoricino) |

Il punto che "clicca" è la **punta del naso**.

![anteprima](docs/mouse_preview.gif)

## Requisiti

- Windows 10/11
- .NET 8 SDK per compilare (Visual Studio 2022 va bene)

## Compilare e avviare

```powershell
.\run.ps1
```

Compila, avvia e resta in attesa: **Ctrl+C chiude l'app in modo pulito** e il cursore torna normale. Se PowerShell blocca gli script per la execution policy, usa `.\run.cmd`, che fa la stessa cosa.

Da qualsiasi terminale puoi anche chiudere l'istanza in esecuzione con `TopoCursor.exe --quit`.

Evita invece `dotnet run` seguito da Ctrl+C: l'app viene uccisa senza poter fare pulizia, e solo il processo guardiano rimette a posto il cursore.

oppure, per un exe singolo da mettere dove vuoi:

```bat
publish.cmd
```

`publish\TopoCursor.exe` richiede il .NET 8 Desktop Runtime. Dentro `publish.cmd` c'è anche la variante self-contained, che non ha prerequisiti.

Una volta partito, l'app vive nella **tray** (icona col topolino). Dal menu puoi:

- **In pausa**: torna il cursore normale (anche con la scorciatoia **Ctrl+Alt+M**, o con doppio click sull'icona)
- **Dimensione**: scegli 1×, 2×, 3× o 4×
- **Nascondi cursore di sistema**:
  - *Sempre*
  - *Solo freccia e mano*: l'I-beam del testo e le frecce di ridimensionamento restano visibili
  - *Mai*
- **Avvia con Windows**
- **Ripristina sprite e config di default** (utile dopo un aggiornamento che aggiunge animazioni)
- **Apri cartella sprite/config** e **Ricarica sprite**

## Cambiare lo spritesheet

Al primo avvio l'app crea `%APPDATA%\TopoCursor\` con dentro `config.json` e `mouse.png`. Puoi modificarli mentre l'app gira: si ricarica da sola.

Se metti un `config.json` accanto all'exe, l'app lo usa al posto di `%APPDATA%` (modalità portable).

Lo sheet è organizzato **a righe**: ogni animazione occupa una riga, con i frame affiancati da sinistra a destra.

```jsonc
{
  "spritesheet": "mouse.png",
  "frameWidth": 48, "frameHeight": 32,
  "hotspot": { "x": 37, "y": 18 },   // pixel del frame che sta sotto il puntatore reale
  "facing": "right",                 // verso in cui guarda lo sprite (l'app lo specchia quando vai nell'altro senso)
  "scale": 2,                        // ingrandimento intero, nearest-neighbor
  "animations": {
    "run":   { "row": 0, "frames": 4, "fps": 12 },  // obbligatoria (fps ignorati: dipendono dalla velocità)
    "idle":  { "row": 1, "frames": 6, "fps": 4 },   // obbligatoria
    "sleep": { "row": 2, "frames": 4, "fps": 2 },   // facoltativa
    "bite":  { "row": 3, "frames": 7, "fps": 14 },  // obbligatoria, suonata una volta per click
    "drag":  { "row": 4, "frames": 4, "fps": 12 },  // facoltativa: trascinamento (fps scalati con la velocità)
    "hold":  { "row": 5, "frames": 4, "fps": 4 }    // facoltativa: tasto premuto ma fermo durante un trascinamento
  },
  "behavior": {
    "idleAfterMs": 140,        // dopo quanto smette di correre
    "sleepAfterMs": 8000,      // 0 = non dorme mai
    "runFpsMin": 8, "runFpsMax": 22,
    "runSpeedForMaxFps": 1600, // px/s a cui la corsa raggiunge runFpsMax
    "turnThresholdPx": 6,      // isteresi prima di girarsi
    "biteOn": "any",           // "left" | "right" | "any"
    "dragButton": "left",      // "left" | "right" | "middle" | "none"
    "dragThresholdPx": 5,      // movimento col tasto premuto prima che diventi trascinamento
    "hideSystemCursor": "all"  // "all" | "pointers" | "none"
  }
}
```

Lo sprite di default viene generato da `tools/gen_sprites.py` (Python + Pillow). Puoi modificare lì colori e pose e rigenerarlo con:

```
python tools/gen_sprites.py assets
```

## Come funziona

- **Overlay**: una finestra popup `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`. Viene disegnata con `UpdateLayeredWindow`, con alpha per-pixel premoltiplicato. I click la attraversano e non ruba mai il focus.
- **Input**: un hook `WH_MOUSE_LL` aggiorna la posizione appena arriva l'evento e intercetta i click, senza consumarli. Un timer a ~60 Hz manda avanti le animazioni e fa da fallback con `GetCursorPos`.
- **Cursore di sistema**: viene sostituito con un cursore trasparente tramite `SetSystemCursor`. Per ripristinarlo si ricarica lo schema dell'utente con `SystemParametersInfo(SPI_SETCURSORS)`. Il ripristino avviene:
  - all'uscita
  - in caso di crash
  - da parte di un piccolo processo *watchdog*, che se ne accorge anche quando l'app viene uccisa da Task Manager o fermata dal debugger
- **Non è un Windows Service**: i servizi girano nella sessione 0 e non possono disegnare sul desktop. Per partire all'avvio usa l'opzione **Avvia con Windows**, che scrive una chiave in `HKCU\...\Run`.

## Se il cursore resta invisibile

Succede solo se muoiono sia l'app sia il watchdog. Hai tre modi per riaverlo:

- rilancia TopoCursor e chiudilo
- esegui `TopoCursor.exe --restore` (Win+R)
- fai logout

## Limiti noti

- Le app che impostano un **proprio** cursore personalizzato continuano a mostrarlo sopra il topo. Esempi: alcuni giochi, cursori CSS nei browser, software 3D.
- L'overlay ha 1–2 frame di ritardo rispetto al cursore hardware. Nell'uso normale non si nota, nei giochi sì: mettilo in pausa con Ctrl+Alt+M.
- A ogni frame il topo controlla se qualche finestra gli è finita sopra (menu, tooltip, taskbar, icona trascinata, app "sempre in primo piano") e si rimette in cima. Restano però sopra di lui le finestre che Windows tiene in una "banda" superiore, riservata alla shell e alle app di accessibilità firmate: menu Start, ricerca, Alt+Tab, Visualizzazione attività, centro notifiche, Game Bar.
- Non appare sopra i giochi a schermo intero esclusivo né sul desktop sicuro di UAC.
