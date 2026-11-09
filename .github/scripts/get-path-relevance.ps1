<#
 .SYNOPSIS
    Decides whether a set of changed files can affect a build or test outcome.

 .DESCRIPTION
    Called by the path-relevance steps in buildandtest.yml and
    nuget-publish.yml and exercised by CiPathRelevanceTests.cs.

    This replaces the 'paths:' filter that used to sit on the pull_request
    trigger. Filtering the whole workflow out would stop 'build-and-test
    summary' from ever reporting, and a required status check that never
    reports blocks the pull request forever; deciding relevance here skips the
    expensive jobs instead, while the summary job still reports.

    If a build input is classified as documentation, the required summary can
    report success even though the build, test and NativeAOT jobs did not run.
    Treat paths outside the designated documentation locations as relevant.

    The docs/ tree and the root README.md, CONTRIBUTING.md, and SECURITY.md are
    skippable. Markdown elsewhere is relevant because analyzers consume release
    tracking files and packages include README files. Paths are matched
    case-sensitively because Git distinguishes names by case. Validation of the
    docs/migrate/2.0.x bundle runs before this gate in buildandtest.yml.

 .PARAMETER ChangedFile
    Repository-relative paths, as produced by 'git diff --name-only
    --no-renames'. An empty set means the branch changes nothing and nothing
    needs to be rebuilt.

 .OUTPUTS
    [bool] $true when at least one changed file can affect a build or test.
#>
param(
    [AllowEmptyCollection()]
    [AllowEmptyString()]
    [string[]] $ChangedFile = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-DocumentationOnlyPath([string] $Path)
{
    if ([string]::IsNullOrEmpty($Path)) {
        return $true
    }

    # git reports forward slashes on every platform, including Windows.
    $normalized = $Path -replace '\\', '/'

    if ($normalized -clike 'docs/*') {
        return $true
    }

    return $normalized -cin @('README.md', 'CONTRIBUTING.md', 'SECURITY.md')
}

foreach ($file in $ChangedFile) {
    if (-not (Test-DocumentationOnlyPath $file)) {
        return $true
    }
}

return $false
