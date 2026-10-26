# 06 – Control panel & snook

ServNook ships two programs that share one core:

- **`ServNook.exe`**: the control panel and setup wizard.
- **`snook.exe`**: the same operations from a terminal or script.

Both are small, self-contained .NET Framework 4.8 executables (about 180 KB and 130 KB), and Windows 10 and 11 already include the runtime.

## ServNook.exe

### First run: the setup window

When `apache\` or a PHP version is missing, ServNook opens the setup window instead of the panel:

| Part | What it does |
|---|---|
| Component list | Shows the version ServNook found for each component and whether a checksum is available. Apache and PHP are required; everything else can be unticked. |
| **Use a zip I downloaded…** | For the selected component, use a file you downloaded yourself (for offline installs, or when a lookup fails). PHP's branch is read from the file name. |
| **Open download page** | Opens the selected component's official download page |
| **Look up again** | Re-runs the version lookup |
| Visual C++ line | Shows whether the VC++ 2017–2026 x64 runtime (14.50 or newer) is installed, and offers **Install it**. Windows asks for admin permission; this is the only step that needs it. |
| Ports and root password | HTTP port (80), MySQL port (3306), and an optional root password |
| **Install** | Downloads, verifies, unpacks, writes config and initialises MariaDB, with live progress. **Stop** cancels; finished downloads are cached in `tmp\downloads\` and reused on the next try. |

### The panel

```text
┌──────────────┬───────────────────────────────────────────────────────────────┐
│ 🔥 ServNook    │ ServNook                       ● All 2 services running  [■ Stop all]│
│    v1.4.0    │ C:\servnook · PHP 8.5.11 · Node 24.22.0                          │
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
| **Databases** (page) | MariaDB status, your databases with table counts and sizes (each opens in phpMyAdmin), recent backups with **Restore…**, **Back up now**, and ⋯ for restore/rebuild, XAMPP import, MariaDB configuration and the backups folder ([11](11-databases.md)). Right-click the sidebar item for quick actions. |
| **Node apps** | A process manager for Node apps ([04](04-node-integration.md)): each app shows its address, port, command, status (**Running**, **No response** when the process runs but nothing answers on its port, **Failed**, **Stopped**), auto-start and PID, with **Start/Stop**, **Open** and a ⋯ menu (Restart, View logs, Open folder, Open terminal here, Edit, Remove). Ports are checked before starting, and an app that exits on its own is reported. |
| **Logs** | Apache error log, MariaDB log, ServNook log, or the `logs\` folder |
| **Settings** | See below |
| **Dark mode** switch | Light or dark theme (see Appearance below) |
| **About** / **Quit ServNook** | Version and project link / close ServNook completely. Quit asks whether to stop running servers or leave them running. |

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
| **Activity** | Everything ServNook does, newest last. Filter by **All / Apache / MariaDB / Errors**; **Copy** copies the lines shown; **Clear** empties this view only (log files on disk are kept); ⇕ makes the console taller or smaller; **Auto-scroll** keeps the newest line in view. |
| Tray icon | Closing the window (X) keeps ServNook in the tray (configurable). Right-click for Start all / Stop all, Open localhost and Quit ServNook. |

If MariaDB fails to start and backups exist, ServNook offers to rebuild it from the newest one (the damaged folder is kept). When Windows shuts down or you log off, ServNook stops MariaDB cleanly first, so a shutdown never needs crash recovery.

Only one panel can run per ServNook folder. Separate ServNook folders can run side by side on different ports.

### Server configuration

The gear button on the Apache and MariaDB rows opens the **server configuration manager** on that service's section (also **Databases › ⋯ › MariaDB configuration**). It covers ports, PHP integration, performance, modules, security, logging, virtual hosts and MariaDB's InnoDB, durability, security and diagnostics settings. Each setting shows its owner and effective value. Saving validates the change with the real `httpd`/`mariadbd`, backs up the files, restarts only the affected service, verifies it and rolls back on failure. Details: [12-server-configuration.md](12-server-configuration.md).

### Settings

Settings has five pages: **General** (start servers on open, keep running in the tray, appearance), **Runtime** (PHP and Node.js versions), **Services** (HTTP and MySQL ports, root password), **Security** (network access; phpMyAdmin and MariaDB always stay local) and **Maintenance** (component updates, Visual C++ runtime). The footer shows unsaved changes and which servers saving will restart.

| Setting | Notes |
|---|---|
| PHP version | Choose an installed branch. **Install another version…** downloads one. Saving restarts Apache. |
| Node.js version | Choose an installed major. **Install latest LTS** adds the newest LTS line. |
| Appearance | **Match Windows** (default) follows Windows' light/dark app mode and switches live. You can also pick **Light** or **Dark**. The sidebar's **Dark mode / Light mode** item switches between the two in one click. |
| HTTP port, MySQL port | Saving restarts the affected server and updates phpMyAdmin's config |
| Database root → **Change…** | Runs `ALTER USER` for `root@localhost`, `root@127.0.0.1` and `root@::1` (starting MariaDB if needed), stores the new password, and updates phpMyAdmin's `AllowNoPassword` |
| Network: allow access from other devices | Phones and other PCs on your Wi-Fi can open the sites; phpMyAdmin and MariaDB stay local ([08](08-security.md#opening-servnook-to-your-lan-for-testing-on-a-phone)). Saving restarts Apache. |
| Start servers when ServNook opens | |
| Closing the window keeps ServNook in the tray | |
| **Check for updates…** | Compares installed Apache, the active PHP branch, MariaDB, phpMyAdmin and the active Node major with the latest versions, and updates the ones you tick (see below) |
| **Install / repair Visual C++ runtime** | Re-runs Microsoft's installer |

#### Component updates

**Settings › Maintenance › Check for updates** opens the update manager. For each component it shows the installed and latest version, a status (*Up to date*, *Update available*, *Manual update*, *Check failed*), a link to the publisher's release notes, and when the list was last checked (**Check again** looks again). Only eligible updates can be ticked; **Select all updates** / **Clear selection** do what they say.

Before anything runs, a confirmation lists what each update stops, keeps and checks. Each component is then updated on its own, with its stage shown in its row:

1. **Download and verify** while everything keeps running. The SHA-256 published by PHP, MariaDB, phpMyAdmin and Node.js is checked; Apache Lounge publishes no machine-readable checksum, so its package is accepted only over HTTPS from apachelounge.com. Every package is then opened and must contain the files a Windows x64 build has (for example `bin/httpd.exe`, `php8apache2_4.dll`, `bin/mariadbd.exe`). A package hosted anywhere else without a checksum is refused.
2. **Back up** (MariaDB only): every database is dumped to `data\backups` first. If the backup fails, MariaDB is not touched.
3. **Stop only what uses the component**: Apache for Apache and the active PHP; MariaDB for MariaDB; running Node apps for the active Node.js. phpMyAdmin updates with Apache running. Unrelated services keep running.
4. **Swap**: the old folder moves to `tmp\backup\<component>-<timestamp>\`. Kept from the old version: `php.ini` and extra `ext\*.dll` (PHP), extra `modules\*.so` such as `mod_fcgid.so` (Apache), `config.inc.php` (phpMyAdmin), globally installed npm packages (Node.js). Config files in `config\`, virtual hosts, `www\` and `data\` live outside these folders and are never touched.
5. **Health check**: the new binary must run and report the new version; Apache and PHP must pass `httpd -t`; after restarting, Apache must answer HTTP requests, MariaDB must answer queries (after `mariadb-upgrade`), phpMyAdmin must load.
6. **Roll back** if any check fails: the new folder moves aside, the previous one is put back, and the stopped services start again. The row shows *Previous version kept* with the technical details.

Updates that can't be done safely here are shown as **Manual update** with the reason: a new MariaDB release series (12.3 → 12.4 changes the data format), a different PHP version line or Node.js major (install those side by side in Settings › Runtime), a new phpMyAdmin major, or an Apache/PHP build that needs a newer Visual C++ runtime.

To move MariaDB to a new series by hand: back up with `mariadb-dump --all-databases`, replace `mariadb\` with the new version, start it, then run `mariadb-upgrade -u root`.

## snook

```text
snook status                      Show every service
snook start   [all|apache|mariadb|<node-app>]
snook stop    [all|apache|mariadb|<node-app>]
snook restart [all|apache|mariadb|<node-app>]
snook sites                       Regenerate vhosts for folders in www\ and Node apps
snook php [<branch>]              Show or switch the active PHP version (e.g. 8.4)
snook setup                       Download and install everything (first run)
snook backup                      Back up all databases now (data\backups)
snook backups                     List backups
snook restore [<file>|latest]     Load a backup into the running MariaDB
snook rebuild-db [<file>|latest]  Set a damaged data folder aside and rebuild it from a backup
snook import-xampp [<folder>] [--recover] [--projects]
                                    Copy databases (and htdocs projects) from XAMPP
snook shell                       Open a console with php, node, npm and composer on PATH
snook version
```

- The default target is `all`. As with the panel, `start all` includes only Node apps marked autostart; `stop all` stops everything.
- Node app targets use the app name: `snook restart api`.
- Exit code `0` means success, `1` a failure (details on stderr), and `2` an unusable folder path.
- `snook setup` is the non-interactive version of the setup window. It needs every component to resolve, so offline installs go through the GUI's **Use a zip I downloaded…**.
- `snook` and `ServNook.exe` can be used at the same time; the panel picks up changes within 2 seconds.

```text
> snook status
SERVICE          STATE     PID     PORT    DETAIL
Apache           running   5120    80      2.4.68 · PHP 8.5.11
MariaDB          running   6612    3306    12.3.3
api              stopped   -       3000    Node 24 · npm run dev
```

Editing Node apps from the CLI means editing `node-apps.json` directly, then running `snook sites` and `snook restart apache`.

## Inside the code

```text
src/
├── Core/                 Shared by ServNook.exe and snook.exe
│   ├── Util.cs           Paths (K), logging, JSON helpers, process/port utilities
│   ├── Config.cs         servnook.json and node-apps.json models
│   ├── Resolver.cs       Finds the latest download for each component (+ versions.json pins)
│   ├── Installer.cs      Download + SHA-256, unzip, config templates, MariaDB init, VC++ runtime
│   ├── Setup.cs          First-run orchestration
│   ├── Services.cs       Apache, MariaDB and Node app start/stop/status
│   ├── Backup.cs         Database backups, restore, rebuild-from-backup
│   ├── XamppImport.cs    Copy databases and htdocs projects from XAMPP
│   ├── Vhosts.cs         Generates config/apache/generated/*.conf
│   ├── Updates.cs        Component update rules: eligibility, verification, health checks, rollback
│   ├── OptionFile.cs     my.ini editor that keeps comments, order, encoding and line endings
│   ├── ApacheConf.cs     Controlled httpd.conf edits, Apache probe (httpd -V/-M), vhost checks
│   ├── MariaConfig.cs    MariaDB settings catalog, live/next-start values, mariadbd validation
│   └── ConfigManager.cs  Plan → validate → back up → apply → restart → verify → roll back
├── Gui/                  Windows Forms UI (ServNook.exe)
│   ├── Program.cs        Entry point, single-instance guard
│   ├── MainForm.cs       The panel and tray icon
│   ├── SetupForm.cs      First-run wizard
│   ├── SettingsForm.cs   Settings
│   ├── UpdatesForm.cs    Component update manager and its confirmation dialog
│   ├── ConfigForm.cs     Apache & MariaDB server configuration manager
│   ├── NodeAppsForm.cs   Node app list and editor
│   ├── DatabaseForm.cs   Backups window and XAMPP import window
│   └── Ui.cs             Colors, fonts, buttons, logo drawing
└── Cli/
    └── CliProgram.cs     snook.exe
overlay/                  Config templates, embedded into both executables at build time
tests/                    Logic tests + real MariaDB / Apache integration tests (Mono on Linux)
```

Design notes for contributors:

- **No NuGet dependencies.** JSON uses `System.Web.Script.Serialization`, zips use `System.IO.Compression`, and HTTP uses `HttpWebRequest`, all part of .NET Framework 4.8. The code compiles as C# 5, so the `csc.exe` that ships with Windows can build it.
- **Every long operation runs off the UI thread** (`Task.Run`), with results marshalled back via `TaskScheduler.FromCurrentSynchronizationContext()`.
- **Status is derived, not remembered.** A service is "running" if the PID in `tmp\pids\` is alive with the right process name, or if a matching executable inside the ServNook folder is running. ServNook can crash or be closed without losing track of anything.
- **The resolvers are pure functions** (`ParseApache`, `ParsePhpReleases`, `ParseNodeIndex`, `ParseMariaFiles`, ...) so they can be unit-tested against saved pages. If a publisher changes its page format, fix the parser and add the new sample to `tests/Tests.cs`.
