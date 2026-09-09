<#
Runs the applicable replay projects, including harness negative controls.
Source and output paths do not depend on the caller's working directory.
#>
[CmdletBinding()]
param(
    [ValidateSet('net48', 'net8.0', 'net9.0', 'net10.0')]
    [string]$Framework = 'net10.0',
    [string]$ResultsDirectory,
    [ValidateRange(1, 240)]
    [int]$HangTimeoutMinutes = 20,
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ("opcua-fuzz-tests-" + [guid]::NewGuid().ToString('N'))
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$env:CustomTestTarget = $Framework
$names = @('Opc.Ua.Fuzzing.Tests', 'Opc.Ua.Encoders.Fuzz.Tests', 'Opc.Ua.Certificates.Fuzz.Tests')
if ($Framework -ne 'net48') {
    $names += 'Opc.Ua.Network.Fuzz.Tests', 'Opc.Ua.PubSub.Fuzz.Tests'
}
$failed = @()
foreach ($name in $names) {
    $project = Join-Path $root "fuzzing/$name/$name.csproj"
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Required replay project missing: $project" }
    $arguments = @(
        'test', $project, '-c', 'Release', '-f', $Framework, '-m:1', '-p:FuzzCoverage=true',
        '--results-directory', (Join-Path $ResultsDirectory $name),
        '--logger', 'trx;LogFileName=replay.trx',
        # Backstop for a genuine hang only. These suites run thousands of cases and
        # spawn watchdog child processes, so a short inactivity budget produces false
        # failures (blame aborts, fails to write a dump, and returns a nonzero exit).
        '--blame-hang-timeout', ("{0}m" -f $HangTimeoutMinutes),
        '--collect', 'XPlat Code Coverage',
        '--settings', (Join-Path $root 'fuzzing/fuzz-parity.runsettings')
    )
    if ($NoRestore) { $arguments += '--no-restore' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        $failed += $name
        continue
    }
    $trxPath = Join-Path $ResultsDirectory "$name/replay.trx"
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw "Replay returned success without a test result: $name"
    }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.executed -le 0 -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) {
        throw "Replay must execute tests without failures or skips: $name"
    }
}
if ($failed.Count -gt 0) { throw "Fuzz replay failed: $($failed -join ', '). Results: $ResultsDirectory" }
Write-Output "Fuzz replay results: $ResultsDirectory"
