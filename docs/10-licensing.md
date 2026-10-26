# 10 – Licensing

> This page is a practical summary, not legal advice. If you plan a commercial distribution or are unsure about an obligation, ask a lawyer.

## ServNook itself

ServNook's source code, config templates (`overlay/`) and documentation are released under the **MIT License** ([LICENSE](../LICENSE)). Anyone can use, modify and redistribute them, including commercially, as long as the copyright notice and license text are kept.

## Fonts bundled in ServNook.exe

The panel embeds two typefaces, unmodified, so it looks the same on every PC without installing anything:

| Font | Used for | License |
|---|---|---|
| [Inter](https://github.com/rsms/inter) 4.1 (Regular, Medium, SemiBold, Bold) | All interface text | SIL Open Font License 1.1, © The Inter Project Authors |
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) 2.304 (Regular) | Ports, PIDs and the activity log | SIL Open Font License 1.1, © The JetBrains Mono Project Authors |

The OFL allows bundling the fonts with software. The license texts are in [`assets/fonts/`](../assets/fonts) and in the `licenses\` folder of every release zip. If a font can't be loaded, ServNook falls back to Segoe UI and Consolas.

## The components ServNook downloads

| Component | License | Notes |
|---|---|---|
| Apache HTTP Server (Apache Lounge build) | Apache License 2.0 | Apache Lounge builds include libraries such as OpenSSL, PCRE2, zlib, nghttp2, brotli and libxml2, each under its own permissive license. See `apache\LICENSE.txt` and the readme in the zip. |
| PHP | BSD 3-Clause for current releases (PHP retired the PHP License 3.01 in 2026; older releases carry the PHP License) | Windows builds bundle libraries such as OpenSSL, libcurl, ICU, libxml2 and libzip. See `license.txt` in each PHP folder. |
| MariaDB Server | **GNU GPL v2** | Copyleft; see below. Client libraries are LGPL. |
| phpMyAdmin | **GNU GPL v2** | Copyleft; see below |
| Node.js | MIT | Bundles V8 (BSD), npm (Artistic 2.0), OpenSSL, ICU and others. See `node\<major>\LICENSE`. |
| Composer | MIT | |
| Mozilla CA bundle (`cacert.pem`, via curl.se) | Mozilla Public License 2.0 | |
| Microsoft Visual C++ Redistributable | Microsoft software license | ServNook launches Microsoft's own installer from Microsoft's site; it never redistributes the runtime. |

Each component keeps its license file inside its folder after installation.

## Why the default distribution model is simple

**ServNook releases contain only ServNook.** The setup window downloads each component from its official publisher onto the user's machine. ServNook therefore doesn't *distribute* Apache, PHP, MariaDB, phpMyAdmin or Node.js; each user obtains them from the source under that component's license, the same as visiting the download pages by hand.

So for the standard release zip, you only need to:

- keep ServNook's MIT `LICENSE` in the zip (the build script does), and
- not misrepresent the components (the README's "not affiliated" statement covers this).

## If you redistribute binaries

If you publish an **offline bundle** (a zip that already contains Apache, PHP, MariaDB and so on, see [07](07-packaging-and-releases.md#offline-bundles)), you become a distributor of those programs and take on their conditions:

1. **Keep every license file** that came in each component's folder, and add a top-level `THIRD-PARTY-NOTICES.txt` listing each component, its version, its license and its source URL.
2. **Apache 2.0 (Apache, OpenSSL and others):** include the license and any `NOTICE` files, and state that you didn't modify the binaries.
3. **GPL v2 (MariaDB, phpMyAdmin):** you must make the **corresponding source code** available. The usual ways:
   - host the exact source tarballs (for example `mariadb-12.3.3.tar.gz` from mariadb.org, and the phpMyAdmin source zip) next to your bundle in the same GitHub release, **or**
   - include a written offer, valid for at least three years, to provide the source on request.

   phpMyAdmin is written in PHP, so its "source" is the shipped files, but linking to the official source release keeps things clear. Pointing only at the upstream project's website isn't the safe option for binaries you distribute yourself; ship or offer the source.
4. **MPL 2.0 (CA bundle):** keep the file's license header (it's in the file), and make it clear where the file came from.
5. **Don't bundle the Visual C++ Redistributable** unless you've checked Microsoft's redistribution terms. Linking users to Microsoft's download (as ServNook does) avoids the question.

Bundling these programs together doesn't make ServNook itself subject to the GPL. ServNook runs MariaDB and phpMyAdmin as separate programs, which the GPL treats as "mere aggregation", not as a combined work.

## Trademarks

"Apache", "PHP", "MariaDB", "phpMyAdmin", "Node.js" and "XAMPP" are trademarks of their respective owners. Using the names to say what ServNook contains or works with is fine ("includes Apache and PHP", "an XAMPP alternative"). Don't use them in a way that suggests endorsement, and don't use their logos as ServNook's branding. Naming the project "XAMPP-something" or using the XAMPP logo is not OK.

## Contributions

By contributing to ServNook, contributors agree their contributions are licensed under the MIT License (stated in [CONTRIBUTING.md](../CONTRIBUTING.md)). No CLA is required.
