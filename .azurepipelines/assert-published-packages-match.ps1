<#
.SYNOPSIS
    Proves that every candidate package already present on a target feed is
    byte-for-byte this candidate, before an irreversible push runs.

.DESCRIPTION
    release.yml pushes with "--skip-duplicate" so that a promotion which
    failed part-way through can be recovered by re-running the same candidate
    (see docs/ReleaseProcess.md). On its own that switch is unsafe: it treats
    *any* pre-existing id/version as a successful no-op without ever looking
    at what is actually published. If a different build had already claimed
    the candidate's immutable version, the promotion would report success
    while the feed permanently served foreign bytes.

    This script closes that hole. For each candidate package it asks the feed
    whether the exact id/version already exists:

      - absent          -> nothing to prove, the push will create it.
      - present, equal  -> a genuine re-run of this candidate; the push may
                           safely skip it as a duplicate.
      - present, differs-> the version is taken by something else. Fail, so
                           the release never claims to have published bytes
                           the feed does not serve.

    Equality is decided by Get-NuGetPackageContentDigest rather than the
    manifest's raw file hash, because nuget.org repository-signs packages at
    ingestion: the served bytes legitimately differ from the pushed bytes in
    the signature part alone. Every other entry must match exactly.

    The script fails closed. An unreachable feed, an unreadable response or a
    package that cannot be downloaded for comparison is an error, not a pass.

    Scope: ".nupkg" packages, which is what "--skip-duplicate" silently
    swallows. Symbol packages (".snupkg") are not compared - nuget.org serves
    them through the symbol server rather than the flat container, so there is
    no equivalent artifact to hash. A symbol duplicate cannot mask wrong
    package content.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,

    [Parameter(Mandatory)]
    [string]$ManifestPath,

    [Parameter(Mandatory)]
    [ValidateSet('nuget.org', 'GitHub Packages')]
    [string]$Feed,

    [string]$GitHubPackagesOwner = 'OPCFoundation',

    [string]$GitHubToken = $env:GITHUB_TOKEN,

    [string]$NuGetOrgFlatContainerUrl = 'https://api.nuget.org/v3-flatcontainer',

    [string]$GitHubPackagesUrl = 'https://nuget.pkg.github.com',

    # nuget.org deliberately never receives the ".Debug" package IDs; they are
    # published only to GitHub Packages. See docs/ReleaseProcess.md.
    [switch]$ExcludeDebugPackages
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'package-version-policy.ps1')

function Get-PublishedPackageBytes {
    <#
    .SYNOPSIS
        Downloads an exact id/version from the feed, or returns $null when the
        feed states it does not exist. Any other outcome throws.
    #>
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination,
        [hashtable]$Headers = @{}
    )

    $response = Invoke-WebRequest -Uri $Uri -Headers $Headers -SkipHttpErrorCheck `
        -MaximumRetryCount 3 -RetryIntervalSec 5 -OutFile $Destination -PassThru
    if ($response.StatusCode -eq 404) {
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Force
        }
        return $null
    }
    if ($response.StatusCode -ne 200) {
        throw (
            "Unable to read '$Uri' (HTTP $($response.StatusCode)). Whether the candidate's " +
            'version is already taken cannot be decided, so the release is blocked; ' +
            'see docs/ReleaseProcess.md.')
    }
    if (-not (Test-Path -LiteralPath $Destination)) {
        throw "'$Uri' answered HTTP 200 but produced no package to compare."
    }
    return $Destination
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$packages = @($manifest.archives | Where-Object { $_.type -ceq 'package' })
if ($packages.Count -eq 0) {
    throw "'$ManifestPath' lists no packages, so there is nothing to verify."
}

$headers = @{}
if ($Feed -ceq 'GitHub Packages') {
    if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
        throw (
            'No GitHub token was supplied, so GitHub Packages cannot be queried for an ' +
            'already-published candidate version. The release is blocked; ' +
            'see docs/ReleaseProcess.md.')
    }
    $headers['Authorization'] = "Bearer $GitHubToken"
}

$workingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('n'))
[void](New-Item -ItemType Directory -Path $workingDirectory -Force)
try {
    $mismatches = @()
    $verified = 0
    $absent = 0
    foreach ($package in $packages) {
        $id = [string]$package.id
        if ($ExcludeDebugPackages -and
            $id.EndsWith('.Debug', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $localPath = Join-Path $PackageDirectory ([string]$package.file)
        if (-not (Test-Path -LiteralPath $localPath)) {
            throw "The candidate package '$($package.file)' is missing from '$PackageDirectory'."
        }

        $lowerId = $id.ToLowerInvariant()
        $lowerVersion = ([string]$package.version).ToLowerInvariant()
        $uri = if ($Feed -ceq 'nuget.org') {
            "$NuGetOrgFlatContainerUrl/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"
        }
        else {
            "$GitHubPackagesUrl/$GitHubPackagesOwner/download/$lowerId/$lowerVersion/" +
                "$lowerId.$lowerVersion.nupkg"
        }

        $destination = Join-Path $workingDirectory "$lowerId.$lowerVersion.nupkg"
        $downloaded = Get-PublishedPackageBytes -Uri $uri -Destination $destination -Headers $headers
        if ($null -eq $downloaded) {
            $absent++
            continue
        }

        $publishedDigest = Get-NuGetPackageContentDigest -Path $downloaded
        $candidateDigest = Get-NuGetPackageContentDigest -Path $localPath
        Remove-Item -LiteralPath $downloaded -Force
        if ($publishedDigest -cne $candidateDigest) {
            $mismatches += [pscustomobject]@{
                PackageId = $id
                Version = [string]$package.version
                Published = $publishedDigest
                Candidate = $candidateDigest
            }
            continue
        }
        $verified++
    }

    if ($mismatches.Count -gt 0) {
        $details = $mismatches | ForEach-Object {
            "$($_.PackageId) $($_.Version) (published $($_.Published), candidate $($_.Candidate))"
        }
        throw (
            "$Feed already serves $($mismatches.Count) of this candidate's immutable " +
            'package version(s) with different content, so promoting would report success ' +
            'while the feed keeps the other build''s bytes forever. Release a new version ' +
            "instead of re-using a taken one: $($details -join '; ')")
    }

    Write-Host (
        "Verified $Feed duplicates: $verified package(s) already published are byte-identical " +
        "to this candidate and $absent are not yet published.")
}
finally {
    Remove-Item -LiteralPath $workingDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
