<p align="center"><img src="assets/webhut-512.png" width="96" alt="WebHut logo"></p>

<h1 align="center">WebHut</h1>

<p align="center"><b>A portable, XAMPP-style local development stack for Windows:<br>Apache, the latest PHP, MariaDB, phpMyAdmin and Node.js in one folder, with one control panel.<br>Optional: MySQL, PostgreSQL and MongoDB.</b></p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download for Windows</b></a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#webhut-xampp-and-laragon">Compare</a> ·
  <a href="docs/">Docs</a> ·
  Free and open source (MIT)
</p>

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/dashboard-dark.png">
    <img src="docs/images/dashboard-light.png" width="860" alt="The WebHut control panel with Apache and MariaDB running">
  </picture>
</p>

---

## Why

XAMPP made local PHP development easy: unzip, click Start, go. But two gaps have grown over the years:

1. **PHP versions lag.** New PHP releases take a while to reach XAMPP builds.
2. **No Node.js.** Modern projects mix PHP back ends with Node tooling (Vite, Next.js, Express APIs, websocket servers), so you end up installing and juggling Node separately.

WebHut keeps the XAMPP experience (a portable folder, a panel with Start/Stop buttons, nothing installed system-wide) and fixes both:

- **The latest stable PHP**, downloaded straight from windows.php.net at setup, with several versions side by side and a one-click switch.
- **Node.js built in.** Register a Node app once; WebHut starts it with `PORT` set and Apache serves it at `http://<name>.localhost`, websockets included.
- **Automatic sites.** Every folder in `www\` becomes `http://<folder>.localhost` with no hosts-file editing and no admin rights.
- **Your databases survive crashes.** Crash-safe MariaDB settings, a clean database stop when Windows shuts down, automatic backups, and one-click "rebuild from backup". No more "MySQL shutdown unexpectedly" after the battery dies.
- **Test on your phone.** One switch lets devices on your Wi-Fi open your sites (`http://192.168.x.x/...`), like XAMPP. phpMyAdmin and the database stay private.
- **Moving from XAMPP is one click.** WebHut copies your XAMPP databases and `htdocs` projects, even when XAMPP's MySQL has crashed, without changing XAMPP.
- **Always current.** WebHut ships no third-party binaries. It downloads the official builds when you run it and verifies checksums where the publisher provides them, and its update checker keeps you current.

## WebHut, XAMPP and Laragon

All three give you Apache, PHP and a database on Windows. Where they differ:

| | **WebHut** | XAMPP | Laragon |
|---|---|---|---|
| Price | Free, MIT licensed | Free | Free for personal use; paid license for commercial use |
| PHP version at setup | Newest stable, downloaded when you install | The one bundled with that XAMPP release | Bundled; add others yourself |
| Several PHP versions side by side | Yes, one-click switch | One per installation | Yes |
| Node.js apps | Managed processes, each at its own `name.localhost`, websockets work | Not included | Node.js included |
| Databases | MariaDB, plus optional MySQL 8.4, PostgreSQL and MongoDB, each on its own port | MariaDB | MySQL; others as add-ons |
| Automatic site addresses | `folder.localhost`, no hosts file, no admin rights | Edit vhosts yourself | `folder.test` |
| Database crash safety | Crash-safe settings, backups every 2 h and on stop, rebuild from backup | Manual | Manual |
| Moving from XAMPP | One-click import of databases and `htdocs` | — | Manual |
| Component updates | Built-in, with checksum checks, health check and rollback | Install a newer bundle | Automatic with a paid license |

Comparison as of October 2026. If something here is out of date, please [open an issue](../../issues) and it will be fixed.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/sites-dark.png">
    <img src="docs/images/sites-light.png" width="860" alt="WebHut's Sites page: every folder in www gets its own .localhost address">
  </picture>
</p>

## What you get

| Component | Version WebHut installs | Downloaded from |
|---|---|---|
| Apache HTTP Server | newest 2.4.x Win64 build | [Apache Lounge](https://www.apachelounge.com/download/) |
| PHP | newest stable branch, x64 **Thread Safe** (8.5 as of October 2026) | [windows.php.net](https://windows.php.net/download/) |
| MariaDB | newest stable LTS series (12.3 as of October 2026) | [mariadb.org REST API](https://mariadb.org/download/) |
| phpMyAdmin | newest release | [phpmyadmin.net](https://www.phpmyadmin.net/downloads/) |
| Node.js | newest LTS line | [nodejs.org](https://nodejs.org/en/download) |
| Composer | latest stable | [getcomposer.org](https://getcomposer.org/download/) |
| CA certificates | Mozilla bundle, so PHP's HTTPS calls work | [curl.se](https://curl.se/docs/caextract.html) |
| *Optional:* MySQL | newest 8.4 LTS, port 3307 | [dev.mysql.com](https://dev.mysql.com/downloads/mysql/) |
| *Optional:* PostgreSQL | newest supported release, port 5432 | [EnterpriseDB Windows binaries](https://www.enterprisedb.com/download-postgresql-binaries) |
| *Optional:* MongoDB + mongosh | newest production release, port 27017 | [mongodb.com](https://www.mongodb.com/try/download/community) |
| *Optional:* Adminer | web UI for MySQL and PostgreSQL at `/adminer` | [adminer.org](https://www.adminer.org/) |

Requirements: Windows 10 or 11 (x64), and the **Microsoft Visual C++ Redistributable 2017–2026 (x64)**, which the setup window checks for and can install. WebHut itself runs on the .NET Framework 4.8 that is built into Windows 10 (version 1903 and later) and Windows 11.

## Quick start

1. Download **`WebHut-Setup-<version>.exe`** from [Releases](../../releases/latest) and run it. It installs to `C:\WebHut` by default (no admin rights needed) and adds Start-menu and desktop shortcuts.
   Prefer a portable copy? Download `WebHut-<version>-win64.zip` instead and extract it to a short path **without spaces**, such as `C:\webhut`.
2. WebHut opens with its setup window, which finds the latest Apache, PHP, MariaDB, phpMyAdmin and Node.js. Click **Install**.
3. Click **Start all**, then **Open localhost**.

| | |
|---|---|
| Your PHP sites | Create `C:\webhut\www\blog`, click **Refresh sites**, then open `http://blog.localhost` |
| Your Node apps | **Node apps → Add**, choose the folder and command, then click **Start** and open `http://<name>.localhost` |
| Database | `127.0.0.1:3306`, user `root`, no password by default (set one in **Settings**) |
| phpMyAdmin | `http://localhost/phpmyadmin` |
| Coming from XAMPP | Stop MySQL in XAMPP, then **Database tools → Import from XAMPP…** ([docs/11](docs/11-databases.md#moving-from-xampp)) |
| Backups | Automatic every 2 hours and before each stop; restore or rebuild in **Database tools** |
| Terminal | **Shell** opens a console with `php`, `node`, `npm`, `npx`, `composer` and `mariadb` on `PATH` |
| Command line | `whut status`, `whut start`, `whut php 8.4` and more ([docs/06](docs/06-control-panel.md#whut)) |

> Windows may show a SmartScreen warning the first time ("Windows protected your PC") because WebHut is new and unsigned. Click **More info → Run anyway**. See [docs/07](docs/07-packaging-and-releases.md#code-signing-and-smartscreen) for how maintainers can sign releases.

## Folder layout after setup

```text
C:\webhut\
├── WebHut.exe, whut.exe   Control panel and command-line tool
├── webhut.json               Your settings (PHP/Node version, ports, ...)
├── node-apps.json          Node apps WebHut manages
├── apache\                 Official Apache build, untouched
├── php\8.5\  php\8.4\      PHP versions side by side (each with its own php.ini)
├── mariadb\                Official MariaDB build, untouched
├── node\24\                Portable Node.js (+ global\ for npm -g installs)
├── apps\phpmyadmin\
├── config\
│   ├── apache\             httpd.conf, generated\ vhosts, custom\ (yours)
│   └── mariadb\my.ini
├── data\mariadb\           Your databases
├── data\backups\           Automatic database backups (.sql.gz) - copy these off the laptop too
├── www\                    Your sites: www\blog -> http://blog.localhost
├── logs\                   apache-error.log, mariadb.err, node-<app>.log, webhut.log
├── ssl\cacert.pem          CA bundle used by PHP's curl/openssl
├── tools\                  composer.phar + composer.bat
└── tmp\                    sessions, uploads, downloads, pids, backups
```

Settings and data live **outside** the component folders, so upgrading Apache, MariaDB or PHP never touches your config or databases.

## Documentation

| | |
|---|---|
| [01 – Architecture](docs/01-architecture.md) | How the pieces fit, process model, ports, portability |
| [02 – Build the stack by hand](docs/02-build-from-scratch.md) | Exactly what WebHut automates, step by step, for learning or custom setups |
| [03 – Configuration reference](docs/03-configuration.md) | `httpd.conf`, vhosts, `php.ini`, `my.ini`, phpMyAdmin, `webhut.json`, `versions.json` |
| [04 – Node.js integration](docs/04-node-integration.md) | Node apps, reverse proxy, websockets, Vite/Next.js/Express recipes |
| [05 – PHP versions](docs/05-php-versions.md) | Switching versions, adding versions, per-site versions with `mod_fcgid` |
| [06 – Control panel & whut](docs/06-control-panel.md) | Every button and command, and how the panel works inside |
| [07 – Building & releasing](docs/07-packaging-and-releases.md) | Compile, package, publish on GitHub, pin versions, sign |
| [08 – Security](docs/08-security.md) | Keeping a dev stack safe |
| [09 – Troubleshooting](docs/09-troubleshooting.md) | Port conflicts, missing DLLs, extensions, MariaDB, Node |
| [10 – Licensing](docs/10-licensing.md) | Component licenses and what redistribution requires |
| [11 – Databases](docs/11-databases.md) | Crash safety, backups, rebuild from backup, moving from XAMPP |
| [12 – Server configuration](docs/12-server-configuration.md) | Apache & MariaDB settings manager: ownership, validation, rollback |
| [13 – MySQL, PostgreSQL and MongoDB](docs/13-more-databases.md) | Adding the optional engines, ports, connecting, Adminer, passwords |

## Building from source

```powershell
# On Windows - uses the C# compiler built into .NET Framework 4.8, no SDK needed
powershell -ExecutionPolicy Bypass -File build\build.ps1 -Package -Version 1.0.0
```

```bash
# On Linux/macOS with Mono (also runs the test suite)
./build/build.sh && ./tests/run.sh   # + tests/integration.sh against a real MariaDB
```

Pushing a tag such as `v1.0.0` builds and publishes a release through GitHub Actions. See [docs/07](docs/07-packaging-and-releases.md).

## Status and scope

WebHut is for **local development only**. Everything listens on `127.0.0.1`, and nothing is hardened for production or network exposure. See [Security](docs/08-security.md).

## Contributing

Issues and pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md).

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Release builds are made from this repository by [GitHub Actions](.github/workflows/release.yml); nothing is built or signed on a personal machine.
- Every signing request is approved by hand before a release is signed.
- Team roles:
  - Committers and reviewers: [@hashencoder](https://github.com/hashencoder)
  - Approvers: [@hashencoder](https://github.com/hashencoder)

### Privacy

WebHut collects no personal data and has no telemetry or analytics. It connects to the internet only to:
- download Apache, PHP, MariaDB, phpMyAdmin, Node.js, Composer and the CA certificate bundle from their publishers' official sites (listed under [What you get](#what-you-get)), when you install or update them;
- ask GitHub's public API whether a newer WebHut release exists, when you open the About window or check for updates.

Your sites and databases stay on your PC. The servers WebHut runs listen only on this PC unless you turn on network access in Settings.

## License

WebHut's code and documentation are [MIT licensed](LICENSE). The components WebHut downloads keep their own licenses; see [docs/10-licensing.md](docs/10-licensing.md).

WebHut is an independent project, not affiliated with or endorsed by Apache Friends (XAMPP), the Apache Software Foundation, Apache Lounge, The PHP Group, the MariaDB Foundation, the phpMyAdmin project, the OpenJS Foundation, or the Composer project.
