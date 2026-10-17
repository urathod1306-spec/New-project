#!/usr/bin/env bash
# Load check: 50 virtual users for 10 minutes against a fresh copy of the app with the benchmark volume.
#   bash tests/LoadTest/run-load.sh [minutes] [users]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; ROOT="$HERE/../.."; OUT="$HERE/out"; MIN="${1:-10}"; VUS="${2:-50}"
rm -rf "$OUT" && mkdir -p "$OUT"
dotnet publish "$ROOT/src/NabconsIntranet" -c Release -o "$OUT/app" >/dev/null
dotnet build "$HERE" -c Release -o "$OUT/tool" >/dev/null
export Urls=http://127.0.0.1:8099 Notifications__PollSeconds=60
( cd "$OUT/app" && nohup ./NabconsIntranet > first.log 2>&1 & )
for i in $(seq 1 60); do curl -s -o /dev/null http://127.0.0.1:8099/Account/Login && break; sleep 1; done
pkill -x NabconsIntranet; sleep 2
"$OUT/tool/LoadTest" seed "$OUT/app/data/intranet.db"
( cd "$OUT/app" && nohup ./NabconsIntranet > run.log 2>&1 & )
for i in $(seq 1 60); do curl -s -o /dev/null http://127.0.0.1:8099/Account/Login && break; sleep 1; done
"$OUT/tool/LoadTest" run http://127.0.0.1:8099 "$MIN" "$VUS" | tee "$OUT/results.txt"
ps -o rss=,pcpu= -C NabconsIntranet | awk '{printf "Server memory %.0f MB, average CPU %.0f%%\n", $1/1024, $2}' | tee -a "$OUT/results.txt"
pkill -x NabconsIntranet || true
