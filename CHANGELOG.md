# Changelog

All notable changes to WebHut are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [1.5.0] - 2026-10-21

### Added
- **Sites page:** every site with its address, folder and type, each with Open.
- **Databases page:**
  - MariaDB status, and your databases with table counts and sizes.
  - Recent backups with one-click Restore, plus Back up now.
  - Quick access to restore/rebuild, XAMPP import and my.ini.
- **Right-click quick menus** on the Sites and Databases sidebar items. Left-click opens the page.
- **New popup menus:** anchored beside their item, rounded with a shadow, icons and short descriptions. They close on Escape or an outside click, support the keyboard, and stay on screen.
- **Toast notifications** in the bottom-right of the window for service started/stopped, Start all / Stop all, settings saved, backups, restores, refreshed sites and copied logs.

### Changed
- **Settings, redesigned:**
  - A sidebar with General, Runtime, Services, Security and Maintenance.
  - Rounded section cards with aligned rows and short helper text.
  - Switches instead of checkboxes, compact Install buttons, and wider port fields.
  - Warnings for a passwordless root user and for network access. phpMyAdmin and MariaDB always stay local.
  - A sticky footer showing unsaved changes and which servers saving will restart.
- **Node apps, redesigned as a process manager:**
  - A helpful empty state, and each app as a card with its address, port, command and PID.
  - A status for each app: Running, **No response** (the process runs but nothing answers on its port), Failed or Stopped.
  - Auto-start shown as text so it can't be confused with Start/Stop.
  - Start/Stop and Open on every row, plus a ⋯ menu: Restart, View logs, Open folder, Open terminal here, Edit, Remove.
  - The port is checked before starting, and an app that exits unexpectedly is reported.
  - Search appears once you have more than five apps.
- Dropdowns use the new popup.

## [1.4.0] - 2026-10-21

Dashboard refinements from a design review.

### Changed
- **No more duplicated navigation:**
  - The sidebar now lists places: Dashboard, **Sites** (a menu of your sites, the www folder and Refresh), **Databases** (phpMyAdmin, backups and XAMPP import, my.ini), **Node apps**, **Logs** (each log file) and **Settings**.
  - Everyday actions stay in Quick Actions.
  - The sidebar is narrower, and its footer separates the **Dark mode** switch from the smaller **About** and **Quit WebHut**.
- **Header health chip** next to Start all: all running, some running, all stopped, starting/stopping, or failed.
- **Service table:**
  - A wider Details column, so versions no longer wrap.
  - Port and PID aligned in JetBrains Mono.
  - Compact icon buttons for the log and the config.
  - **Open** is disabled until the service is running, with a tooltip saying what it opens.
- **Clear service states:** a spinner pill and disabled buttons while starting or stopping, and a red **Failed** pill when a start fails.
- **Activity console:**
  - Compact by default, with ⇕ to expand it.
  - Filters: **All / Apache / MariaDB / Errors**.
  - A **Copy** button, and **Clear** clears only the view (log files are kept).
- **Spacing:** Quick Action tiles are smaller and horizontal, and card spacing and heights are consistent.

## [1.3.1] - 2026-10-21

### Fixed
- **Text too big at 125% / 150% display scaling.** WebHut loaded its fonts before declaring itself DPI-aware, so GDI+ cached a 96-dpi screen. The layout then stayed at 100% while Windows drew text at the real scale, which wrapped details and cut off tile captions. WebHut now declares DPI awareness first and reads the scale straight from Windows.
- The window now fits the screen's work area on high-scaling displays.
- Log timestamps always use `HH:mm:ss`. Windows time formats with dots (e.g. `19.10.01`) used to lose the grey `[time]` styling in the activity log.

## [1.3.0] - 2026-10-21

### Added
- **Dark theme.** By default WebHut follows Windows' light/dark app mode and switches live when Windows does. **Settings → Appearance** can force Light or Dark, and the sidebar has a one-click **Dark mode / Light mode** switch. Title bars, dialogs, lists and inputs follow the theme too.

### Changed
- **Brand palette:**
  - Brand orange #EA4B0C, with #C93F08 on hover; the dark-theme accent is #FF762E.
  - Light theme: Cloud White #F6F8FB background, white surfaces.
  - Dark theme: Midnight Slate #151C26 / #10151D.
  - Status colours: running #16A36A, stopped #8A96A6, warning #D99516, error #DC4545, info #3284D6.
- **Typography:**
  - Inter for all interface text: title 28px/700, headings 20px/600, sidebar 14px/500, service names 15px/600, buttons 14px/600, body 14px, secondary text 12px, table headers 11px/600.
  - JetBrains Mono 12px for ports, PIDs and the activity log.
  - Both fonts are embedded in WebHut.exe (SIL OFL 1.1), so nothing needs installing.
- Dialogs are slightly wider to fit the new type.

## [1.2.0] - 2026-10-21

### Changed
- New control panel design:
  - **Sidebar**: Dashboard, Open localhost, phpMyAdmin, www folder, Shell, Refresh sites, Node apps, Database tools and Log files, with Settings, About and Quit WebHut at the bottom.
  - **Services table**: an icon for each service, a status pill (Running / Stopped / Starting… / Stopping…) and icon buttons for Start/Stop, Open, Logs and Config. A summary line shows how many services are running.
  - **Quick Actions**: tiles for the everyday tasks.
  - **Activity log**: a dark log with coloured lines, a **Clear** button and an **Auto-scroll** switch.
- All icons are drawn by WebHut itself, so the panel looks the same on Windows 10 and 11. Sizes follow the display scaling (DPI).

### Added
- **About** shows the version and folder, and links to the project page.

## [1.1.2] - 2026-10-21

### Added
- A **Quit** button next to Start all / Stop all fully closes WebHut, like XAMPP's Quit. The window's X still only hides WebHut to the tray. Before this, Exit was only in the tray icon's menu, which Windows 11 often hides behind the ^ arrow.

## [1.1.1] - 2026-10-21

### Changed
- **Start all** and **Stop all** are now one button. It reads "Start all" when nothing is running and "Stop all" once anything is, and shows "Starting…" or "Stopping…" while it works. The tray menu works the same way.

## [1.1.0] - 2026-10-21

### Changed
- The control panel is now `WebHut.exe`; it was `WebHutPanel.exe`.
- The command-line tool is now `whut.exe`: `whut start`, `whut php 8.4`, `whut backup`. It was `webhut.exe`.
- When you extract the new zip over an older folder, WebHut removes the leftover `WebHutPanel.exe` on first start.

## [1.0.2] - 2026-10-21

### Fixed
- Double-clicking `whut.exe` (the command-line tool) flashed a console window and closed. It now opens the control panel, `WebHut.exe`.

### Changed
- The control panel logs a line when it starts, which makes it easier to diagnose problems.

## [1.0.1] - 2026-10-21

### Fixed
- Setup couldn't find any components on Windows 10: WebHut forced TLS 1.3, which Windows 10 doesn't support, so every download lookup failed. WebHut now lets Windows choose the TLS version.

### Changed
- Setup writes each component lookup (version found, or the reason it failed) to `logs\webhut.log`.

## [1.0.0] - 2026-10-21

### Added
- `WebHut.exe` control panel: start, stop and monitor Apache, MariaDB and Node apps; tray icon; activity log.
- First-run setup wizard that downloads official Windows builds of Apache (Apache Lounge), PHP (x64 Thread Safe), MariaDB (latest LTS), phpMyAdmin, Node.js (latest LTS), Composer and the Mozilla CA bundle, verifying SHA-256 checksums where the publisher provides them.
- "Use a zip I downloaded" fallback for every component.
- Automatic virtual hosts: every folder in `www\` becomes `http://<folder>.localhost` (serves `public\` when present).
- Node apps: run any command with `PORT` set, reverse-proxied by Apache at `http://<name>.localhost`, websockets included.
- Several PHP versions side by side with one-click switching; per-site PHP versions through `mod_fcgid` (manual, documented).
- Component update checker that keeps `php.ini` and the phpMyAdmin config and backs up previous versions.
- `whut.exe` command-line tool: `status`, `start`, `stop`, `restart`, `sites`, `php`, `setup`, `shell`.
- WebHut shell with `php`, `node`, `npm`, `composer` and `mariadb` on `PATH`, npm cache and globals kept inside WebHut.
- Crash safety: durable InnoDB settings, a clean MariaDB stop on Windows shutdown and logoff, automatic backups (every 2 hours and before stopping, newest 10 kept), restore, and one-click "rebuild from backup" when MariaDB won't start.
- Import from XAMPP: copies databases (a logical copy, safe across MariaDB versions) and `htdocs` projects, with recovery modes for a crashed XAMPP MySQL. XAMPP itself is never modified.
- `whut backup`, `backups`, `restore`, `rebuild-db`, `import-xampp`.
- Network access option: devices on the local network can open the sites (private addresses only; phpMyAdmin and MariaDB stay local), with `<site>.<ip>.nip.io` names for phone testing.
- Integration test suites against real MariaDB and real Apache + PHP.
- Portable: config uses no absolute paths except `php.ini`, which is rewritten automatically when the folder moves.
