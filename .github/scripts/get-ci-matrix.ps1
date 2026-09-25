<#
 .SYNOPSIS
    Expands the GitHub Actions build and test workload for a CI scope.

 .DESCRIPTION
    Produces the job matrices consumed by .github/workflows/buildandtest.yml
    (pull-request scope) and .github/workflows/nightly.yml (full scope), plus a
    manifest that records every (project x profile) tuple the run is expected to
    cover.

    The workload is described once, here, as a table of profiles. A profile is a
    (runner OS, CustomTestTarget, test-host target framework, configuration,
    category filter, tier) tuple. Projects are discovered from the file system -
    the same '*.Tests.csproj' sweep .azurepipelines/get-matrix.ps1 performs - so
    neither matrix has to be hand-maintained as projects are added or renamed.

    CustomTestTarget, not '--framework', is the mechanism that pins the stack to
    a single target framework (see targets.props). The standard profiles are not
    runnable target frameworks: 'netstandard2.0' hosts its tests on net48 and
    'netstandard2.1' hosts them on net8.0, so a profile carries both the
    CustomTestTarget it builds with and the framework its tests actually run on.

    GitHub Actions refuses to start a workflow run whose matrices expand past 256
    jobs, and that limit cannot be raised. The project fan-out is therefore
    batched: each matrix entry carries several projects that the executor runs in
    sequence, keeping per-project results. The batch size is the smallest one
    that fits the budget, so it adapts as test projects are added instead of
    silently truncating the matrix.

 .PARAMETER Scope
    'pr' expands the workload that gates a pull request. 'full' expands the
    complete workload Azure Pipelines used to run on a schedule.

 .PARAMETER ExcludeMacOS
    Drop the macOS test profile. The batch size is deliberately computed from the
    full profile table regardless of this switch, so excluding macOS does not
    reshuffle the batches of the remaining profiles.

 .PARAMETER OnlyProject
    Restrict the test matrix to the single test project whose path ends with this
    value. Used by the protected private-fuzz-corpus job so it covers exactly the
    profiles this table already enables for that project, instead of repeating
    the profile list in a second place. The non-mainline tiers are dropped, since
    they cover named projects of their own.

 .PARAMETER MaxEntries
    The documented GitHub Actions ceiling on matrix jobs per workflow run.
    Exceeding it is an error, never a truncation.

 .PARAMETER ReservedEntries
    Entries reserved for the jobs that are not part of these matrices - Native
    AoT, the fuzz contract and replay jobs, coverage and the summary - plus
    headroom.

 .PARAMETER MaxJobTimeoutMinutes
    Upper bound on a batch job's 'timeout-minutes', below the 360-minute ceiling
    GitHub enforces on hosted runners. A batch whose per-project budgets add up
    past this is an error rather than a clamp: clamping would let the executor
    outlive the job, which is the one case that produces no annotation and no
    results.

 .PARAMETER RepositoryRoot
    Repository root. Defaults to the root two levels above this script.

 .PARAMETER ManifestPath
    Optional path to write the expanded workload manifest to. The workflows
    upload it so a run's intended coverage can be compared against another CI
    system's inventory.
#>

Param(
    [ValidateSet('pr', 'full')]
    [string] $Scope = 'pr',
    [switch] $ExcludeMacOS,
    [string] $OnlyProject = '',
    [int]    $MaxEntries = 256,
    [int]    $ReservedEntries = 40,
    [int]    $MaxJobTimeoutMinutes = 350,
    [string] $RepositoryRoot = '',
    [string] $ManifestPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..' '..')).Path
}
else {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

# Runner images. Deliberately the GitHub-hosted labels: the Azure image aliases
# (windows-2022-g2, ubuntu-22.04-g2) name Managed DevOps Pool images and mean
# nothing here.
$RunnerImages = @{
    windows = 'windows-latest'
    linux   = 'ubuntu-latest'
    macos   = 'macos-latest'
}

# Hosted runner architectures, recorded in the manifest so a comparison against
# another CI system's inventory is not silently architecture-blind.
$RunnerArchitectures = @{
    windows = 'x64'
    linux   = 'x64'
    macos   = 'arm64'
}

$LatestSdk = '10.0.401'

# The category filter the fast legs use. Tiers that lift it declare so below.
$DefaultFilter = 'TestCategory!=LongRunning&TestCategory!=Stress'

# Projects that are not part of the mainline sweep, matching the exclusions in
# .azurepipelines/test.yml:
#   Aot                   - published and run as native executables rather than
#                           by 'dotnet test'; the workflows have their own jobs.
#   Stress                - opt-in tier owned by .github/workflows/stress-test.yml.
#   Subscriptions.Durable - its own tier, run unfiltered below.
#   OneFuzz.Validator     - pins net10.0 to match the net10.0-only drop it
#                           validates, so a legacy leg fails with NETSDK1005
#                           before the no-op shell can take effect. It is covered
#                           by the fuzz-public-drop job instead.
$MainlineExclusions = @(
    'Opc.Ua.Aot.Tests.csproj',
    'Opc.Ua.Stress.Tests.csproj',
    'Opc.Ua.Subscriptions.Durable.Tests.csproj',
    'Opc.Ua.OneFuzz.Validator.Tests.csproj')

$DurableProject = 'Opc.Ua.Subscriptions.Durable.Tests.csproj'

<#
 .SYNOPSIS
    Sorts strings with an ordinal comparison so the batches a run produces do not
    depend on the runner's culture.
#>
function Sort-Ordinal([string[]] $values)
{
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $values) {
        $list.Add($value)
    }
    $list.Sort([System.StringComparer]::Ordinal)
    return $list.ToArray()
}

<#
 .SYNOPSIS
    Converts an absolute path below the repository root into a forward-slashed
    repository-relative path.
#>
function ConvertTo-RelativePath([string] $fullName)
{
    return $fullName.Substring($RepositoryRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

<#
 .SYNOPSIS
    Discovers files below the repository root, skipping build output.
#>
function Find-RepositoryFile([string] $filter)
{
    $found = Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Filter $filter |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' } |
        ForEach-Object { ConvertTo-RelativePath $_.FullName }
    return Sort-Ordinal @($found)
}

<#
 .SYNOPSIS
    Returns the SDK versions a profile needs, newest last so the latest SDK
    drives the build while the older runtime is available to host its tests.
#>
function Get-SdkVersions([string] $framework)
{
    switch ($framework) {
        'net8.0' { return @('8.0.x', $LatestSdk) }
        'net9.0' { return @('9.0.x', $LatestSdk) }
        default { return @($LatestSdk) }
    }
}

# The workload. Each profile fans out over the project set named by its tier.
#
#   customTestTarget - passed as /p:CustomTestTarget for restore, build and test
#   framework        - the target framework the tests actually execute on
#   scopes           - which CI scopes include the profile
#
# The Linux net10.0 Release mainline profile is pull-request only on purpose: in
# the full scope the long-running tier runs the identical tuple with the category
# filter lifted, so keeping both would run the same tests twice.
$Profiles = @(
    @{ id = 'windows-net48'; os = 'windows'; customTestTarget = 'net48'; framework = 'net48'; configuration = 'Release'; scopes = @('pr', 'full') }
    @{ id = 'windows-net10.0'; os = 'windows'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Release'; scopes = @('pr', 'full') }
    @{ id = 'linux-net10.0'; os = 'linux'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Release'; scopes = @('pr') }
    @{ id = 'macos-net10.0'; os = 'macos'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Release'; scopes = @('pr', 'full') }
    @{ id = 'windows-net10.0-debug'; os = 'windows'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Debug'; scopes = @('full') }
    @{ id = 'linux-net10.0-debug'; os = 'linux'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Debug'; scopes = @('full') }
    @{ id = 'windows-net9.0'; os = 'windows'; customTestTarget = 'net9.0'; framework = 'net9.0'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'linux-net9.0'; os = 'linux'; customTestTarget = 'net9.0'; framework = 'net9.0'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'windows-net8.0'; os = 'windows'; customTestTarget = 'net8.0'; framework = 'net8.0'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'linux-net8.0'; os = 'linux'; customTestTarget = 'net8.0'; framework = 'net8.0'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'windows-net472'; os = 'windows'; customTestTarget = 'net472'; framework = 'net472'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'windows-netstandard2.0'; os = 'windows'; customTestTarget = 'netstandard2.0'; framework = 'net48'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'windows-netstandard2.1'; os = 'windows'; customTestTarget = 'netstandard2.1'; framework = 'net8.0'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'linux-netstandard2.1'; os = 'linux'; customTestTarget = 'netstandard2.1'; framework = 'net8.0'; configuration = 'Release'; scopes = @('full') }
    @{ id = 'linux-long-running'; os = 'linux'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Release'; scopes = @('full')
        tier = 'long-running'; filter = ''; hangTimeout = '30m'; coverage = $false
    }
    @{ id = 'windows-durable'; os = 'windows'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Release'; scopes = @('full')
        tier = 'durable'; filter = ''; hangTimeout = '30m'; coverage = $false
    }
    @{ id = 'linux-durable'; os = 'linux'; customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Release'; scopes = @('full')
        tier = 'durable'; filter = ''; hangTimeout = '30m'; coverage = $false
    }
)

# Solution builds. 'solutions' is either '*' for every discovered .slnx or an
# explicit list. Mirrors the Azure 'Build' stage: every solution across every
# supported target framework in both configurations for the full scope, narrowed
# for pull requests, plus the Linux leg that proves UA.slnx still compiles for
# every Linux-capable target framework.
$BuildProfiles = @(
    @{ id = 'pr-windows'; os = 'windows'; scopes = @('pr'); solutions = '*'
        tfms = @('net48', 'net10.0'); configurations = @('Debug', 'Release')
    }
    @{ id = 'pr-windows-legacy'; os = 'windows'; scopes = @('pr'); solutions = @('UA.slnx')
        tfms = @('net472', 'netstandard2.0'); configurations = @('Release')
    }
    @{ id = 'pr-linux'; os = 'linux'; scopes = @('pr'); solutions = @('UA.slnx')
        tfms = @('netstandard2.1', 'net8.0', 'net9.0', 'net10.0'); configurations = @('Release')
    }
    @{ id = 'full-windows'; os = 'windows'; scopes = @('full'); solutions = '*'
        tfms = @('net472', 'net48', 'netstandard2.0', 'netstandard2.1', 'net8.0', 'net9.0', 'net10.0')
        configurations = @('Debug', 'Release')
    }
    @{ id = 'full-linux'; os = 'linux'; scopes = @('full'); solutions = @('UA.slnx')
        tfms = @('netstandard2.1', 'net8.0', 'net9.0', 'net10.0'); configurations = @('Release')
    }
)

$allTestProjects = Find-RepositoryFile '*.Tests.csproj'
$mainlineProjects = @($allTestProjects | Where-Object { $MainlineExclusions -notcontains (Split-Path $_ -Leaf) })
$durableProjects = @($allTestProjects | Where-Object { (Split-Path $_ -Leaf) -eq $DurableProject })
$solutions = Find-RepositoryFile '*.slnx'

if ($mainlineProjects.Count -eq 0) {
    throw "Discovery found no mainline test projects beneath '$RepositoryRoot'. A matrix that runs nothing would report success without testing anything."
}
if ($durableProjects.Count -eq 0) {
    throw "Discovery found no '$DurableProject'. The durable-subscription tier would silently disappear."
}
if ($solutions.Count -eq 0) {
    throw "Discovery found no '*.slnx' beneath '$RepositoryRoot'."
}

if (-not [string]::IsNullOrWhiteSpace($OnlyProject)) {
    $normalized = $OnlyProject.Replace('\', '/')
    $mainlineProjects = @($mainlineProjects | Where-Object { $_.EndsWith($normalized, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($mainlineProjects.Count -eq 0) {
        throw "No discovered test project path ends with '$OnlyProject'. A matrix that runs nothing would report success without testing anything."
    }
    # The named tiers cover named projects, so narrowing to one project leaves
    # them with nothing to run rather than silently re-running it unfiltered.
    $durableProjects = @()
}

$projectsByTier = @{
    mainline       = $mainlineProjects
    'long-running' = if ([string]::IsNullOrWhiteSpace($OnlyProject)) { $mainlineProjects } else { @() }
    durable        = $durableProjects
}

<#
 .SYNOPSIS
    Expands the solution build matrix for a scope.
#>
function Expand-BuildMatrix([string] $scope)
{
    $entries = @()
    foreach ($buildProfile in $BuildProfiles) {
        if ($buildProfile.scopes -notcontains $scope) {
            continue
        }

        $profileSolutions = $buildProfile.solutions
        if ($profileSolutions -eq '*') {
            $profileSolutions = $solutions
        }
        foreach ($solution in $profileSolutions) {
            if ($solutions -notcontains $solution) {
                throw "Build profile '$($buildProfile.id)' names solution '$solution', which was not discovered."
            }
            $stem = [System.IO.Path]::GetFileNameWithoutExtension($solution)
            foreach ($tfm in $buildProfile.tfms) {
                foreach ($configuration in $buildProfile.configurations) {
                    $entries += [ordered]@{
                        id               = "$($buildProfile.id)-$stem-$tfm-$configuration"
                        name             = "$stem $tfm $configuration ($($buildProfile.os))"
                        os               = $buildProfile.os
                        runsOn           = $RunnerImages[$buildProfile.os]
                        arch             = $RunnerArchitectures[$buildProfile.os]
                        solution         = $solution
                        customTestTarget = $tfm
                        configuration    = $configuration
                        dotnet           = (Get-SdkVersions $tfm) -join "`n"
                    }
                }
            }
        }
    }
    return $entries
}

<#
 .SYNOPSIS
    Splits an ordered project list into contiguous batches of at most $size.
#>
function Split-IntoBatches([string[]] $projects, [int] $size)
{
    $batches = @()
    for ($index = 0; $index -lt $projects.Count; $index += $size) {
        $take = [System.Math]::Min($size, $projects.Count - $index)
        $batches += , @($projects[$index..($index + $take - 1)])
    }
    return $batches
}

<#
 .SYNOPSIS
    Counts the test matrix entries a batch size produces for a scope, ignoring
    which optional runners are enabled so the batch size is stable.
#>
function Measure-TestEntries([string] $scope, [int] $size)
{
    $total = 0
    foreach ($testProfile in $Profiles) {
        if ($testProfile.scopes -notcontains $scope) {
            continue
        }
        $tier = 'mainline'
        if ($testProfile.ContainsKey('tier')) {
            $tier = $testProfile.tier
        }
        $total += [System.Math]::Ceiling($projectsByTier[$tier].Count / [double]$size)
    }
    return [int]$total
}

<#
 .SYNOPSIS
    Expands the test matrix and the per-tuple manifest for a scope.
#>
function Expand-TestMatrix([string] $scope, [int] $size, [bool] $includeMacOS)
{
    $entries = @()
    $manifest = @()
    foreach ($testProfile in $Profiles) {
        if ($testProfile.scopes -notcontains $scope) {
            continue
        }
        if ($testProfile.os -eq 'macos' -and -not $includeMacOS) {
            continue
        }

        $tier = 'mainline'
        if ($testProfile.ContainsKey('tier')) {
            $tier = $testProfile.tier
        }
        $filter = $DefaultFilter
        if ($testProfile.ContainsKey('filter')) {
            $filter = $testProfile.filter
        }
        $hangTimeout = '10m'
        if ($testProfile.ContainsKey('hangTimeout')) {
            $hangTimeout = $testProfile.hangTimeout
        }
        # coverlet.collector 10.x ships build assets for net8.0 and newer only, so
        # a .NET Framework test host cannot load the 'XPlat Code Coverage'
        # collector at all - VSTest only warns and writes no report. The
        # netstandard2.0 profile is covered by the same rule because it hosts its
        # tests on net48. The long-running and durable tiers opt out explicitly:
        # they re-run projects the filtered legs already covered, so folding their
        # numbers into the merged report would double-count them.
        $coverage = -not $testProfile.framework.StartsWith('net4')
        if ($testProfile.ContainsKey('coverage')) {
            $coverage = [bool]$testProfile.coverage
        }
        # One combined ceiling per project, shared by that project's build and
        # test invocation in run-dotnet-tests.ps1. The job timeout below budgets
        # it exactly once per project, so the executor must not spend it twice.
        # Keep 45 minutes for mainline too: on hosted Windows, the net48
        # Opc.Ua.Server.Tests build plus its otherwise healthy test run normally
        # takes about 25 minutes and has exceeded 30 under runner variance.
        $perProjectTimeout = 45

        $batches = Split-IntoBatches $projectsByTier[$tier] $size
        for ($index = 0; $index -lt $batches.Count; $index++) {
            $batch = $batches[$index]
            $batchId = "$($testProfile.id)-$(($index + 1).ToString('00'))"
            # 20 minutes of fixed cost - checkout, SDK install, restore - plus
            # the combined build-and-test ceiling for every project in the
            # batch. It has to stay a true upper bound on what the executor can
            # spend, or GitHub cancels the job before run-dotnet-tests.ps1 can
            # attribute the overrun to a project.
            $jobTimeout = 20 + ($batch.Count * $perProjectTimeout)
            if ($jobTimeout -gt $MaxJobTimeoutMinutes) {
                throw ("Batch '$batchId' of $($batch.Count) projects needs $jobTimeout minutes, past the " +
                    "$MaxJobTimeoutMinutes-minute ceiling. Clamping it would let the executor outlive the " +
                    'job, so lower the per-project ceiling or raise ReservedEntries so smaller batches fit.')
            }
            $entries += [ordered]@{
                id                = $batchId
                name              = "$($testProfile.id) ($($index + 1)/$($batches.Count))"
                os                = $testProfile.os
                runsOn            = $RunnerImages[$testProfile.os]
                arch              = $RunnerArchitectures[$testProfile.os]
                profile           = $testProfile.id
                tier              = $tier
                customTestTarget  = $testProfile.customTestTarget
                framework         = $testProfile.framework
                configuration     = $testProfile.configuration
                filter            = $filter
                hangTimeout       = $hangTimeout
                coverage          = $coverage
                projects          = $batch -join ';'
                perProjectTimeout = $perProjectTimeout
                timeoutMinutes    = $jobTimeout
                dotnet            = (Get-SdkVersions $testProfile.framework) -join "`n"
            }

            foreach ($project in $batch) {
                # Every automatic entry uses the corpus checked into the
                # repository. The private crash corpus is only ever replayed by
                # the protected job in nightly.yml.
                $corpus = 'none'
                if ($project -like 'fuzzing/*') {
                    $corpus = 'public-checked-in'
                }
                $manifest += [ordered]@{
                    project          = $project
                    profile          = $testProfile.id
                    tier             = $tier
                    os               = $testProfile.os
                    runsOn           = $RunnerImages[$testProfile.os]
                    arch             = $RunnerArchitectures[$testProfile.os]
                    customTestTarget = $testProfile.customTestTarget
                    framework        = $testProfile.framework
                    configuration    = $testProfile.configuration
                    filter           = $filter
                    coverage         = $coverage
                    corpus           = $corpus
                    batch            = $batchId
                }
            }
        }
    }
    return @{ entries = $entries; manifest = $manifest }
}

$buildEntries = @(Expand-BuildMatrix $Scope)
$budget = $MaxEntries - $buildEntries.Count - $ReservedEntries
if ($budget -lt 1) {
    throw "The '$Scope' solution build matrix alone needs $($buildEntries.Count) of the $MaxEntries entries GitHub Actions allows per workflow run, leaving no room for tests."
}

$batchSize = 1
while ((Measure-TestEntries $Scope $batchSize) -gt $budget) {
    $batchSize++
    if ($batchSize -gt $mainlineProjects.Count) {
        throw "No batch size fits the '$Scope' test workload into $budget matrix entries."
    }
}

$expanded = Expand-TestMatrix $Scope $batchSize (-not $ExcludeMacOS)
$testEntries = @($expanded.entries)
$totalEntries = $testEntries.Count + $buildEntries.Count

Write-Host "Scope:             $Scope"
Write-Host "Repository root:   $RepositoryRoot"
Write-Host "Mainline projects: $($mainlineProjects.Count)"
Write-Host "Solutions:         $($solutions -join ', ')"
Write-Host "Batch size:        $batchSize"
Write-Host "Test entries:      $($testEntries.Count)"
Write-Host "Build entries:     $($buildEntries.Count)"
Write-Host "Reserved entries:  $ReservedEntries"
Write-Host "Total matrix jobs: $totalEntries (limit $MaxEntries)"

if ($totalEntries -gt $MaxEntries) {
    throw "The '$Scope' workload expands to $totalEntries matrix jobs, past the $MaxEntries GitHub Actions allows per workflow run. Lower the budget so a larger batch size is chosen rather than dropping entries."
}

if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
    $manifestDirectory = Split-Path -Parent $ManifestPath
    if (-not [string]::IsNullOrWhiteSpace($manifestDirectory) -and -not (Test-Path -LiteralPath $manifestDirectory)) {
        $null = New-Item -ItemType Directory -Path $manifestDirectory -Force
    }
    $document = [ordered]@{
        scope        = $Scope
        batchSize    = $batchSize
        includeMacOS = (-not $ExcludeMacOS)
        builds       = $buildEntries
        tests        = @($expanded.manifest)
    }
    $document | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $ManifestPath -Encoding utf8
    Write-Host "Wrote the workload manifest to '$ManifestPath'."
}

$testsJson = ConvertTo-Json -InputObject $testEntries -Depth 5 -Compress
$buildsJson = ConvertTo-Json -InputObject $buildEntries -Depth 5 -Compress

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "tests=$testsJson" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "builds=$buildsJson" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "batch_size=$batchSize" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "test_entry_count=$($testEntries.Count)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "build_entry_count=$($buildEntries.Count)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}
else {
    Write-Output $testsJson
    Write-Output $buildsJson
}
