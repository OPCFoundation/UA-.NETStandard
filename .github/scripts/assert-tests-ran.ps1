<#
 .SYNOPSIS
    Fails when a filtered test run executed no tests.

 .DESCRIPTION
    The opt-in stress, stability and long-haul workflows select their tests with
    a 'dotnet test --filter'. A filter that no longer matches anything - because
    a test moved to another project or its category was renamed - makes
    'dotnet test' print "No test matches the given testcase filter" and exit 0,
    so the workflow stays green while testing nothing. The Connection Stability
    workflow did exactly that after the stability test moved from
    Opc.Ua.Client.Tests to Opc.Ua.Sessions.Tests.

    Reads every TRX file under the results directory and fails unless at least
    one test executed and passed.

 .PARAMETER ResultsDirectory
    Directory the run wrote its TRX files to (searched recursively).
#>
param(
    [Parameter(Mandatory = $true)] [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'

$trxFiles = @(Get-ChildItem -Path $ResultsDirectory -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
if ($trxFiles.Count -eq 0)
{
    Write-Host "::error::No TRX file under '$ResultsDirectory': the test run produced no results."
    exit 1
}

$executed = 0
$passed = 0
foreach ($trx in $trxFiles)
{
    [xml] $doc = Get-Content -LiteralPath $trx.FullName -Raw
    $counters = $doc.TestRun.ResultSummary.Counters
    if ($null -ne $counters)
    {
        $executed += [int] $counters.executed
        $passed += [int] $counters.passed
    }
}

Write-Host "Executed $executed test(s), $passed passed, across $($trxFiles.Count) TRX file(s)."
if ($executed -eq 0)
{
    Write-Host "::error::The test filter matched no runnable test. Check that the project and category in the workflow still exist."
    exit 1
}
if ($passed -eq 0)
{
    Write-Host "::error::$executed test(s) executed but none passed."
    exit 1
}
