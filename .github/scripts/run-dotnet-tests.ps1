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

    The strict TRX evaluator must establish run completion before an exit-code
    discrepancy can be tolerated. Public replay inventories are frozen before
    building and checked against the actual output before testing. Fuzz output,
    raw result documents and dumps remain in the runner-private results tree.
    Only sanitized proofs and coverage are staged for public upload.

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
    Fresh runner-private directory for per-project results and diagnostics.

 .PARAMETER PublicResultsDirectory
    Separate fresh staging directory. Fuzz projects contribute only explicit
    sanitized proof files and coverage; raw TRX, logs and dumps stay private.

 .PARAMETER InputScope
    Public replays require frozen inventories and copied-input verification.
    Private replays may consume approved overlays, but cannot publish results,
    collect public coverage, or produce release assurance.

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
    [string] $PublicResultsDirectory = '',
    [string] $AssuranceDirectory = '',
    [string] $AssuranceWorkflow = '',
    [switch] $RequireAssurance,
    [ValidateSet('public', 'private')]
    [string] $InputScope = 'public',
    [switch] $Coverage,
    [switch] $QuietOutput
)

$ErrorActionPreference = 'Stop'

# The verdict rule lives in its own file so it can be tested without building or
# running anything - see tests/Opc.Ua.Tools.Tests/CiTestVerdictTests.cs.
. (Join-Path $PSScriptRoot 'get-test-verdict.ps1')

$projectList = @($Projects -split ';' | ForEach-Object {
    $path = $_.Trim().Replace('\', '/')
    while ($path.StartsWith('./')) { $path = $path.Substring(2) }
    if ([System.IO.Path]::IsPathRooted($path) -or '..' -in $path.Split('/')) {
        throw 'Projects must be repository-relative paths without parent traversal.'
    }
    $path
} | Where-Object { $_ })
if ($projectList.Count -eq 0) {
    throw 'No projects were supplied. An entry that runs nothing would report success without testing anything.'
}
if ($Coverage -and $Framework.StartsWith('net4')) {
    throw 'Coverage is not supported by the selected .NET Framework test host.'
}
if ($InputScope -eq 'private' -and ($PublicResultsDirectory -or $AssuranceDirectory -or
    $AssuranceWorkflow -or $RequireAssurance -or $Coverage)) {
    throw 'Private replay cannot publish results, collect public coverage, or produce release assurance.'
}
if ($RequireAssurance -and ($projectList.Count -ne 1 -or -not $AssuranceDirectory -or -not $AssuranceWorkflow)) {
    throw 'A required assurance entry must select exactly one project and an evidence destination.'
}
$stems = @($projectList | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) })
if (@($stems | Select-Object -Unique).Count -ne $projectList.Count) {
    throw 'Project result directories must be unique within a batch.'
}

function New-ResultDirectory([string] $path)
{
    if (Test-Path -LiteralPath $path) {
        if (@(Get-ChildItem -LiteralPath $path -Force).Count -gt 0) {
            throw 'Result directories must be fresh; stale evidence cannot be reused.'
        }
    }
    $null = New-Item -ItemType Directory -Path $path -Force
    return (Resolve-Path -LiteralPath $path).Path
}

$resultsRoot = New-ResultDirectory $ResultsDirectory
$publicRoot = ''
if ($PublicResultsDirectory) {
    $publicRoot = [System.IO.Path]::GetFullPath($PublicResultsDirectory)
    if ($publicRoot -eq $resultsRoot -or
        $publicRoot.StartsWith($resultsRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        $resultsRoot.StartsWith($publicRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Public staging and private results must be separate directory trees.'
    }
    $publicRoot = New-ResultDirectory $publicRoot
}
$assuranceRoot = ''
if ($AssuranceDirectory) {
    $assuranceRoot = New-ResultDirectory $AssuranceDirectory
}
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot)
$assuranceScripts = Join-Path $repositoryRoot '.azurepipelines/assurance'
$catalog = Get-Content -LiteralPath (Join-Path $assuranceScripts 'profiles.json') -Raw | ConvertFrom-Json
$replayDefinitions = @($catalog.profiles.jobs | Where-Object { $_.corpusRoot }) +
    @($catalog.additionalReplayProjects)

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
    if ($captureOutput) {
        Write-Host "dotnet $($arguments[0]) (output retained privately on the runner)"
    }
    else {
        Write-Host "dotnet $($arguments -join ' ')"
    }

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
    if ($captureOutput) {
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $streamWriter = [System.IO.StreamWriter]::new($logPath, $true)
        $streamWriter.AutoFlush = $true
        $writer = [System.IO.TextWriter]::Synchronized($streamWriter)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if ($captureOutput) {
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
        if ($captureOutput) {
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
        if ($captureOutput) {
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
function Get-ProjectProperties([string] $project, [switch] $InnerBuild)
{
    $arguments = @(
        'msbuild', $project, '-nologo',
        '-getProperty:IsTestProject;TargetFrameworks;TargetFramework;_RestrictedToLegacyTfm;TargetDir',
        "-p:CustomTestTarget=$CustomTestTarget", "-p:Configuration=$Configuration")
    # Never override a project's pinned framework while deciding applicability.
    # Once selected, an inner evaluation resolves the actual output directory.
    if ($InnerBuild) { $arguments += "-p:TargetFramework=$Framework" }
    $output = & dotnet @arguments 2>&1 | Out-String
    if ($captureOutput) {
        $output | Out-File -LiteralPath $logPath -Append
    }
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
    Reads counts only from a sanitized, strictly validated result proof.
#>
function Measure-TestResults([string] $directory, [string] $kind)
{
    $proofPath = Join-Path $directory 'public-results.json'
    & (Join-Path $assuranceScripts 'results.ps1') -ResultsPath $directory -Kind $kind `
        -OutputPath $proofPath -StrictTrx
    if ($LASTEXITCODE -ne 0) { throw 'The result proof producer failed.' }
    $proof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
    return [pscustomobject]@{
        Files = @($proof.documents).Count
        Total = [long] $proof.counts.total
        Passed = [long] $proof.counts.passed
        Failed = [long] $proof.counts.failed
        Completed = $proof.status -ceq 'completed'
    }
}

$records = @()
foreach ($project in $projectList) {
    $project = $project.Replace('\', '/')
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($project)
    $captureOutput = $QuietOutput -or $InputScope -eq 'private' -or $project.StartsWith('fuzzing/')
    $projectResults = Join-Path $resultsRoot $stem
    $null = New-Item -ItemType Directory -Path $projectResults
    $logPath = Join-Path $projectResults 'build-and-test.log'
    $previousReplayPath = $env:OPCUA_ASSURANCE_REPLAY_PATH
    $replay = @($replayDefinitions | Where-Object { $_.project -ceq $project })
    $kind = if ($replay.Count -eq 1 -and $InputScope -eq 'public') { 'fuzz-replay' } else { 'trx' }
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
        notApplicableRule = ''
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
            if ($properties._RestrictedToLegacyTfm -ne 'true' -or $RequireAssurance) {
                throw 'The selected project has no verified applicability exclusion.'
            }
            $record.outcome = 'not-applicable'
            $record.notApplicableRule = 'RestrictForLegacyTfm'
            $record.reason = "RestrictForLegacyTfm makes this an empty shell for CustomTestTarget=$CustomTestTarget."
            Write-Host "Not applicable: $($record.reason)"
            continue
        }

        if ($declared -notcontains $Framework) {
            throw ("The project builds [$($declared -join ', ')] for CustomTestTarget=$CustomTestTarget " +
                "but this profile hosts its tests on $Framework. Either follow CustomTestTarget in the " +
                "project's TargetFrameworks or set RestrictForLegacyTfm so the profile skips it explicitly.")
        }

        if ($replay.Count -gt 1 -or
            ($InputScope -eq 'public' -and $project.EndsWith('.Fuzz.Tests.csproj') -and $replay.Count -ne 1)) {
            throw 'The selected replay project has no unique committed-public input definition.'
        }
        if ($kind -eq 'fuzz-replay') {
            & (Join-Path $assuranceScripts 'fuzz-inputs.ps1') -Project $project `
                -OutputPath (Join-Path $projectResults 'public-inputs.json') -RequireCommitted
            if ($LASTEXITCODE -ne 0) { throw 'The public replay inventory could not be frozen.' }
        }

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
        if ($kind -eq 'fuzz-replay') {
            $builtProperties = Get-ProjectProperties $project -InnerBuild
            if (-not $builtProperties.TargetDir) { throw 'The evaluated build output directory is missing.' }
            & (Join-Path $assuranceScripts 'fuzz-inputs.ps1') -Project $project `
                -BuildOutput $builtProperties.TargetDir -OutputPath (Join-Path $projectResults 'copied-inputs.json') `
                -RequireCommitted
            if ($LASTEXITCODE -ne 0) { throw 'The built public replay inputs could not be verified.' }
            $before = Get-Content -LiteralPath (Join-Path $projectResults 'public-inputs.json') -Raw | ConvertFrom-Json
            $after = Get-Content -LiteralPath (Join-Path $projectResults 'copied-inputs.json') -Raw | ConvertFrom-Json
            if ($before.inventoryDigest -cne $after.inventoryDigest) {
                throw 'Public replay inputs changed after selection.'
            }
        }
        $env:OPCUA_ASSURANCE_REPLAY_PATH = Join-Path $projectResults 'fuzz-replay.xml'

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

        $results = Measure-TestResults $projectResults $kind
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
            -ReportsCompleted $results.Completed `
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
        if ($Coverage -and
            @(Get-ChildItem -LiteralPath $projectResults -Recurse -File -Filter '*.cobertura.xml').Count -eq 0) {
            throw 'The selected coverage-enabled project did not produce a coverage fragment.'
        }

        $record.outcome = 'passed'
        Write-Host "Passed: $($results.Passed)/$($results.Total)."
    }
    catch {
        $record.outcome = 'failed'
        $_ | Out-String | Out-File -LiteralPath (Join-Path $projectResults 'runner-error.log') -Append
        $record.reason = if ($captureOutput) {
            'Build, execution, or evidence verification failed; details remain private on the runner.'
        } else {
            $_.Exception.Message
        }
        Write-Host "::error title=$stem ($CustomTestTarget/$Configuration)::$($record.reason)"
    }
    finally {
        $env:OPCUA_ASSURANCE_REPLAY_PATH = $previousReplayPath
        try {
            if ($assuranceRoot -and $AssuranceWorkflow -and $InputScope -eq 'public' -and
                $project -cin @($catalog.profiles.jobs.project)) {
                $destination = if ($RequireAssurance) { $assuranceRoot } else { Join-Path $assuranceRoot $stem }
                $null = New-Item -ItemType Directory -Path $destination -Force
                & (Join-Path $assuranceScripts 'write-job.ps1') -Project $project -ResultsPath $projectResults `
                    -OutputPath (Join-Path $destination 'evidence.job.json') -Workflow $AssuranceWorkflow `
                    -HostTfm $Framework -LibraryTfm $CustomTestTarget -Configuration $Configuration `
                    -Filter $Filter -StrictTrx -ExecutionFailed:($record.outcome -ne 'passed')
                if ($LASTEXITCODE -ne 0) { throw 'The assurance record could not be produced.' }
                if ($RequireAssurance) {
                    $proof = Get-Content -LiteralPath (Join-Path $destination 'evidence.job.results.json') -Raw |
                        ConvertFrom-Json
                    if ($proof.status -cne 'completed' -or
                        ($proof.kind -ceq 'trx' -and $proof.counts.skipped -ne 0)) {
                        throw 'Selected profile execution was not complete.'
                    }
                }
            }
            elseif ($RequireAssurance) {
                throw 'No release profile applies to the selected project.'
            }

            if ($publicRoot) {
                $destination = Join-Path $publicRoot $stem
                $null = New-Item -ItemType Directory -Path $destination -Force
                if ($captureOutput) {
                    foreach ($name in @('public-inputs.json', 'public-results.json')) {
                        $file = Join-Path $projectResults $name
                        if (Test-Path -LiteralPath $file -PathType Leaf) {
                            Copy-Item -LiteralPath $file -Destination $destination
                        }
                    }
                    $index = 0
                    foreach ($file in Get-ChildItem -LiteralPath $projectResults -Recurse -File -Filter '*.cobertura.xml') {
                        $index++
                        Copy-Item -LiteralPath $file.FullName -Destination (
                            Join-Path $destination "coverage-$index.cobertura.xml")
                    }
                }
                else {
                    Get-ChildItem -LiteralPath $projectResults -Force |
                        Copy-Item -Destination $destination -Recurse
                }
            }
        }
        catch {
            $_ | Out-String | Out-File -LiteralPath (Join-Path $projectResults 'runner-error.log') -Append
            $record.outcome = 'failed'
            $record.reason = 'The selected result or assurance evidence could not be published safely.'
            Write-Host "::error title=$stem ($CustomTestTarget/$Configuration)::$($record.reason)"
        }
        $records += [pscustomobject]$record
        Write-Host '::endgroup::'
    }
}

$summary = [ordered]@{
    customTestTarget = $CustomTestTarget
    framework        = $Framework
    configuration    = $Configuration
    filter           = $Filter
    coverage         = [bool]$Coverage
    inputScope       = $InputScope
    projects         = $records
}
$summary | ConvertTo-Json -Depth 5 |
    Out-File -LiteralPath (Join-Path $resultsRoot 'batch-summary.json') -Encoding utf8
if ($publicRoot) {
    $summary | ConvertTo-Json -Depth 5 |
        Out-File -LiteralPath (Join-Path $publicRoot 'batch-summary.json') -Encoding utf8
}

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
