# Changelog

All notable changes to Kiln are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-10-21

### Added
- `Kiln.exe` control panel: start, stop and monitor Apache, MariaDB and Node apps; tray icon; activity log.
- First-run setup wizard that downloads official Windows builds of Apache (Apache Lounge), PHP (x64 Thread Safe), MariaDB (latest LTS), phpMyAdmin, Node.js (latest LTS), Composer and the Mozilla CA bundle, verifying SHA-256 checksums where the publisher provides them.
- "Use a zip I downloaded" fallback for every component.
- Automatic virtual hosts: every folder in `www\` becomes `http://<folder>.localhost` (serves `public\` when present).
- Node apps: run any command with `PORT` set, reverse-proxied by Apache at `http://<name>.localhost`, websockets included.
- Several PHP versions side by side with one-click switching; per-site PHP versions through `mod_fcgid` (manual, documented).
- Component update checker that keeps `php.ini` and the phpMyAdmin config and backs up previous versions.
- `kilnctl.exe` command-line tool: `status`, `start`, `stop`, `restart`, `sites`, `php`, `setup`, `shell`.
- Kiln shell with `php`, `node`, `npm`, `composer` and `mariadb` on `PATH`, npm cache and globals kept inside Kiln.
- Portable: config uses no absolute paths except `php.ini`, which is rewritten automatically when the folder moves.
