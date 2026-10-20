# 03 – Configuration reference

All settings are plain text files you can open from the panel (each service's **Config** button) or in any editor.

| File | What it controls | Who writes it |
|---|---|---|
| `kiln.json` | Active PHP/Node versions, ports, root password, panel behavior | Kiln (Settings window); safe to hand-edit while Kiln is closed |
| `node-apps.json` | Node apps | Kiln (Node apps window); safe to hand-edit |
| `config\apache\httpd.conf` | Apache core config | Created once at setup; yours to edit |
| `config\apache\default-vhost.conf` | `http://localhost` and `/phpmyadmin` | Created once; yours to edit |
| `config\apache\php-mod.conf`, `php-fcgid.conf` | How Apache runs PHP | Created once; yours to edit |
| `config\apache\generated\*.conf` | One vhost per `www\` folder and per Node app | **Regenerated** by Kiln; don't edit |
| `config\apache\custom\*.conf` | Your own vhosts and overrides | You; never touched by Kiln |
| `php\<branch>\php.ini` | PHP settings for that version | Created when the version is installed; kept across PHP updates |
| `config\mariadb\my.ini` | MariaDB settings | Created once; yours to edit |
| `apps\phpmyadmin\config.inc.php` | phpMyAdmin | Created once; Kiln updates only the port and `AllowNoPassword` |

After editing Apache or PHP config, restart Apache (**Stop**, then **Start**, or `kilnctl restart apache`). Kiln runs `httpd -t` before every start and shows Apache's exact error if the config is invalid.

---

## kiln.json

```json
{
  "php": "8.5",
  "node": "24",
  "httpPort": 80,
  "mysqlPort": 3306,
  "mysqlRootPassword": "",
  "startOnLaunch": false,
  "minimizeToTray": true,
  "lastRoot": "C:/kiln",
  "manifestUrl": "",
  "lanAccess": false,
  "autoBackup": true,
  "backupEveryHours": 2,
  "backupKeep": 10
}
```

| Key | Meaning |
|---|---|
| `php` | Active PHP branch, the folder name under `php\` |
| `node` | Active Node.js major, the folder name under `node\` |
| `httpPort`, `mysqlPort` | Listening ports. Change these in **Settings** so dependent files (phpMyAdmin config) update too. |
| `mysqlRootPassword` | Used by Kiln to stop MariaDB cleanly and by the dashboard. Stored in plain text, which is acceptable for a local dev stack and the same trade-off XAMPP makes. Change it with **Settings → Database root → Change…**, which also changes it inside MariaDB. |
| `startOnLaunch` | Start Apache, MariaDB and auto-start Node apps when the panel opens |
| `minimizeToTray` | Closing the window hides Kiln to the tray instead of exiting |
| `lastRoot` | Where the folder was last time; used to fix `php.ini` paths after a move. Don't edit. |
| `lanAccess` | Let devices on your local network open the sites ([08](08-security.md#opening-kiln-to-your-lan-for-testing-on-a-phone)). Set in **Settings → Network**. |
| `autoBackup`, `backupEveryHours`, `backupKeep` | Automatic database backups ([11](11-databases.md#backups)). Set in **Database…** |
| `manifestUrl` | Optional URL of a `versions.json` that pins component versions (below). Empty means the maintainer default. |

## node-apps.json

```json
{
  "apps": [
    {
      "name": "api",
      "dir": "www/api",
      "command": "npm run dev",
      "port": 3000,
      "autostart": true,
      "env": { "DATABASE_URL": "mysql://root@127.0.0.1:3306/api" }
    }
  ]
}
```

| Key | Meaning |
|---|---|
| `name` | Lowercase letters, digits and hyphens. The app is served at `http://<name>.localhost` (and `<name>.test`). |
| `dir` | App folder. A path relative to the Kiln root (`www/api`) or absolute (`D:/code/api`). |
| `command` | Run with `cmd.exe` in `dir`. Any command works: `npm run dev`, `npm start`, `node server.js`, `npx vite --port 5173`. |
| `port` | Kiln sets `PORT` to this value and proxies the hostname to it. The app must listen on it. |
| `autostart` | Include in **Start all** and `kilnctl start` |
| `env` | Extra environment variables |

See [04 – Node.js integration](04-node-integration.md) for framework recipes.

## Apache

### httpd.conf

[`overlay/config/apache/httpd.conf`](../overlay/config/apache/httpd.conf) is commented line by line. The key points:

- **No absolute paths.** `${KILN_ROOT}`, `${PHP_DIR}`, `${KILN_HTTP_PORT}` and `${KILN_MYSQL_PORT}` are defined on the command line by Kiln. If you start Apache any other way, Apache stops with `KILN_ROOT is not defined`.
- `Listen 127.0.0.1:${KILN_HTTP_PORT}` and `Require local` keep everything on this machine. With network access on, Kiln adds `-C "Define KILN_LAN 1"`: Apache then listens on `0.0.0.0` and every `<IfDefine KILN_LAN>` block replaces `Require local` with `Require ip <private ranges>`. Use the same pattern in your custom vhosts.
- `AllowOverride All` under `www\`, so `.htaccess` files from WordPress, Laravel and similar work.
- Modules loaded: alias, auth_basic, authn_core, authn_file, authz_core, authz_host, authz_user, autoindex, deflate, dir, env, expires, filter, headers, log_config, mime, proxy, proxy_http, rewrite, setenvif. To add one, add a `LoadModule` line; every module that ships with Apache is in `apache\modules\`.
- `SetEnv KILN_ROOT` and `SetEnv KILN_MYSQL_PORT` expose those values to PHP as `$_SERVER['KILN_ROOT']` and `$_SERVER['KILN_MYSQL_PORT']`.
- Logs go to `logs\apache-error.log` and `logs\apache-access.log`.

### Generated vhosts

On every Apache start (and on **Refresh sites** / `kilnctl sites`), Kiln writes:

- **`generated\sites.conf`**: one vhost per folder in `www\` whose name is a valid hostname label (letters, digits, hyphens; no spaces or dots). The folder `Shop` becomes `shop.localhost`.
  - `DocumentRoot` is `www\<folder>\public` when a `public` subfolder exists (Laravel, Symfony, Slim and others), otherwise `www\<folder>`.
  - Folders whose names aren't valid hostnames are still reachable at `http://localhost/<folder>/`.
- **`generated\node-apps.conf`**: one reverse-proxy vhost per Node app.

A folder is **skipped** when:
- a Node app has the same name (the Node app wins), or
- `config\apache\custom\<name>.conf` exists. This is how you take over a site's config.

### Custom vhosts

Anything in `config\apache\custom\*.conf` is included after the generated files and never overwritten. Example: serving `www\shop\web` (for a framework that uses `web\` instead of `public\`):

```apache
# config/apache/custom/shop.conf
<VirtualHost *:${KILN_HTTP_PORT}>
    ServerName shop.localhost
    DocumentRoot "${KILN_ROOT}/www/shop/web"
    <Directory "${KILN_ROOT}/www/shop/web">
        AllowOverride All
        Require local
    </Directory>
</VirtualHost>
```

Because the file is named `shop.conf`, Kiln stops generating a vhost for `www\shop`.

### HTTPS (optional, manual)

Kiln 1.0 serves HTTP only, which is enough for most local work: browsers treat `localhost` and `*.localhost` as secure contexts, so service workers, `crypto.subtle` and similar APIs work over plain HTTP there. If you need real HTTPS:

1. Create a locally trusted certificate with [mkcert](https://github.com/FiloSottile/mkcert): `mkcert -install`, then `mkcert -cert-file C:\kiln\ssl\local.pem -key-file C:\kiln\ssl\local-key.pem localhost "*.localhost" blog.localhost`. (Wildcards one level under `localhost` aren't accepted by every browser, so list the names you need.)
2. Create `config\apache\custom\ssl.conf`:

```apache
LoadModule ssl_module modules/mod_ssl.so
LoadModule socache_shmcb_module modules/mod_socache_shmcb.so
Listen 127.0.0.1:443
SSLSessionCache "shmcb:${KILN_ROOT}/tmp/ssl_scache(512000)"

<VirtualHost *:443>
    ServerName blog.localhost
    DocumentRoot "${KILN_ROOT}/www/blog/public"
    SSLEngine on
    SSLCertificateFile    "${KILN_ROOT}/ssl/local.pem"
    SSLCertificateKeyFile "${KILN_ROOT}/ssl/local-key.pem"
    <Directory "${KILN_ROOT}/www/blog/public">
        AllowOverride All
        Require local
    </Directory>
</VirtualHost>
```

## PHP: php.ini

Each `php\<branch>\php.ini` is generated from [`overlay/php/php.ini.template`](../overlay/php/php.ini.template) when that version is installed:

| Setting | Value | Why |
|---|---|---|
| `display_errors`, `error_reporting` | `On`, `E_ALL` | You want to see problems while developing |
| `error_log` | `logs\php_errors.log` | One place for PHP errors from every site |
| `memory_limit` | `512M` | Composer and big imports need room |
| `upload_max_filesize`, `post_max_size` | `128M` | Large database imports through phpMyAdmin |
| `max_execution_time` | `120` | |
| `extension_dir` | `php\<branch>\ext` | Absolute, so it works the same under Apache and on the CLI |
| `extension = ...` | Only DLLs present in that build | No "unable to load dynamic library" warnings across PHP versions |
| `date.timezone` | Detected from Windows | Avoids warnings and wrong timestamps |
| `session.save_path`, `upload_tmp_dir` | `tmp\sessions`, `tmp\uploads` | Kept inside Kiln |
| `curl.cainfo`, `openssl.cafile` | `ssl\cacert.pem` | HTTPS from PHP works out of the box |
| `opcache.enable` / `validate_timestamps` | `1` / `1`, `revalidate_freq=0` | Fast, but edits show up immediately |
| `SMTP`, `smtp_port` | `localhost`, `1025` | Point `mail()` at a local mail catcher such as [Mailpit](https://mailpit.axllent.org/) |

**Xdebug** isn't bundled. Download the DLL matching your PHP version (x64, TS) from <https://xdebug.org/download>, put it in `php\<branch>\ext\`, and uncomment the Xdebug lines at the end of `php.ini`.

The same `php.ini` is used by Apache (through `PHPIniDir`) and by `php.exe` in the Kiln shell, because PHP's CLI looks for `php.ini` next to `php.exe`.

## MariaDB: my.ini

[`overlay/config/mariadb/my.ini`](../overlay/config/mariadb/my.ini) contains only path-free settings:

- `utf8mb4` everywhere, a 64 MB `max_allowed_packet`, and a 256 MB InnoDB buffer pool.
- **Crash safety:** `innodb_flush_log_at_trx_commit = 1` (committed data is on disk before MariaDB answers), `innodb_doublewrite = 1`, `innodb_file_per_table = 1`, and `aria_recover_options = BACKUP,QUICK`. See [11 – Databases](11-databases.md).

Paths, port and bind address are passed on the command line, and command-line options win over `my.ini`.

## phpMyAdmin: config.inc.php

- `auth_type = cookie`: you log in with a MariaDB user.
- `host = 127.0.0.1`, plus the port from `kiln.json`.
- `AllowNoPassword`: `true` while root has no password. Kiln flips it when you set a password in Settings.
- `blowfish_secret`: 32 random characters generated per install.
- `TempDir = apps\phpmyadmin\tmp`.

> phpMyAdmin officially lists supported PHP versions per release. When a new PHP version is fresher than the newest phpMyAdmin release, you may see deprecation notices in phpMyAdmin until it catches up. They're harmless. If they bother you, run phpMyAdmin on an older PHP version with a per-site version through `mod_fcgid` ([05](05-php-versions.md)), or use a desktop client such as HeidiSQL or DBeaver.

## versions.json (maintainers)

See [07 – Pinning versions](07-packaging-and-releases.md#pinning-versions-with-versionsjson).
