# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
# OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
<#
.SYNOPSIS
Publishes and replays an uninstrumented, relocatable OneFuzz drop without uploading it.
.DESCRIPTION
All SDK output and restore assets use an isolated --artifacts-path. The manifest is
authoritative; missing callbacks, inputs, dependencies, or ownership fail the build.
Without OwnershipProfile no OneFuzzConfig.json is emitted. Existing output/work
directories are never reused or deleted. A failed run retains diagnostics but does
not write publication.json. By default, declared corpus generators materialize
ephemeral fixtures in the isolated work directory. An explicit CorpusRoot supplies
all corpus inputs instead and disables generation.
#>
[CmdletBinding()]
param(
    [string]$Manifest,
    [string[]]$Areas = @(),
    [string]$OutputDirectory,
    [string]$WorkDirectory,
    [string]$CorpusRoot,
    [string]$OwnershipProfile,
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Manifest) {
    $Manifest = Join-Path $root 'fuzzing\fuzz-targets.json'
}
$externalCorpus = $PSBoundParameters.ContainsKey('CorpusRoot')
if ($externalCorpus -and [string]::IsNullOrWhiteSpace($CorpusRoot)) {
    throw 'An explicit CorpusRoot must be a nonempty existing directory.'
}
if (-not $externalCorpus) {
    $CorpusRoot = $root
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root ('artifacts\onefuzz\drop-' + [guid]::NewGuid().ToString('N'))
}
if (-not $WorkDirectory) {
    $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('opcua-onefuzz-build-' + [guid]::NewGuid().ToString('N'))
}
$Manifest = [IO.Path]::GetFullPath($Manifest)
$CorpusRoot = [IO.Path]::GetFullPath($CorpusRoot)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if ($OutputDirectory -eq $WorkDirectory -or
    $WorkDirectory.StartsWith($OutputDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The output directory must not equal or contain the isolated work directory.'
}
foreach ($directory in @($OutputDirectory, $WorkDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        throw "Use a new output/work directory; refusing to reuse or delete: $directory"
    }
}
if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) {
    throw "Required fuzz manifest is missing: $Manifest"
}
if (-not (Test-Path -LiteralPath $CorpusRoot -PathType Container)) {
    throw "Required corpus root is missing: $CorpusRoot"
}
if ($OwnershipProfile) {
    $OwnershipProfile = (Get-Item -LiteralPath $OwnershipProfile).FullName
}
& (Join-Path $PSScriptRoot 'validate-fuzz-manifest.ps1') -ManifestPath $Manifest -RepositoryRoot $root

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit $LASTEXITCODE."
    }
}

function Resolve-DeclaredPath {
    param([string]$Base, [string]$Relative)
    $parts = $Relative -split '[\\/]'
    if ([IO.Path]::IsPathRooted($Relative) -or $parts.Count -eq 0 -or
        @($parts | Where-Object { -not $_ -or $_ -in '.', '..' -or $_ -match '[:*?]' }).Count -ne 0) {
        throw "Expected a portable repository-relative path: $Relative"
    }
    $current = $Base
    foreach ($part in $parts) {
        $entries = @(Get-ChildItem -LiteralPath $current -Force |
            Where-Object { [string]::Equals($_.Name, $part, [StringComparison]::Ordinal) })
        if ($entries.Count -ne 1) {
            throw "Required path is missing or incorrectly cased under $Base`: $Relative"
        }
        if (($entries[0].Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Links are not allowed in a OneFuzz drop: $Relative"
        }
        $current = $entries[0].FullName
    }
    return $current
}

function Get-SafeFiles {
    param([string]$Path)
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $item = Get-Item -LiteralPath $pending.Pop() -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Links are not allowed in a OneFuzz drop: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $item.FullName -Force) {
                $pending.Push($child.FullName)
            }
        }
        else {
            $item.FullName
        }
    }
}

[void][IO.Directory]::CreateDirectory($WorkDirectory)
$sdk = Join-Path $WorkDirectory 'sdk'
$validatorOutput = Join-Path $WorkDirectory 'validator'
$payload = Join-Path $WorkDirectory 'payload'
[void][IO.Directory]::CreateDirectory($payload)
$validatorProject = Join-Path $root 'fuzzing\OneFuzz\Opc.Ua.OneFuzz.Validator\Opc.Ua.OneFuzz.Validator.csproj'
$common = @(
    '-c', 'Release', '-p:CustomTestTarget=net10.0', '-p:FuzzCoverage=true', '-p:UseSharedCompilation=false',
    '-maxcpucount:1', "--artifacts-path", $sdk,
    "-p:RestoreConfigFile=$(Join-Path $root 'NuGet.config')", '--nologo', '--verbosity', 'minimal'
)
Invoke-DotNet (@('publish', $validatorProject, '--output', $validatorOutput) + $common)
$validator = Join-Path $validatorOutput 'Opc.Ua.OneFuzz.Validator.dll'
$selection = @($Areas | ForEach-Object { $_ -split ',' })
$manifestArguments = @($validator, 'check-manifest', '--manifest', $Manifest)
if ($selection.Count -gt 0) {
    $manifestArguments += '--areas', ($selection -join ',')
}
Invoke-DotNet $manifestArguments
$manifestHash = (Get-FileHash -LiteralPath $Manifest -Algorithm SHA256).Hash.ToLowerInvariant()
$snapshot = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json -AsHashtable
$selected = @($snapshot.areas | Where-Object { $selection.Count -eq 0 -or $_.id -cin $selection })
if ($selected.Count -eq 0) {
    throw 'No selected manifest areas.'
}
$snapshot.areas = $selected
$snapshot | ConvertTo-Json -Depth 100 |
    Set-Content -LiteralPath (Join-Path $payload 'fuzz-targets.json') -Encoding utf8
$generatedPaths = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$generationEvidence = [Collections.Generic.List[object]]::new()
if (-not $externalCorpus) {
    $requiredCorpora = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($area in $selected) {
        foreach ($target in $area.targets) {
            foreach ($corpus in $target.corpus) {
                [void]$requiredCorpora.Add($corpus)
            }
        }
    }
    foreach ($generator in $snapshot.corpusGenerators) {
        $outputs = @($generator.outputs | Where-Object { $requiredCorpora.Contains($_.path) })
        if ($outputs.Count -eq 0) { continue }
        $generatorProject = Resolve-DeclaredPath $root $generator.project
        $generatorWork = Join-Path (Join-Path $WorkDirectory 'generators') $generator.id
        $generatorPublished = Join-Path $generatorWork 'published'
        $generatorOutput = Join-Path $generatorWork 'corpus'
        [void][IO.Directory]::CreateDirectory($generatorOutput)
        Invoke-DotNet (@('publish', $generatorProject, '-f', 'net10.0', '--output', $generatorPublished) + $common)
        $generatorDll = Join-Path $generatorPublished ([IO.Path]::GetFileNameWithoutExtension($generatorProject) + '.dll')
        $generatorArguments = @($generator.arguments | ForEach-Object {
            $_.Replace('{outputRoot}', $generatorOutput)
        })
        Invoke-DotNet (@($generatorDll) + $generatorArguments)
        $materialized = @()
        foreach ($output in $outputs) {
            $directory = Resolve-DeclaredPath $generatorOutput $output.directory
            $files = @(Get-SafeFiles $directory)
            if ($files.Count -eq 0) {
                throw "Generator $($generator.id) produced an empty required corpus: $($output.path)"
            }
            $generatedPaths.Add($output.path, $directory)
            $materialized += [ordered]@{
                path = $output.path
                files = @($files | Sort-Object -CaseSensitive | ForEach-Object {
                    [ordered]@{
                        path = [IO.Path]::GetRelativePath($directory, $_).Replace('\', '/')
                        bytes = (Get-Item -LiteralPath $_).Length
                        sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                })
            }
        }
        $generationEvidence.Add([ordered]@{
            id = $generator.id
            project = $generator.project
            arguments = $generator.arguments
            outputs = $materialized
        })
    }
}
$copied = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$hashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)

function Copy-DropFile {
    param([string]$Source, [string]$Relative)
    $relativePath = $Relative.Replace('\', '/')
    if ($relativePath -iin @('fuzz-targets.json', 'build.json', 'OneFuzzConfig.json',
        'validation.json', 'publication.json', 'files.sha256', '.publication-complete.tmp')) {
        throw "A published input may not replace reserved drop metadata: $relativePath"
    }
    if ([IO.Path]::GetFileName($relativePath).StartsWith('SharpFuzz', [StringComparison]::OrdinalIgnoreCase)) {
        throw "A service drop must not contain a SharpFuzz runner or local instrumentation: $relativePath"
    }
    $hash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    if ($copied.ContainsKey($relativePath)) {
        if ($copied[$relativePath] -cne $relativePath -or $hashes[$relativePath] -cne $hash) {
            throw "Conflicting published dependency/input at $relativePath."
        }
        return
    }
    $destination = Join-Path $payload $relativePath
    [void][IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
    Copy-Item -LiteralPath $Source -Destination $destination
    $copied.Add($relativePath, $relativePath)
    $hashes.Add($relativePath, $hash)
}

foreach ($area in $selected) {
    $project = Resolve-DeclaredPath $root $area.project
    if ([IO.Path]::GetExtension($project) -cne '.csproj') {
        throw "Manifest project is not a csproj: $($area.project)"
    }
    $published = Join-Path (Join-Path $WorkDirectory 'areas') $area.id
    Invoke-DotNet (@(
        'publish', $project, '-f', 'net10.0', '-p:OneFuzz=true',
        '--output', $published
    ) + $common)
    foreach ($file in Get-SafeFiles $published) {
        Copy-DropFile $file ([IO.Path]::GetRelativePath($published, $file))
    }
    foreach ($target in $area.targets) {
        foreach ($corpus in $target.corpus) {
            $source = if ($generatedPaths.ContainsKey($corpus)) {
                $generatedPaths[$corpus]
            }
            else {
                Resolve-DeclaredPath $CorpusRoot $corpus
            }
            $files = @(Get-SafeFiles $source)
            if ($files.Count -eq 0) {
                throw "Required corpus is empty for $($target.id): $corpus"
            }
            foreach ($file in $files) {
                $relative = if (Test-Path -LiteralPath $source -PathType Leaf) {
                    $corpus
                }
                else {
                    Join-Path $corpus ([IO.Path]::GetRelativePath($source, $file))
                }
                Copy-DropFile $file $relative
            }
        }
        foreach ($dictionary in $target.dictionaries) {
            $source = Resolve-DeclaredPath $root $dictionary
            if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -eq 0) {
                throw "Required dictionary is missing or empty: $dictionary"
            }
            Copy-DropFile $source $dictionary
        }
    }
}

$sourceCommit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot record the source revision.'
}
$sourceChanges = & git -C $root status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot record source working-tree state.'
}
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot record the SDK version.'
}
$metadata = [ordered]@{
    schemaVersion = 1
    sourceCommit = $sourceCommit.Trim()
    sourceWorkingTreeDirty = @($sourceChanges).Count -ne 0
    manifestSourceSha256 = $manifestHash
    sdkVersion = $sdkVersion.Trim()
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    builtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    configuration = 'Release'
    framework = 'net10.0'
    workerOS = 'azurelinux3'
    workerArchitecture = $null
    resolvedServiceImage = $null
    instrumentation = 'service-owned; drop is not preinstrumented'
    instrumenterVersion = $null
    isolatedSdkArtifacts = $true
    corpusSource = if ($externalCorpus) { 'explicit-external-root' } else { 'repository-and-declared-generators' }
    corpusGeneration = $generationEvidence.ToArray()
    areas = @($selected.id)
    ownershipProfileSha256 = $null
    serviceSubmissionPerformed = $false
}
if ($OwnershipProfile) {
    $profile = Get-Content -LiteralPath $OwnershipProfile -Raw | ConvertFrom-Json -AsHashtable
    $metadata.workerArchitecture = $profile.worker.architecture
    $metadata.ownershipProfileSha256 =
        (Get-FileHash -LiteralPath $OwnershipProfile -Algorithm SHA256).Hash.ToLowerInvariant()
}
$metadata | ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath (Join-Path $payload 'build.json') -Encoding utf8
if ($OwnershipProfile) {
    Invoke-DotNet @($validator, 'configure', '--drop', $payload, '--profile', $OwnershipProfile)
}

# Validation runs after relocation, with an external BCL-only validator and no
# checkout/NuGet fallback for application assemblies.
[void][IO.Directory]::CreateDirectory((Split-Path $OutputDirectory -Parent))
Move-Item -LiteralPath $payload -Destination $OutputDirectory
$results = Join-Path $WorkDirectory 'validation'
Invoke-DotNet @(
    $validator, 'validate', '--drop', $OutputDirectory, '--results', $results,
    '--timeout-seconds', $TimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
)
Copy-Item -LiteralPath (Join-Path $results 'validation.json') -Destination (Join-Path $OutputDirectory 'validation.json')
$publication = Join-Path $WorkDirectory 'publication.json'
[ordered]@{
    schemaVersion = 1
    status = 'validated'
    serviceSubmissionPerformed = $false
    serviceCanaryPerformed = $false
    configurationGenerated = [bool]$OwnershipProfile
} | ConvertTo-Json | Set-Content -LiteralPath $publication -Encoding utf8
$inventory = @(Get-SafeFiles $OutputDirectory | Sort-Object -CaseSensitive | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetRelativePath($OutputDirectory, $_).Replace('\', '/'))"
})
$inventory += "$((Get-FileHash -LiteralPath $publication -Algorithm SHA256).Hash.ToLowerInvariant())  publication.json"
$inventory | Set-Content -LiteralPath (Join-Path $OutputDirectory 'files.sha256') -Encoding utf8
$publicationStaging = Join-Path $OutputDirectory '.publication-complete.tmp'
Copy-Item -LiteralPath $publication -Destination $publicationStaging
[IO.File]::Move($publicationStaging, (Join-Path $OutputDirectory 'publication.json'))
Write-Output "Validated uninstrumented drop: $OutputDirectory"
Write-Output "Build and replay evidence: $WorkDirectory"
