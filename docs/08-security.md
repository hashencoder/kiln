# 08 – Security

Kiln is a **local development** stack. Its defaults trade hardening for convenience on the assumption that only you, on your own machine, can reach it. This page explains what protects you, what doesn't, and what to change if your situation differs.

## What Kiln does by default

| Protection | How |
|---|---|
| Nothing listens on your network | Apache listens on `127.0.0.1:80` and MariaDB binds `127.0.0.1`. Other devices on your Wi-Fi can't connect, and Windows Firewall isn't involved. |
| Belt and braces | Every site, phpMyAdmin and every Node proxy vhost also has `Require local`, so even if someone changes `Listen` to `0.0.0.0`, Apache still refuses non-local clients until `Require` is changed too. |
| Verified downloads | SHA-256 is checked for PHP, MariaDB, Node.js, phpMyAdmin, Composer and the CA bundle, using the hashes each publisher publishes. (For PHP, the hash comes from windows.php.net's `releases.json`; if only the fallback page scrape works, there's no hash and Kiln logs a warning.) A mismatch aborts setup and deletes the file. All downloads use HTTPS. |
| Official binaries only | Kiln never modifies or repackages Apache, PHP, MariaDB, phpMyAdmin or Node.js. |
| No admin rights | Kiln runs as your user. The only elevation prompt is the optional Visual C++ Redistributable installer, run from Microsoft's own download. |
| Unique secrets | phpMyAdmin's cookie-encryption secret is random per install. |
| PHP source is never served | `.phps` is denied, and `.ht*` files are denied. |

## Known weak spots (by design)

| Item | Risk | Mitigation |
|---|---|---|
| MariaDB `root` has no password by default | Any program on your PC can read and modify your databases | Set a password in **Settings → Database root → Change…** |
| `kiln.json` stores the root password in plain text | Readable by anything running as your user | It's needed to stop MariaDB cleanly. Don't reuse a real password. |
| `display_errors = On`, directory listings on | Leaks paths and stack traces | Fine locally; never copy this `php.ini` to a server |
| Apache Lounge zips have no machine-readable checksum | A compromised download page or TLS interception could swap the zip | HTTPS plus Apache Lounge's reputation. Maintainers can pin Apache with a hand-verified `sha256` in `versions.json` ([07](07-packaging-and-releases.md#pinning-versions-with-versionsjson)). |
| Dev servers (Vite, Next.js) run with dev settings | Some dev servers have had vulnerabilities that let a malicious website you visit read local files | Keep Node packages updated. Kiln keeps the proxy on loopback, but a page in your own browser can still reach loopback. |

### DNS rebinding and browser-originated attacks

Any website you visit can make your browser send requests to `127.0.0.1`. Apache's name-based vhosts help (a request with a foreign `Host` header lands on the default dashboard, not your app), and Vite and Next.js check `Host` headers in development. Still:

- Don't leave admin tools you've installed yourself (Adminer, file managers, `phpinfo.php`) in `www\` without protection.
- Stop the stack when you're not developing (**Stop all**, or turn off **Start servers when Kiln opens**).

## Opening Kiln to your LAN (for testing on a phone)

Only do this on a network you trust, and undo it afterwards.

1. Set a MariaDB root password first.
2. In `config\apache\httpd.conf`, change `Listen 127.0.0.1:${KILN_HTTP_PORT}` to `Listen 0.0.0.0:${KILN_HTTP_PORT}`.
3. For the site you want to test, add a custom vhost in `config\apache\custom\` with `Require ip 192.168.1.0/24` (your subnet) instead of `Require local`, and a `ServerAlias` for your PC's IP address.
4. Restart Apache. Windows Firewall will ask; allow it on **Private** networks only.

Alternatively, use a tunnelling tool (for example `cloudflared tunnel --url http://localhost:80`) to share a single site temporarily, without opening any port.

## Production

**Don't use Kiln in production**, or anywhere reachable from the internet. Use a properly configured server (Linux with your distro's packages, containers, or managed hosting) with production `php.ini` settings, real TLS, a firewall and least-privilege database users.

## Reporting a vulnerability in Kiln

Maintainers: enable **GitHub private vulnerability reporting** (Settings → Code security) and replace this paragraph with how you want reports sent. Vulnerabilities in Apache, PHP, MariaDB, phpMyAdmin or Node.js themselves go to those projects; Kiln picks up their fixes through **Check for updates**.
