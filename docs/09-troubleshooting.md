# 09 – Troubleshooting

Start with the **Activity** log in the panel and the service's **Logs** button. WebHut's own log is `logs\webhut.log`. Most problems below show an exact message there.

## WebHut won't open

| Symptom | Fix |
|---|---|
| "The WebHut folder path contains a space" | Move the folder to a path without spaces, such as `C:\webhut`. Apache and PHP config break on spaces. |
| "WebHut is inside Program Files" | Move it to a folder you own, such as `C:\webhut` or `D:\dev\webhut`. |
| "WebHut is already running for this folder" | It's in the system tray (click the **^** arrow next to the clock). |
| "Windows protected your PC" | **More info → Run anyway**. WebHut is new and not code-signed yet. |
| Nothing happens, or a .NET error | WebHut needs .NET Framework 4.8, which is built into Windows 10 1903+ and Windows 11. On older Windows 10, install it from Microsoft. |

## Setup problems

| Symptom | Fix |
|---|---|
| A component shows "Not found" | The lookup failed (offline, proxy, or the publisher changed its page). Select it, click **Open download page**, download the right file (see [02](02-build-from-scratch.md)), then **Use a zip I downloaded…**. |
| "SHA-256 mismatch" | The download was corrupted or tampered with. WebHut already deleted it. Click **Install** again. If it repeats, check for a corporate proxy rewriting downloads. |
| Downloads fail behind a corporate proxy | WebHut uses the Windows proxy settings (Settings → Network → Proxy). If your proxy needs a login, download the zips in your browser and use **Use a zip I downloaded…**. |
| "MariaDB initialisation failed" | Usually antivirus blocking `mariadb-install-db.exe`, or a half-created `data\mariadb` from an interrupted run. Remove `data\mariadb` (only if it holds nothing you need) and start MariaDB again; WebHut initialises an empty or missing data folder on start. |

## Apache won't start

| Message | Cause | Fix |
|---|---|---|
| "Port 80 is already in use by …" | Another program holds port 80 | See [Port 80 is taken](#port-80-is-taken) |
| "Windows couldn't load Apache's runtime DLLs" | Visual C++ Redistributable missing or too old | **Settings → Install / repair Visual C++ runtime**, or install <https://aka.ms/vc14/vc_redist.x64.exe> |
| "Cannot load …php8apache2_4.dll" | The PHP build is Non Thread Safe, or the VC++ runtime is too old | Install the **x64 Thread Safe** build (WebHut's default). Update the VC++ runtime. |
| "Apache configuration test failed: Syntax error on line N of …" | A config edit broke something | Open the file named in the message. WebHut shows Apache's exact error. |
| "WEBHUT_ROOT is not defined" | Apache was started without WebHut's arguments | Start it from WebHut or `whut`, or copy the arguments from [02](02-build-from-scratch.md#9-start-everything) |
| "AH00072: make_sock: could not bind to address" | Port in use, sometimes by a stale `httpd.exe` | Run `whut stop apache`, or end `httpd.exe` in Task Manager |
| Clicking Stop takes ~15 s, then "didn't stop cleanly" | Apache couldn't be signalled (security software sometimes blocks it) | Harmless; WebHut ends the process instead |

### Port 80 is taken

WebHut names the program holding the port. Common culprits:

| Holder | Fix |
|---|---|
| **System (PID 4)** / HTTP.sys | IIS or another HTTP.sys user. In an admin prompt run `net stop W3SVC` and set "World Wide Web Publishing Service" to Manual. Also check `netsh http show servicestate`. |
| Another XAMPP, WAMP, Laragon or Docker | Stop it, or give WebHut a different port |
| Skype (old versions), VMware Workstation sharing, some VPN or antivirus web shields | Turn off their use of port 80 |

Or avoid the conflict: **Settings → HTTP port → 8080**. Sites are then at `http://blog.localhost:8080`.

## MariaDB won't start

| Symptom | Fix |
|---|---|
| "Port 3306 is already in use by mysqld.exe / mariadbd.exe" | Another MySQL or MariaDB (an old XAMPP, or a MySQL service) is running. Stop it (`services.msc`), or set WebHut's MySQL port to 3307. |
| MariaDB won't start after a power cut or crash (`mariadb.err` mentions corruption or a failed recovery) | WebHut offers **Rebuild from backup** right away (or use **Database… → Rebuild from backup…**, or `whut rebuild-db`). The damaged folder is kept as `data\mariadb-damaged-<date>`. If you need changes newer than the last backup, see [11](11-databases.md#when-xampps-mysql-is-the-one-that-crashed): the same recovery modes apply to `data\mariadb-damaged-*`. |
| "Access denied for user 'root'" when stopping | `mysqlRootPassword` in `webhut.json` doesn't match the real password. Fix it there; WebHut falls back to ending the process in the meantime. |
| XAMPP import: "XAMPP's MySQL is running" | Stop MySQL in the XAMPP Control Panel first. Both would use the same files. |
| XAMPP import: "XAMPP's MySQL didn't start" | Tick **XAMPP's MySQL is damaged** and scan again ([11](11-databases.md#when-xampps-mysql-is-the-one-that-crashed)) |
| An imported app can't log in to the database | It used a custom MySQL user from XAMPP; users aren't copied. Create the user in WebHut ([11](11-databases.md#after-importing)). |
| phpMyAdmin: "Login without a password is forbidden" | Root has no password but `AllowNoPassword` is false. Set it to `true` in `apps\phpmyadmin\config.inc.php`, or set a password via Settings. |

## PHP problems

| Symptom | Fix |
|---|---|
| "Unable to load dynamic library …" | An `extension =` line points at a DLL that isn't in `ext\`. WebHut only enables existing DLLs, so this comes from a manual edit or a copied `php.ini`. Remove the line. |
| "cURL error 60: SSL certificate problem" / Composer TLS errors | `ssl\cacert.pem` is missing. Re-run setup with **CA certificates** ticked, or download <https://curl.se/ca/cacert.pem> to `ssl\`. |
| Changes in `php.ini` have no effect | Restart Apache. Check `phpinfo()` → "Loaded Configuration File" to see which `php.ini` is in use. |
| `php -v` in another terminal shows a different version | Only the WebHut **Shell** puts WebHut's PHP on `PATH`. Use it, or add `C:\webhut\php\8.5` to your user `PATH` yourself. |
| Deprecation notices in phpMyAdmin | phpMyAdmin hasn't caught up with the newest PHP yet. They're harmless; see [03](03-configuration.md#phpmyadmin-configincphp). |
| "Maximum execution time exceeded" on big imports | Raise `max_execution_time` in `php.ini`, or import with `mariadb -u root dbname < dump.sql` in the Shell |

## Sites and hostnames

| Symptom | Fix |
|---|---|
| `http://blog.localhost` shows the WebHut dashboard | No vhost for `blog` yet. Click **Refresh sites**. Folder names must be letters, digits and hyphens. |
| `blog.localhost` doesn't resolve | Some browsers or OS DNS settings don't map `*.localhost`. Use Chrome, Edge or Firefox, or add `127.0.0.1 blog.localhost` to the hosts file (admin). Check that no "secure DNS" extension is overriding it. |
| Laravel or Symfony shows a directory listing or 403 | WebHut serves `public\` only if it exists at Refresh time. Click **Refresh sites** after creating the project. |
| `.htaccess` rules ignored | They work under `www\`. For custom document roots, include `AllowOverride All` in your custom vhost. |
| 403 Forbidden from another device | Expected: `Require local`. See [08](08-security.md#opening-webhut-to-your-lan-for-testing-on-a-phone). |

## Node apps

| Symptom | Fix |
|---|---|
| "exited right away" | The command failed. Read the log lines WebHut shows. Usually `npm install` hasn't been run, or the script name is wrong. |
| "nothing is listening on port N" | The app ignored `PORT`. Check its log for the port it chose, then set that port in WebHut or pass `--port N` in the command. |
| `http://<name>.localhost` shows 503 | The app isn't running, or it's on a different port than configured |
| Hot reload doesn't connect | See the Vite notes in [04](04-node-integration.md#vite-notes) (`hmr.clientPort`) |
| "Invalid Host header" / blocked request | Add the hostname to the dev server's allowed hosts ([04](04-node-integration.md)) |
| `EADDRINUSE` in the log | Another process uses the port, often a previous run. **Stop** the app (it kills the whole process tree), or end `node.exe` in Task Manager. |
| npm errors about paths too long | Move WebHut to a short path such as `C:\webhut`, or enable Windows long paths (`LongPathsEnabled` in the registry, admin). |

## Antivirus interference

Security software sometimes quarantines `httpd.exe`, `mariadbd.exe` or WebHut itself, or slows `npm install` a lot. If files disappear or starts time out, add `C:\webhut` to your antivirus exclusions, only if you trust the source of your WebHut download.

## Starting over

- **Reset config only**: stop everything, delete `config\` (back up `config\apache\custom\` first), and start the servers again. WebHut recreates any missing config file from its templates on every start, and it never overwrites existing ones.
- **Reinstall a component**: stop everything and delete its folder (for example `mariadb\`). For Apache or the last PHP version, the setup window reappears on the next launch; for MariaDB or phpMyAdmin, **Settings → Check for updates…** offers to install it; for Node.js, use **Settings → Install latest LTS**.
- **Full reset, keeping databases**: keep `data\mariadb\`, `www\` and `node-apps.json`; delete everything else except `WebHut.exe`, `whut.exe` and their `.config` files; run `WebHut.exe`.

## Reporting a bug

Include the output of `whut status`, the last 50 lines of `logs\webhut.log`, the relevant server log, your Windows version (`winver`), and what you clicked. Remove any passwords first.
