# 02 – Build the stack by hand

`ServNook.exe` automates everything on this page. Doing it once by hand is the best way to understand what ServNook does, to debug an unusual machine, or to build your own variant. Every config file referenced here is in the repo's [`overlay/`](../overlay) folder; those are the same templates embedded in `ServNook.exe`.

Commands are for **Command Prompt** (`cmd.exe`). The examples use `C:\servnook`; any path without spaces works.

## 0. Prerequisites

- Windows 10 or 11, 64-bit.
- **Microsoft Visual C++ Redistributable 2017–2026 (x64)**. Apache Lounge's VS18 builds need runtime 14.50 or newer, and PHP's VS17 builds run on the same runtime. Download it from <https://aka.ms/vc14/vc_redist.x64.exe> (Microsoft's "latest supported" link) and install it.
- About 1.5 GB of free disk space.

Check what's installed:

```cmd
reg query "HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" /v Version
```

## 1. Create the folder skeleton

```cmd
mkdir C:\servnook
cd /d C:\servnook
for %d in (www logs tmp tmp\pids tmp\sessions tmp\uploads tmp\downloads ssl tools data config\apache\generated config\apache\custom config\mariadb php node apps) do mkdir %d
```

## 2. Apache

1. From <https://www.apachelounge.com/download/>, download the newest **Win64** zip, for example `httpd-2.4.68-260610-Win64-VS18.zip`. Verify it against the checksums Apache Lounge links next to the file.
2. The zip contains an `Apache24` folder plus a few readme files. Move the **contents** of `Apache24` to `C:\servnook\apache`, so that `C:\servnook\apache\bin\httpd.exe` exists.
3. Copy ServNook's Apache config into place:

```text
overlay/config/apache/httpd.conf          -> C:\servnook\config\apache\httpd.conf
overlay/config/apache/php-mod.conf        -> C:\servnook\config\apache\php-mod.conf
overlay/config/apache/php-fcgid.conf      -> C:\servnook\config\apache\php-fcgid.conf
overlay/config/apache/default-vhost.conf  -> C:\servnook\config\apache\default-vhost.conf
```

You never edit `C:\servnook\apache\conf\httpd.conf`; ServNook's own file is passed to Apache with `-f`.

## 3. PHP

1. From <https://windows.php.net/download/>, download the **VS17 x64 Thread Safe** zip of the newest branch, for example `php-8.5.x-Win32-vs17-x64.zip`. Pick the one **without** `nts` in its name: `mod_php` needs Thread Safe. Check its SHA-256 against the value on the download page.
2. Extract it to `C:\servnook\php\8.5`, so that `C:\servnook\php\8.5\php.exe` and `php8apache2_4.dll` exist.
3. Create `C:\servnook\php\8.5\php.ini` from [`overlay/php/php.ini.template`](../overlay/php/php.ini.template):
   - Replace `{{SERVNOOK_ROOT}}` with `C:/servnook` and `{{PHP_DIR}}` with `C:/servnook/php/8.5` (forward slashes).
   - Replace `{{TIMEZONE}}` with your zone, for example `Asia/Kolkata` or `Europe/Berlin`.
   - Replace `{{EXTENSIONS}}` with one `extension = <name>` line for each `ext\php_<name>.dll` you want. ServNook enables `bz2 curl exif fileinfo gd gettext intl mbstring mysqli openssl pdo_mysql pdo_sqlite soap sockets sodium sqlite3 xsl zip` when present.
   - PHP 8.4 and older only: add `zend_extension = opcache`. From PHP 8.5, OPcache is built in and there's no `php_opcache.dll`.
4. Download the CA bundle <https://curl.se/ca/cacert.pem> to `C:\servnook\ssl\cacert.pem`. Windows builds of PHP ship without CA certificates, so without this file every HTTPS request from PHP (Composer, Guzzle, `file_get_contents('https://…')`) fails with a certificate error.

Test it:

```cmd
C:\servnook\php\8.5\php.exe -v
C:\servnook\php\8.5\php.exe -m
```

`-m` should list the extensions you enabled, without warnings.

## 4. MariaDB

1. From <https://mariadb.org/download/>, choose the newest **LTS** release, **Windows x86_64, ZIP file**, for example `mariadb-12.3.3-winx64.zip`. Verify the SHA-256.
2. Move the contents of the zip's top folder to `C:\servnook\mariadb`, so that `C:\servnook\mariadb\bin\mariadbd.exe` exists.
3. Copy [`overlay/config/mariadb/my.ini`](../overlay/config/mariadb/my.ini) to `C:\servnook\config\mariadb\my.ini`.
4. Create the system tables (one time only):

```cmd
C:\servnook\mariadb\bin\mariadb-install-db.exe --datadir=C:\servnook\data\mariadb --port=3306
```

Add `--password=yourpassword` to set a root password; without it, `root` has no password, the way XAMPP does it. The data folder must not exist beforehand.

## 5. phpMyAdmin

1. Download `phpMyAdmin-<version>-all-languages.zip` from <https://www.phpmyadmin.net/downloads/> and verify the SHA-256.
2. Move the contents of its top folder to `C:\servnook\apps\phpmyadmin`.
3. Copy [`overlay/apps/phpmyadmin/config.inc.php`](../overlay/apps/phpmyadmin/config.inc.php) there and replace:
   - `{{BLOWFISH_SECRET}}` with **32** random letters and digits, for example from `php -r "echo bin2hex(random_bytes(16));"`
   - `{{MYSQL_PORT}}` with `3306`
   - `{{ALLOW_NO_PASSWORD}}` with `true` (no root password) or `false`
4. Create `C:\servnook\apps\phpmyadmin\tmp`.

## 6. Node.js

1. From <https://nodejs.org/en/download>, download the **Windows x64 .zip** of the current LTS, for example `node-v24.x.y-win-x64.zip`. Verify it against `SHASUMS256.txt` in the same release folder.
2. Move the contents of its top folder to `C:\servnook\node\24`, and create `C:\servnook\node\24\global`.

## 7. Composer (optional)

Download <https://getcomposer.org/download/latest-stable/composer.phar> to `C:\servnook\tools\`, then create `C:\servnook\tools\composer.bat`:

```bat
@echo off
php "%~dp0composer.phar" %*
```

## 8. A first site

```cmd
mkdir C:\servnook\www\hello
echo ^<?php phpinfo(); > C:\servnook\www\hello\index.php
```

Create `C:\servnook\config\apache\generated\sites.conf`:

```apache
<VirtualHost *:${SERVNOOK_HTTP_PORT}>
    ServerName hello.localhost
    DocumentRoot "${SERVNOOK_ROOT}/www/hello"
    <Directory "${SERVNOOK_ROOT}/www/hello">
        Options Indexes FollowSymLinks
        AllowOverride All
        Require local
    </Directory>
</VirtualHost>
```

## 9. Start everything

Save this as `C:\servnook\start-manual.cmd`. It's what ServNook does, minus the polish:

```bat
@echo off
setlocal
set SERVNOOK_ROOT=C:/servnook
set PHP_DIR=C:/servnook/php/8.5
set PATH=C:\servnook\php\8.5;C:\servnook\node\24;C:\servnook\node\24\global;C:\servnook\mariadb\bin;C:\servnook\tools;%PATH%
set NPM_CONFIG_PREFIX=C:\servnook\node\24\global
set NPM_CONFIG_CACHE=C:\servnook\tmp\npm-cache

set HTTPD_ARGS=-d "C:/servnook/apache" -f "C:/servnook/config/apache/httpd.conf" -C "Define SERVNOOK_ROOT %SERVNOOK_ROOT%" -C "Define PHP_DIR %PHP_DIR%" -C "Define SERVNOOK_HTTP_PORT 80" -C "Define SERVNOOK_MYSQL_PORT 3306"

echo Checking Apache config...
C:\servnook\apache\bin\httpd.exe %HTTPD_ARGS% -t || goto :eof

echo Starting MariaDB...
start "MariaDB" /min C:\servnook\mariadb\bin\mariadbd.exe --defaults-file=C:/servnook/config/mariadb/my.ini --datadir=C:/servnook/data/mariadb --port=3306 --bind-address=127.0.0.1 --log-error=C:/servnook/logs/mariadb.err --pid-file=C:/servnook/tmp/pids/mariadb.pid

echo Starting Apache...
start "Apache" /min C:\servnook\apache\bin\httpd.exe %HTTPD_ARGS%

echo Open http://hello.localhost and http://localhost/phpmyadmin
```

To stop:

- **Apache**: switch to its minimized console window and press **Ctrl+C**. A console Apache can't be stopped with `httpd -k shutdown`, which only controls Apache installed as a Windows service. (ServNook stops it gracefully by signalling the `ap<pid>_shutdown` event that Apache's parent process waits on.)
- **MariaDB**: run

```bat
C:\servnook\mariadb\bin\mariadb-admin.exe --protocol=TCP -h 127.0.0.1 -P 3306 -u root shutdown
```

## 10. Verify

| Check | Expected |
|---|---|
| <http://hello.localhost> | The `phpinfo()` page, showing `Loaded Configuration File: C:\servnook\php\8.5\php.ini` |
| <http://localhost/phpmyadmin> | The login page. Log in as `root` with the password you set (or none). |
| `mariadb -h 127.0.0.1 -u root -e "select version()"` | The MariaDB version |
| `php -r "echo file_get_contents('https://example.com') ? 'TLS ok' : 'fail';"` | `TLS ok` (proves `cacert.pem` is wired up) |
| `node -v` and `npm -v` | The Node and npm versions |

If anything fails, look in `C:\servnook\logs\apache-error.log` and `C:\servnook\logs\mariadb.err`, then see [09 – Troubleshooting](09-troubleshooting.md).
