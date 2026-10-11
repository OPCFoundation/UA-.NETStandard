<#
 .SYNOPSIS
    Refreshes .github/ci-test-durations.json from the results of a CI run.

 .DESCRIPTION
    get-ci-matrix.ps1 packs test projects into batches by the weights in
    .github/ci-test-durations.json. The weights only steer balance - a stale or
    missing weight never changes what runs - but batches drift out of balance as
    suites grow, so refresh them from a recent run now and then:

        gh run download <run-id> --pattern 'dotnet-results-*' --dir ./ci-results
        ./.github/scripts/update-test-durations.ps1 -ResultsPath ./ci-results -Source 'Run <run-id>'

    Each project's weight is its slowest profile's test minutes, rounded up,
    plus one minute for its incremental build in a batch that already built the
    shared dependencies. The cold build of those dependencies is a fixed cost of
    the batch, not of any project, so first-in-batch build time is ignored.
    Projects the run did not execute keep their current weight.

 .PARAMETER ResultsPath
    Directory holding the downloaded 'dotnet-results-*' artifacts. Every
    batch-summary.json below it is read.

 .PARAMETER Source
    Short description of the run the weights came from, recorded in the table.

 .PARAMETER DurationsPath
    The table to update. Defaults to .github/ci-test-durations.json.
#>

Param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsPath,
    [Parameter(Mandatory = $true)]
    [string] $Source,
    [string] $DurationsPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($DurationsPath)) {
    $DurationsPath = Join-Path $PSScriptRoot '..' 'ci-test-durations.json'
}

$table = Get-Content -LiteralPath $DurationsPath -Raw | ConvertFrom-Json -AsHashtable
$weights = @{}
foreach ($entry in $table.projects.GetEnumerator()) {
    $weights[$entry.Key] = [int]$entry.Value
}

$summaries = @(Get-ChildItem -LiteralPath $ResultsPath -Recurse -File -Filter 'batch-summary.json')
if ($summaries.Count -eq 0) {
    throw "No batch-summary.json below '$ResultsPath'. Download the run's 'dotnet-results-*' artifacts first."
}

$slowest = @{}
foreach ($summary in $summaries) {
    $document = Get-Content -LiteralPath $summary.FullName -Raw | ConvertFrom-Json
    foreach ($record in $document.projects) {
        # A failed or timed-out project's duration says nothing about a healthy
        # run, and a not-applicable one did not run at all.
        if ($record.outcome -ne 'passed' -or $null -eq $record.testSeconds) {
            continue
        }
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($record.project)
        if (-not $slowest.ContainsKey($stem) -or $record.testSeconds -gt $slowest[$stem]) {
            $slowest[$stem] = [int]$record.testSeconds
        }
    }
}
if ($slowest.Count -eq 0) {
    throw "The summaries below '$ResultsPath' record no passed project with a duration."
}

foreach ($entry in $slowest.GetEnumerator()) {
    $weights[$entry.Key] = [int][System.Math]::Ceiling($entry.Value / 60.0) + 1
}

$keys = [System.Collections.Generic.List[string]]::new()
foreach ($key in $weights.Keys) {
    $keys.Add($key)
}
$keys.Sort([System.StringComparer]::Ordinal)
$projects = [ordered]@{}
foreach ($key in $keys) {
    $projects[$key] = $weights[$key]
}

[ordered]@{
    source         = $Source
    defaultMinutes = [int]$table.defaultMinutes
    projects       = $projects
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $DurationsPath -Encoding utf8NoBOM

Write-Host "Updated $($slowest.Count) of $($projects.Count) weights in '$DurationsPath' from $($summaries.Count) batch summaries."
