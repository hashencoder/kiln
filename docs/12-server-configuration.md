# Server configuration (Apache and MariaDB)

The **server configuration manager** lets you change common Apache and MariaDB settings without editing files by hand. You can still edit the files yourself, and the manager keeps your edits.

To open it, click the gear button on the Apache or MariaDB row of the dashboard. It opens on that service's section. You can also use **Databases › ⋯ › MariaDB configuration**. The **Apache / MariaDB** switch at the top of the sidebar changes service.

| Apache | MariaDB |
|---|---|
| General: port, server name, document root, directory listing | General: port, bind address, data folder, character set and collation, backup & restore |
| PHP Integration: PHP version, mod_php / mod_fcgid, php.ini, phpinfo(), runtime definitions | Performance: maximum connections, maximum packet |
| Performance: ThreadsPerChild, MaxConnectionsPerChild, keep-alive, compression | InnoDB: buffer pool (with presets), redo log size |
| Modules: every module in `apache\modules`, searchable, with its real loaded state | Reliability & Recovery: flush at commit, doublewrite, file per table, Aria recovery |
| Security: network access, listening address, `.htaccess`, dotfile protection, vhost check | Security: root password, test connection, anonymous accounts, authentication plugins |
| Logging: log level, log files, live viewer with error filter | Logging & Diagnostics: error log viewer, slow query log and viewer, live server figures |
| Virtual Hosts: generated and custom files, new custom vhost | Advanced: my.ini, validation, effective variables, where settings come from, defaults |
| Advanced: syntax test, change preview, startup command, restore defaults | |

## Who owns each setting

Every row is labelled with the place its value lives. For each setting there is exactly one control, and it writes to exactly one place.

| Setting | Owner | Stored in |
|---|---|---|
| Apache HTTP port, network access | WebHut settings | `webhut.json` (also shown in Settings) |
| PHP version | WebHut runtime manager | `webhut.json` |
| PHP mode, threads, keep-alive, log level, listing, `.htaccess`, modules | WebHut's Apache config | `config\apache\httpd.conf` |
| Generated virtual hosts | WebHut site manager | `config\apache\generated\` (rewritten on every start) |
| Custom virtual hosts and directives | You | `config\apache\custom\*.conf` (never touched) |
| MariaDB port | WebHut settings | `webhut.json`, passed as `--port` |
| MariaDB bind address | WebHut security | always `--bind-address=127.0.0.1` |
| MariaDB data folder, error log, PID file | WebHut runtime | `--datadir`, `--log-error`, `--pid-file` |
| Buffer pool, connections, durability, slow log, character set | MariaDB configuration | `config\mariadb\my.ini` `[mysqld]` |
| Client character set | MariaDB configuration | `my.ini` `[client]` |
| Anything else in my.ini | You | kept as written |

### Precedence in MariaDB

MariaDB uses the first of these that sets a value:

1. WebHut's startup arguments.
2. Changes made while the server runs (`SET GLOBAL`), until the next restart.
3. `my.ini`, where the last occurrence in any server group wins (`[mysqld]`, `[server]`, `[mariadb]`, `[mariadb-11.4]` …).
4. MariaDB's built-in defaults.

Each MariaDB row shows **my.ini**, **running** and **next start** values. These come from the server itself, and a row turns amber when the values disagree.

If `my.ini` sets an option that a startup argument overrides (`port`, `bind-address`, `datadir`, `log-error`, `pid-file`), **Advanced** explains why it has no effect and offers to comment the line out.

A value set in a group read after `[mysqld]` can't be edited from this window. The row shows it read-only, and you edit `my.ini` yourself.

## How Save works

1. **Field checks.** Values are checked against the ranges the installed MariaDB reports (`information_schema.SYSTEM_VARIABLES`). Collations must belong to the chosen character set. Ports must be free. Required Apache modules can't be turned off, and dependencies are added or enforced (for example `deflate_module` needs `filter_module`).
2. **Candidate check, before anything is written:**
   - **Apache:** the new `httpd.conf` is tested with `httpd -t`. It uses the same `httpd.exe`, ServerRoot, `-C "Define …"` runtime definitions and environment as a normal start.
   - **MariaDB:** the new `my.ini` is read by `mariadbd --help --verbose`, which rejects unknown options and bad values without starting a server.
   - If the change needs a restart, a throw-away MariaDB is started with the new `my.ini`. It uses an empty scratch folder in `tmp\config-check` and a free port, and is stopped as soon as it reports ready. This catches problems option parsing can't, such as memory that can't be allocated. The real data folder is never opened, and no second server touches it.
3. **Backup.** The files about to change are copied to `config\backups\<time>\`, with a note of what changed. The newest 30 sets are kept.
4. **Write.** Each file is written next to the original and copied over it, so it is never missing. `my.ini` keeps its comments, unknown options, order, encoding (UTF-8 with or without BOM, or ANSI) and line endings.
5. **Apply:**
   - **MariaDB settings the server can change while running** (according to the server itself) are applied with `SET GLOBAL` and read back. The result is *Applied immediately*.
   - **Other settings** restart MariaDB only. A database backup is taken first if automatic backups are on and the last one is older than 15 minutes.
   - **Apache changes** restart Apache only.
   - **A MariaDB port change** restarts MariaDB and also Apache, because Apache passes the port to PHP and phpMyAdmin.
   - **A stopped service** isn't started. The change is saved and validated for its next start.
6. **Verify:**
   - **MariaDB:** it must answer queries and still list every database it had before. Its live values must match what you chose (sizes may be rounded; that is reported, not treated as an error).
   - **Apache:** it must answer HTTP.
7. **Roll back** if applying, restarting or verifying fails:
   - The previous files and settings are restored and the service is started again.
   - The footer shows *Failed — previous configuration restored*, with the technical details and the error-log tail.
   - If even the restore can't bring the service back, the footer says so and names the backup folder.

Success is reported only after verification, never just because a file was saved.

## Version differences handled

- **Dynamic vs restart:** whether a variable can change while the server runs comes from the installed server. For example, `innodb_log_file_size` resizes online from MariaDB 10.11 but needs a restart before that; `innodb_doublewrite` needs a restart.
- **Supported or not:** a setting the installed version doesn't know is shown as *not supported* and can't be edited.
- **Deprecation warnings** that `mariadbd` prints for your `my.ini` are shown after saving.
- **ThreadsPerChild** applies to Windows' `mpm_winnt`. Other MPMs are reported as not using it.

## Root password

The root password is never written to `my.ini` or the logs. WebHut keeps it in `webhut.json` encrypted with Windows DPAPI for your Windows account (`mysqlRootPasswordProtected`). A plain-text password from an older version is encrypted the first time this version starts.

A `webhut.json` copied to another PC or Windows account can't decrypt the password. WebHut says so; enter the password again under MariaDB › Security.

Changing the password uses `ALTER USER` for `root@localhost`, `root@127.0.0.1` and `root@::1`. The SQL is sent on the client's standard input, not on its command line.

## Things the manager deliberately doesn't do

- Move or re-initialise the data folder. It is never moved, deleted or created over an existing one.
- Expose MariaDB to the network. Remote database access needs firewall rules, a separate user and strong authentication. Apache's network access never changes MariaDB.
- Reset the root password automatically.
- Edit generated vhosts (they are rewritten on every start) or custom ones (they are yours).
- Convert existing databases when you change the server character set or collation.
- Restore databases when you change configuration.

## Testing

- **`tests/run.sh`:** unit tests for the my.ini editor (round trip, sections, prefixes, comments, BOM, CRLF), the httpd.conf editor (MPM block, top-level directives, www block, modules, PHP mode, dotfile block), vhost checks and plan validation.
- **`tests/config-integration.sh`:** runs the real ConfigManager against a real MariaDB and a real Apache + PHP on Linux. It covers:
  - live and restart-only MariaDB settings;
  - rejection of unknown options and impossible values;
  - port changes;
  - a simulated failed restart with rollback;
  - persistence across restarts;
  - Apache modules, port and PHP switches;
  - generated and custom vhosts.
