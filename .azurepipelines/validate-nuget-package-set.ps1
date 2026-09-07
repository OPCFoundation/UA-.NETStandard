<#
.SYNOPSIS
    Validates a directory of signed NuGet packages and writes a deterministic manifest.

.DESCRIPTION
    Reads package IDs and versions from embedded nuspec files, rejects duplicate
    package identities, mixed versions, orphaned symbol packages, and optionally
    verifies package signatures. The resulting JSON manifest is suitable for
    binding a later promotion workflow to the exact package bytes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,

    [Parameter(Mandatory)]
    [string]$ManifestPath,

    [string]$ExpectedVersion,

    [switch]$RequireDebug,

    [switch]$VerifySignatures
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$Package
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Package.FullName)
    try {
        $nuspecs = @($archive.Entries | Where-Object {
            $_.FullName -notmatch '/' -and $_.FullName.EndsWith(
                '.nuspec',
                [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($nuspecs.Count -ne 1) {
            throw "Package '$($Package.Name)' contains $($nuspecs.Count) root nuspec files."
        }

        $reader = [System.IO.StreamReader]::new($nuspecs[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $nuspec.package.metadata
        if ([string]::IsNullOrWhiteSpace($metadata.id) -or
            [string]::IsNullOrWhiteSpace($metadata.version)) {
            throw "Package '$($Package.Name)' has no package ID or version."
        }

        return [pscustomobject]@{
            Id = [string]$metadata.id
            Version = [string]$metadata.version
        }
    }
    finally {
        $archive.Dispose()
    }
}

$resolvedDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $resolvedDirectory -File |
    Where-Object {
        $_.Name.EndsWith('.nupkg', [System.StringComparison]::OrdinalIgnoreCase) -or
        $_.Name.EndsWith('.snupkg', [System.StringComparison]::OrdinalIgnoreCase)
    } |
    Sort-Object Name)
if ($packages.Count -eq 0) {
    throw "No NuGet packages were found in '$resolvedDirectory'."
}

$archives = @()
foreach ($package in $packages) {
    $identity = Get-PackageIdentity -Package $package
    $type = if ($package.Name.EndsWith(
        '.snupkg',
        [System.StringComparison]::OrdinalIgnoreCase)) {
        'symbols'
    }
    else {
        'package'
    }

    if ($VerifySignatures -and $type -eq 'package') {
        & dotnet nuget verify --all $package.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed for '$($package.Name)'."
        }
    }

    $archives += [pscustomobject]@{
        id = $identity.Id
        version = $identity.Version
        type = $type
        file = $package.Name
        sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$normalPackages = @($archives | Where-Object type -eq 'package')
if ($normalPackages.Count -eq 0) {
    throw "No .nupkg files were found in '$resolvedDirectory'."
}

$duplicates = @($normalPackages |
    Group-Object { "$($_.id.ToLowerInvariant())|$($_.version.ToLowerInvariant())" } |
    Where-Object Count -gt 1)
if ($duplicates.Count -gt 0) {
    $duplicateNames = $duplicates | ForEach-Object {
        ($_.Group | ForEach-Object file) -join ', '
    }
    throw "Duplicate package ID/version pairs were found: $($duplicateNames -join '; ')."
}

$versions = @($normalPackages.version | Sort-Object -Unique)
if ($versions.Count -ne 1) {
    throw "The package set contains multiple versions: $($versions -join ', ')."
}
if ($ExpectedVersion -and $versions[0] -cne $ExpectedVersion) {
    throw "Expected package version '$ExpectedVersion', but found '$($versions[0])'."
}

$normalKeys = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($package in $normalPackages) {
    [void]$normalKeys.Add("$($package.id)|$($package.version)")
}

$orphanedSymbols = @($archives | Where-Object {
    $_.type -eq 'symbols' -and -not $normalKeys.Contains("$($_.id)|$($_.version)")
})
if ($orphanedSymbols.Count -gt 0) {
    throw "Symbol packages without matching packages were found: $($orphanedSymbols.file -join ', ')."
}

$debugPackages = @($normalPackages | Where-Object {
    $_.id.EndsWith('.Debug', [System.StringComparison]::OrdinalIgnoreCase)
})
if ($RequireDebug -and $debugPackages.Count -eq 0) {
    throw 'The package set does not contain any .Debug package IDs.'
}
if ($RequireDebug -and $normalPackages.Count -eq $debugPackages.Count) {
    throw 'The package set does not contain any Release package IDs.'
}

$manifestDirectory = Split-Path -Parent $ManifestPath
if ($manifestDirectory) {
    New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null
}

$manifest = [ordered]@{
    packageVersion = $versions[0]
    packageCount = $normalPackages.Count
    symbolPackageCount = @($archives | Where-Object type -eq 'symbols').Count
    debugPackageCount = $debugPackages.Count
    archives = @($archives | Sort-Object id, type, file)
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $ManifestPath -Encoding utf8NoBOM

Write-Host (
    "Validated $($manifest.packageCount) package(s), " +
    "$($manifest.symbolPackageCount) symbol package(s), and " +
    "$($manifest.debugPackageCount) Debug package(s) at version $($manifest.packageVersion).")
