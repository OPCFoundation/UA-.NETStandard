param($file = "ab-results.txt")
$rows = @()
$cur = $null
foreach ($l in Get-Content $file) {
    if ($l -match '^### side=(\w) round=(\d) (.*)$') { $cur = @{ side = $Matches[1]; scen = $Matches[3].Trim() }; continue }
    if ($null -eq $cur) { continue }
    if ($l -match 'req/s=(\d+)') { $cur.tput = [double]$Matches[1] }
    if ($l -match 'notif/s=(\d+)') { $cur.tput = [double]$Matches[1] }
    if ($l -match 'p50=([\d.]+)ms p90=([\d.]+)ms p99=([\d.]+)ms') { $cur.p50 = [double]$Matches[1]; $cur.p90 = [double]$Matches[2]; $cur.p99 = [double]$Matches[3] }
    if ($l -match 'pauseMs=(\d+)') { $cur.pause = [double]$Matches[1] }
    if ($l -match 'contention=(\d+)') { $cur.cont = [double]$Matches[1] }
    if ($l -match 'per-op: allocB=(\d+) cpuUs=([\d.]+)') { $cur.alloc = [double]$Matches[1]; $cur.cpu = [double]$Matches[2]; $rows += [pscustomobject]$cur; $cur = $null }
}
function Med($xs) { $s = @($xs | Sort-Object); if ($s.Count -eq 0) { return [double]::NaN }; return $s[[int][Math]::Floor($s.Count / 2)] }
$out = @()
foreach ($g in ($rows | Group-Object scen)) {
    $a = $g.Group | Where-Object side -eq 'A'; $b = $g.Group | Where-Object side -eq 'B'
    $o = [ordered]@{ scenario = $g.Name; n = "$(@($a).Count)/$(@($b).Count)" }
    foreach ($m in 'tput', 'p50', 'p90', 'p99', 'alloc', 'cpu', 'pause', 'cont') {
        $ma = Med ($a | ForEach-Object { $_.$m }); $mb = Med ($b | ForEach-Object { $_.$m })
        $o["$m"] = "{0:G4} -> {1:G4} ({2:+0;-0}%)" -f $ma, $mb, (100 * ($mb - $ma) / $ma)
    }
    $out += [pscustomobject]$o
}
$out | Format-List
