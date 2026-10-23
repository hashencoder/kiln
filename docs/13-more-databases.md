# 13 – MySQL, PostgreSQL and MongoDB

MariaDB is WebHut's default database. You can add **MySQL**, **PostgreSQL** and **MongoDB** next to it, plus **Adminer**, a small web page for browsing MySQL, PostgreSQL and MariaDB databases. None of them is installed unless you choose it.

| Engine | Version WebHut installs | Port | User | Download | From |
|---|---|---|---|---|---|
| MySQL | newest **8.4 LTS** release | **3307** (MariaDB keeps 3306) | `root`, no password | a few hundred MB | Oracle's CDN (cdn.mysql.com) |
| PostgreSQL | newest supported release | 5432 | `postgres`, no password | about 330 MB (includes pgAdmin's files) | Windows binaries from get.enterprisedb.com, the builds the PostgreSQL project links to |
| MongoDB | newest production Community Server | 27017 | no authentication | about 950 MB | fastdl.mongodb.org (SHA-256 checked) |
| mongosh | latest MongoDB Shell | – | – | about 60 MB | GitHub releases of mongodb-js/mongosh (SHA-256 checked) |
| Adminer | latest | – | – | under 1 MB | GitHub releases of vrana/adminer (SHA-256 checked), at `http://localhost/adminer` |

Like everything in WebHut, they **listen on 127.0.0.1 only**: other devices can't reach them, even when network access is on in Settings. The passwordless defaults are meant for a local development PC; set passwords if other people use your PC (see below).

## Adding an engine

- **In the setup window** (first run): tick MySQL, PostgreSQL, MongoDB, mongosh or Adminer. They start unticked.
- **Later**: **Databases → More database engines → Add**. Adding MongoDB also adds mongosh; adding MySQL or PostgreSQL also adds Adminer.
- **From the command line**:

  ```
  whut install mysql postgresql mongodb adminer
  ```

The download goes into `tmp\downloads`, the program into its own folder (`mysql\`, `postgresql\`, `mongodb\`, `mongosh\`, `apps\adminer\`), and its data into `data\mysql`, `data\postgresql` or `data\mongodb`. The data folder is created on the first start and never deleted by WebHut.

Downloads are checked before they are installed. MongoDB, mongosh and Adminer publish SHA-256 checksums, and WebHut verifies them. Oracle (MySQL) and EnterpriseDB (PostgreSQL) don't publish machine-readable checksums, so for those WebHut uses HTTPS from the official host and checks that the package contains the expected programs.

## Starting and stopping

Each engine you add gets a row on the **Dashboard** with Start/Stop, its log and a button that opens its data folder. **Start all** starts them too. The command line works the same way:

```
whut start postgresql
whut stop mongodb
whut status
```

`whut start mysql` means MariaDB until MySQL is installed, then MySQL. Use `whut start mariadb` for MariaDB.

When Windows shuts down, WebHut stops MariaDB, MySQL and PostgreSQL cleanly first, just like MariaDB on its own.

| Engine | First start | Clean stop |
|---|---|---|
| MySQL | `mysqld --initialize-insecure` creates `data\mysql` with a passwordless `root` | `mysqladmin shutdown` |
| PostgreSQL | `initdb -U postgres -A trust -E UTF8` creates `data\postgresql` | `pg_ctl stop -m fast` |
| MongoDB | creates `data\mongodb` | `db.shutdownServer()` through mongosh (without mongosh the process is ended; MongoDB's journal makes that safe) |

## Connecting

From your code:

| Engine | Connection |
|---|---|
| MySQL | host `127.0.0.1`, port `3307`, user `root`, empty password. Laravel: `DB_CONNECTION=mysql`, `DB_PORT=3307` |
| PostgreSQL | host `127.0.0.1`, port `5432`, user `postgres`, empty password. Laravel: `DB_CONNECTION=pgsql`. PHP's `pgsql` and `pdo_pgsql` drivers are switched on in every installed PHP when you add PostgreSQL or Adminer |
| MongoDB | `mongodb://127.0.0.1:27017`. PHP needs the `mongodb` extension from PECL, which WebHut doesn't install; Node apps use the `mongodb` npm package as usual |

From WebHut's **Shell**, `psql`, `mongosh` and the MySQL client are on PATH. The `mysql` command is MariaDB's client (connect to MySQL with `mysql -P 3307 -u root`); MySQL's own client is `mysql\bin\mysql.exe`. `PGHOST`, `PGPORT` and `PGUSER` are set, so plain `psql` connects to WebHut's PostgreSQL.

In the browser, **Adminer** at `http://localhost/adminer` opens MySQL, PostgreSQL and MariaDB. The **Open** button on a MySQL or PostgreSQL row opens Adminer with the server filled in; click **Login** (the password stays empty). phpMyAdmin stays the tool for MariaDB.

Desktop tools are linked from the Databases page rather than bundled: **pgAdmin** for PostgreSQL and **MongoDB Compass** for MongoDB.

## Ports

**Settings → Services → Ports** shows a port for each engine you added. WebHut won't let two servers share a port, and saving a change restarts the engine if it's running. Update your apps' connection settings afterwards.

## Passwords

The engines start without passwords, like XAMPP's MySQL, because they only accept connections from this PC. To set one:

- **MySQL**: `mysql -P 3307 -u root -e "ALTER USER 'root'@'localhost' IDENTIFIED BY 'new-password';"`. WebHut then can't run `mysqladmin shutdown` without the password, so **Stop** ends the process instead; InnoDB recovers on the next start.
- **PostgreSQL**: `psql -c "ALTER USER postgres PASSWORD 'new-password';"`, then change `trust` to `scram-sha-256` in `data\postgresql\pg_hba.conf` and restart.
- **MongoDB**: create an admin user in mongosh and add `--auth` yourself. WebHut starts MongoDB without authentication.

Adminer is set up to accept an empty password because Apache only serves `/adminer` to this PC. If you set passwords, Adminer asks for them as normal.

## What isn't covered yet

- **Backups**: WebHut's automatic backups, restore and "rebuild after a crash" cover MariaDB only. For the others use `mysqldump -P 3307`, `pg_dump` and `mongodump` (MongoDB Database Tools, a separate download).
- **Component updates**: the update manager doesn't update these engines yet. To move to a newer release, stop the engine, delete its program folder (not `data\…`) and add it again. The data folder is reused (within the same major version for PostgreSQL and MySQL; a major PostgreSQL upgrade needs `pg_upgrade` or a dump and restore).
- **Config files**: the engines run with their defaults plus the options above. Server settings for MySQL, PostgreSQL and MongoDB aren't in WebHut's configuration manager yet.

## Removing an engine

Stop it, then delete its program folder (`mysql\`, `postgresql\`, `mongodb\`, `mongosh\` or `apps\adminer\`). Its row disappears from the Dashboard. The data folder stays until you delete it yourself.
