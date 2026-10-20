#!/usr/bin/env bash
# Runs benchmark B2 "Server-16" once (M0-07 A1, TDD §8 spike 2) with docker-compose.b2.yml, from the source/ folder:
#   scripts/run-b2.sh                          full run: 30 s warm-up + 10 min measured
#   B2_MEASURE_TICKS=1800 scripts/run-b2.sh    short run (CI)
# Results: $B2_RESULTS (default ./b2-results) gets summary.json, ticks.csv, server.log and bots.log.
# Exit code: 0 when the bots passed (all in session, reconnect and duplicate checks) and the network thresholds held;
# 1 otherwise. Timing thresholds are reported but only count on the benchmark machine (M0-07 A4, A5).
set -euo pipefail
cd "$(dirname "$0")/.."

compose=(docker compose -f docker-compose.b2.yml)
results="${B2_RESULTS:-./b2-results}"
export B2_RESULTS="$results"
measure="${B2_MEASURE_TICKS:-18000}"
warmup="${B2_WARMUP_TICKS:-900}"
# Real time the run may take: startup + warm-up + measured ticks at 30 Hz + 5 min spare.
timeout_s=$(( (warmup + measure) / 30 + 300 ))

rm -rf "$results"
mkdir -p "$results"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
"${compose[@]}" build server
"${compose[@]}" up -d server

for _ in $(seq 1 60); do
  if "${compose[@]}" logs server | grep -q '"server.listening"'; then break; fi
  sleep 2
done
"${compose[@]}" logs server | grep -q '"netem.applied"' || { echo "netem was not applied on the server"; "${compose[@]}" logs server; exit 1; }

set +e
timeout "$timeout_s" "${compose[@]}" run --rm --no-deps bots > "$results/bots.log" 2>&1
bots_exit=$?
set -e
grep -q '"netem.applied"' "$results/bots.log" || { echo "netem was not applied on the bots"; bots_exit=1; }

for _ in $(seq 1 30); do
  [ -f "$results/summary.json" ] && break
  sleep 2
done
"${compose[@]}" logs server > "$results/server.log" 2>&1
"${compose[@]}" down -v >/dev/null 2>&1 || true

echo "--- bots (exit $bots_exit)"
grep -E '"bot\.|"bots\.finished"' "$results/bots.log" || true
echo "--- server"
grep -E '"b2\.' "$results/server.log" || true
[ -f "$results/summary.json" ] || { echo "no summary.json"; exit 1; }
cat "$results/summary.json"

py=$(command -v python3 || command -v python)
network_failures=$("$py" -c 'import json,sys; print(len(json.load(open(sys.argv[1]))["network_failures"]))' "$results/summary.json")
if [ "$bots_exit" -ne 0 ] || [ "$network_failures" -ne 0 ]; then
  echo "B2 FAILED (bots exit $bots_exit, $network_failures network failures)"
  exit 1
fi
echo "B2 passed the network checks"
