param($file = "variants-results.txt")
# Median and range (min-max) per scenario and variant of the result blocks that variants.sh appends.
# rate: req/s, notif/s or events/s; gen/s: generated events; deliv: received/expected;
# p50/p99: request latency, or write->notify / event->notify latency; cpuUs/allocB: server per
# request or notification; cpuUs2/allocB2: server per written value or generated event;
# pauseMs, gc (gen0/gen1/gen2), cores, wsMB: server process in the window.
$rows = @()
$cur = $null
function Close() { if ($null -ne $script:cur) { $script:rows += [pscustomobject]$script:cur; $script:cur = $null } }
foreach ($l in Get-Content $file) {
    if ($l -match '^### variant=(\S+) round=(\d+) (.*)$') { Close; $script:cur = @{ variant = $Matches[1]; scen = $Matches[3].Trim() }; continue }
    if ($null -eq $cur) { continue }
    if ($l -match '^FAILED') { $cur.failed = 1 }
    elseif ($l -match '^\s+client: ') {
        if ($l -match '(req|notif|events)/s=(\d+)') { $cur.rate = [double]$Matches[2] }
        if ($l -match 'generated/s=(\d+)') { $cur.'gen/s' = [double]$Matches[1] }
        if ($l -match 'received/expected=([\d.]+) %') { $cur.deliv = [double]$Matches[1] }
        $lat = $l
        if ($l -match '(write|event)->notify latency (.*)$') { $lat = $Matches[2] }
        if ($lat -match 'p50=([\d.]+)ms .*?p99=([\d.]+)ms') { $cur.p50 = [double]$Matches[1]; $cur.p99 = [double]$Matches[2] }
    }
    elseif ($l -match '^\s+server: ') {
        if ($l -match 'gen0=(\d+) gen1=(\d+) gen2=(\d+) pauseMs=(\d+)') { $cur.gc = "$($Matches[1])/$($Matches[2])/$($Matches[3])"; $cur.pauseMs = [double]$Matches[4] }
        if ($l -match 'cpuCores=([\d.]+)') { $cur.cores = [double]$Matches[1] }
        if ($l -match 'wsMB=(\d+)') { $cur.wsMB = [double]$Matches[1] }
    }
    elseif ($l -match '^\s+per-op: allocB=(\d+) cpuUs=([\d.]+)') { $cur.allocB = [double]$Matches[1]; $cur.cpuUs = [double]$Matches[2] }
    elseif ($l -match '^\s+per-op2: allocB=(\d+) cpuUs=([\d.]+)') { $cur.allocB2 = [double]$Matches[1]; $cur.cpuUs2 = [double]$Matches[2] }
}
Close
function Num($x) { $f = if ([Math]::Abs($x) -ge 100) { '{0:0}' } else { '{0:0.##}' }; return [string]::Format([Globalization.CultureInfo]::InvariantCulture, $f, $x) }
function Fmt($xs) {
    $s = @($xs | Where-Object { $null -ne $_ } | Sort-Object)
    if ($s.Count -eq 0) { return '' }
    $m = $s[[int][Math]::Floor($s.Count / 2)]
    if ($s.Count -eq 1 -or $s[0] -eq $s[-1]) { return (Num $m) }
    return "$(Num $m) [$(Num $s[0])-$(Num $s[-1])]"
}
foreach ($g in ($rows | Group-Object scen)) {
    "## $($g.Name)"
    $out = foreach ($v in ($g.Group | Group-Object variant)) {
        $ok = @($v.Group | Where-Object { -not $_.failed })
        $o = [ordered]@{ variant = $v.Name; n = "$($ok.Count)/$(@($v.Group).Count)" }
        foreach ($m in 'rate', 'gen/s', 'deliv', 'p50', 'p99', 'cpuUs', 'allocB', 'cpuUs2', 'allocB2', 'pauseMs', 'cores', 'wsMB') {
            $f = Fmt ($ok | ForEach-Object { $_.$m })
            if ($f -ne '') { $o[$m] = $f }
        }
        $o.gc = ($ok | ForEach-Object { $_.gc }) -join ' '
        [pscustomobject]$o
    }
    $out | Format-List | Out-String -Width 250
}
