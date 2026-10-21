#!/usr/bin/env bash
# Builds WebHutPanel.exe (GUI) and webhut.exe (console) for Windows.
#
# Works on Linux/macOS with Mono's C# compiler (mcs) and .NET Framework 4.8
# reference assemblies (Debian/Ubuntu: apt install mono-devel). On Windows,
# use build.ps1 instead (it uses the csc.exe that ships with Windows).
#
# Output: dist/WebHutPanel.exe, dist/webhut.exe
set -euo pipefail
cd "$(dirname "$0")/.."

REF="${MONO_REF:-/usr/lib/mono/4.8-api}"
OUT=dist
mkdir -p "$OUT" build/obj

# Icon (assets/webhut.ico is committed; regenerate with: python3 build/make_icon.py assets/webhut.ico assets/webhut-512.png)
ICON=assets/webhut.ico

# Embed every file in overlay/ as a resource named overlay/<relative path>
RES=()
while IFS= read -r -d '' f; do
  rel="${f#./}"
  RES+=("-resource:${rel},${rel}")
done < <(cd . && find overlay -type f -print0 | sort -z)

REFS=(mscorlib System System.Core System.Drawing System.Windows.Forms System.Web.Extensions
      System.IO.Compression System.IO.Compression.FileSystem System.Net.Http)
REFARGS=(-nostdlib -noconfig -lib:"$REF" -lib:"$REF/Facades")
for r in "${REFS[@]}"; do REFARGS+=(-r:"$r.dll"); done

COMMON=(-langversion:7.2 -optimize+ -debug- -platform:anycpu -warn:4 -nowarn:1591 "${REFARGS[@]}")

mcs "${COMMON[@]}" -target:winexe -out:"$OUT/WebHutPanel.exe" -win32icon:"$ICON" \
    -main:WebHut.Program "${RES[@]}" src/Core/*.cs src/Gui/*.cs

mcs "${COMMON[@]}" -target:exe -out:"$OUT/webhut.exe" -win32icon:"$ICON" \
    -main:WebHut.CliProgram "${RES[@]}" src/Core/*.cs src/Cli/*.cs

cp build/WebHutPanel.exe.config "$OUT/WebHutPanel.exe.config"
cp build/WebHutPanel.exe.config "$OUT/webhut.exe.config"
ls -la "$OUT"
