#!/usr/bin/env bash
# Runs ServNook's real Apache config (httpd.conf, default vhost, generated site
# and Node-proxy vhosts, mod_php) on a real Apache + PHP (Linux), started by
# snook, and checks what browsers would get: PHP pages, *.localhost vhosts,
# public/ detection, the Node reverse proxy, and the network-access rules.
#
# Needs: apt install apache2 libapache2-mod-php mono-devel; ./build/build.sh first. Run as root.
set -uo pipefail
cd "$(dirname "$0")/.."
W="${TMPDIR:-/tmp}/servnook-ap-$$"
PORT=8088
pass=0; fail=0
ok()   { echo "  ok   $1"; pass=$((pass+1)); }
bad()  { echo "  FAIL $1"; fail=$((fail+1)); }
check(){ if eval "$2"; then ok "$1"; else bad "$1"; fi; }
get()  { curl -s -m 5 -H "Host: $1" "http://127.0.0.1:$PORT$2"; }
code() { curl -s -m 5 -o /dev/null -w '%{http_code}' -H "Host: $1" "http://127.0.0.1:$PORT$2"; }
stop_apache() { [ -f "$W/tmp/pids/httpd.pid" ] && kill "$(cat "$W/tmp/pids/httpd.pid")" 2>/dev/null; sleep 2; }
cleanup() { [ -n "$KEEP_LOG" ] && cp "$W/logs/apache-error.log" "$KEEP_LOG" 2>/dev/null; stop_apache; [ -n "${NODEPID:-}" ] && kill "$NODEPID" 2>/dev/null; ip addr del 10.99.0.1/32 dev lo 2>/dev/null; rm -rf "$W"; }
trap cleanup EXIT
KEEP_LOG="${KEEP_LOG:-}"

PHPSO=$(ls /usr/lib/apache2/modules/libphp*.so | head -1)
mkdir -p "$W/apache/bin" "$W/apache/conf" "$W/php/8.5" "$W/www"
cp dist/snook.exe dist/snook.exe.config "$W/"
ln -s /usr/lib/apache2/modules "$W/apache/modules"
cp /etc/mime.types "$W/apache/conf/mime.types"
ln -s "$PHPSO" "$W/php/8.5/php8apache2_4.dll"
touch "$W/php/8.5/php.exe"
# Stand-in for httpd.exe: Linux needs an MPM module and a run-as user (Windows has mpm_winnt built in),
# and must stay in the foreground like Windows' httpd does.
cat > "$W/apache/bin/httpd.exe" <<EOF
#!/bin/sh
exec /usr/sbin/apache2 -C "LoadModule mpm_prefork_module /usr/lib/apache2/modules/mod_mpm_prefork.so" -C "User www-data" -C "Group www-data" -C "Mutex file:$W/tmp default" "\$@" -DFOREGROUND
EOF
chmod +x "$W/apache/bin/httpd.exe"
chmod o+rx "$W" "$W/www"

# Sites and a fake Node app
mkdir -p "$W/www/blog/public" "$W/www/shop" "$W/www/api"
echo '<?php echo "blog-public " . PHP_VERSION;' > "$W/www/blog/public/index.php"
echo '<?php echo "shop-root";' > "$W/www/shop/index.php"
echo 'node-app-ok' > "$W/www/api/index.html"
echo 'secret' > "$W/www/shop/.htaccess-test"; printf 'Options -Indexes\n' > "$W/www/shop/.htaccess"
chmod -R o+rX "$W/www"
printf '{"apps":[{"name":"api","dir":"www/api","command":"x","port":3456,"autostart":false,"env":{}}]}\n' > "$W/node-apps.json"
(cd "$W/www/api" && exec python3 -m http.server 3456 --bind 127.0.0.1 >/dev/null 2>&1) & NODEPID=$!

cfg() { printf '{ "php": "8.5", "httpPort": %s, "lanAccess": %s }\n' "$PORT" "$1" > "$W/servnook.json"; }

echo "== start through snook (config test + launch)"
cfg false
(cd "$W" && setsid mono snook.exe start apache) > "$W/start.log" 2>&1
check "snook starts Apache with ServNook's httpd.conf (httpd -t passed)" 'grep -q "Apache is running" "$W/start.log"'
[ "$fail" -eq 0 ] || { cat "$W/start.log"; tail -20 "$W/logs/apache-error.log" 2>/dev/null; exit 1; }

echo "== pages"
check "dashboard at http://localhost runs PHP"            'get localhost / | grep -q "is running"'
check "blog.localhost serves www/blog/public (PHP)"       'get blog.localhost / | grep -q "blog-public"'
check "shop.localhost serves www/shop (no public/)"       '[ "$(get shop.localhost /)" = "shop-root" ]'
check "http://localhost/shop/ works like XAMPP htdocs"    '[ "$(get localhost /shop/)" = "shop-root" ]'
check "unknown host falls back to the dashboard"          'get nothing.localhost / | grep -q "is running"'
check ".htaccess files are never served"                  '[ "$(code shop.localhost /.htaccess)" = "403" ]'
check "api.localhost is proxied to the Node app's port"   '[ "$(get api.localhost /)" = "node-app-ok" ]'

echo "== network access OFF (default)"
LANIP=$(hostname -I | awk '{print $1}')
check "not reachable from the network ($LANIP)"           '! curl -s -m 3 -o /dev/null "http://$LANIP:$PORT/"'
stop_apache

echo "== network access ON"
ip addr add 10.99.0.1/32 dev lo 2>/dev/null; HAVE_PRIV=$?
cfg true
(cd "$W" && setsid mono snook.exe start apache) > "$W/start2.log" 2>&1
check "starts with network access on"                    'grep -q "Apache is running" "$W/start2.log"'
check "still works locally"                              'get blog.localhost / | grep -q "blog-public"'
if [ "$HAVE_PRIV" -eq 0 ]; then
  check "a private-network client (10.99.0.1) gets the dashboard" \
        'curl -s -m 5 --interface 10.99.0.1 "http://10.99.0.1:$PORT/" | grep -q "is running"'
  check "a private-network client can open a project folder" \
        '[ "$(curl -s -m 5 --interface 10.99.0.1 "http://10.99.0.1:$PORT/shop/")" = "shop-root" ]'
  check "blog.<ip>.nip.io reaches the blog vhost" \
        'curl -s -m 5 --interface 10.99.0.1 -H "Host: blog.10.99.0.1.nip.io" "http://10.99.0.1:$PORT/" | grep -q "blog-public"'
  check "phpMyAdmin stays local-only even with network access" \
        '[ "$(curl -s -m 5 --interface 10.99.0.1 -o /dev/null -w "%{http_code}" "http://10.99.0.1:$PORT/phpmyadmin/")" = "403" ]'
else
  echo "  (skipped private-address checks: can't add a test IP here)"
fi
case "$LANIP" in 10.*|192.168.*|172.1[6-9].*|172.2[0-9].*|172.3[01].*) ;; *)
  check "a public/non-private address is refused (403)" '[ "$(curl -s -m 5 -o /dev/null -w "%{http_code}" "http://$LANIP:$PORT/")" = "403" ]';;
esac
check "dashboard lists the network address"              'get localhost / | grep -q "On your network"'
stop_apache

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ] || { echo "--- logs ---"; cat "$W/start.log" "$W/start2.log"; tail -20 "$W/logs/apache-error.log"; }
[ "$fail" -eq 0 ]
