<#
 .SYNOPSIS
    Decides whether a set of changed files can affect a build or test outcome.

 .DESCRIPTION
    Called by .github/scripts/get-ci-matrix.ps1's callers - currently the
    'Resolve path relevance' step in .github/workflows/buildandtest.yml - and
    exercised directly by tests/Opc.Ua.Tools.Tests/CiPathRelevanceTests.cs.

    This replaces the 'paths:' filter that used to sit on the pull_request
    trigger. Filtering the whole workflow out would stop 'build-and-test
    summary' from ever reporting, and a required status check that never
    reports blocks the pull request forever; deciding relevance here skips the
    expensive jobs instead, while the summary job still reports.

    That makes this rule fail-dangerous in exactly one direction. If it wrongly
    reports a change as irrelevant, the build, test and NativeAOT jobs are
    skipped and the required summary still reports success - a broken change
    merges with a green check. If it wrongly reports an irrelevant change as
    relevant, CI merely does needless work.

    So the rule is a deny-list, not an allow-list of build inputs. An allow-list
    of extensions cannot be kept complete: the source generators consume .xml
    and .csv design files (see the AdditionalFiles items in
    src/Opc.Ua.WotCon/Opc.Ua.WotCon.csproj), test projects carry XML and JSON
    fixtures and NodeSet assets, .editorconfig is enforced at build time, and
    every new input of that kind would have to be remembered here. Only files
    that cannot change a build or test outcome are skippable: Markdown, and the
    docs/ tree, which holds nothing but Markdown and images.

 .PARAMETER ChangedFile
    Repository-relative paths, as produced by 'git diff --name-only'. An empty
    set means the branch changes nothing and nothing needs to be rebuilt.

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
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $true
    }

    # git reports forward slashes on every platform, including Windows.
    $normalized = $Path -replace '\\', '/'

    if ([System.IO.Path]::GetExtension($normalized) -ieq '.md') {
        return $true
    }

    return $normalized -like 'docs/*'
}

foreach ($file in $ChangedFile) {
    if (-not (Test-DocumentationOnlyPath $file)) {
        return $true
    }
}

return $false
