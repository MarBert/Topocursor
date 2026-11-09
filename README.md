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

Funziona su **Windows 10/11** e su **Linux con X11** (vedi [Linux](#linux)).

## Struttura

```
TopoCursor.sln
assets/            config.json, mouse.png, topo.ico (condivisi)
src/Core/          config, decoder PNG, sprite, macchina a stati: niente codice di piattaforma
src/Windows/       overlay layered, hook del mouse, tray (WinForms)          → TopoCursor.exe
src/Linux/         overlay X11, XInput2, XFixes                              → topocursor
tools/             generatore dello spritesheet di default
```

## Windows

Serve il .NET 8 SDK (va bene anche Visual Studio 2022, aprendo `TopoCursor.sln`).

```powershell
.\run.ps1
```

Compila, avvia e resta in attesa: **Ctrl+C chiude l'app in modo pulito** e il cursore torna normale. Se PowerShell blocca gli script per la execution policy, usa `.\run.cmd`, che fa la stessa cosa.

Da qualsiasi terminale puoi anche chiudere l'istanza in esecuzione con `src\Windows\bin\Release\net8.0-windows\TopoCursor.exe --quit`.

Per compilare senza avviare: `dotnet build src\Windows -c Release` (oppure `dotnet build -c Release` per tutta la soluzione).

Evita invece `dotnet run` seguito da Ctrl+C: l'app viene uccisa senza poter fare pulizia, e solo il processo guardiano rimette a posto il cursore.

Per un exe singolo da mettere dove vuoi:

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

## Linux

Funziona con le sessioni **X11** (Xorg), con o senza compositor. **Wayland non è supportato**: per sicurezza, sotto Wayland un'app normale non può leggere la posizione globale del puntatore né disegnare sopra le altre finestre. Se il programma rileva una sessione Wayland, si ferma e lo spiega.

### 1. Controlla di essere su X11

```bash
echo $XDG_SESSION_TYPE
```

Se risponde `x11` sei a posto e puoi passare al punto 2. Se risponde `wayland`, dipende dal desktop:

| Desktop | X11 disponibile? |
|---|---|
| XFCE, Cinnamon (Linux Mint), MATE, i3, Openbox e altri window manager X11 | Sì, di solito è la sessione predefinita. |
| KDE Plasma fino alla 6.7 | Sì: nella schermata di login scegli **Plasma (X11)**. Su alcune distro va prima installata la sessione X11 (su Arch il pacchetto `plasma-x11-session`). Da Plasma 6.8 la sessione X11 non esiste più. |
| GNOME fino alla 48 (es. Ubuntu 24.04 LTS, Debian 13) | Sì: nella schermata di login, dopo aver scelto l'utente, clicca la rotellina in basso a destra e scegli **Ubuntu on Xorg** / **GNOME on Xorg**. |
| GNOME 49 e successivi (es. Ubuntu 25.10 e successive, Fedora 43 e successive) | **No**: GNOME non ha più una sessione X11. L'unica strada è installare accanto un desktop X11, per esempio XFCE o Cinnamon, e sceglierlo al login. |

### 2. Installa .NET 8 SDK e librerie X11

Le librerie X11 sono già presenti su qualsiasi desktop X11, ma il comando le installa nel caso manchino.

**Ubuntu** (22.04 e successive)

```bash
sudo apt install dotnet-sdk-8.0 libx11-6 libxext6 libxfixes3 libxi6
```

**Debian**: .NET non è nei repository ufficiali, quindi si aggiunge prima quello Microsoft (qui per Debian 12; per un'altra versione sostituisci `12`):

```bash
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt update && sudo apt install dotnet-sdk-8.0 libx11-6 libxext6 libxfixes3 libxi6
```

**Fedora**

```bash
sudo dnf install dotnet-sdk-8.0 libX11 libXext libXfixes libXi
```

**Arch / Manjaro**

```bash
sudo pacman -S dotnet-sdk-8.0 libx11 libxext libxfixes libxi
```

**Qualsiasi distro, senza root**: usa lo script ufficiale, che installa in `~/.dotnet`:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
echo 'export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc && source ~/.bashrc
```

Per controllare l'installazione: `dotnet --list-sdks` deve elencare un SDK `8.x`. Una versione più recente da sola non basta, perché per avviarlo serve il runtime 8.

### 3. Scarica, compila e prova

```bash
git clone https://github.com/MarBert/Topocursor.git
cd Topocursor
bash run.sh
```

`run.sh` compila solo il progetto Linux e lo avvia nel terminale. **Ctrl+C** lo chiude in modo pulito, **Ctrl+Alt+M** lo mette in pausa e lo riprende.

Al primo avvio stampa una riga come questa:

```
TopoCursor avviato – compositor: sì, XInput2: sì, nascondi cursore: sì.
```

Le voci della riga:

- **compositor**: con `sì` hai la trasparenza vera; con `no` funziona lo stesso, ma i bordi semitrasparenti vengono tagliati netti.
- **XInput2**: con `no (polling)` funziona comunque, ma i tap velocissimi del touchpad potrebbero sfuggire.

### 4. Installalo in modo permanente (facoltativo)

Crea un eseguibile singolo che include .NET: puoi copiarlo dove vuoi e non serve più l'SDK.

```bash
bash publish.sh                                              # → publish-linux/topocursor (ARM: bash publish.sh linux-arm64)
install -Dm755 publish-linux/topocursor ~/.local/bin/topocursor
topocursor --autostart on                                    # parte da solo a ogni login
```

Se il terminale dice `topocursor: command not found`, la cartella `~/.local/bin` non è nel `PATH`. Esci e rientra nella sessione, oppure usa il percorso completo `~/.local/bin/topocursor`.

Per avviarlo subito senza terminale: `nohup topocursor >/dev/null 2>&1 &`

### Comandi

```bash
topocursor                  # avvia
topocursor --quit           # chiude l'istanza in esecuzione (da un altro terminale)
topocursor --autostart on   # avvio automatico al login (~/.config/autostart); "off" per toglierlo
topocursor --reset          # rimette config e sprite di default in ~/.config/topocursor
topocursor --help
```

Se non l'hai installato, l'eseguibile compilato da `run.sh` è in `src/Linux/bin/Release/net8.0/topocursor`.

### Disinstallare

```bash
topocursor --quit
topocursor --autostart off
rm ~/.local/bin/topocursor
rm -r ~/.config/topocursor        # config e sprite personalizzati
```

### Differenze rispetto a Windows

- **Nessuna icona nella tray**: le impostazioni si cambiano da `~/.config/topocursor/config.json`, che si ricarica da solo appena lo salvi.
- `hideSystemCursor: "pointers"` equivale ad `"all"`: X11 permette di nascondere il cursore solo tutto insieme.
- **Il cursore non può restare invisibile**: è il server X a nasconderlo per conto del programma, e lo rimette da solo appena la connessione si chiude, anche se il processo viene ucciso con `kill -9`. Non serve nessun watchdog.
- Il cursore viene nascosto davvero ovunque, anche nelle app che ne usano uno personalizzato.

### Problemi comuni

| Sintomo | Soluzione |
|---|---|
| `Sessione Wayland rilevata` | Vedi il [punto 1](#1-controlla-di-essere-su-x11). |
| `Permission denied` su `./run.sh` | Lancialo con `bash run.sh`, oppure rendilo eseguibile con `chmod +x run.sh publish.sh`. |
| `dotnet: command not found` | L'SDK non è installato o non è nel `PATH`: vedi il [punto 2](#2-installa-net-8-sdk-e-librerie-x11). |
| `You must install .NET to run this application` | Hai installato .NET con lo script in `~/.dotnet` ma manca `DOTNET_ROOT`: vedi il [punto 2](#2-installa-net-8-sdk-e-librerie-x11). In alternativa usa la versione di `publish.sh`, che include .NET. |
| `Unable to load shared library 'libX11.so.6'` (o `libXfixes`, `libXi`, `libXext`) | Installa le librerie X11 del punto 2. |
| `Impossibile connettersi al server X` | Lo stai avviando fuori dalla sessione grafica, per esempio via SSH: lancialo da un terminale del desktop. |
| `TopoCursor è già in esecuzione` | C'è già un'istanza, magari avviata in automatico al login: `topocursor --quit`. |
| `Ctrl+Alt+M è già usata da un'altra applicazione` | La scorciatoia è occupata. Il resto funziona: per uscire usa `topocursor --quit`. |
| Il topo finisce sotto pannelli, menu o lock screen | Si rialza da solo nel giro di pochi centesimi di secondo, ma alcuni desktop tengono i propri pannelli sempre sopra. |

Sotto Linux, `dotnet build` dell'intera soluzione richiede i target WindowsDesktop, che non tutti gli SDK Linux hanno (mancano per esempio in quelli delle distribuzioni). Per questo `run.sh` e `publish.sh` compilano solo `src/Linux`.

## Cambiare lo spritesheet

Al primo avvio l'app crea la cartella di configurazione con dentro `config.json` e `mouse.png`:

- **Windows**: `%APPDATA%\TopoCursor\`
- **Linux**: `~/.config/topocursor/`

Puoi modificarli mentre l'app gira: si ricarica da sola.

Se metti un `config.json` accanto all'eseguibile, l'app lo usa al posto della cartella di configurazione (modalità portable).

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
    "hideSystemCursor": "all"  // "all" | "pointers" (solo Windows) | "none"
  }
}
```

Il PNG viene letto da un decoder interno (`src/Core/PngDecoder.cs`), identico su Windows e Linux: supporta tutti i formati PNG comuni, ma non quelli interlacciati.

Lo sprite di default viene generato da `tools/gen_sprites.py` (Python + Pillow). Puoi modificare lì colori e pose e rigenerarlo con:

```
python tools/gen_sprites.py assets
```

## Come funziona

La logica (stati, velocità, direzione, trascinamento) sta in `src/Core/PetController.cs` ed è la stessa sulle due piattaforme. Ognuna fornisce un overlay (`IOverlay`) e una fonte di input.

### Windows

- **Overlay**: una finestra popup `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`. Viene disegnata con `UpdateLayeredWindow`, con alpha per-pixel premoltiplicato. I click la attraversano e non ruba mai il focus.
- **Input**: un hook `WH_MOUSE_LL` aggiorna la posizione appena arriva l'evento e intercetta i click, senza consumarli. Un timer a ~60 Hz manda avanti le animazioni e fa da fallback con `GetCursorPos`.
- **Cursore di sistema**: viene sostituito con un cursore trasparente tramite `SetSystemCursor`. Per ripristinarlo si ricarica lo schema dell'utente con `SystemParametersInfo(SPI_SETCURSORS)`. Il ripristino avviene:
  - all'uscita
  - in caso di crash
  - da parte di un piccolo processo *watchdog*, che se ne accorge anche quando l'app viene uccisa da Task Manager o fermata dal debugger
- **Non è un Windows Service**: i servizi girano nella sessione 0 e non possono disegnare sul desktop. Per partire all'avvio usa l'opzione **Avvia con Windows**, che scrive una chiave in `HKCU\...\Run`.

### Linux (X11)

- **Overlay**: una finestra *override-redirect*, quindi il window manager non la gestisce e non ha decorazioni, con input shape vuota, così i click la attraversano. Con un compositor usa un visual ARGB a 32 bit con alpha per-pixel. Senza compositor la sagoma viene ritagliata con una bounding shape. Ogni frame è una Pixmap lato server, impostata come sfondo della finestra.
- **Input**: eventi *raw* di XInput2 sulla root window. Arrivano subito, anche mentre un'altra app ha un grab attivo (per esempio mentre trascini un'icona) e anche per i tap brevissimi del touchpad. Senza XInput2 si ripiega sul polling di `XQueryPointer`.
- **Cursore di sistema**: `XFixesHideCursor` sulla root.
- **Sempre in cima**: ogni ~45 ms controlla con `XQueryTree` se una finestra visibile gli è finita sopra, e in quel caso si rialza.

## Se il cursore resta invisibile (Windows)

Succede solo se muoiono sia l'app sia il watchdog. Hai tre modi per riaverlo:

- rilancia TopoCursor e chiudilo
- esegui `TopoCursor.exe --restore` (Win+R)
- fai logout

## Limiti noti

- Su Windows, le app che impostano un **proprio** cursore personalizzato continuano a mostrarlo sopra il topo. Esempi: alcuni giochi, cursori CSS nei browser, software 3D.
- L'overlay ha 1–2 frame di ritardo rispetto al cursore hardware. Nell'uso normale non si nota, nei giochi sì: mettilo in pausa con Ctrl+Alt+M.
- A ogni frame il topo controlla se qualche finestra gli è finita sopra (menu, tooltip, taskbar, icona trascinata, app "sempre in primo piano") e si rimette in cima. Restano però sopra di lui le finestre che Windows tiene in una "banda" superiore, riservata alla shell e alle app di accessibilità firmate: menu Start, ricerca, Alt+Tab, Visualizzazione attività, centro notifiche, Game Bar.
- Non appare sopra i giochi a schermo intero esclusivo né sul desktop sicuro di UAC.
- Su Linux alcuni compositor o desktop possono mostrare sopra il topo i propri pannelli o le schermate di blocco: lì si rialza, ma solo se il compositor glielo permette.
