# Changelog

All notable changes to WebHut are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

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
