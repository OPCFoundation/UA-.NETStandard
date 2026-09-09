# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$fixture = Join-Path $PSScriptRoot "obj/assurance-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
try {
    $kind = 'trx'
    $expected = 'failed'
    $total = 1
    $executed = 1
    $passed = 1
    $skipped = 0
    $outcome = 'Completed'
    $caseOutcome = 'Passed'
    switch ($Scenario) {
        'missing-trx' { $expected = 'missing' }
        'zero-trx' { $total = 0; $executed = 0; $passed = 0 }
        'ignored-trx' { $executed = 0; $passed = 0; $skipped = 1; $caseOutcome = 'NotExecuted' }
        'aborted-trx' { $outcome = 'Aborted' }
        'valid-trx' { $expected = 'completed' }
        'inconsistent-trx' { $total = 2 }
        'missing-mtp' { $kind = 'mtp-trx'; $expected = 'missing' }
        'valid-mtp' { $kind = 'mtp-trx'; $expected = 'completed' }
        'missing-sarif' { $kind = 'sarif'; $expected = 'missing' }
        'valid-sarif' { $kind = 'sarif'; $expected = 'completed' }
        'failed-sarif' { $kind = 'sarif' }
        'producer-record' { $expected = 'completed' }
        default { throw 'Unknown test scenario.' }
    }
    if ($kind -eq 'sarif' -and $expected -ne 'missing') {
        $success = $Scenario -ne 'failed-sarif'
        @{version='2.1.0'; runs=@(@{
            tool=@{driver=@{name='CodeQL';version='2.22.0'}}
            invocations=@(@{executionSuccessful=$success})
            results=@(@{message=@{text='RESTRICTED_SENTINEL'};ruleId='private-rule'})
        })} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'results.sarif')
    }
    elseif ($expected -ne 'missing') {
        $results = if ($total -gt 0) {
            "<UnitTestResult testId='case-1' outcome='$caseOutcome'><Output><StdOut>RESTRICTED_SENTINEL</StdOut></Output></UnitTestResult>"
        } else { '' }
        @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results>$results</Results><ResultSummary outcome="$outcome">
<Counters total="$total" executed="$executed" passed="$passed" failed="0" notExecuted="$skipped" />
</ResultSummary></TestRun>
"@ | Set-Content (Join-Path $fixture 'results.trx')
    }
    $output = Join-Path $fixture 'summary.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/assurance-results.ps1') `
        -ResultsPath $fixture -Kind $kind -OutputPath $output -Enforce
    $code = $LASTEXITCODE
    if (-not (Test-Path $output)) { throw 'Result producer did not emit its public summary.' }
    $text = Get-Content $output -Raw
    $actual = $text | ConvertFrom-Json
    if ($actual.status -ne $expected) { throw "Expected $expected, got $($actual.status)." }
    if (($code -eq 0) -ne ($expected -eq 'completed')) { throw 'Gate exit does not match result status.' }
    if ($text.Contains('RESTRICTED_SENTINEL') -or $text.Contains('private-rule')) { throw 'Private result leaked.' }
    if ($expected -eq 'completed' -and $kind -ne 'sarif' -and $actual.counts.executed -ne 1) {
        throw 'Executed count must come from the result document.'
    }
    if ($Scenario -eq 'producer-record') {
        # Synthetic CI metadata stays inside this disposable fixture. Nothing is
        # published and the source identity must still be observed from git.
        $sha = (& git -C $root rev-parse HEAD).Trim()
        $env:GITHUB_ACTIONS = 'true'
        $env:GITHUB_RUN_ID = 'fixture-123'
        $env:GITHUB_RUN_ATTEMPT = '2'
        $env:GITHUB_JOB = 'fixture-job'
        $env:GITHUB_WORKFLOW_SHA = $sha
        $recordPath = Join-Path $fixture 'public/security.job.json'
        & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/write-assurance-job.ps1') `
            -Project 'tests/Opc.Ua.Core.Security.Tests/Opc.Ua.Core.Security.Tests.csproj' `
            -ResultsPath $fixture -OutputPath $recordPath -Workflow '.github/workflows/buildandtest.yml'
        if ($LASTEXITCODE -ne 0) { throw 'Job producer failed.' }
        $record = Get-Content $recordPath -Raw | ConvertFrom-Json
        if ($record.sourceSha -cne $sha -or $record.producer.runId -ne 'fixture-123' -or
            $record.producer.attempt -ne 2 -or $record.counts.executed -ne 1 -or
            -not (Test-Path (Join-Path (Split-Path $recordPath) $record.resultDocument))) {
            throw 'Producer lost observed source/run/result identity.'
        }
    }
    exit 0
}
finally {
    Remove-Item $fixture -Recurse -Force
}
