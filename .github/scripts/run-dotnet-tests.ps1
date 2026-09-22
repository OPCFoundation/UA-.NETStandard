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
    broken discovery rather than a pass, and fails here. Unlike the Azure
    template this script does not tolerate a non-zero test-host exit after an
    otherwise green run: on GitHub-hosted runners that exit is a failure.

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
    Wall-clock ceiling for one project's build or test invocation. The blame
    collector only reacts to inactivity inside the test host and can itself fail
    to unwind, so this is the backstop that turns a stuck run into a named
    per-project failure instead of an unexplained job timeout.

 .PARAMETER ResultsDirectory
    Directory that receives one subdirectory of results per project, plus the
    machine-readable batch summary.

 .PARAMETER Coverage
    Collect Cobertura coverage. Never set for a .NET Framework test host:
    coverlet.collector ships build assets for net8.0 and newer only, so the
    collector cannot load there and VSTest would only warn.

 .PARAMETER QuietOutput
    Write each project's build and test output to a log file under the results
    directory instead of the console. Required by the private-fuzz-corpus job:
    a failing fuzz test prints a base64 reproducer, and a public repository's
    job log is world-readable, so that output has to stay inside the results
    tree the job keeps private.
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

$projectList = @($Projects -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($projectList.Count -eq 0) {
    throw 'No projects were supplied. An entry that runs nothing would report success without testing anything.'
}

$null = New-Item -ItemType Directory -Path $ResultsDirectory -Force
$resultsRoot = (Resolve-Path -LiteralPath $ResultsDirectory).Path

<#
 .SYNOPSIS
    Runs 'dotnet' with the supplied arguments, streaming its output to the log
    and killing the process tree if it outlives the per-project ceiling.

 .DESCRIPTION
    System.Diagnostics.Process is used instead of the call operator so the child
    can be killed on timeout, and instead of Start-Process so the arguments -
    which include filter expressions containing '&' and '!' - are passed through
    verbatim rather than re-parsed by a shell.
#>
function Invoke-Dotnet([string[]] $arguments, [int] $timeoutMinutes, [string] $logPath)
{
    Write-Host "dotnet $($arguments -join ' ')"

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

        if (-not $process.WaitForExit($timeoutMinutes * 60 * 1000)) {
            Write-Host "::error title=CI test runner::'dotnet $($arguments[0])' exceeded the $timeoutMinutes-minute per-project ceiling and was killed."
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
    $trxFiles = @(Get-ChildItem -LiteralPath $directory -Recurse -File -Filter *.trx -ErrorAction SilentlyContinue)
    foreach ($trxFile in $trxFiles) {
        # XmlDocument.Load rather than [xml](Get-Content): PubSub emits several
        # megabytes of results and the array-of-lines cast fails on files that
        # size.
        $document = [System.Xml.XmlDocument]::new()
        $document.Load($trxFile.FullName)
        foreach ($counters in $document.GetElementsByTagName('Counters')) {
            $total += Get-CounterValue $counters 'total'
            $passed += Get-CounterValue $counters 'passed'
            foreach ($name in @('failed', 'error', 'timeout', 'aborted', 'passedButRunAborted')) {
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

        $build = Invoke-Dotnet $buildArguments $PerProjectTimeoutMinutes $logPath
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

        $test = Invoke-Dotnet $testArguments $PerProjectTimeoutMinutes $logPath

        $results = Measure-TestResults $projectResults
        $record.total = $results.Total
        $record.passed = $results.Passed
        $record.failed = $results.Failed

        if ($results.Files -eq 0) {
            throw ('No TRX was produced. Every mainline test project runs on VSTest and must emit one; ' +
                'a Microsoft.Testing.Platform project belongs in a dedicated job instead of this matrix.')
        }
        if ($test.TimedOut) {
            throw "The test run exceeded the $PerProjectTimeoutMinutes-minute per-project ceiling."
        }
        if ($test.ExitCode -ne 0) {
            throw "'dotnet test' exited with code $($test.ExitCode)."
        }
        if ($results.Failed -gt 0) {
            throw "$($results.Failed) test(s) failed, errored, timed out or aborted."
        }
        if ($results.Total -le 0) {
            throw 'No tests were recorded. The project is applicable to this profile, so discovery is broken.'
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
