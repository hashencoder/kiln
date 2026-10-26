#!/usr/bin/env bash
# Configuration manager against a real MariaDB and a real Apache + PHP (Linux stand-ins for the Windows .exe files).
# Covers: live vs restart-only MariaDB settings, validation with mariadbd and httpd -t, scratch start,
# port changes, simulated failed restarts with rollback, persistence, Apache modules, PHP switch, vhosts.
#
# Needs: apt install mariadb-server apache2 libapache2-mod-php mono-devel; ./build/build.sh first. Run as root.
set -uo pipefail
cd "$(dirname "$0")/.."
W="${TMPDIR:-/tmp}/servnook-cfg-$$"
cleanup() {
  for p in $(pgrep mariadbd) $(pgrep apache2) $(pgrep -x httpd); do grep -q "$W" /proc/$p/cmdline 2>/dev/null && kill -9 $p 2>/dev/null; done
  sleep 1; rm -rf "$W"
}
trap cleanup EXIT

mkdir -p "$W/mariadb/bin" "$W/config/mariadb" "$W/data" "$W/apache/bin" "$W/apache/conf" "$W/php/8.5" "$W/php/8.4" "$W/www/blog/public" "$W/www/emptydir" "$W/www/mine" "$W/config/apache/custom"
cp dist/snook.exe dist/snook.exe.config "$W/"
mcs -langversion:7.2 -target:exe -out:"$W/ConfigInt.exe" -r:"$W/snook.exe" tests/ConfigInt.cs

# MariaDB (see integration.sh)
for n in mariadbd mariadb mariadb-dump mariadb-admin; do ln -s "$(command -v $n || echo /usr/sbin/$n)" "$W/mariadb/bin/$n.exe"; done
sed "s|^\[mysqld\]\$|[mysqld]\nuser = root\nsocket = $W/k.sock|" overlay/config/mariadb/my.ini > "$W/config/mariadb/my.ini"
mariadb-install-db --datadir="$W/data/mariadb" --auth-root-authentication-method=normal --user=root >/dev/null 2>&1

# Apache + PHP (see apache-smoke.sh)
PHPSO=$(ls /usr/lib/apache2/modules/libphp*.so | head -1)
ln -s /usr/lib/apache2/modules "$W/apache/modules"
cp /etc/mime.types "$W/apache/conf/mime.types"
for v in 8.5 8.4; do ln -s "$PHPSO" "$W/php/$v/php8apache2_4.dll"; touch "$W/php/$v/php.exe"; echo "; php $v" > "$W/php/$v/php.ini"; done
# A copy of the real binary named "httpd", so the process is called httpd as on Windows. setsid: Apache's
# graceful stop signals its whole process group, which must not include the test driver.
cp /usr/sbin/apache2 "$W/apache/bin/httpd"
cat > "$W/apache/bin/httpd.exe" <<EOF
#!/bin/sh
exec setsid $W/apache/bin/httpd -C "LoadModule mpm_prefork_module /usr/lib/apache2/modules/mod_mpm_prefork.so" -C "User www-data" -C "Group www-data" -C "Mutex file:$W/tmp default" "\$@" -DFOREGROUND
EOF
chmod +x "$W/apache/bin/httpd.exe"
echo '<?php echo "blog-public";' > "$W/www/blog/public/index.php"
echo '<?php echo "PHP_DIR=" . php_ini_loaded_file();' > "$W/www/v.php"
echo 'mine-ok' > "$W/www/mine/index.html"
printf '<VirtualHost *:${SERVNOOK_HTTP_PORT}>\n    ServerName mine.localhost\n    DocumentRoot "${SERVNOOK_ROOT}/www/mine"\n    <Directory "${SERVNOOK_ROOT}/www/mine">\n        Require local\n    </Directory>\n</VirtualHost>\n' > "$W/config/apache/custom/mine.conf"
chmod -R o+rX "$W"
printf '{ "php": "8.5", "httpPort": 8091, "mysqlPort": 3321, "autoBackup": false }\n' > "$W/servnook.json"
mkdir -p "$W/tmp"

cd "$W" && mono ConfigInt.exe mariadb apache
