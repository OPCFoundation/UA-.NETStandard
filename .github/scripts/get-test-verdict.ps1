<#
 .SYNOPSIS
    Decides whether one test project's run passed, and why.

 .DESCRIPTION
    Dot-sourced by .github/scripts/run-dotnet-tests.ps1 and exercised directly by
    tests/Opc.Ua.Tools.Tests/CiTestVerdictTests.cs. It is separated from the
    executor because it is the single decision that determines whether continuous
    integration reports green: a rule that is too strict reports a false red, and
    one that is too lax lets a genuinely broken suite merge. Keeping it in its own
    file lets it be tested without building or running anything.

    The emitted TRX decides the verdict, not the 'dotnet test' exit code. A
    non-zero exit is tolerated only after the strict TRX evaluator established
    completed reports with consistent counters, recorded results and no run-level
    errors. Green aggregate counters alone cannot establish completion.
#>

<#
 .SYNOPSIS
    Returns the verdict for a single project's run.

 .PARAMETER TrxFileCount
    Number of TRX files the run produced.

 .PARAMETER Total
    Total number of tests recorded across those files.

 .PARAMETER Passed
    Recorded tests that actually ran and passed. A run whose tests were all
    skipped reports Total > 0 with Passed = 0 and must not be treated as green.

 .PARAMETER Failed
    Sum of every TRX counter that represents a failed, non-passing, or
    unfinished test: failed, error, timeout, aborted, passedButRunAborted,
    inconclusive, notRunnable, disconnected, warning, completed, inProgress,
    and pending. This is the same fail-closed set the Azure gate uses.

 .PARAMETER ExitCode
    Exit code of the 'dotnet test' process.

 .PARAMETER TimedOut
    Whether the executor killed the process at its per-project ceiling.

 .PARAMETER TimeoutMinutes
    The ceiling that was applied, used only to phrase the reason.

 .PARAMETER ReportsCompleted
    Whether strict validation of the actual result documents succeeded.

 .OUTPUTS
    An object with Passed (bool), Tolerated (bool) and Reason (string). Tolerated
    is only ever true alongside Passed, and always carries a reason so the
    condition stays visible in the job summary.
#>
function Get-TestRunVerdict
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [int] $TrxFileCount,
        [Parameter(Mandatory = $true)] [int] $Total,
        [Parameter(Mandatory = $true)] [int] $Passed,
        [Parameter(Mandatory = $true)] [int] $Failed,
        [Parameter(Mandatory = $true)] [int] $ExitCode,
        [Parameter(Mandatory = $true)] [bool] $TimedOut,
        [Parameter(Mandatory = $true)] [bool] $ReportsCompleted,
        [Parameter(Mandatory = $false)] [int] $TimeoutMinutes = 0
    )

    # A timeout is never benign: the executor killed a process that was still
    # running, so whatever the partial results say, the run did not complete.
    if ($TimedOut) {
        return [pscustomobject]@{
            Passed    = $false
            Tolerated = $false
            Reason    = "The test run exceeded the $TimeoutMinutes-minute per-project ceiling."
        }
    }

    # Every mainline project runs on VSTest, so silence here is a broken run
    # rather than an empty one - reporting it as a pass would hide a suite that
    # stopped executing entirely.
    if ($TrxFileCount -le 0) {
        return [pscustomobject]@{
            Passed    = $false
            Tolerated = $false
            Reason    = ('No TRX was produced. Every mainline test project runs on VSTest and must emit one; ' +
                'a Microsoft.Testing.Platform project belongs in a dedicated job instead of this matrix.')
        }
    }

    # Evaluated before the exit code so a host that died mid-run is rejected
    # rather than tolerated below.
    if ($Failed -gt 0) {
        return [pscustomobject]@{
            Passed    = $false
            Tolerated = $false
            Reason    = "$Failed test(s) failed, were non-passing, or did not finish."
        }
    }

    if ($Total -le 0) {
        return [pscustomobject]@{
            Passed    = $false
            Tolerated = $false
            Reason    = 'No tests were recorded. The project is applicable to this profile, so discovery is broken.'
        }
    }

    # Total counts every recorded test including skipped ones, so a suite that
    # was entirely skipped still reports Total > 0 with nothing verified. The
    # executor's contract is that an applicable project executes at least one
    # test, so require a passing test rather than merely a recorded one.
    if ($Passed -le 0) {
        return [pscustomobject]@{
            Passed    = $false
            Tolerated = $false
            Reason    = ("None of the $Total recorded test(s) passed - every one was skipped or not executed. " +
                'The project is applicable to this profile, so it must run at least one test.')
        }
    }

    if (-not $ReportsCompleted) {
        return [pscustomobject]@{
            Passed    = $false
            Tolerated = $false
            Reason    = 'The result documents do not prove completed, consistent, successful execution.'
        }
    }

    if ($ExitCode -ne 0) {
        return [pscustomobject]@{
            Passed    = $true
            Tolerated = $true
            Reason    = ("Tolerated: $Passed of $Total test(s) passed with no failure, but the host exited " +
                "with code $ExitCode (at-exit stall).")
        }
    }

    return [pscustomobject]@{ Passed = $true; Tolerated = $false; Reason = '' }
}

<#
.SYNOPSIS
Distinguishes an intentionally empty matrix from missing or malformed discovery.
#>
function Test-CiMatrixSelected
{
    param([Parameter(Mandatory = $true)][string] $Matrix)

    $entries = ConvertFrom-Json -InputObject $Matrix -NoEnumerate -ErrorAction Stop
    if ($entries -isnot [array]) {
        throw 'Discovery did not produce a matrix array.'
    }
    return $entries.Count -gt 0
}

<#
.SYNOPSIS
Requires success for selected jobs and permits skips only for unselected jobs.
#>
function Get-CiJobVerdict
{
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary] $Results,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary] $Expected
    )

    $bad = @()
    foreach ($name in $Expected.Keys) {
        if (-not $Results.Contains($name) -or $Expected[$name] -isnot [bool]) {
            $bad += "$name=missing selection or result"
        }
    }
    foreach ($name in $Results.Keys) {
        $result = $Results[$name]
        if (-not $Expected.Contains($name) -or
            ($result -cne 'success' -and -not ($result -ceq 'skipped' -and $Expected[$name] -eq $false))) {
            $bad += "$name=$result"
        }
    }
    return [pscustomobject]@{ Passed = $bad.Count -eq 0; Failures = $bad }
}

<#
.SYNOPSIS
Requires each selected batch and each applicable coverage-enabled project fragment.
#>
function Assert-CiCoverage
{
    param(
        [Parameter(Mandatory = $true)][string] $Matrix,
        [Parameter(Mandatory = $true)][string] $ArtifactsDirectory
    )

    $null = Test-CiMatrixSelected $Matrix
    $entries = @(ConvertFrom-Json -InputObject $Matrix)
    $artifacts = @()
    if (Test-Path -LiteralPath $ArtifactsDirectory) {
        $artifacts = @(Get-ChildItem -LiteralPath $ArtifactsDirectory -Directory -Filter 'dotnet-results-*')
    }
    if ($artifacts.Count -ne $entries.Count) {
        throw 'Expected test-job artifacts are missing or duplicated; coverage is incomplete.'
    }
    $ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $reports = 0
    foreach ($entry in $entries) {
        if ($entry.id -cnotmatch '^[A-Za-z0-9._-]+$' -or -not $ids.Add($entry.id) -or
            $entry.coverage -isnot [bool]) {
            throw 'Invalid or duplicate coverage matrix entry.'
        }
        $artifact = Join-Path $ArtifactsDirectory "dotnet-results-$($entry.id)"
        $summaryPath = Join-Path $artifact 'batch-summary.json'
        if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
            throw 'A selected batch did not publish its per-project result summary.'
        }
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        if ($summary.customTestTarget -cne $entry.customTestTarget -or
            $summary.framework -cne $entry.framework -or $summary.configuration -cne $entry.configuration -or
            $summary.filter -cne $entry.filter -or $summary.coverage -isnot [bool] -or
            $summary.coverage -ne $entry.coverage -or $summary.inputScope -cne 'public') {
            throw 'The batch summary does not match the selected public CI profile.'
        }
        $projects = @($entry.projects -split ';' | Where-Object { $_ })
        if ($projects.Count -eq 0 -or @($summary.projects).Count -ne $projects.Count -or
            @($projects | Select-Object -Unique).Count -ne $projects.Count) {
            throw 'The batch summary does not cover exactly the selected projects.'
        }
        foreach ($project in $projects) {
            $records = @($summary.projects | Where-Object { $_.project -ceq $project })
            if ($records.Count -ne 1) {
                throw 'A selected project has a missing or duplicate result.'
            }
            $record = $records[0]
            if ($record.outcome -ceq 'not-applicable') {
                $explicitlyUnsupported = $record.notApplicableRule -ceq 'SupportedTestTargets' -and
                    @($record.supportedTestTargets).Count -gt 0 -and
                    $entry.customTestTarget -cnotin @($record.supportedTestTargets)
                if ($record.notApplicableRule -cne 'RestrictForLegacyTfm' -and -not $explicitlyUnsupported) {
                    throw 'An unverified project skip cannot excuse missing coverage.'
                }
                continue
            }
            if ($record.outcome -cne 'passed' -or $record.passed -le 0 -or
                $record.total -lt $record.passed -or $record.failed -ne 0) {
                throw 'A selected applicable project did not complete successfully.'
            }
            if (-not $entry.coverage) {
                continue
            }
            $stem = [System.IO.Path]::GetFileNameWithoutExtension($project)
            $directory = Join-Path $artifact $stem
            $fragments = @()
            if (Test-Path -LiteralPath $directory -PathType Container) {
                $fragments = @(Get-ChildItem -LiteralPath $directory -Recurse -File -Filter '*.cobertura.xml')
            }
            if ($fragments.Count -eq 0) {
                throw 'A selected coverage-enabled project did not publish its coverage fragment.'
            }
            $reports += $fragments.Count
        }
    }
    return [pscustomobject]@{ Reports = $reports; HasReports = $reports -gt 0 }
}
