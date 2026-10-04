<#
.SYNOPSIS
    Proves that the committed preview package build number still sorts above every published preview.

.DESCRIPTION
    Only relevant for a stable candidate package set. version.targets pins the
    preview-only package families to the explicitly committed number in
    preview-version.props whenever the root version is an exact stable release
    such as "2.0.0", because that version carries no git-height placeholder to
    reuse. Ordinary development meanwhile keeps publishing
    "2.0.0-preview.<height>" packages with a growing height, so the committed
    number silently goes stale and the release would sort *below* a CI package.
    NuGet versions are immutable, so the mistake is unrecoverable once pushed.

    This script queries the feeds the repository actually publishes to and
    fails the run when any already-published version would outrank the
    candidate. It fails closed: an unreachable feed is an error, not a pass,
    because an irreversible publication must never proceed on unverified
    ordering. See docs/ReleaseProcess.md.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,

    [string]$GitHubPackagesOwner = 'OPCFoundation',

    [string]$GitHubToken = $env:GITHUB_TOKEN,

    [string]$NuGetOrgIndexUrl = 'https://api.nuget.org/v3-flatcontainer',

    [string]$GitHubApiUrl = 'https://api.github.com'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'package-version-policy.ps1')

function Get-NuGetOrgPublishedVersion {
    param(
        [Parameter(Mandatory)][string]$Uri
    )

    $response = Invoke-WebRequest -Uri $Uri -SkipHttpErrorCheck `
        -MaximumRetryCount 3 -RetryIntervalSec 5
    if ($response.StatusCode -eq 404) {
        # The package has never been published to nuget.org.
        return @()
    }
    if ($response.StatusCode -ne 200) {
        throw (
            "Unable to read published versions from nuget.org ('$Uri' returned " +
            "HTTP $($response.StatusCode)). Preview ordering cannot be proved, so the release " +
            'is blocked; see docs/ReleaseProcess.md.')
    }

    return @(($response.Content | ConvertFrom-Json).versions)
}

function Get-GitHubPackagesPublishedVersion {
    <#
        Deliberately uses the GitHub REST packages API rather than the
        nuget.pkg.github.com flat-container endpoint. That endpoint answers
        403 for a package that exists but is not readable *and* for one that
        was never published, which makes "not published yet" and "the token
        is wrong" indistinguishable - and this gate must never guess. The
        REST API separates them: 404 means no such package, and 403 carries
        an explicit "you need at least read:packages scope" message.
    #>
    param(
        [Parameter(Mandatory)][string]$Owner,
        [Parameter(Mandatory)][string]$PackageId,
        [Parameter(Mandatory)][hashtable]$Headers,
        [Parameter(Mandatory)][string]$ApiUrl
    )

    $versions = @()
    $page = 1
    $maxPages = 100
    while ($true) {
        $uri = "$ApiUrl/orgs/$Owner/packages/nuget/$PackageId/versions?per_page=100&page=$page"
        $response = Invoke-WebRequest -Uri $uri -Headers $Headers -SkipHttpErrorCheck `
            -MaximumRetryCount 3 -RetryIntervalSec 5
        if ($response.StatusCode -eq 404) {
            # The package has never been published to GitHub Packages.
            return @()
        }
        if ($response.StatusCode -ne 200) {
            $message = ''
            try { $message = ($response.Content | ConvertFrom-Json).message } catch { $message = '' }
            throw (
                "Unable to read published versions from GitHub Packages ('$uri' returned " +
                "HTTP $($response.StatusCode)$(if ($message) { ": $message" })). A 401/403 means the " +
                "supplied credentials cannot read that organization's packages; the job needs the " +
                "'packages: read' permission (a classic PAT needs the 'read:packages' scope). " +
                'Preview ordering cannot be proved, so the release is blocked; ' +
                'see docs/ReleaseProcess.md.')
        }

        $pageVersions = @(($response.Content | ConvertFrom-Json) | ForEach-Object { [string]$_.name })
        $versions += $pageVersions
        if ($pageVersions.Count -lt 100) {
            break
        }

        $page++
        if ($page -gt $maxPages) {
            # Never spin forever inside a release gate: a feed that keeps
            # answering "full page" is malfunctioning, and an unbounded loop
            # would hang the publish job instead of failing it.
            throw (
                "GitHub Packages returned $maxPages full pages of versions for '$PackageId' " +
                'without terminating, which cannot be a correct response. Preview ordering ' +
                'cannot be proved, so the release is blocked; see docs/ReleaseProcess.md.')
        }
    }

    return $versions
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.channel -ne 'stable') {
    Write-Host (
        "Package set channel is '$($manifest.channel)'; the committed preview build number " +
        'only applies to an exact stable base version, so there is nothing to verify.')
    return
}

$baseVersion = [string]$manifest.basePackageVersion
$previewBuildNumber = Get-PreviewPackageBuildNumber
$candidateVersion = ConvertTo-PreviewPackageVersion -Version $baseVersion `
    -PreviewPackageBuildNumber $previewBuildNumber

$packageIds = @($manifest.archives |
    Where-Object { $_.type -eq 'package' -and (Test-PreviewPackageId -PackageId $_.id) } |
    ForEach-Object { [string]$_.id } |
    Sort-Object -Unique)
if ($packageIds.Count -eq 0) {
    throw (
        "The stable package set for '$baseVersion' contains no preview-family packages at all. " +
        'Either version.targets no longer pins them or the package set is incomplete; ' +
        'see docs/ReleaseProcess.md.')
}

$gitHubHeaders = @{}
if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
    $gitHubHeaders['Authorization'] = "Bearer $GitHubToken"
    $gitHubHeaders['Accept'] = 'application/vnd.github+json'
    $gitHubHeaders['X-GitHub-Api-Version'] = '2022-11-28'
}
else {
    throw (
        'No GitHub token was supplied, so the GitHub Packages feed cannot be queried for ' +
        'already-published preview versions. Preview ordering cannot be proved, so the ' +
        'release is blocked; see docs/ReleaseProcess.md.')
}

$violations = @()
foreach ($packageId in $packageIds) {
    $published = [ordered]@{
        'nuget.org' = Get-NuGetOrgPublishedVersion `
            -Uri "$NuGetOrgIndexUrl/$($packageId.ToLowerInvariant())/index.json"
        'GitHub Packages' = Get-GitHubPackagesPublishedVersion `
            -Owner $GitHubPackagesOwner `
            -PackageId $packageId `
            -Headers $gitHubHeaders `
            -ApiUrl $GitHubApiUrl
    }

    foreach ($feedName in $published.Keys) {
        $blocking = Get-BlockingPublishedPreviewVersions `
            -BaseVersion $baseVersion `
            -PublishedVersions $published[$feedName] `
            -PreviewPackageBuildNumber $previewBuildNumber
        if ($blocking.Count -gt 0) {
            $violations += [pscustomobject]@{
                PackageId = $packageId
                Feed = $feedName
                Blocking = $blocking
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $details = $violations | ForEach-Object {
        "$($_.PackageId) on $($_.Feed): $($_.Blocking -join ', ')"
    }
    throw (
        "The candidate preview version '$candidateVersion' does not sort strictly above every " +
        "already-published preview version. Raise PreviewPackageBuildNumber in " +
        "preview-version.props above the highest number listed below and rebuild the " +
        "candidate: $($details -join '; ').")
}

Write-Host (
    "Verified that '$candidateVersion' sorts strictly above every published preview version of " +
    "$($packageIds.Count) preview-family package(s) on nuget.org and GitHub Packages.")
