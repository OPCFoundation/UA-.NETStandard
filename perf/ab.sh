#!/bin/bash
# Paired A/B runs: alternates a baseline and a candidate harness build per scenario and round.
# BASELINE: harness built in a second worktree of the commit to compare against.
# CANDIDATE: harness to compare (default: the build in this worktree).
# SCENARIOS: file with one scenario per line (default: server-scenarios.txt).
# ROUNDS: rounds per scenario (default 3). OUT: result file (default ab-results.txt).
set -e
A=${BASELINE:-C:/uabase/perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe}
B=${CANDIDATE:-./ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe}
O=${OUT:-ab-results.txt}
ROUNDS=${ROUNDS:-3}
SCENARIOS=${SCENARIOS:-server-scenarios.txt}
if ! [[ "$ROUNDS" =~ ^[1-9][0-9]{0,5}$ ]] || [ "$ROUNDS" -gt 100000 ]; then
  echo 'ROUNDS must be an integer from 1 through 100000.' >&2
  exit 2
fi
if [ ! -r "$SCENARIOS" ]; then
  echo "Cannot read scenario list: $SCENARIOS" >&2
  exit 2
fi
rm -f "$O"
failed=0
while IFS= read -r s || [ -n "$s" ]; do
  # tolerate CRLF scenario files
  s=${s%$'\r'}
  [ -z "$s" ] && continue
  for ((r=1; r<=ROUNDS; r++)); do
    for side in A B; do
      exe=$A; [ $side = B ] && exe=$B
      echo "### side=$side round=$r $s" >> "$O"
      # defaults first, so that a scenario can override them
      if timeout 300 "$exe" run --sessions 20 --duration 12 --warmup 6 $s --out "$O" >/dev/null 2>&1; then
        code=0
      else
        code=$?
        failed=1
      fi
      printf 'EXIT code=%s\n' "$code" >> "$O"
    done
  done
done < "$SCENARIOS"
echo done >> "$O"
exit "$failed"
