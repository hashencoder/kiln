#!/usr/bin/env bash
# Runs Kiln's logic tests under Mono in a throwaway folder.
set -euo pipefail
cd "$(dirname "$0")/.."
WORK="${TMPDIR:-/tmp}/kiln-test-$$"
mkdir -p "$WORK/served"
cp dist/kilnctl.exe "$WORK/"
mcs -langversion:7.2 -target:exe -out:"$WORK/Tests.exe" -r:"$WORK/kilnctl.exe" \
    -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll tests/Tests.cs

# A fake PHP zip served over HTTP for the download tests
( cd "$WORK/served" && mkdir p && echo x > p/php.exe && mkdir p/ext && echo x > p/ext/php_curl.dll \
  && (cd p && python3 -c "import shutil; shutil.make_archive('../payload','zip','.')") \
  && sha256sum payload.zip | cut -d' ' -f1 > payload.zip.sha )
PORT=$(python3 -c "import socket; s=socket.socket(); s.bind(('127.0.0.1',0)); print(s.getsockname()[1])")
( cd "$WORK/served" && python3 -m http.server "$PORT" --bind 127.0.0.1 >/dev/null 2>&1 ) &
SERVER=$!
trap 'kill $SERVER 2>/dev/null; rm -rf "$WORK"' EXIT
sleep 1

echo "== kilnctl smoke test"
( cd "$WORK" && mono kilnctl.exe version && mono kilnctl.exe help | head -3 )
echo "== logic tests"
NO_PROXY=127.0.0.1 no_proxy=127.0.0.1 mono "$WORK/Tests.exe" "$PORT"
