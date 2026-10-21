#!/usr/bin/env bash
# Database integration test against a real MariaDB server (Linux + Mono).
# Exercises whut's real start/stop, crash durability, backup, restore,
# rebuild-from-backup and XAMPP import code paths. Linux MariaDB binaries
# stand in for the Windows .exe files via symlinks.
#
# Needs: apt install mariadb-server mariadb-client mono-devel; ./build/build.sh first.
set -uo pipefail
cd "$(dirname "$0")/.."
W="${TMPDIR:-/tmp}/webhut-int-$$"
PORT=3311
pass=0; fail=0
ok()   { echo "  ok   $1"; pass=$((pass+1)); }
bad()  { echo "  FAIL $1"; fail=$((fail+1)); }
check(){ if eval "$2"; then ok "$1"; else bad "$1"; fi; }
q()    { mariadb --protocol=TCP -h 127.0.0.1 -P "$PORT" -u root -N -e "$1" 2>/dev/null; }
KLOG="${TMPDIR:-/tmp}/webhut-int-$$.log"; : > "$KLOG"
kctl() { echo "### whut $*" >> "$KLOG"; (cd "$W" && mono whut.exe "$@") 2>&1 | grep -v max_open_files | tee -a "$KLOG"; return "${PIPESTATUS[0]}"; }
cleanup() { for p in $(pgrep mariadbd); do grep -q "$W" /proc/$p/cmdline 2>/dev/null && kill -9 $p; done; sleep 1; rm -rf "$W" "$KLOG"; }
trap cleanup EXIT

mkdir -p "$W/mariadb/bin" "$W/config/mariadb" "$W/data"
cp dist/whut.exe dist/whut.exe.config "$W/"
for n in mariadbd mariadb mariadb-dump mariadb-admin; do
  ln -s "$(command -v $n || echo /usr/sbin/$n)" "$W/mariadb/bin/$n.exe"
done
# Windows' mariadb-install-db gives root password login over TCP; Linux's defaults to socket auth.
printf '#!/bin/sh\nexec mariadb-install-db --auth-root-authentication-method=normal --user=root "$@"\n' > "$W/mariadb/bin/mariadb-install-db.exe"
chmod +x "$W/mariadb/bin/mariadb-install-db.exe"
# WebHut's real my.ini, plus user=root because this sandbox runs as root
# (Linux-only lines: run as root here, and a socket path; Windows uses neither)
sed "s|^\[mysqld\]\$|[mysqld]\nuser = root\nsocket = $W/k.sock|" overlay/config/mariadb/my.ini > "$W/config/mariadb/my.ini"
printf '{ "mysqlPort": %s, "autoBackup": true, "backupKeep": 10 }\n' "$PORT" > "$W/webhut.json"
mariadb-install-db --datadir="$W/data/mariadb" --auth-root-authentication-method=normal --user=root >/dev/null 2>&1

echo "== start / stop"
kctl start mariadb >/dev/null
check "whut starts MariaDB with WebHut's my.ini" '[ "$(q "select 1")" = "1" ]'
check "crash-safe setting active (innodb_flush_log_at_trx_commit=1)" '[ "$(q "select @@innodb_flush_log_at_trx_commit")" = "1" ]'
q "create database shop; create table shop.orders (id int primary key, note varchar(50)) engine=innodb;"
q "use shop; insert into orders select seq, concat('order ', seq) from seq_1_to_100;"

echo "== backup"
kctl backup >/dev/null
check "backup file created" 'ls "$W"/data/backups/mariadb-*.sql.gz >/dev/null 2>&1'
check "backup contains the shop database" 'zcat "$W"/data/backups/mariadb-*.sql.gz | grep -q "CREATE DATABASE.*shop"'

echo "== power cut (kill -9) right after committed writes"
q "use shop; insert into orders select seq, 'late' from seq_101_to_150;"
kill -9 "$(cat "$W/tmp/pids/mariadb.pid")"; sleep 1
kctl start mariadb >/dev/null
check "restarts after a hard kill (automatic crash recovery)" '[ "$(q "select 1")" = "1" ]'
check "no committed rows lost (150 of 150)" '[ "$(q "select count(*) from shop.orders")" = "150" ]'

echo "== restore"
q "drop table shop.orders;"
kctl restore latest >/dev/null
check "restore brings back the dropped table (100 rows at backup time)" '[ "$(q "select count(*) from shop.orders")" = "100" ]'

echo "== damaged data folder -> rebuild from backup"
# Make the existing backups look a day old, so the next clean stop is due for one
for f in "$W"/data/backups/mariadb-*.sql.gz; do mv "$f" "$(echo "$f" | sed -E 's/mariadb-[0-9]{8}-/mariadb-20200101-/')"; done
before=$(ls "$W"/data/backups/*.sql.gz | wc -l)
kctl stop mariadb >/dev/null
check "clean stop takes an automatic backup first" '[ "$(ls "$W"/data/backups/*.sql.gz | wc -l)" -gt "$before" ]'
for f in "$W"/data/mariadb/ib_logfile0 "$W"/data/mariadb/ibdata1; do head -c 1048576 /dev/urandom | dd of="$f" bs=1M seek=0 conv=notrunc status=none; done
kctl start mariadb >/dev/null 2>&1
check "MariaDB refuses to start on damaged files" '[ -z "$(q "select 1")" ]'
kctl rebuild-db latest >/dev/null 2>&1
check "rebuild-db starts MariaDB again" '[ "$(q "select 1")" = "1" ]'
check "rebuilt database has the data from the backup" '[ "$(q "select count(*) from shop.orders")" = "100" ]'
check "damaged folder kept aside, not deleted" 'ls -d "$W"/data/mariadb-damaged-* >/dev/null 2>&1'

echo "== import from XAMPP"
X="$W/xampp"
mkdir -p "$X/mysql/bin" "$X/htdocs/legacy-shop" "$X/htdocs/dashboard"
ln -s "$(command -v mariadbd || echo /usr/sbin/mariadbd)" "$X/mysql/bin/mysqld.exe"
printf '[mysqld]\nuser=root\nport=3306\nsocket=%s/xi.sock\n' "$W" > "$X/mysql/bin/my.ini"
echo '<?php echo "legacy";' > "$X/htdocs/legacy-shop/index.php"
mariadb-install-db --datadir="$X/mysql/data" --user=root >/dev/null 2>&1   # XAMPP-like: root has its own auth
/usr/sbin/mariadbd --no-defaults --user=root --datadir="$X/mysql/data" --port=3399 --bind-address=127.0.0.1 --socket="$W/x.sock" --pid-file="$W/x.pid" >/dev/null 2>&1 &
sleep 4
mariadb --protocol=TCP -h 127.0.0.1 -P 3399 -u root -e "create database legacy_shop; create table legacy_shop.products (id int primary key, name varchar(40)); insert into legacy_shop.products values (1,'tea'),(2,'coffee'); create database phpmyadmin;" 2>/dev/null \
  || mariadb --socket="$W/x.sock" -u root -e "create database legacy_shop; create table legacy_shop.products (id int primary key, name varchar(40)); insert into legacy_shop.products values (1,'tea'),(2,'coffee'); create database phpmyadmin;"
mariadb-admin --socket="$W/x.sock" -u root shutdown 2>/dev/null; sleep 2
kctl import-xampp "$X" --projects > "$W/import.log" 2>&1
check "XAMPP database copied into WebHut" '[ "$(q "select group_concat(name order by id) from legacy_shop.products")" = "tea,coffee" ]'
check "XAMPP's phpmyadmin settings db not copied" '[ -z "$(q "show databases like '"'"'phpmyadmin'"'"'")" ]'
check "htdocs project copied to www, demo folders skipped" '[ -f "$W/www/legacy-shop/index.php" ] && [ ! -d "$W/www/dashboard" ]'
check "XAMPP's own data untouched (still has legacy_shop)" '[ -d "$X/mysql/data/legacy_shop" ]'
check "temporary XAMPP server stopped afterwards" '! pgrep -f "mysqld.exe.*$X" >/dev/null'

kctl stop mariadb >/dev/null
echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ] || { echo "--- whut output ---"; cat "$KLOG"; }
[ "$fail" -eq 0 ]
