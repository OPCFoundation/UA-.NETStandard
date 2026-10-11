#!/bin/bash
# Paired A/B runs: alternates a baseline and a candidate harness build per scenario and round.
# BASELINE: harness built in a second worktree of the commit to compare against.
# SCENARIOS: file with one scenario per line (default: server-scenarios.txt).
# ROUNDS: rounds per scenario (default 3). OUT: result file (default ab-results.txt).
A=${BASELINE:-C:/uabase/perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe}
B=./ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe
O=${OUT:-ab-results.txt}; rm -f "$O"
ROUNDS=${ROUNDS:-3}
SCENARIOS=${SCENARIOS:-server-scenarios.txt}
while IFS= read -r s || [ -n "$s" ]; do
  # tolerate CRLF scenario files
  s=${s%$'\r'}
  [ -z "$s" ] && continue
  for r in $(seq 1 "$ROUNDS"); do
    for side in A B; do
      exe=$A; [ $side = B ] && exe=$B
      echo "### side=$side round=$r $s" >> "$O"
      # defaults first, so that a scenario can override them
      timeout 300 $exe run --sessions 20 --duration 12 --warmup 6 $s --out "$O" >/dev/null 2>&1
    done
  done
done < "$SCENARIOS"
echo done >> "$O"
