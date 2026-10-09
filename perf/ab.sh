#!/bin/bash
# BASELINE: harness built in a second worktree of the commit to compare against.
A=${BASELINE:-C:/uabase/perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe}
B=./ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe
O=ab-results.txt; rm -f $O
while IFS= read -r s; do
  for r in 1 2 3; do
    for side in A B; do
      exe=$A; [ $side = B ] && exe=$B
      echo "### side=$side round=$r $s" >> $O
      timeout 300 $exe run $s --sessions 20 --duration 12 --warmup 6 --out $O >/dev/null 2>&1
    done
  done
done <<'S'
--scenario read --nodes 1
--scenario read --nodes 100
--scenario write --nodes 100
--scenario browse
--scenario read --nodes 100 --security encrypt
--scenario sub --subs 5 --nodes 100 --pub 100 --write-interval 50
S
echo done >> $O
