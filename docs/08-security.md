# 08 – Security

WebHut is a **local development** stack. Its defaults trade hardening for convenience on the assumption that only you, on your own machine, can reach it. This page explains what protects you, what doesn't, and what to change if your situation differs.

## What WebHut does by default

| Protection | How |
|---|---|
| Nothing listens on your network (by default) | Apache listens on `127.0.0.1:80` and MariaDB binds `127.0.0.1`. Other devices on your Wi-Fi can't connect, and Windows Firewall isn't involved. Network access is an explicit opt-in (below). |
| Belt and braces | Every site, phpMyAdmin and every Node proxy vhost also has `Require local`, so even if someone changes `Listen` to `0.0.0.0`, Apache still refuses non-local clients until `Require` is changed too. |
| Verified downloads | SHA-256 is checked for PHP, MariaDB, Node.js, phpMyAdmin, Composer and the CA bundle, using the hashes each publisher publishes. (For PHP, the hash comes from windows.php.net's `releases.json`; if only the fallback page scrape works, there's no hash and WebHut logs a warning.) A mismatch aborts setup and deletes the file. All downloads use HTTPS. |
| Official binaries only | WebHut never modifies or repackages Apache, PHP, MariaDB, phpMyAdmin or Node.js. |
| No admin rights | WebHut runs as your user. The only elevation prompt is the optional Visual C++ Redistributable installer, run from Microsoft's own download. |
| Unique secrets | phpMyAdmin's cookie-encryption secret is random per install. |
| PHP source is never served | `.phps` is denied, and `.ht*` files are denied. |

## Known weak spots (by design)

| Item | Risk | Mitigation |
|---|---|---|
| MariaDB `root` has no password by default | Any program on your PC can read and modify your databases | Set a password in **Settings → Database root → Change…** |
| `webhut.json` stores the root password in plain text | Readable by anything running as your user | It's needed to stop MariaDB cleanly. Don't reuse a real password. |
| `display_errors = On`, directory listings on | Leaks paths and stack traces | Fine locally; never copy this `php.ini` to a server |
| Apache Lounge zips have no machine-readable checksum | A compromised download page or TLS interception could swap the zip | HTTPS plus Apache Lounge's reputation. Maintainers can pin Apache with a hand-verified `sha256` in `versions.json` ([07](07-packaging-and-releases.md#pinning-versions-with-versionsjson)). |
| Dev servers (Vite, Next.js) run with dev settings | Some dev servers have had vulnerabilities that let a malicious website you visit read local files | Keep Node packages updated. WebHut keeps the proxy on loopback, but a page in your own browser can still reach loopback. |

### DNS rebinding and browser-originated attacks

Any website you visit can make your browser send requests to `127.0.0.1`. Apache's name-based vhosts help (a request with a foreign `Host` header lands on the default dashboard, not your app), and Vite and Next.js check `Host` headers in development. Still:

- Don't leave admin tools you've installed yourself (Adminer, file managers, `phpinfo.php`) in `www\` without protection.
- Stop the stack when you're not developing (**Stop all**, or turn off **Start servers when WebHut opens**).

## Opening WebHut to your LAN (for testing on a phone)

**Settings → Network → "Allow access from other devices on my network"** (off by default). When it's on:

- Apache listens on all addresses instead of only `127.0.0.1`.
- Sites accept connections from this PC and from **private network addresses only**: `10.x`, `172.16–31.x`, `192.168.x`, link-local, and IPv6 ULA/link-local. A request from a public address gets `403`, even if your router forwards a port by mistake.
- **phpMyAdmin stays local-only**, and **MariaDB stays on `127.0.0.1`**. Nothing on your network can reach the database directly.
- The panel and the dashboard show your address, e.g. `http://192.168.1.5/`.
- Windows Firewall asks once whether `httpd.exe` may accept connections. Allow **Private networks** only.

From a phone on the same Wi-Fi:

| Address | Opens |
|---|---|
| `http://192.168.1.5/` | The WebHut dashboard |
| `http://192.168.1.5/blog/` | `www\blog`, XAMPP-style |
| `http://blog.192.168.1.5.nip.io/` | The `blog` vhost, including its `public\` folder. [nip.io](https://nip.io) is a free public DNS service that maps that name back to `192.168.1.5`, so it needs internet access; the traffic itself stays on your network. |
| `http://api.192.168.1.5.nip.io/` | The Node app `api`, through Apache's proxy |

Turn it off again on networks you don't trust (cafés, hotels, office Wi-Fi). Everyone on those networks also has a private address.

Alternatively, use a tunnelling tool (for example `cloudflared tunnel --url http://localhost:80`) to share one site temporarily with someone outside your network, without opening anything.

## Production

**Don't use WebHut in production**, or anywhere reachable from the internet. Use a properly configured server (Linux with your distro's packages, containers, or managed hosting) with production `php.ini` settings, real TLS, a firewall and least-privilege database users.

## Reporting a vulnerability in WebHut

Maintainers: enable **GitHub private vulnerability reporting** (Settings → Code security) and replace this paragraph with how you want reports sent. Vulnerabilities in Apache, PHP, MariaDB, phpMyAdmin or Node.js themselves go to those projects; WebHut picks up their fixes through **Check for updates**.
