# 11 – Databases: crash safety, backups, and moving from XAMPP

A dead battery or a forced shutdown shouldn't cost you your databases. This page explains what WebHut does to prevent that, how backups work, and how to bring your databases over from XAMPP.

## Why XAMPP's MySQL "crashes" after a shutdown

The classic XAMPP message, "MySQL shutdown unexpectedly", usually appears after the laptop lost power or Windows shut down while MySQL was running. The server never got the chance to close its files cleanly. On the next start it has to recover, and with XAMPP's older MariaDB version and settings that recovery sometimes fails, typically on the internal Aria system tables or the InnoDB log. The usual forum fixes involve deleting files by hand and hoping.

## What WebHut does differently

WebHut uses four layers of protection, in this order:

| Layer | What it does | Protects against |
|---|---|---|
| **1. Durable settings** | `innodb_flush_log_at_trx_commit = 1` (every committed change is on disk before MariaDB says OK), `innodb_doublewrite = 1` (half-written pages are repaired), `innodb_file_per_table = 1`, `aria_recover_options = BACKUP,QUICK` (system tables repair themselves). Plus a current MariaDB LTS, which recovers far more reliably than XAMPP's 10.4. | Power cuts and hard kills. In our tests, killing MariaDB mid-work lost **zero** committed rows and it restarted on its own. |
| **2. Clean stop on Windows shutdown** | When Windows shuts down or you log off while the WebHut panel is open (or in the tray), WebHut stops MariaDB properly. Windows shows "WebHut is stopping the database safely…" for the second or two this takes. | Regular shutdowns and restarts. No recovery is needed on the next start at all. |
| **3. Automatic backups** | Every 2 hours while MariaDB runs, and whenever you stop it (if the last backup is more than 15 minutes old). The newest 10 are kept in `data\backups\`. | Anything layers 1 and 2 can't handle, plus your own mistakes (a dropped table, a bad migration) |
| **4. Rebuild from backup** | If MariaDB can't start, WebHut offers to set the damaged folder aside and rebuild from the newest backup in one click | The rare case where the data files really are damaged |

Layer 2 works only while WebHut is running. If you start the servers and then **Exit** WebHut choosing "leave them running", Windows ends MariaDB abruptly at shutdown. Layers 1, 3 and 4 still protect you. Leaving WebHut in the tray (the default when you close the window) is the safest setup.

What none of this can do is protect against a dying disk. Copy `data\backups\` somewhere else (cloud storage, an external drive) if the data matters.

## Backups

Open **Database tools** in the panel.

| Control | |
|---|---|
| **Back up automatically every N hours, keep the newest N** | On by default: every 2 hours, keep 10. Settings are saved to `webhut.json` (`autoBackup`, `backupEveryHours`, `backupKeep`). |
| **Back up now** | Starts MariaDB if needed and backs up immediately |
| **Restore selected…** | Loads the selected backup into the running server. Tables in the backup replace current tables of the same name; other databases and tables are left alone. WebHut backs up the current state first. |
| **Rebuild from backup…** | For a MariaDB that won't start: stops it, moves `data\mariadb` to `data\mariadb-damaged-<date>`, creates a fresh one, and loads the selected backup. **Nothing is deleted.** Changes made after that backup aren't included. |
| **Open folder** | `data\backups\` |

What's in a backup:

- Each file is `mariadb-YYYYMMDD-HHMMSS.sql.gz`: a compressed, plain SQL dump made with `mariadb-dump --single-transaction --routines --events --triggers` of **your** databases (everything except MariaDB's own `mysql`, `sys`, `information_schema` and `performance_schema`).
- Because MariaDB's own user tables aren't included, restoring a backup never changes your root password or other users.
- The files are standard. You can restore them anywhere: `gunzip -c file.sql.gz | mariadb -u root`, or import them in phpMyAdmin (it accepts `.sql.gz`).

When MariaDB fails to start, WebHut asks right away whether to rebuild from the newest backup. You can also do it any time from **Database tools → Rebuild from backup…**, or with `whut rebuild-db`.

## Moving from XAMPP

**Database tools → Import from XAMPP…** copies your XAMPP databases, and optionally your `htdocs` projects, into WebHut. XAMPP isn't changed: nothing is moved or deleted, so you can keep using it until you're happy with WebHut.

1. **Stop MySQL in the XAMPP Control Panel.** WebHut needs XAMPP's data files to itself for a moment.
2. Open **Import from XAMPP…**. WebHut finds `C:\xampp` (or any `X:\xampp`) automatically; use **Browse…** for another location.
3. Click **Scan**. WebHut lists XAMPP's databases and `htdocs` projects.
4. Tick what you want and click **Import**.

How it works:

- WebHut starts **XAMPP's own** MySQL temporarily, on a private port, with `--skip-grant-tables`, so XAMPP's root password doesn't matter. It then dumps the selected databases with `mariadb-dump`, stops XAMPP's server again, and loads the dump into WebHut's MariaDB.
- This is a **logical copy** (SQL), not a file copy. That's what makes it safe across versions: XAMPP ships MariaDB 10.4, WebHut runs 12.x, and copying data folders between those versions directly is a common way to corrupt them.
- Before loading anything, WebHut backs up its own current databases. If a database with the same name already exists in WebHut, its tables are replaced by XAMPP's, and you're warned first.
- XAMPP's internal `phpmyadmin` database isn't copied. The empty `test` database is unticked by default in the import window (`whut` imports it too).
- `htdocs` projects are copied to `www\<name>`. A project that already exists in `www\` is skipped, never overwritten. XAMPP's own folders (`dashboard`, `img`, `webalizer`, `xampp`) are skipped. After importing, click **Refresh sites** to open them at `http://<name>.localhost`.

### When XAMPP's MySQL is the one that crashed

This is exactly the situation that drives people away from XAMPP, so WebHut handles it:

1. Tick **XAMPP's MySQL is damaged (won't start)** before scanning.
2. WebHut first tries a normal start, then InnoDB recovery modes 1, 2 and 3 in turn. These modes let a damaged server start just enough to read its data, without modifying the data files.
3. If one works, you'll see which mode was needed. Import as usual. The very latest changes from before the crash may be missing.

If even recovery mode 3 fails, the files are badly damaged. Higher recovery levels (4–6) can lose data permanently, so WebHut won't try them automatically. The [MariaDB documentation on InnoDB recovery modes](https://mariadb.com/kb/en/innodb-recovery-modes/) explains the manual options. Make a copy of `C:\xampp\mysql\data` before experimenting.

### After importing

- Apps that connected as `root` with no password work unchanged (WebHut's default is the same).
- Apps that used a **custom MySQL user** from XAMPP need that user created in WebHut, because users aren't copied. In phpMyAdmin go to **User accounts → Add user account**, or run:

```sql
CREATE USER 'shop'@'localhost' IDENTIFIED BY 'secret';
GRANT ALL ON shop.* TO 'shop'@'localhost';
```

- Hostnames change from `http://localhost/myproject` to `http://myproject.localhost`. If an app stores its own URL (WordPress `siteurl` and `home`, Laravel `APP_URL`), update it.

### From the command line

```text
whut import-xampp                        # auto-detect C:\xampp, databases only
whut import-xampp D:\xampp --projects    # also copy htdocs projects
whut import-xampp --recover              # XAMPP's MySQL is damaged
whut backup | backups | restore [file|latest] | rebuild-db [file|latest]
```
