<#
 .SYNOPSIS
    Builds and runs a batch of test projects for one CI profile.

 .DESCRIPTION
    Shared by .github/workflows/buildandtest.yml and .github/workflows/nightly.yml
    through the .github/actions/run-dotnet-tests composite action. One matrix
    entry carries several projects, so this script keeps per-project results: it
    decides applicability, builds, tests, evaluates the emitted TRX and reports a
    row per project.

    Applicability is decided by evaluating the project rather than by guessing
    from its path. Directory.Build.targets turns a project that opts into
    RestrictForLegacyTfm into an empty no-op shell on the legacy profiles, and
    marks the shell 'IsTestProject=false'. A shell is recorded as not applicable
    with its reason - it is explicitly not counted as a passing test run.

    A project that is applicable must produce a TRX recording at least one
    executed test. A run that emits nothing, or emits a TRX with no results, is a
    broken discovery rather than a pass, and fails here.

    The recorded results decide the verdict, not the 'dotnet test' exit code. A
    non-zero exit is tolerated when - and only when - the TRX records at least
    one test and no failure, error, timeout, abort or passedButRunAborted: that
    combination means the host died during process exit, after the last test and
    every teardown had already run. This matches the Azure gate in
    .azurepipelines/test.yml. It was originally assumed to be a macOS-only
    quirk, but Windows hosts do it too (observed on run 35714133848, job
    'test-windows-net48 (5/30)', where Opc.Ua.Client.Tests reported 256 passed
    and 0 failed and the host still exited 1). A host that dies mid-run is not
    tolerated - that leaves a non-zero counter, which fails above.

 .PARAMETER Projects
    Semicolon-separated, repository-relative project paths to run in order.

 .PARAMETER CustomTestTarget
    The value passed as /p:CustomTestTarget for build and test. This, not
    '--framework', is what pins the whole stack to a single target framework
    (see targets.props).

 .PARAMETER Framework
    The target framework the tests actually execute on. It differs from
    CustomTestTarget for the standard profiles: netstandard2.0 hosts its tests on
    net48 and netstandard2.1 hosts them on net8.0.

 .PARAMETER Configuration
    Debug or Release.

 .PARAMETER Filter
    NUnit category filter. Empty runs every category, which is what the
    long-running and durable tiers do.

 .PARAMETER HangTimeout
    --blame-hang-timeout for a single test.

 .PARAMETER PerProjectTimeoutMinutes
    Wall-clock ceiling for one project, shared by that project's build and test
    invocations. The blame collector only reacts to inactivity inside the test
    host and can itself fail to unwind, so this is the backstop that turns a
    stuck run into a named per-project failure instead of an unexplained job
    timeout.

    It is a single combined budget on purpose. get-ci-matrix.ps1 derives each
    job's `timeout-minutes` from it as `20 + projectCount * thisValue`; if the
    build and the test each got the full value, a batch could burn twice that
    and GitHub would cancel the job - with no per-project annotation and no
    results - before the executor ever reached the ceiling it promises here.

 .PARAMETER ResultsDirectory
    Directory that receives one subdirectory of results per project, plus the
    machine-readable batch summary.

 .PARAMETER Coverage
    Collect Cobertura coverage. Never set for a .NET Framework test host:
    coverlet.collector ships build assets for net8.0 and newer only, so the
    collector cannot load there and VSTest would only warn.

 .PARAMETER QuietOutput
    Write each project's build and test output to a log file under the results
    directory instead of the console. Use it for inputs that must not reach a
    world-readable job log: a failing fuzz test prints a base64 reproducer, so
    a replay of unpublished crash inputs has to keep its output in the results
    tree.
#>

Param(
    [Parameter(Mandatory = $true)]
    [string] $Projects,
    [Parameter(Mandatory = $true)]
    [string] $CustomTestTarget,
    [Parameter(Mandatory = $true)]
    [string] $Framework,
    [Parameter(Mandatory = $true)]
    [string] $Configuration,
    [string] $Filter = '',
    [string] $HangTimeout = '10m',
    [int]    $PerProjectTimeoutMinutes = 30,
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [switch] $Coverage,
    [switch] $QuietOutput
)

$ErrorActionPreference = 'Stop'

# The verdict rule lives in its own file so it can be tested without building or
# running anything - see tests/Opc.Ua.Tools.Tests/CiTestVerdictTests.cs.
. (Join-Path $PSScriptRoot 'get-test-verdict.ps1')

$projectList = @($Projects -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($projectList.Count -eq 0) {
    throw 'No projects were supplied. An entry that runs nothing would report success without testing anything.'
}

$null = New-Item -ItemType Directory -Path $ResultsDirectory -Force
$resultsRoot = (Resolve-Path -LiteralPath $ResultsDirectory).Path

<#
 .SYNOPSIS
    Runs 'dotnet' with the supplied arguments, streaming its output to the log
    and killing the process tree if the project's remaining budget runs out.

 .DESCRIPTION
    System.Diagnostics.Process is used instead of the call operator so the child
    can be killed on timeout, and instead of Start-Process so the arguments -
    which include filter expressions containing '&' and '!' - are passed through
    verbatim rather than re-parsed by a shell.

    The caller passes a running stopwatch covering the whole project rather than
    a fresh per-invocation timeout, so the build and the test share one ceiling.
    See the PerProjectTimeoutMinutes help above for why the two must not each
    get the full value.
#>
function Invoke-Dotnet(
    [string[]] $arguments,
    [System.Diagnostics.Stopwatch] $budget,
    [int] $budgetMinutes,
    [string] $logPath)
{
    Write-Host "dotnet $($arguments -join ' ')"

    $remainingMs = ([long]$budgetMinutes * 60 * 1000) - $budget.ElapsedMilliseconds
    if ($remainingMs -le 0) {
        Write-Host ("::error title=CI test runner::The $budgetMinutes-minute per-project ceiling was " +
            "already spent before 'dotnet $($arguments[0])' could start.")
        return [pscustomobject]@{ ExitCode = -1; TimedOut = $true }
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    foreach ($argument in $arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false

    $writer = $null
    $subscriptions = @()
    if ($QuietOutput) {
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $writer = [System.IO.StreamWriter]::new($logPath, $true)
        $writer.AutoFlush = $true
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if ($QuietOutput) {
            # Event-driven rather than ReadToEnd on both streams in turn, which
            # deadlocks as soon as one pipe buffer fills.
            $handler = {
                if ($null -ne $EventArgs.Data) {
                    $Event.MessageData.WriteLine($EventArgs.Data)
                }
            }
            $subscriptions += Register-ObjectEvent -InputObject $process -EventName OutputDataReceived -Action $handler -MessageData $writer
            $subscriptions += Register-ObjectEvent -InputObject $process -EventName ErrorDataReceived -Action $handler -MessageData $writer
        }

        $null = $process.Start()
        if ($QuietOutput) {
            $process.BeginOutputReadLine()
            $process.BeginErrorReadLine()
        }

        if (-not $process.WaitForExit([int][System.Math]::Min([long][int]::MaxValue, $remainingMs))) {
            Write-Host ("::error title=CI test runner::'dotnet $($arguments[0])' exhausted the " +
                "$budgetMinutes-minute per-project ceiling and was killed.")
            $process.Kill($true)
            $null = $process.WaitForExit(60 * 1000)
            return [pscustomobject]@{ ExitCode = -1; TimedOut = $true }
        }
        if ($QuietOutput) {
            # The timed overload returns as soon as the process exits; only the
            # parameterless one waits for the asynchronous readers to drain, and
            # without it the tail of the log is lost.
            $process.WaitForExit()
        }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; TimedOut = $false }
    }
    finally {
        foreach ($subscription in $subscriptions) {
            Unregister-Event -SourceIdentifier $subscription.Name -ErrorAction SilentlyContinue
        }
        if ($null -ne $writer) {
            $writer.Dispose()
        }
        $process.Dispose()
    }
}

<#
 .SYNOPSIS
    Evaluates the properties that decide whether a project takes part in this
    profile, without restoring or building it.
#>
function Get-ProjectProperties([string] $project)
{
    $output = & dotnet msbuild $project `
        '-getProperty:IsTestProject;TargetFrameworks;TargetFramework' `
        "-p:CustomTestTarget=$CustomTestTarget" `
        "-p:Configuration=$Configuration" `
        -nologo | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Could not evaluate '$project' for CustomTestTarget=$CustomTestTarget (exit code $LASTEXITCODE)."
    }

    # MSBuild prints the requested properties as a JSON document, but an
    # evaluation-time warning would be printed alongside it, so take the document
    # rather than the whole stream.
    $start = $output.IndexOf('{')
    $end = $output.LastIndexOf('}')
    if ($start -lt 0 -or $end -le $start) {
        throw "Evaluating '$project' produced no property document.`n$output"
    }
    return ($output.Substring($start, $end - $start + 1) | ConvertFrom-Json).Properties
}

<#
 .SYNOPSIS
    Sums the counters of every TRX below a directory.
#>
function Measure-TestResults([string] $directory)
{
    $total = 0
    $passed = 0
    $failed = 0
    $nonPassingCounters = @(
        'failed',
        'error',
        'timeout',
        'aborted',
        'passedButRunAborted',
        'inconclusive',
        'notRunnable',
        'disconnected',
        'warning',
        'completed',
        'inProgress',
        'pending')
    $trxFiles = @(Get-ChildItem -LiteralPath $directory -Recurse -File -Filter *.trx -ErrorAction SilentlyContinue)
    foreach ($trxFile in $trxFiles) {
        # XmlDocument.Load rather than [xml](Get-Content): PubSub emits several
        # megabytes of results and the array-of-lines cast fails on files that
        # size.
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($trxFile.FullName)
        foreach ($counters in $document.GetElementsByTagName('Counters')) {
            $total += Get-CounterValue $counters 'total'
            $passed += Get-CounterValue $counters 'passed'
            foreach ($name in $nonPassingCounters) {
                $failed += Get-CounterValue $counters $name
            }
        }
    }
    return [pscustomobject]@{ Files = $trxFiles.Count; Total = $total; Passed = $passed; Failed = $failed }
}

<#
 .SYNOPSIS
    Reads a TRX counter attribute, treating an absent attribute as zero.
#>
function Get-CounterValue([System.Xml.XmlElement] $element, [string] $name)
{
    $raw = $element.GetAttribute($name)
    if ([string]::IsNullOrEmpty($raw)) {
        return 0
    }
    return [int]$raw
}

$records = @()
foreach ($project in $projectList) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "::group::$stem ($CustomTestTarget / $Framework / $Configuration)"

    $record = [ordered]@{
        project       = $project
        customTarget  = $CustomTestTarget
        framework     = $Framework
        configuration = $Configuration
        outcome       = 'failed'
        reason        = ''
        total         = 0
        passed        = 0
        failed        = 0
    }

    try {
        if (-not (Test-Path -LiteralPath $project)) {
            throw "The project file does not exist."
        }

        $properties = Get-ProjectProperties $project
        $declared = @()
        if (-not [string]::IsNullOrWhiteSpace($properties.TargetFrameworks)) {
            $declared = @($properties.TargetFrameworks -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        }
        elseif (-not [string]::IsNullOrWhiteSpace($properties.TargetFramework)) {
            $declared = @($properties.TargetFramework)
        }

        if ($properties.IsTestProject -eq 'false') {
            # Directory.Build.targets emptied the project for this profile. The
            # reason is recorded so a profile that skips everything is visible
            # rather than looking like a clean run.
            $record.outcome = 'not-applicable'
            $record.reason = "RestrictForLegacyTfm makes this an empty shell for CustomTestTarget=$CustomTestTarget."
            Write-Host "Not applicable: $($record.reason)"
            continue
        }

        if ($declared -notcontains $Framework) {
            throw ("The project builds [$($declared -join ', ')] for CustomTestTarget=$CustomTestTarget " +
                "but this profile hosts its tests on $Framework. Either follow CustomTestTarget in the " +
                "project's TargetFrameworks or set RestrictForLegacyTfm so the profile skips it explicitly.")
        }

        $projectResults = Join-Path $resultsRoot $stem
        $null = New-Item -ItemType Directory -Path $projectResults -Force
        $logPath = Join-Path $projectResults 'build-and-test.log'

        $buildArguments = @(
            'build', $project,
            '--configuration', $Configuration,
            '--framework', $Framework,
            "/p:CustomTestTarget=$CustomTestTarget")
        if ($Coverage) {
            # Not what switches coverlet on - the XPlat data collector does the
            # instrumentation. It turns ContinuousIntegrationBuild off in
            # common.props so SourceLink does not rewrite the source paths
            # embedded in the pdb to deterministic '/_/...' paths, which the
            # changed-lines gate needs in order to map coverage back onto the
            # working tree. It has to be set for the build, not just the test.
            $buildArguments += '/p:CollectCoverage=true'
        }

        # One stopwatch for the build and the test together. Giving each its own
        # PerProjectTimeoutMinutes would let a project burn twice the amount
        # get-ci-matrix.ps1 budgeted for it in the job's timeout-minutes, so
        # GitHub would cancel the whole batch - with no annotation and no
        # results - instead of this executor failing that one project.
        $projectBudget = [System.Diagnostics.Stopwatch]::StartNew()

        $build = Invoke-Dotnet $buildArguments $projectBudget $PerProjectTimeoutMinutes $logPath
        if ($build.TimedOut) {
            throw ("The build exhausted the $PerProjectTimeoutMinutes-minute per-project ceiling, " +
                'which the build and the test share.')
        }
        if ($build.ExitCode -ne 0) {
            throw "The build failed with exit code $($build.ExitCode)."
        }

        $testArguments = @(
            'test', $project,
            '--no-build',
            '--configuration', $Configuration,
            '--framework', $Framework,
            "/p:CustomTestTarget=$CustomTestTarget",
            '--logger', 'trx',
            '--results-directory', $projectResults,
            # Kill (and name) any single test that outlives the profile's hang
            # budget so a silent hang surfaces as "Test <Name> exceeded the
            # configured timeout" rather than burning the job timeout with
            # nothing to go on. The mini dump carries every thread's stack, which
            # is what a teardown or shutdown hang needs, without serialising the
            # managed heap of a server-hosting test process.
            '--blame-hang-timeout', $HangTimeout,
            '--blame-hang-dump-type', 'mini',
            # Crash detection: the sequence file records which test was running
            # when the host exited, and the mini dump captures the native state.
            # Only written on an actual crash, so healthy runs pay no cost.
            '--blame-crash',
            '--blame-crash-dump-type', 'mini')
        if ($Coverage) {
            $testArguments += @(
                '/p:CollectCoverage=true',
                '--collect:XPlat Code Coverage',
                '--settings', './tests/coverlet.runsettings.xml')
        }
        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $testArguments += @('--filter', $Filter)
        }

        $test = Invoke-Dotnet $testArguments $projectBudget $PerProjectTimeoutMinutes $logPath

        $results = Measure-TestResults $projectResults
        $record.total = $results.Total
        $record.passed = $results.Passed
        $record.failed = $results.Failed

        if ($results.Files -eq 0) {
            throw ('No TRX was produced. Every mainline test project runs on VSTest and must emit one; ' +
                'a Microsoft.Testing.Platform project belongs in a dedicated job instead of this matrix.')
        }

        $verdict = Get-TestRunVerdict `
            -TrxFileCount $results.Files `
            -Total $results.Total `
            -Passed $results.Passed `
            -Failed $results.Failed `
            -ExitCode $test.ExitCode `
            -TimedOut $test.TimedOut `
            -TimeoutMinutes $PerProjectTimeoutMinutes
        if (-not $verdict.Passed) {
            throw $verdict.Reason
        }
        if ($verdict.Tolerated) {
            # Surfaced as a warning annotation so a host that keeps dying at exit
            # stays visible instead of being silently swallowed.
            $record.reason = $verdict.Reason
            Write-Host "::warning title=$stem ($CustomTestTarget/$Configuration)::$($verdict.Reason)"
        }

        $record.outcome = 'passed'
        Write-Host "Passed: $($results.Passed)/$($results.Total)."
    }
    catch {
        $record.outcome = 'failed'
        $record.reason = $_.Exception.Message
        Write-Host "::error title=$stem ($CustomTestTarget/$Configuration)::$($record.reason)"
    }
    finally {
        $records += [pscustomobject]$record
        Write-Host '::endgroup::'
    }
}

$summaryPath = Join-Path $resultsRoot 'batch-summary.json'
[ordered]@{
    customTestTarget = $CustomTestTarget
    framework        = $Framework
    configuration    = $Configuration
    filter           = $Filter
    coverage         = [bool]$Coverage
    projects         = $records
} | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $summaryPath -Encoding utf8

$lines = @(
    "### $CustomTestTarget / $Framework / $Configuration",
    '',
    '| Project | Outcome | Passed | Total | Detail |',
    '| --- | --- | ---: | ---: | --- |')
foreach ($record in $records) {
    $detail = $record.reason -replace '\r?\n', ' '
    $lines += "| $([System.IO.Path]::GetFileNameWithoutExtension($record.project)) | $($record.outcome) | $($record.passed) | $($record.total) | $detail |"
}
$lines += ''
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append
}
$lines | Write-Host

$failedRecords = @($records | Where-Object { $_.outcome -eq 'failed' })
if ($failedRecords.Count -gt 0) {
    throw "$($failedRecords.Count) of $($records.Count) project(s) failed: $(($failedRecords.project | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) }) -join ', ')."
}

$executed = @($records | Where-Object { $_.outcome -eq 'passed' })
if ($executed.Count -eq 0) {
    # Not a failure: batches are arbitrary slices of the profile's project list,
    # so one can legitimately contain only projects this profile restricts. It is
    # still worth surfacing, because a batch that tests nothing is otherwise
    # indistinguishable from a healthy one.
    Write-Host ("::warning title=CI test runner::Every project in this batch is restricted for " +
        "CustomTestTarget=$CustomTestTarget, so no tests ran.")
}

Write-Host "$($executed.Count) project(s) passed, $($records.Count - $executed.Count) not applicable."
