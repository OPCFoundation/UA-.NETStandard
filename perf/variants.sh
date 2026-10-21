#!/bin/bash
# Paired runs of one harness build under several configurations (GC mode, affinity, ...).
# VARIANTS: file with one variant per line, "<name>|<environment>|<extra arguments>", for example
#   server-datas|SERVER_DOTNET_gcServer=1|
#   pcores|SERVER_DOTNET_PROCESSOR_COUNT=8|--server-affinity 0xFF
# SCENARIOS: file with the harness arguments of one scenario per line ('#' starts a comment).
# HARNESS: harness executable (default: the build in this worktree).
# ROUNDS: rounds per scenario (default 5). OUT: result file (default variants-results.txt).
# OWN: ServerLoadHarness processes of one run (default 2: the harness and its server child).
# WORKERS: build directory of the native benchmark workers, substituted for {workers}.
# The variants run in turn and in reverse order every other round. A run during which a test
# host or another load harness ran on the machine is discarded (logged in <OUT>.discarded) and
# repeated once the machine has been idle for a minute.
cd "$(dirname "$0")" || exit 1
H=${HARNESS:-./ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe}
O=${OUT:-variants-results.txt}; rm -f "$O" "$O.discarded"
ROUNDS=${ROUNDS:-5}
OWN=${OWN:-2}
V=${VARIANTS:?VARIANTS file required}
S=${SCENARIOS:?SCENARIOS file required}
WORKERS=${WORKERS:-}
W=$(mktemp -d)

foreign() {
  local list; list=$(tasklist //FO CSV 2>/dev/null || ps -e -o comm=)
  local hosts; hosts=$(echo "$list" | grep -ci 'testhost')
  local harness; harness=$(echo "$list" | grep -ci 'ServerLoadHarness')
  [ "$hosts" -gt 0 ] || [ "$harness" -gt "$1" ]
}

wait_idle() {
  local idle=0
  while [ $idle -lt 6 ]; do
    if foreign 0; then idle=0; else idle=$((idle + 1)); fi
    sleep 10
  done
}

mapfile -t variants < <(grep -v '^\s*\(#\|$\)' "$V" | tr -d '\r')
wait_idle
while IFS= read -r s || [ -n "$s" ]; do
  s=${s%$'\r'}
  case "$s" in ''|\#*) continue;; esac
  for r in $(seq 1 "$ROUNDS"); do
    order=$(seq 0 $((${#variants[@]} - 1)))
    [ $((r % 2)) -eq 0 ] && order=$(echo "$order" | sort -rn)
    for i in $order; do
      IFS='|' read -r name envs args <<< "${variants[$i]}"
      run="${s//\{workers\}/$WORKERS} ${args//\{workers\}/$WORKERS}"
      while true; do
        rm -f "$W/run.txt" "$W/flag" "$W/done"
        ( while [ ! -f "$W/done" ]; do foreign "$OWN" && touch "$W/flag"; sleep 2; done ) &
        poller=$!
        echo "### variant=$name round=$r $s" > "$W/run.txt"
        # shellcheck disable=SC2086 # word splitting of the environment and argument lists
        env $envs timeout 900 "$H" run $run --out "$W/run.txt" </dev/null >/dev/null 2>&1 || echo "FAILED" >> "$W/run.txt"
        touch "$W/done"; wait $poller
        if [ ! -f "$W/flag" ]; then cat "$W/run.txt" >> "$O"; break; fi
        echo "$(date +%H:%M:%S) discarded variant=$name round=$r $s" >> "$O.discarded"
        wait_idle
      done
    done
  done
done < "$S"
rm -rf "$W"
echo done >> "$O"
