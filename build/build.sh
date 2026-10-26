#!/usr/bin/env bash
# Builds ServNook.exe (GUI) and snook.exe (console) for Windows.
#
# Works on Linux/macOS with Mono's C# compiler (mcs) and .NET Framework 4.8
# reference assemblies (Debian/Ubuntu: apt install mono-devel). On Windows,
# use build.ps1 instead (it uses the csc.exe that ships with Windows).
#
# Output: dist/ServNook.exe, dist/snook.exe
set -euo pipefail
cd "$(dirname "$0")/.."

REF="${MONO_REF:-/usr/lib/mono/4.8-api}"
OUT=dist
mkdir -p "$OUT" build/obj

# Icon (assets/servnook.ico is committed; regenerate everything from assets/logo/servnook-icon.svg with: python3 build/make_icon.py)
ICON=assets/servnook.ico

# Embed every file in overlay/ as a resource named overlay/<relative path>
RES=()
while IFS= read -r -d '' f; do
  rel="${f#./}"
  RES+=("-resource:${rel},${rel}")
done < <(cd . && find overlay -type f -print0 | sort -z)

REFS=(mscorlib System System.Core System.Security System.Drawing System.Windows.Forms System.Web.Extensions
      System.IO.Compression System.IO.Compression.FileSystem System.Net.Http)
REFARGS=(-nostdlib -noconfig -lib:"$REF" -lib:"$REF/Facades")
for r in "${REFS[@]}"; do REFARGS+=(-r:"$r.dll"); done

COMMON=(-langversion:7.2 -optimize+ -debug- -platform:anycpu -warn:4 -nowarn:1591 "${REFARGS[@]}")

# Fonts (Inter, JetBrains Mono; SIL OFL 1.1) are embedded in the GUI only, as fonts/<file>
FONTS=()
for f in assets/fonts/*.ttf; do FONTS+=("-resource:$f,fonts/$(basename "$f")"); done
# Logo renders (build/make_icon.py), drawn by Ui.DrawLogo
for f in assets/logo/servnook-[0-9]*.png; do FONTS+=("-resource:$f,logo/$(basename "$f")"); done
FONTS+=("-resource:assets/servnook.ico,logo/servnook.ico")

mcs "${COMMON[@]}" -target:winexe -out:"$OUT/ServNook.exe" -win32icon:"$ICON" \
    -main:ServNook.Program "${RES[@]}" "${FONTS[@]}" src/Core/*.cs src/Gui/*.cs

mcs "${COMMON[@]}" -target:exe -out:"$OUT/snook.exe" -win32icon:"$ICON" \
    -main:ServNook.CliProgram "${RES[@]}" src/Core/*.cs src/Cli/*.cs

cp build/ServNook.exe.config "$OUT/ServNook.exe.config"
cp build/ServNook.exe.config "$OUT/snook.exe.config"
ls -la "$OUT"
