#requires -Version 7
<#
.SYNOPSIS
    Runs the server load regression floors: ServerLoadHarness scenarios checked against
    perf/regression/load-floors.json.

.DESCRIPTION
    Every round runs each scenario of the floors file once, in file order, so the calibration
    scenario and the measured scenarios share the conditions of the round. A check compares
    the median over the rounds with its floor (min) or ceiling (max). A check with 'per' is a
    ratio to a metric of another scenario of the same round (the calibration), computed per
    round before the median is taken, so the speed of the machine cancels out.

    The server child runs with Server GC (SERVER_DOTNET_gcServer=1) and so does the client
    (DOTNET_gcServer=1), unless the caller set them.

    Exit code 0: every check passed. 1: a check failed. 2: a scenario did not produce a result.

.PARAMETER Harness
    The ServerLoadHarness executable (the apphost, not the .dll: the harness starts itself as
    the server child). Default: perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness[.exe].

.PARAMETER Floors
    The floors file. Default: load-floors.json next to this script.

.PARAMETER Rounds
    Rounds to run; overrides the floors file.

.PARAMETER ExtraArgs
    Arguments appended to every harness run, for example '--inject-alloc 2048' to check that
    the floors fail on a simulated regression.

.PARAMETER Only
    Run only these scenarios (and the scenarios their checks are ratios to); the checks of
    the other scenarios are skipped. For validating a floor against a simulated regression.

.PARAMETER OutDirectory
    Where the raw result blocks (results.txt) and the summary (summary.md, results.json) are
    written. Default: TestResults/load-floors.

.EXAMPLE
    pwsh perf/regression/Invoke-LoadFloors.ps1 -Rounds 1
#>
[CmdletBinding()]
param(
    [string] $Harness,
    [string] $Floors = (Join-Path $PSScriptRoot 'load-floors.json'),
    [int] $Rounds = 0,
    [string] $ExtraArgs = '',
    [string[]] $Only = @(),
    [string] $OutDirectory = 'TestResults/load-floors'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $Harness) {
    $name = if ($IsWindows) { 'ServerLoadHarness.exe' } else { 'ServerLoadHarness' }
    $Harness = Join-Path $PSScriptRoot "../ServerLoadHarness/bin/Release/net10.0/$name"
}
if (-not (Test-Path -LiteralPath $Harness -PathType Leaf)) {
    throw "Harness '$Harness' not found. Build it: dotnet build perf/ServerLoadHarness -c Release -p:CustomTestTarget=net10.0"
}
$Harness = (Resolve-Path -LiteralPath $Harness).Path

$config = Get-Content -LiteralPath $Floors -Raw | ConvertFrom-Json
if ($Rounds -le 0) {
    $Rounds = [int]$config.rounds
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$rawFile = Join-Path $OutDirectory 'results.txt'
Set-Content -LiteralPath $rawFile -Value '' -NoNewline

# Server GC for both processes; the harness passes SERVER_ variables to the server child.
if (-not $env:SERVER_DOTNET_gcServer) { $env:SERVER_DOTNET_gcServer = '1' }
if (-not $env:DOTNET_gcServer) { $env:DOTNET_gcServer = '1' }

$invariant = [System.Globalization.CultureInfo]::InvariantCulture

function ConvertTo-Number([string] $text) {
    return [double]::Parse($text, $invariant)
}

<#
 Parses one RESULT block of the harness into a flat metric table. Names:
   client line : reqPerSec, valuesPerSec, notifPerSec, eventsPerSec, generatedPerSec,
                 delivered (received/expected as a fraction), p50Ms, p99Ms (the
                 write->notify or event->notify latency, else the request latency)
   server line : serverCpuCores, serverPauseMs, serverContention, serverAllocMB
   per-op      : allocB, cpuUs (server per operation), allocB2, cpuUs2 (per-op2)
#>
function ConvertFrom-ResultBlock([string[]] $lines) {
    $m = @{}
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('client:')) {
            $pairs = @{
                'req/s' = 'reqPerSec'; 'values/s' = 'valuesPerSec'; 'notif/s' = 'notifPerSec'
                'events/s' = 'eventsPerSec'; 'generated/s' = 'generatedPerSec'
            }
            foreach ($key in $pairs.Keys) {
                $match = [regex]::Match($trimmed, "(?<![\w/])$([regex]::Escape($key))=([\d.]+)")
                if ($match.Success) { $m[$pairs[$key]] = ConvertTo-Number $match.Groups[1].Value }
            }
            $match = [regex]::Match($trimmed, 'received/expected=([\d.]+)\s?%')
            if ($match.Success) { $m['delivered'] = (ConvertTo-Number $match.Groups[1].Value) / 100 }
            # the notification latency when the line has one, else the request latency
            $notify = [regex]::Match($trimmed, '->notify latency (.*)$')
            $source = if ($notify.Success) { $notify.Groups[1].Value } else { $trimmed }
            $p50 = [regex]::Matches($source, 'p50=([\d.]+)ms')
            $p99 = [regex]::Matches($source, 'p99=([\d.]+)ms')
            if ($p50.Count -gt 0) { $m['p50Ms'] = ConvertTo-Number $p50[$p50.Count - 1].Groups[1].Value }
            if ($p99.Count -gt 0) { $m['p99Ms'] = ConvertTo-Number $p99[$p99.Count - 1].Groups[1].Value }
        }
        elseif ($trimmed.StartsWith('server:')) {
            foreach ($pair in @(@('cpuCores', 'serverCpuCores'), @('pauseMs', 'serverPauseMs'), @('contention', 'serverContention'), @('allocMB', 'serverAllocMB'))) {
                $match = [regex]::Match($trimmed, "\b$($pair[0])=([\d.]+)")
                if ($match.Success) { $m[$pair[1]] = ConvertTo-Number $match.Groups[1].Value }
            }
        }
        elseif ($trimmed.StartsWith('per-op:') -or $trimmed.StartsWith('per-op2:')) {
            $suffix = if ($trimmed.StartsWith('per-op2:')) { '2' } else { '' }
            $match = [regex]::Match($trimmed, 'allocB=([\d.]+) cpuUs=([\d.]+)')
            if ($match.Success) {
                $m["allocB$suffix"] = ConvertTo-Number $match.Groups[1].Value
                $m["cpuUs$suffix"] = ConvertTo-Number $match.Groups[2].Value
            }
        }
    }
    return $m
}

function Get-Median([double[]] $values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $mid = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$mid] }
    return ($sorted[$mid - 1] + $sorted[$mid]) / 2
}

$common = [string]$config.commonArgs
$selected = @($config.scenarios | ForEach-Object { $_.name })
if ($Only.Count -gt 0) {
    $references = @($config.checks |
        Where-Object { $Only -contains $_.scenario -and $_.PSObject.Properties.Name -contains 'per' } |
        ForEach-Object { $_.per.scenario })
    $selected = @($Only) + $references
}
# results[round][scenario] = metric table
$results = @()
for ($round = 1; $round -le $Rounds; $round++) {
    $roundResults = @{}
    foreach ($scenario in $config.scenarios) {
        if ($selected -notcontains $scenario.name) { continue }
        $arguments = "run $($scenario.args) $common $ExtraArgs".Trim() -split '\s+'
        Write-Host "::group::round $round/$Rounds $($scenario.name): $($arguments -join ' ')"
        $output = & $Harness @arguments 2>&1 | ForEach-Object { "$_" }
        $exit = $LASTEXITCODE
        $start = -1
        for ($i = 0; $i -lt $output.Count; $i++) {
            if ($output[$i].StartsWith('RESULT ')) { $start = $i }
        }
        if ($exit -ne 0 -or $start -lt 0) {
            $output | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
            Write-Host '::endgroup::'
            Write-Host "::error title=Load floors::$($scenario.name) produced no result (exit code $exit)."
            exit 2
        }
        $block = @($output[$start..($output.Count - 1)] | Where-Object { $_ -match '^(RESULT |  )' })
        $block | ForEach-Object { Write-Host $_ }
        Add-Content -LiteralPath $rawFile -Value "# round $round $($scenario.name)"
        Add-Content -LiteralPath $rawFile -Value $block
        $roundResults[$scenario.name] = ConvertFrom-ResultBlock $block
        Write-Host '::endgroup::'
    }
    $results += , $roundResults
}

$failures = 0
$rows = @()
foreach ($check in $config.checks) {
    if ($selected -notcontains $check.scenario) { continue }
    $values = @()
    foreach ($roundResults in $results) {
        $own = $roundResults[$check.scenario]
        if (-not $own -or -not $own.ContainsKey($check.metric)) { continue }
        $value = $own[$check.metric]
        if ($check.PSObject.Properties.Name -contains 'per') {
            $reference = $roundResults[$check.per.scenario]
            if (-not $reference -or -not $reference.ContainsKey($check.per.metric) -or $reference[$check.per.metric] -le 0) { continue }
            $value = $value / $reference[$check.per.metric]
        }
        $values += $value
    }
    $median = Get-Median $values
    $label = if ($check.PSObject.Properties.Name -contains 'per') {
        "$($check.scenario).$($check.metric) / $($check.per.scenario).$($check.per.metric)"
    } else {
        "$($check.scenario).$($check.metric)"
    }
    $hasMin = $check.PSObject.Properties.Name -contains 'min'
    $hasMax = $check.PSObject.Properties.Name -contains 'max'
    $ok = -not [double]::IsNaN($median)
    if ($ok -and $hasMin) { $ok = $median -ge [double]$check.min }
    if ($ok -and $hasMax) { $ok = $median -le [double]$check.max }
    if (-not $ok) { $failures++ }
    $bound = if ($hasMin) { ">= $($check.min)" } else { "<= $($check.max)" }
    $rows += [pscustomobject]@{
        Check = $label
        Median = if ([double]::IsNaN($median)) { 'n/a' } else { $median.ToString('G4', $invariant) }
        Rounds = ($values | ForEach-Object { $_.ToString('G4', $invariant) }) -join ', '
        Bound = $bound
        Result = if ($ok) { 'pass' } else { 'FAIL' }
        Catches = [string]$check.catches
    }
}

# Report-only metrics: throughput and latency of every scenario (median over the rounds).
$report = @()
foreach ($scenario in $config.scenarios) {
    if ($selected -notcontains $scenario.name) { continue }
    $metrics = @{}
    foreach ($roundResults in $results) {
        foreach ($key in $roundResults[$scenario.name].Keys) {
            if (-not $metrics.ContainsKey($key)) { $metrics[$key] = @() }
            $metrics[$key] += $roundResults[$scenario.name][$key]
        }
    }
    $parts = foreach ($key in ($metrics.Keys | Sort-Object)) {
        "$key=$((Get-Median $metrics[$key]).ToString('G4', $invariant))"
    }
    $report += "| $($scenario.name) | $($parts -join ' ') |"
}

$summary = @(
    "## Server load floors ($Rounds round(s), median)"
    ''
    '| Check | Median | Rounds | Bound | Result | Catches |'
    '|---|---|---|---|---|---|'
)
$summary += $rows | ForEach-Object { "| $($_.Check) | $($_.Median) | $($_.Rounds) | $($_.Bound) | $($_.Result) | $($_.Catches) |" }
$summary += ''
$summary += '### Measured (report only)'
$summary += ''
$summary += '| Scenario | Metrics |'
$summary += '|---|---|'
$summary += $report
if ($ExtraArgs) {
    $summary += ''
    $summary += "Extra harness arguments: ``$ExtraArgs``"
}

$summaryText = $summary -join [Environment]::NewLine
Write-Host $summaryText
Set-Content -LiteralPath (Join-Path $OutDirectory 'summary.md') -Value $summaryText
$rows | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDirectory 'results.json')
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summaryText
}

if ($failures -gt 0) {
    Write-Host "::error title=Load floors::$failures check(s) failed; see the summary."
    exit 1
}
Write-Host 'All load floors passed.'
exit 0
