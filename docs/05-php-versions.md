# 05 – PHP versions

ServNook installs the newest stable PHP branch at setup. You can install more branches side by side and switch between them in seconds.

## Installing another version

**Settings → PHP version → Install another version…** lists the branches windows.php.net currently publishes (for example 8.5, 8.4, 8.3) and installs the newest x64 Thread Safe build of the one you pick into `php\<branch>\`, with its own `php.ini`.

From the command line:

```cmd
snook php 8.4
```

This installs 8.4 if needed, makes it active, and restarts Apache if it's running.

> windows.php.net lists only supported branches. For an end-of-life version (for example 7.4 for a legacy project), download the **x64 Thread Safe** zip from <https://windows.php.net/downloads/releases/archives/> yourself, extract it to `php\7.4\`, and copy a `php.ini` from another version folder, fixing `extension_dir` and the extension list. Old versions get no security fixes, so keep them local.

## Switching the global version (default mode)

In the default mode, PHP runs inside Apache as a module, so **one PHP version serves every site at a time**:

1. **Settings → PHP version** → choose → **Save**.
2. ServNook restarts Apache with `-C "Define PHP_DIR <root>/php/<branch>"`, which loads that version's `php8apache2_4.dll` and `php.ini`.

The **Shell** and Node apps also put the active version first on `PATH`, so `php -v` in the shell matches what Apache runs.

This mode is what XAMPP does, and it's the fastest and simplest option.

## Per-site PHP versions (advanced, mod_fcgid)

To run, say, a legacy app on PHP 8.3 while everything else uses 8.5, switch Apache to FastCGI mode. Apache then starts `php-cgi.exe` processes, and each folder can point at a different PHP version.

### 1. Get mod_fcgid

Download `mod_fcgid-*-win64-VS18.zip` from the **modules** section of <https://www.apachelounge.com/download/> (match your Apache build) and copy `mod_fcgid.so` into `apache\modules\`.

### 2. Make sure each PHP version has php-cgi.exe

Every Windows PHP zip contains `php-cgi.exe`. The Thread Safe builds ServNook installs work; Non Thread Safe builds are slightly faster under FastCGI if you want to install those instead. `php-cgi.exe` reads the `php.ini` in its own folder, so each version keeps its own settings.

### 3. Turn on FastCGI mode

ServNook 1.0 has no button for this yet. In `config\apache\httpd.conf`, uncomment this line near the top (remove the `#`):

```apache
Define SERVNOOK_PHP_FCGID 1
```

With that defined, `httpd.conf` includes `php-fcgid.conf` instead of `php-mod.conf`. Every site now runs the active PHP version (from Settings) through `php-cgi.exe`. Generated vhosts and phpMyAdmin already allow `ExecCGI`; if you write your own vhosts, include `ExecCGI` in their `Options` too, or PHP files return 403.

### 4. Override the version per site

Create `config\apache\custom\<anything>.conf`:

```apache
# config/apache/custom/legacy-php.conf
<Directory "${SERVNOOK_ROOT}/www/legacy">
    FcgidWrapper "${SERVNOOK_ROOT}/php/8.3/php-cgi.exe" .php
</Directory>
```

Restart Apache and check `http://legacy.localhost/` with a `phpinfo()` page.

### Trade-offs

| | `mod_php` (default) | `mod_fcgid` |
|---|---|---|
| Versions at once | One | One per directory |
| Speed | Fastest | Slightly slower; processes are reused |
| Setup | Automatic | One manual step |
| Memory | Low | One worker pool per version |
| `phpinfo()` "Server API" | Apache 2.0 Handler | CGI/FastCGI |

To go back, comment the `Define SERVNOOK_PHP_FCGID 1` line out again and restart Apache.

## Updating PHP

**Settings → Check for updates…** compares the installed patch version of the active branch (for example 8.5.9) with the newest one (8.5.11) and updates it in place:

- Apache is stopped, the new PHP is unpacked, your `php.ini` is carried over, and Apache starts again.
- The previous folder is moved to `tmp\backup\php-<date>\`. Delete old backups whenever you like.

Extensions you added by hand (Xdebug, for example) live in `ext\` and are **not** carried over. Copy the new version-matched DLL in after updating.

A new branch, such as 8.6 when it's released, is a separate install (**Install another version…**), so you choose when to move.

## Composer and the PHP version

`composer` in the ServNook shell runs with the active PHP. To run Composer with a specific version without switching:

```cmd
C:\servnook\php\8.3\php.exe C:\servnook\tools\composer.phar install
```
