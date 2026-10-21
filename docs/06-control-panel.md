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
┌──────────────┬───────────────────────────────────────────────────────────────┐
│ 🔥 WebHut    │ WebHut                       ● All 2 services running  [■ Stop all]│
│    v1.4.0    │ C:\webhut · PHP 8.5.11 · Node 24.22.0                          │
│              │ ┌ Services ─────────────────────────────────────────────────┐ │
│ ▌Dashboard   │ │ SERVICE  DETAILS               PORT  PID   STATUS          │ │
│  Sites    ›  │ │ Apache   2.4.69 · PHP 8.5.11   80    5120  ● Running  [■ Stop] [Open] 📄 ⚙ │
│  Databases › │ │ MariaDB  12.3.3                3306  6612  ● Running  [■ Stop] [Open] 📄 ⚙ │
│  Node apps   │ └───────────────────────────────────────────────────────────┘ │
│  Logs     ›  │ ┌ Quick Actions ────────────────────────────────────────────┐ │
│  Settings    │ │ Open localhost · phpMyAdmin · www folder · Shell · Refresh · Node apps │
│              │ └───────────────────────────────────────────────────────────┘ │
│ Dark mode ◯  │ ┌ Activity ─────── [All|Apache|MariaDB|Errors] Copy Clear ⇕ ◉ ┐ │
│ About · Quit │ │ [10:41:02]  Apache is running: http://localhost/           │ │
└──────────────┴───────────────────────────────────────────────────────────────┘
```

**Sidebar** (places):

| Item | What it does |
|---|---|
| **Sites** (page) | Every site with its address, folder and type (PHP, PHP with `public\`, Node app), each with **Open**. **www folder** and **Refresh sites** at the top. Right-click the sidebar item for quick actions. |
| **Databases** (page) | MariaDB status, your databases with table counts and sizes (each opens in phpMyAdmin), recent backups with **Restore…**, **Back up now**, and ⋯ for restore/rebuild, XAMPP import, `my.ini` and the backups folder ([11](11-databases.md)). Right-click the sidebar item for quick actions. |
| **Node apps** | A process manager for Node apps ([04](04-node-integration.md)): each app shows its address, port, command, status (**Running**, **No response** when the process runs but nothing answers on its port, **Failed**, **Stopped**), auto-start and PID, with **Start/Stop**, **Open** and a ⋯ menu (Restart, View logs, Open folder, Open terminal here, Edit, Remove). Ports are checked before starting, and an app that exits on its own is reported. |
| **Logs** | Apache error log, MariaDB log, WebHut log, or the `logs\` folder |
| **Settings** | See below |
| **Dark mode** switch | Light or dark theme (see Appearance below) |
| **About** / **Quit WebHut** | Version and project link / close WebHut completely. Quit asks whether to stop running servers or leave them running. |

**Dashboard:**

| Control | Action |
|---|---|
| Health chip | All running (green), some running (amber), all stopped (grey), or "A service failed to start" (red). Shows "Starting services…" while **Start all** works. |
| **Start all / Stop all** | One button that follows the servers' state. **Start all** starts Apache and MariaDB, plus Node apps marked *Start with "Start all"*. **Stop all** stops every service. |
| Status pill | **Running**, **Stopped**, **Starting… / Stopping…** (with a spinner; that row's buttons are disabled meanwhile), or **Failed** (start failed; see Activity). Refreshed every 2 seconds. |
| **Start / Stop** | Start or stop that service. Failures show Apache's, MariaDB's or the app's own last log lines. |
| **Open** | Apache: `http://localhost`. MariaDB: phpMyAdmin. Node app: `http://<name>.localhost`. Disabled until the service (and Apache) is running; hover for details. |
| 📄 / ⚙ | The service's log (`apache-error.log`, `mariadb.err`, `node-<name>.log`) / its config (`httpd.conf`, `my.ini`, `node-apps.json`) |
| Quick Actions | Open localhost, phpMyAdmin, www folder, Shell (a console with `php`, `node`, `npm`, `npx`, `composer`, `mariadb` and `httpd` on `PATH`), Refresh sites (regenerates vhosts and restarts Apache if running), Node apps |
| **Activity** | Everything WebHut does, newest last. Filter by **All / Apache / MariaDB / Errors**; **Copy** copies the lines shown; **Clear** empties this view only (log files on disk are kept); ⇕ makes the console taller or smaller; **Auto-scroll** keeps the newest line in view. |
| Tray icon | Closing the window (X) keeps WebHut in the tray (configurable). Right-click for Start all / Stop all, Open localhost and Quit WebHut. |

If MariaDB fails to start and backups exist, WebHut offers to rebuild it from the newest one (the damaged folder is kept). When Windows shuts down or you log off, WebHut stops MariaDB cleanly first, so a shutdown never needs crash recovery.

Only one panel can run per WebHut folder. Separate WebHut folders can run side by side on different ports.

### Settings

Settings has five pages: **General** (start servers on open, keep running in the tray, appearance), **Runtime** (PHP and Node.js versions), **Services** (HTTP and MySQL ports, root password), **Security** (network access; phpMyAdmin and MariaDB always stay local) and **Maintenance** (component updates, Visual C++ runtime). The footer shows unsaved changes and which servers saving will restart.

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
