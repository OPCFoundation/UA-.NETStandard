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
    non-zero exit is tolerated when - and only when - the results record at least
    one *passing* test and no failure of any kind, which means the host died
    during process exit after the last test and every teardown had already run.
    This matches the Azure gate in .azurepipelines/test.yml. A host that dies
    mid-run leaves a non-zero failed/aborted/passedButRunAborted counter and is
    rejected, and a suite whose tests were all skipped is rejected as well.
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
    Recorded tests that failed, errored, timed out, aborted, or passed in a run
    that was aborted.

 .PARAMETER ExitCode
    Exit code of the 'dotnet test' process.

 .PARAMETER TimedOut
    Whether the executor killed the process at its per-project ceiling.

 .PARAMETER TimeoutMinutes
    The ceiling that was applied, used only to phrase the reason.

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
            Reason    = "$Failed test(s) failed, errored, timed out or aborted."
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
