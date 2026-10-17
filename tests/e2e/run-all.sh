#!/usr/bin/env bash
# Browser (Playwright) acceptance tests. Each suite runs against a FRESH copy of the app with demo data.
# Needs: .NET 10 SDK, Node 18+ with the "playwright" package, Python 3 with Pillow.
#   bash tests/e2e/run-all.sh            screenshots go to tests/e2e/out/
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; ROOT="$HERE/../.."; OUT="$HERE/out"; APP="$OUT/app"
export BASE_URL="${BASE_URL:-http://127.0.0.1:8080}"
export Notifications__PollSeconds=30   # the shortest allowed poll, so badge tests finish quickly
mkdir -p "$OUT"
python3 -I "$HERE/make-test-images.py" "$OUT/img"
dotnet publish "$ROOT/src/NabconsIntranet" -c Release -o "$OUT/publish" >/dev/null
fail=0
for suite in core registration registration-others profile-photos-card leads desk-notifications enote; do
  pkill -x NabconsIntranet 2>/dev/null || true; sleep 1
  rm -rf "$APP"; cp -r "$OUT/publish" "$APP"
  ( cd "$APP" && nohup ./NabconsIntranet > console.log 2>&1 & )
  for i in $(seq 1 40); do curl -s -o /dev/null "$BASE_URL/Account/Login" && break; sleep 1; done
  mkdir -p "$OUT/$suite"
  echo "=== $suite"
  case $suite in
    profile-photos-card|leads|enote) args=("$OUT/img" "$OUT/$suite") ;;
    *) args=("$OUT/$suite") ;;
  esac
  result=$(node "$HERE/$suite.e2e.js" "${args[@]}" 2>&1 || true)
  echo "$result" | grep -E "FAIL|PASSED|FAILED|Error" || true
  echo "$result" | grep -q "PASSED" || fail=1
done
pkill -x NabconsIntranet 2>/dev/null || true
exit $fail
