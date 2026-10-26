# 07 – Building & releasing

This page is for maintainers: how to build ServNook, test it, publish a release on GitHub, and keep users on good component versions.

## What a release contains

```text
ServNook-1.0.0-win64.zip
└── ServNook\
    ├── ServNook.exe          Control panel + setup wizard
    ├── ServNook.exe.config   Runtime settings (.NET 4.8, TLS defaults, long paths)
    ├── snook.exe            Command-line tool
    ├── snook.exe.config
    ├── LICENSE
    └── README.txt          Short user instructions (from dist-readme.txt)
```

The zip is under 200 KB because ServNook **downloads** Apache, PHP, MariaDB, phpMyAdmin, Node.js and Composer from their official sources on first run. This keeps every release current without you re-packaging binaries, and it avoids redistributing third-party binaries at all ([10 – Licensing](10-licensing.md)).

If you want an offline "full" bundle as well, see [Offline bundles](#offline-bundles).

## Building

### On Windows (no SDK needed)

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1                       # dist\ServNook.exe, dist\snook.exe
powershell -ExecutionPolicy Bypass -File build\build.ps1 -Package -Version 1.0.0  # + dist\ServNook-1.0.0-win64.zip and .sha256
```

`build.ps1` uses `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, which is part of every Windows 10 and 11 install. Every file under `overlay\` is embedded as a resource named `overlay/<path>`.

Visual Studio or JetBrains Rider work too: create a .NET Framework 4.8 WinForms project, add `src\Core` and `src\Gui`, add the `overlay\` files as embedded resources with the logical names above, and reference `System.Web.Extensions` and `System.IO.Compression.FileSystem`.

### On Linux or macOS (Mono)

```bash
sudo apt install mono-devel          # or: brew install mono
./build/build.sh                     # dist/ServNook.exe, dist/snook.exe
./tests/run.sh                       # logic tests + a snook smoke test
```

The output is a normal Windows executable; Mono's compiler targets the same .NET Framework 4.8 reference assemblies.

### Tests

Two suites:

- **`tests/run.sh`** runs `tests/Tests.cs`, the platform-independent logic. Details below.
- **`tests/apache-smoke.sh`** runs ServNook's real Apache config on a real Apache + PHP, started by `snook`: the dashboard, `*.localhost` vhosts, `public\` detection, `localhost/<folder>/`, `.htaccess` protection, the Node reverse proxy, and the network-access rules (private addresses allowed, public addresses refused, phpMyAdmin local-only). Needs `apt install apache2 libapache2-mod-php`.
- **`tests/integration.sh`** runs `snook` against **real MariaDB servers** (Linux binaries standing in for the Windows ones): start and stop, a hard kill right after committed writes (no rows may be lost), backup, restore, rebuild after deliberately corrupting the data files, and a full XAMPP import. Needs `apt install mariadb-server mariadb-client`.

`tests/Tests.cs` covers the platform-independent logic: config round-trips, config templates, `php.ini` generation, vhost generation rules, zip unpacking and upgrade backups, folder relocation, every download-page parser (against saved samples), and downloads with SHA-256 verification against a local HTTP server. Run it before every release. Then do the manual Windows check below.

### Manual release check (Windows)

On a clean Windows VM (or Windows Sandbox):

1. Extract the zip to `C:\servnook`, run `ServNook.exe`, install everything.
2. **Start all**. Check that `http://localhost` shows the dashboard with PHP, Apache and database details.
3. Create `www\test\index.php`, click **Refresh sites**, and open `http://test.localhost`.
4. Log in to phpMyAdmin as `root`.
5. Add a Node app (for example `npm create vite@latest` in the Shell, then `npx vite --port 5173`), start it, and open `http://<name>.localhost`. Edit a file and confirm hot reload works.
6. **Settings → Install another version…** → PHP 8.4, switch to it, and confirm `phpinfo` shows 8.4.
7. Close ServNook with servers running, reopen it, and confirm the servers show as running. **Stop all**.
8. Move the folder to `D:\servnook2`, start everything again, and confirm PHP still loads its extensions.

## Publishing on GitHub

`.github/workflows/release.yml` builds on `windows-latest` for every pull request and tag. For a tag `v*`, it also creates a GitHub Release with the zip and its `.sha256` attached:

```bash
# update CHANGELOG.md and K.AppVersion (src/Core/Util.cs) + AssemblyInfo.cs first
git tag v1.0.0
git push origin v1.0.0
```

Or publish a release on GitHub (**Releases → Draft a new release**, tag `v1.0.0`). The workflow then builds ServNook and attaches the zip to that release.

Release checklist:

- [ ] `K.AppVersion` in `src/Core/Util.cs` and the versions in `src/Core/AssemblyInfo.cs` match the tag
- [ ] `CHANGELOG.md` updated
- [ ] `./tests/run.sh`, `./tests/apache-smoke.sh` and `./tests/integration.sh` pass
- [ ] Manual Windows check done
- [ ] `versions.json` reviewed (below)

## Pinning versions with versions.json

By default, every ServNook install looks up the **latest** version of each component live. As a maintainer, you can override that for everyone by publishing a `versions.json` in your repo:

1. Set `DefaultManifestUrl` in `src/Core/Util.cs` to the raw URL, for example `https://raw.githubusercontent.com/<you>/servnook/main/versions.json`, and release that build.
2. Edit `versions.json` whenever you want to pin or un-pin something. Installs pick it up on their next setup or update check, with no new release needed.

```json
{
  "components": {
    "apache":     { "version": "2.4.68 (VS18)", "url": "https://.../httpd-2.4.68-260610-Win64-VS18.zip", "sha256": "..." },
    "php-8.5":    { "version": "8.5.11", "url": "https://windows.php.net/downloads/releases/php-8.5.11-Win32-vs17-x64.zip", "sha256": "..." },
    "mariadb":    { "version": "12.3.3", "url": "https://downloads.mariadb.org/rest-api/mariadb/12.3.3/mariadb-12.3.3-winx64.zip", "sha256": "4b57...85a0" },
    "phpmyadmin": { "version": "5.2.3",  "url": "https://files.phpmyadmin.net/phpMyAdmin/5.2.3/phpMyAdmin-5.2.3-all-languages.zip", "sha256": "..." },
    "node":       { "version": "24.11.1","url": "https://nodejs.org/dist/v24.11.1/node-v24.11.1-win-x64.zip", "sha256": "..." }
  }
}
```

| Key | Pins |
|---|---|
| `apache` | Apache |
| `php-<branch>` | A specific PHP branch, for example `php-8.5` |
| `mariadb` | MariaDB |
| `phpmyadmin` | phpMyAdmin |
| `node` | The default Node LTS (setup) |
| `node-<major>` | A specific Node major (updates) |

When to pin:

- **A bad upstream release.** Pin the previous good version until a fix ships.
- **Apache with a checksum.** Apache Lounge has no machine-readable checksum, so pinning Apache with a `sha256` you verified by hand gives users a verified download.
- **A publisher changed its download page** and the live lookup broke. Pin a URL while you fix the parser.

Users can point at their own manifest with `manifestUrl` in `servnook.json` (useful for teams that want everyone on identical versions).

## Code signing and SmartScreen

Unsigned new executables trigger Windows SmartScreen ("Windows protected your PC") until they build up reputation, and some antivirus engines flag small unsigned tools that download and launch programs. Options, from cheapest:

1. **Do nothing**, and document "More info → Run anyway" (already in the README and `README.txt`). Reputation builds slowly as people download.
2. **Submit each release to Microsoft** for analysis at <https://www.microsoft.com/wdsi/filesubmission> as a software developer. This helps with Defender false positives.
3. **Free signing for open source.** [SignPath Foundation](https://signpath.org/) signs releases for qualifying OSS projects through a GitHub Actions integration.
4. **A paid certificate** or Microsoft's cloud code-signing service (Trusted Signing), then add a `signtool sign` step to the workflow after the build step.

Signing `ServNook.exe` and `snook.exe` (not just the zip) is what matters.

## Renaming the project

If you fork ServNook and want your own name (for example "Forge"):

1. Find-and-replace `ServNook` → `Forge` and `snook` → `forge` across the repo. Check `K.AppName`, the `-main:ServNook.Program` and `-main:ServNook.CliProgram` arguments in both build scripts (the namespace), and the `SERVNOOK_` variable names in `overlay/` and `src/`.
2. Rename `snook` in both build scripts and the docs.
3. Replace the logo: edit the master vector `assets/logo/servnook-icon.svg`, then run `python3 build/make_icon.py` (needs `pip install cairosvg fonttools pillow`). It regenerates `assets/servnook.ico`, `assets/servnook-512.png`, the PNG sizes embedded in the app (`assets/logo/servnook-<n>.png`), the flame-only and monochrome SVGs, the light/dark wordmark lockups and the installer images. Also update the inline SVG in `overlay/www/index.php`.
4. Don't call the project "XAMPP" or anything confusingly similar. That name belongs to Apache Friends. "An XAMPP alternative" in the description is fine.

## Offline bundles

Some users want a zip that works without internet (classrooms, air-gapped machines). If you publish one, you're **redistributing** third-party binaries, so read [10 – Licensing](10-licensing.md) first. To build one:

1. Run `ServNook.exe` once on a clean machine and complete setup.
2. Delete `servnook.json`, `node-apps.json`, `data\`, `logs\`, `tmp\`, `config\apache\generated\` and `apps\phpmyadmin\config.inc.php` (so each install generates its own secret). Each `php.ini` keeps a `; servnook-root:` marker line, which ServNook uses to fix paths wherever the bundle is extracted.
3. Add a `THIRD-PARTY-NOTICES.txt` with every component's license (see [10](10-licensing.md#if-you-redistribute-binaries)).
4. Zip the folder.

On first launch, ServNook sees `apache\` and `php\` already present and skips setup. It writes a fresh phpMyAdmin config with a new secret, and MariaDB is initialised on its first start.

## Distribution channels (optional)

Once releases are stable, you can submit ServNook to **winget** (a manifest PR to `microsoft/winget-pkgs` pointing at the release zip) and **Scoop** (an `extras` bucket manifest). Both need a stable download URL and a SHA-256, which the release workflow already produces.


## Installer

`build\build.ps1 -Package` also builds **`ServNook-Setup-<version>.exe`** with [Inno Setup 6](https://jrsoftware.org/isinfo.php) when `ISCC.exe` is installed (the release workflow installs it). The script is `build\installer.iss`.

- **Per-user, no admin rights.** The default folder is `C:\ServNook`. The wizard refuses folders with spaces or inside Program Files, because ServNook writes inside its own folder.
- **Upgrades in place.**
  - The running panel is asked to close (`ServNook.exe --quit`; versions before 1.6.2 are ended instead). Apache, MariaDB and Node apps keep running.
  - Only the program files are replaced. Settings, `config\`, `www\` and `data\` are not touched.
  - Installing over a folder that came from the zip works the same way.
- **Uninstall** (Settings › Apps, or Start menu › Uninstall ServNook):
  - Stops the servers with `snook stop all`.
  - Removes the programs ServNook downloaded: `apache`, `php`, `mariadb`, `node`, `apps`, `tools`, `ssl`, `tmp`, `logs`.
  - Then **asks** whether to delete your sites, databases, backups and settings. *No* is the default; a silent uninstall always keeps them.
- **CI check.** The release workflow installs the setup silently, runs `snook version`, uninstalls it and checks that `www` survived.
