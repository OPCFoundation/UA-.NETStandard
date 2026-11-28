#requires -Version 7
# SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Runs the server load regression floors: ServerLoadHarness scenarios checked against
    perf/regression/load-floors.json (or another floors file, such as alloc-gate.json).

.DESCRIPTION
    Every round runs each scenario of the floors file once, in file order, so the calibration
    scenario and the measured scenarios share the conditions of the round. A check compares
    the median over the rounds (or the minimum, see 'aggregate') with its floor (min) or
    ceiling (max). A check with 'per' is a ratio to a metric of another scenario of the same
    round (the calibration), computed per round before the aggregate is taken, so the speed of
    the machine cancels out.

    Optional keys of the floors file:
      aggregate      'median' (default) or 'min'. 'min' suits byte counts on a shared
                     machine: background work only ever adds bytes to a window, so the
                     smallest round is the closest to the cost of the code.
      confirmRounds  When a scenario fails, run it this many more times and fail only when
                     the confirmation rounds fail the same check too (default 0).
      environment    Variables set for the harness when the caller has not set them, for
                     example DOTNET_TieredCompilation. SERVER_ variables reach the server child.
      headroom       For a 'max' check: report a notice when the aggregate is below this
                     fraction of the ceiling, so an improvement can be locked in.

    The server child runs with Server GC (SERVER_DOTNET_gcServer=1) and so does the client
    (DOTNET_gcServer=1), unless the caller or the floors file set them.

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

.EXAMPLE
    pwsh perf/regression/Invoke-LoadFloors.ps1 -Floors perf/regression/alloc-gate.json
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
function Get-ConfigValue([string] $name, $default) {
    if ($config.PSObject.Properties.Name -contains $name) { return $config.$name }
    return $default
}
if ($Rounds -le 0) {
    $Rounds = [int]$config.rounds
}
$aggregate = [string](Get-ConfigValue 'aggregate' 'median')
if ($aggregate -notin @('median', 'min')) {
    throw "aggregate must be 'median' or 'min', not '$aggregate'."
}
$confirmRounds = [int](Get-ConfigValue 'confirmRounds' 0)
$headroom = [double](Get-ConfigValue 'headroom' 0)
$title = [string](Get-ConfigValue 'title' 'Server load floors')

New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$rawFile = Join-Path $OutDirectory 'results.txt'
Set-Content -LiteralPath $rawFile -Value '' -NoNewline

# Variables of the floors file, then Server GC for both processes, unless the caller set them;
# the harness passes SERVER_ variables to the server child.
$environment = Get-ConfigValue 'environment' $null
if ($environment) {
    foreach ($property in $environment.PSObject.Properties) {
        if (-not [Environment]::GetEnvironmentVariable($property.Name)) {
            [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value)
        }
    }
}
if (-not $env:SERVER_DOTNET_gcServer) { $env:SERVER_DOTNET_gcServer = '1' }
if (-not $env:DOTNET_gcServer) { $env:DOTNET_gcServer = '1' }

$invariant = [System.Globalization.CultureInfo]::InvariantCulture

function ConvertTo-Number([string] $text) {
    return [double]::Parse($text, $invariant)
}

function Format-Number([double] $value) {
    return $value.ToString('G4', $invariant)
}

<#
 Parses one RESULT block of the harness into a flat metric table. Names:
   client line   : reqPerSec, valuesPerSec, notifPerSec, eventsPerSec, generatedPerSec,
                   errors, writeErrors (failed requests or rejected operations),
                   delivered (received/expected as a fraction), p50Ms, p99Ms (the
                   write->notify or event->notify latency, else the request latency)
   server line   : serverCpuCores, serverPauseMs, serverContention, serverAllocMB
   per-op        : allocB, cpuUs (server per operation), allocB2, cpuUs2 (per-op2)
   client-per-op : clientAllocB, clientCpuUs (client per operation)
   with --baseline: netAllocB, netAllocB2, netClientAllocB (the same bytes per operation
                   without the idle allocation rate of the process), serverBackgroundBps,
                   clientBackgroundBps
#>
function ConvertFrom-ResultBlock([string[]] $lines) {
    $m = @{}
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('client:')) {
            $pairs = @{
                'req/s' = 'reqPerSec'; 'values/s' = 'valuesPerSec'; 'notif/s' = 'notifPerSec'
                'events/s' = 'eventsPerSec'; 'generated/s' = 'generatedPerSec'
                'errors' = 'errors'; 'writeErrors' = 'writeErrors'
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
        elseif ($trimmed.StartsWith('per-op:') -or $trimmed.StartsWith('per-op2:') -or $trimmed.StartsWith('client-per-op:')) {
            $prefix = if ($trimmed.StartsWith('client-per-op:')) { 'client' } else { '' }
            $suffix = if ($trimmed.StartsWith('per-op2:')) { '2' } else { '' }
            $match = [regex]::Match($trimmed, 'allocB=([\d.]+) cpuUs=([\d.]+)')
            if ($match.Success) {
                $alloc = if ($prefix) { 'clientAllocB' } else { "allocB$suffix" }
                $cpu = if ($prefix) { 'clientCpuUs' } else { "cpuUs$suffix" }
                $m[$alloc] = ConvertTo-Number $match.Groups[1].Value
                $m[$cpu] = ConvertTo-Number $match.Groups[2].Value
            }
        }
        elseif ($trimmed -match '^(per-op-net|per-op2-net|client-per-op-net|background):') {
            $names = @{ 'per-op-net' = 'netAllocB'; 'per-op2-net' = 'netAllocB2'; 'client-per-op-net' = 'netClientAllocB' }
            if ($Matches[1] -eq 'background') {
                $match = [regex]::Match($trimmed, 'serverB/s=([\d.]+) clientB/s=([\d.]+)')
                if ($match.Success) {
                    $m['serverBackgroundBps'] = ConvertTo-Number $match.Groups[1].Value
                    $m['clientBackgroundBps'] = ConvertTo-Number $match.Groups[2].Value
                }
            }
            else {
                $match = [regex]::Match($trimmed, 'allocB=([\d.]+)')
                if ($match.Success) { $m[$names[$Matches[1]]] = ConvertTo-Number $match.Groups[1].Value }
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

function Get-Aggregate([double[]] $values, [bool] $isFloor) {
    if ($values.Count -eq 0) { return [double]::NaN }
    if ($aggregate -eq 'min') {
        # the round least disturbed by the machine: lowest for a ceiling, highest for a floor
        $measure = $values | Measure-Object -Minimum -Maximum
        return $(if ($isFloor) { $measure.Maximum } else { $measure.Minimum })
    }
    return Get-Median $values
}

$kRunAttempts = 3

# A TCP port no listener uses at the moment, chosen by the operating system.
function Get-FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return [string]$listener.LocalEndpoint.Port
    }
    finally {
        $listener.Stop()
    }
}

# Runs the named scenarios for the given number of rounds and returns, per round, a table of
# scenario name -> metric table. Exits with 2 when a run produces no result.
function Invoke-Rounds([string[]] $names, [int] $count, [string] $label) {
    $all = @()
    for ($round = 1; $round -le $count; $round++) {
        $roundResults = @{}
        foreach ($scenario in $config.scenarios) {
            if ($names -notcontains $scenario.name) { continue }
            $arguments = "run $($scenario.args) $common $ExtraArgs".Trim() -split '\s+'
            Write-Host "::group::$label $round/$count $($scenario.name): $($arguments -join ' ')"
            # A run without a result is retried: on a shared machine the server port can be
            # taken between choosing and binding it, which says nothing about the code.
            for ($attempt = 1; ; $attempt++) {
                $attemptArguments = if ($arguments -contains '--port') { $arguments } else { $arguments + @('--port', (Get-FreePort)) }
                $output = @(& $Harness @attemptArguments 2>&1 | ForEach-Object { "$_" })
                $exit = $LASTEXITCODE
                $start = -1
                for ($i = 0; $i -lt $output.Count; $i++) {
                    if ($output[$i].StartsWith('RESULT ')) { $start = $i }
                }
                if ($exit -eq 0 -and $start -ge 0) { break }
                $output | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
                if ($attempt -ge $kRunAttempts) {
                    Write-Host '::endgroup::'
                    Write-Host "::error title=$title::$($scenario.name) produced no result in $attempt attempts (exit code $exit)."
                    exit 2
                }
                Write-Host "::warning title=$title::$($scenario.name) produced no result (exit code $exit); retrying."
            }
            $block = @($output[$start..($output.Count - 1)] | Where-Object { $_ -match '^(RESULT |  )' })
            $block | ForEach-Object { Write-Host $_ }
            Add-Content -LiteralPath $rawFile -Value "# $label $round $($scenario.name)"
            Add-Content -LiteralPath $rawFile -Value $block
            $roundResults[$scenario.name] = ConvertFrom-ResultBlock $block
            Write-Host '::endgroup::'
        }
        $all += , $roundResults
    }
    return , $all
}

# Evaluates the checks of the given scenarios over the given rounds. Returns one row per check
# with Scenario, Check, Value, Rounds, Bound, Ok, Catches and the numeric Aggregate.
function Measure-Checks($results, [string[]] $names) {
    $rows = @()
    # A failed request or a rejected operation is not throughput, and rejecting work also
    # lowers the bytes per operation: any workload error fails the scenario's measurements.
    foreach ($scenario in $config.scenarios) {
        if ($names -notcontains $scenario.name) { continue }
        $counts = @($results | ForEach-Object {
            $r = $_[$scenario.name]
            $(if ($r.ContainsKey('errors')) { $r['errors'] } else { 0 }) +
            $(if ($r.ContainsKey('writeErrors')) { $r['writeErrors'] } else { 0 })
        })
        $total = ($counts | Measure-Object -Sum).Sum
        $rows += [pscustomobject]@{
            Scenario = $scenario.name
            Check = "$($scenario.name) workload errors"
            Value = "$total"
            Rounds = ($counts | ForEach-Object { "$_" }) -join ', '
            Bound = '= 0'
            Ok = $total -eq 0
            Catches = 'failed requests or rejected operations counted as throughput'
            Aggregate = [double]$total
            Errors = $true
        }
    }
    foreach ($check in $config.checks) {
        if ($names -notcontains $check.scenario) { continue }
        $values = @()
        $missing = 0
        foreach ($roundResults in $results) {
            $own = $roundResults[$check.scenario]
            if (-not $own -or -not $own.ContainsKey($check.metric)) { $missing++; continue }
            $value = $own[$check.metric]
            if ($check.PSObject.Properties.Name -contains 'per') {
                $reference = $roundResults[$check.per.scenario]
                if (-not $reference -or -not $reference.ContainsKey($check.per.metric) -or $reference[$check.per.metric] -le 0) { $missing++; continue }
                $value = $value / $reference[$check.per.metric]
            }
            $values += $value
        }
        $hasMin = $check.PSObject.Properties.Name -contains 'min'
        $hasMax = $check.PSObject.Properties.Name -contains 'max'
        # every round must contribute: an aggregate of the surviving rounds would hide failed runs
        $value = if ($missing -gt 0) { [double]::NaN } else { Get-Aggregate $values $hasMin }
        $label = if ($check.PSObject.Properties.Name -contains 'per') {
            "$($check.scenario).$($check.metric) / $($check.per.scenario).$($check.per.metric)"
        } else {
            "$($check.scenario).$($check.metric)"
        }
        $ok = -not [double]::IsNaN($value)
        if ($ok -and $hasMin) { $ok = $value -ge [double]$check.min }
        if ($ok -and $hasMax) { $ok = $value -le [double]$check.max }
        $rows += [pscustomobject]@{
            Scenario = $check.scenario
            Check = $label
            Value = if ($missing -gt 0) { "n/a ($missing round(s) without a value)" } elseif ([double]::IsNaN($value)) { 'n/a' } else { Format-Number $value }
            Rounds = ($values | ForEach-Object { Format-Number $_ }) -join ', '
            Bound = if ($hasMin) { ">= $($check.min)" } else { "<= $($check.max)" }
            Ok = $ok
            Catches = [string]$check.catches
            Aggregate = $value
            Max = if ($hasMax) { [double]$check.max } else { [double]::NaN }
        }
    }
    return , $rows
}

# The scenarios a set of scenarios needs: their own and the ones their checks are ratios to.
function Add-References([string[]] $names) {
    $references = @($config.checks |
        Where-Object { $names -contains $_.scenario -and $_.PSObject.Properties.Name -contains 'per' } |
        ForEach-Object { $_.per.scenario })
    return @(@($names) + $references | Select-Object -Unique)
}

$common = [string]$config.commonArgs
$selected = @($config.scenarios | ForEach-Object { $_.name })
if ($Only.Count -gt 0) {
    # a typo or a stale name must not turn the run into a silent pass
    $unknown = @($Only | Where-Object { $selected -notcontains $_ })
    if ($unknown.Count -gt 0) {
        Write-Host "::error title=$title::Unknown scenario(s): $($unknown -join ', '). Known: $($selected -join ', ')."
        exit 2
    }
    if (-not ($config.checks | Where-Object { $Only -contains $_.scenario })) {
        Write-Host "::error title=$title::No check uses the selected scenario(s): $($Only -join ', ')."
        exit 2
    }
    $selected = Add-References $Only
}

$results = Invoke-Rounds $selected $Rounds 'round'
$rows = Measure-Checks $results $selected

# Confirmation: a check that failed only counts when a fresh set of rounds of its scenario
# fails it again. A machine that was busy during one scenario does not fail the run; a
# regression fails every time.
$confirmed = @{}
$failedScenarios = @($rows | Where-Object { -not $_.Ok } | ForEach-Object { $_.Scenario } | Select-Object -Unique)
if ($confirmRounds -gt 0 -and $failedScenarios.Count -gt 0) {
    Write-Host "Confirming failed scenario(s): $($failedScenarios -join ', ')"
    $confirmNames = Add-References $failedScenarios
    $confirmResults = Invoke-Rounds $confirmNames $confirmRounds 'confirm'
    foreach ($row in (Measure-Checks $confirmResults $confirmNames)) {
        $confirmed[$row.Check] = $row
    }
}

$failures = 0
foreach ($row in $rows) {
    $row | Add-Member -NotePropertyName Confirm -NotePropertyValue ''
    $isErrors = $row.PSObject.Properties.Name -contains 'Errors'
    if ($confirmed.ContainsKey($row.Check) -and (-not $row.Ok -or $isErrors)) {
        $again = $confirmed[$row.Check]
        $row.Confirm = "$($again.Value) ($($again.Rounds))"
        if ($isErrors) {
            # A workload error in any round, first or confirmation, invalidates the
            # measurements: a confirmation that passed with rejected operations clears nothing.
            $row.Ok = $row.Ok -and $again.Ok
        }
        else {
            $row.Ok = $again.Ok
            if ($again.Ok) { $row.Confirm += ' passed' }
        }
    }
    if (-not $row.Ok) { $failures++ }
}

# Report-only metrics of every scenario (median over the rounds).
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
        "$key=$(Format-Number (Get-Median $metrics[$key]))"
    }
    $report += "| $($scenario.name) | $($parts -join ' ') |"
}

$hasConfirm = $confirmed.Count -gt 0
$summary = @(
    "## $title ($Rounds round(s), $aggregate)"
    ''
    $(if ($hasConfirm) { '| Check | Value | Rounds | Bound | Confirmation | Result | Catches |' } else { '| Check | Value | Rounds | Bound | Result | Catches |' })
    $(if ($hasConfirm) { '|---|---|---|---|---|---|---|' } else { '|---|---|---|---|---|---|' })
)
foreach ($row in $rows) {
    $result = if ($row.Ok) { 'pass' } else { 'FAIL' }
    $summary += if ($hasConfirm) {
        "| $($row.Check) | $($row.Value) | $($row.Rounds) | $($row.Bound) | $($row.Confirm) | $result | $($row.Catches) |"
    } else {
        "| $($row.Check) | $($row.Value) | $($row.Rounds) | $($row.Bound) | $result | $($row.Catches) |"
    }
}

# Lock in improvements: a ceiling far above the measured value no longer catches much.
if ($headroom -gt 0) {
    $loose = @($rows | Where-Object {
        $_.Ok -and $_.PSObject.Properties.Name -contains 'Max' -and -not [double]::IsNaN($_.Max) -and
        -not [double]::IsNaN($_.Aggregate) -and $_.Aggregate -lt $_.Max * $headroom
    })
    if ($loose.Count -gt 0) {
        $summary += ''
        $summary += "### Ceilings more than $([int][Math]::Round((1 - $headroom) * 100)) % above the measured value"
        $summary += ''
        $summary += 'If this pull request made the code cheaper, lower these ceilings in the floors file to keep the gain.'
        $summary += ''
        foreach ($row in $loose) {
            $summary += "- ``$($row.Check)``: $($row.Value) against a ceiling of $($row.Max)"
            Write-Host "::notice title=$title::$($row.Check) measured $($row.Value), ceiling $($row.Max): consider lowering the ceiling."
        }
    }
}

$summary += ''
$summary += '### Measured (report only, median)'
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
$rows | Select-Object Check, Value, Rounds, Bound, Confirm, Ok, Catches | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $OutDirectory 'results.json')
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summaryText
}

if ($failures -gt 0) {
    Write-Host "::error title=$title::$failures check(s) failed; see the summary."
    exit 1
}
Write-Host "All checks of $title passed."
exit 0
