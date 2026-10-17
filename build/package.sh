#!/usr/bin/env bash
# Builds the ready-to-run Windows package: release/NABCONS-Intranet-v<version>-win-x64.zip
# Usage: build/package.sh            (needs the .NET 10 SDK; works on Linux, macOS or Windows Git Bash)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/NabconsIntranet/NabconsIntranet.csproj")"
NAME="NABCONS-Intranet"
OUT="$ROOT/build/out"
STAGE="$OUT/$NAME"
rm -rf "$OUT" && mkdir -p "$STAGE"

dotnet publish "$ROOT/src/NabconsIntranet/NabconsIntranet.csproj" -c Release -r win-x64 -o "$STAGE" -p:ContinuousIntegrationBuild=true
rm -f "$STAGE"/*.pdb "$STAGE"/web.config

# Windows helper scripts and quick-start guide (CRLF line endings for cmd.exe / Notepad)
for f in "$ROOT"/deploy/windows/*; do
  sed 's/\r$//; s/$/\r/' "$f" > "$STAGE/$(basename "$f")"
done
mkdir -p "$STAGE/docs" "$STAGE/sample-data"
for f in "$ROOT"/docs/*.md; do sed 's/\r$//; s/$/\r/' "$f" > "$STAGE/docs/$(basename "$f")"; done
for f in "$ROOT"/SETUP_GUIDE.md "$ROOT"/TESTING_CHECKLIST.md "$ROOT"/CHANGELOG.md; do sed 's/\r$//; s/$/\r/' "$f" > "$STAGE/docs/$(basename "$f")"; done
cp "$ROOT"/deploy/sample-data/* "$STAGE/sample-data/"
sed -i 's/\r$//; s/$/\r/' "$STAGE/appsettings.json"

# Checksums of every file in the package (for the IT transfer / allow-listing process)
( cd "$STAGE" && find . -type f ! -name SHA256SUMS.txt -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS.txt )

mkdir -p "$ROOT/release"
ZIP="$ROOT/release/$NAME-v$VERSION-win-x64.zip"
rm -f "$ZIP"
( cd "$OUT" && zip -qr -9 "$ZIP" "$NAME" )
( cd "$ROOT/release" && sha256sum "$(basename "$ZIP")" > "$(basename "$ZIP").sha256" )
ls -la "$ROOT/release"
cat "$ZIP.sha256"
