# 06 – Control panel & whut

WebHut ships two programs that share one core:

- **`WebHut.exe`**: the control panel and setup wizard.
- **`whut.exe`**: the same operations from a terminal or script.

Both are small, self-contained .NET Framework 4.8 executables (about 180 KB and 130 KB), and Windows 10 and 11 already include the runtime.

## WebHut.exe

### First run: the setup window

When `apache\` or a PHP version is missing, WebHut opens the setup window instead of the panel:

| Part | What it does |
|---|---|
| Component list | Shows the version WebHut found for each component and whether a checksum is available. Apache and PHP are required; everything else can be unticked. |
| **Use a zip I downloaded…** | For the selected component, use a file you downloaded yourself (for offline installs, or when a lookup fails). PHP's branch is read from the file name. |
| **Open download page** | Opens the selected component's official download page |
| **Look up again** | Re-runs the version lookup |
| Visual C++ line | Shows whether the VC++ 2017–2026 x64 runtime (14.50 or newer) is installed, and offers **Install it**. Windows asks for admin permission; this is the only step that needs it. |
| Ports and root password | HTTP port (80), MySQL port (3306), and an optional root password |
| **Install** | Downloads, verifies, unpacks, writes config and initialises MariaDB, with live progress. **Stop** cancels; finished downloads are cached in `tmp\downloads\` and reused on the next try. |

### The panel

```text
┌────────────────┬─────────────────────────────────────────────────────────────┐
│ 🔥 WebHut      │ WebHut                                        [▶ Start all] │
│    v1.2.0      │ Local development environment                               │
│                │ C:\webhut · PHP 8.5.11 · Node 24.22.0                       │
│ ▌Dashboard     │ ┌ Services ─────────────────────────── ● All services running ┐
│  Open localhost│ │ SERVICE  DETAILS          PID   PORT  STATUS     ACTIONS   │
│  phpMyAdmin    │ │ Apache   2.4.69 · PHP 8.5 5120  80    ● Running  [■ Stop] Open Logs Config │
│  www folder    │ │ MariaDB  12.3.3           6612  3306  ● Running  [■ Stop] Open Logs Config │
│  Shell         │ │ api      Node 24 · npm…   -     3000  ● Stopped  [▶ Start] Open Logs Config│
│  Refresh sites │ └─────────────────────────────────────────────────────────────┘
│  Node apps     │ ┌ Quick Actions ──────────────────────────────────────────────┐
│  Database tools│ │ Open localhost │ phpMyAdmin │ www folder │ Shell │ Refresh │ Node apps │
│  Log files     │ └─────────────────────────────────────────────────────────────┘
│                │ ┌ Activity ─────────────────────────── [Clear]  ◉ Auto-scroll ┐
│  Settings      │ │ [10:41:02]  Apache is running: http://localhost/             │
│  About         │ │ [10:41:05]  MariaDB is running on 127.0.0.1:3306             │
│  Quit WebHut   │ └─────────────────────────────────────────────────────────────┘
└────────────────┴─────────────────────────────────────────────────────────────┘
```

| Control | Action |
|---|---|
| Status pill | **Running** (green), **Stopped** (grey), or **Starting… / Stopping…** (amber). Refreshed every 2 seconds. The Services header sums it up: all running, all stopped, or "1 of 2 running". |
| **Start / Stop** | Start or stop that service. Failures show Apache's, MariaDB's or the app's own last log lines. |
| **Open** | Apache: `http://localhost`. MariaDB: phpMyAdmin. Node app: `http://<name>.localhost`. |
| **Logs** | Opens the service's log in Notepad (`apache-error.log`, `mariadb.err`, `node-<name>.log`) |
| **Config** | Opens `httpd.conf`, `my.ini` or `node-apps.json` |
| **Start all / Stop all** | One button that follows the servers' state. When nothing is running it reads **Start all**: Apache and MariaDB, plus Node apps marked *Start with "Start all"*. Once anything is running it reads **Stop all**, which stops every service, Node apps included. |
| **Open localhost / phpMyAdmin / www folder** | In the sidebar and as Quick Actions tiles |
| **Shell** | A console in `www\` with `php`, `node`, `npm`, `npx`, `composer`, `mariadb` and `httpd` on `PATH` |
| **Refresh sites** | Regenerates vhosts from the folders in `www\` and restarts Apache if it's running |
| **Node apps** | Add, edit and remove Node apps ([04](04-node-integration.md)) |
| **Database tools** | Backups (automatic and manual), restore, rebuild from backup, and **Import from XAMPP…** ([11](11-databases.md)) |
| **Log files** | Opens the `logs\` folder |
| **Activity** | Everything WebHut does, newest at the bottom. **Clear** empties the view (not the log file); **Auto-scroll** keeps the newest line in view. |
| **Settings** | See below |
| **Dark mode / Light mode** | Switches the colour theme (see Appearance below) |
| **About** | Version, folder, and a link to the project page |
| **Quit WebHut** | Fully closes WebHut. If servers are running, it asks whether to stop them first or leave them running in the background. |
| Tray icon | Closing the window (X) keeps WebHut in the tray (configurable). Right-click for Start all (or Stop all when servers are running), Open localhost and Quit WebHut. |

If MariaDB fails to start and backups exist, WebHut offers to rebuild it from the newest one (the damaged folder is kept). When Windows shuts down or you log off, WebHut stops MariaDB cleanly first, so a shutdown never needs crash recovery.

Only one panel can run per WebHut folder. Separate WebHut folders can run side by side on different ports.

### Settings

| Setting | Notes |
|---|---|
| PHP version | Choose an installed branch. **Install another version…** downloads one. Saving restarts Apache. |
| Node.js version | Choose an installed major. **Install latest LTS** adds the newest LTS line. |
| Appearance | **Match Windows** (default) follows Windows' light/dark app mode and switches live. You can also pick **Light** or **Dark**. The sidebar's **Dark mode / Light mode** item switches between the two in one click. |
| HTTP port, MySQL port | Saving restarts the affected server and updates phpMyAdmin's config |
| Database root → **Change…** | Runs `ALTER USER` for `root@localhost`, `root@127.0.0.1` and `root@::1` (starting MariaDB if needed), stores the new password, and updates phpMyAdmin's `AllowNoPassword` |
| Network: allow access from other devices | Phones and other PCs on your Wi-Fi can open the sites; phpMyAdmin and MariaDB stay local ([08](08-security.md#opening-webhut-to-your-lan-for-testing-on-a-phone)). Saving restarts Apache. |
| Start servers when WebHut opens | |
| Closing the window keeps WebHut in the tray | |
| **Check for updates…** | Compares installed Apache, the active PHP branch, MariaDB, phpMyAdmin and the active Node major with the latest versions, and updates the ones you tick (see below) |
| **Install / repair Visual C++ runtime** | Re-runs Microsoft's installer |

#### How updates work

1. The affected servers are stopped.
2. The new version is downloaded and verified like at setup.
3. The old folder moves to `tmp\backup\<component>-<timestamp>\`, and the new one takes its place. `php.ini` and phpMyAdmin's `config.inc.php` are carried over.
4. The servers start again.

MariaDB updates are offered **within the same series only** (12.3.x → 12.3.y). Moving to a new LTS series (for example 12.3 → a future 13.x LTS) changes the on-disk format. Do it deliberately: back up with `mariadb-dump --all-databases`, replace `mariadb\` with the new version, start it, then run `mariadb-upgrade -u root`.

## whut

```text
whut status                      Show every service
whut start   [all|apache|mariadb|<node-app>]
whut stop    [all|apache|mariadb|<node-app>]
whut restart [all|apache|mariadb|<node-app>]
whut sites                       Regenerate vhosts for folders in www\ and Node apps
whut php [<branch>]              Show or switch the active PHP version (e.g. 8.4)
whut setup                       Download and install everything (first run)
whut backup                      Back up all databases now (data\backups)
whut backups                     List backups
whut restore [<file>|latest]     Load a backup into the running MariaDB
whut rebuild-db [<file>|latest]  Set a damaged data folder aside and rebuild it from a backup
whut import-xampp [<folder>] [--recover] [--projects]
                                    Copy databases (and htdocs projects) from XAMPP
whut shell                       Open a console with php, node, npm and composer on PATH
whut version
```

- The default target is `all`. As with the panel, `start all` includes only Node apps marked autostart; `stop all` stops everything.
- Node app targets use the app name: `whut restart api`.
- Exit code `0` means success, `1` a failure (details on stderr), and `2` an unusable folder path.
- `whut setup` is the non-interactive version of the setup window. It needs every component to resolve, so offline installs go through the GUI's **Use a zip I downloaded…**.
- `whut` and `WebHut.exe` can be used at the same time; the panel picks up changes within 2 seconds.

```text
> whut status
SERVICE          STATE     PID     PORT    DETAIL
Apache           running   5120    80      2.4.68 · PHP 8.5.11
MariaDB          running   6612    3306    12.3.3
api              stopped   -       3000    Node 24 · npm run dev
```

Editing Node apps from the CLI means editing `node-apps.json` directly, then running `whut sites` and `whut restart apache`.

## Inside the code

```text
src/
├── Core/                 Shared by WebHut.exe and whut.exe
│   ├── Util.cs           Paths (K), logging, JSON helpers, process/port utilities
│   ├── Config.cs         webhut.json and node-apps.json models
│   ├── Resolver.cs       Finds the latest download for each component (+ versions.json pins)
│   ├── Installer.cs      Download + SHA-256, unzip, config templates, MariaDB init, VC++ runtime
│   ├── Setup.cs          First-run orchestration
│   ├── Services.cs       Apache, MariaDB and Node app start/stop/status
│   ├── Backup.cs         Database backups, restore, rebuild-from-backup
│   ├── XamppImport.cs    Copy databases and htdocs projects from XAMPP
│   └── Vhosts.cs         Generates config/apache/generated/*.conf
├── Gui/                  Windows Forms UI (WebHut.exe)
│   ├── Program.cs        Entry point, single-instance guard
│   ├── MainForm.cs       The panel and tray icon
│   ├── SetupForm.cs      First-run wizard
│   ├── SettingsForm.cs   Settings + component updates
│   ├── NodeAppsForm.cs   Node app list and editor
│   ├── DatabaseForm.cs   Backups window and XAMPP import window
│   └── Ui.cs             Colors, fonts, buttons, logo drawing
└── Cli/
    └── CliProgram.cs     whut.exe
overlay/                  Config templates, embedded into both executables at build time
tests/                    Logic tests + a real-MariaDB integration test (Mono on Linux)
```

Design notes for contributors:

- **No NuGet dependencies.** JSON uses `System.Web.Script.Serialization`, zips use `System.IO.Compression`, and HTTP uses `HttpWebRequest`, all part of .NET Framework 4.8. The code compiles as C# 5, so the `csc.exe` that ships with Windows can build it.
- **Every long operation runs off the UI thread** (`Task.Run`), with results marshalled back via `TaskScheduler.FromCurrentSynchronizationContext()`.
- **Status is derived, not remembered.** A service is "running" if the PID in `tmp\pids\` is alive with the right process name, or if a matching executable inside the WebHut folder is running. WebHut can crash or be closed without losing track of anything.
- **The resolvers are pure functions** (`ParseApache`, `ParsePhpReleases`, `ParseNodeIndex`, `ParseMariaFiles`, ...) so they can be unit-tested against saved pages. If a publisher changes its page format, fix the parser and add the new sample to `tests/Tests.cs`.
