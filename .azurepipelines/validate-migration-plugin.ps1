#!/usr/bin/env pwsh
# ========================================================================
# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person
# obtaining a copy of this software and associated documentation
# files (the "Software"), to deal in the Software without
# restriction, including without limitation the rights to use,
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the
# Software is furnished to do so, subject to the following
# conditions:
#
# The above copyright notice and this permission notice shall be
# included in all copies or substantial portions of the Software.
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
# EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
# OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
# NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
# HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/
# ========================================================================

<#
.SYNOPSIS
    Validates the migration plugin manifests and bundled migration docs.

.DESCRIPTION
    Confirms that plugin metadata matches the marketplace entry and that the
    plugin's offline migration-doc bundle matches docs/migrate/2.0.x after
    deterministic link rewriting.

.PARAMETER Update
    Regenerates the bundled migration docs instead of only checking them.
#>

[CmdletBinding()]
param(
    [switch] $Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceDirectory = Join-Path $repositoryRoot 'docs/migrate/2.0.x'
$pluginRoot = Join-Path $repositoryRoot '.agents'
$skillRoot = Join-Path $pluginRoot 'skills/opcua-v20-migration'
$bundleDirectory = Join-Path $skillRoot 'references/stack-migration'
$skillFile = [IO.Path]::GetFullPath((Join-Path $skillRoot 'SKILL.md'))
$pathComparison = if ($IsWindows)
{
    [StringComparison]::OrdinalIgnoreCase
}
else
{
    [StringComparison]::Ordinal
}

function Assert-Condition
{
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition)
    {
        throw $Message
    }
}

function Test-IsUnderDirectory
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Directory
    )

    $directoryPath = [IO.Path]::GetFullPath($Directory).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $directoryPath + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    return $fullPath.StartsWith($prefix, $script:pathComparison)
}

function ConvertTo-NormalizedText
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    $normalized = $Text.Replace("`r`n", "`n")
    return $normalized.TrimEnd([char[]] "`r`n") + "`n"
}

function Convert-MigrationDocument
{
    param(
        [Parameter(Mandatory = $true)]
        [IO.FileInfo] $SourceFile
    )

    $sourceText = ConvertTo-NormalizedText ([IO.File]::ReadAllText($SourceFile.FullName))
    $linkPattern = '(!?\[[^\]]*\]\()([^)]+)(\))'
    $converted = [Text.RegularExpressions.Regex]::Replace(
        $sourceText,
        $linkPattern,
        {
            param([Text.RegularExpressions.Match] $match)

            $target = $match.Groups[2].Value.Trim()
            if ($target.StartsWith('http://', [StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith('#', [StringComparison]::Ordinal) -or
                $target.StartsWith('mailto:', [StringComparison]::OrdinalIgnoreCase))
            {
                return $match.Value
            }

            $fragmentIndex = $target.IndexOf('#', [StringComparison]::Ordinal)
            if ($fragmentIndex -ge 0)
            {
                $linkPath = $target.Substring(0, $fragmentIndex)
                $fragment = $target.Substring($fragmentIndex)
            }
            else
            {
                $linkPath = $target
                $fragment = ''
            }

            $resolved = [IO.Path]::GetFullPath((Join-Path $SourceFile.DirectoryName $linkPath))
            if (Test-IsUnderDirectory $resolved $sourceDirectory)
            {
                return $match.Value
            }

            if ([string]::Equals($resolved, $skillFile, $pathComparison))
            {
                $replacement = '../../SKILL.md'
            }
            elseif (Test-IsUnderDirectory $resolved $repositoryRoot)
            {
                $relative = [IO.Path]::GetRelativePath($repositoryRoot, $resolved) -replace '\\', '/'
                $replacement =
                    'https://github.com/OPCFoundation/UA-.NETStandard/blob/master/' + $relative
            }
            else
            {
                throw "Relative link '$target' in '$($SourceFile.FullName)' leaves the repository."
            }

            return $match.Groups[1].Value + $replacement + $fragment + $match.Groups[3].Value
        })

    if ($SourceFile.Name -eq 'README.md')
    {
        $heading = "# Migrating from 1.5.378 to 2.0.x`n"
        $note = @'

> **Bundled plugin snapshot.** These thematic migration docs ship with the
> `opcua-v20-migration` plugin so its core workflow works offline. Links to
> files outside this directory are optional upstream references.
'@
        $converted = $converted.Replace($heading, $heading + $note + "`n")
    }

    return ConvertTo-NormalizedText $converted
}

Assert-Condition (Test-Path $sourceDirectory -PathType Container) (
    "Migration source directory not found: $sourceDirectory")
Assert-Condition (Test-Path $skillRoot -PathType Container) (
    "Migration skill directory not found: $skillRoot")

$sourceFiles = @(Get-ChildItem $sourceDirectory -Filter '*.md' -File | Sort-Object Name)
Assert-Condition ($sourceFiles.Count -gt 0) 'No migration source documents were found.'

if ($Update)
{
    New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null
    $sourceNames = @($sourceFiles.Name)
    Get-ChildItem $bundleDirectory -Filter '*.md' -File |
        Where-Object { $sourceNames -notcontains $_.Name } |
        Remove-Item -Force
}
else
{
    Assert-Condition (Test-Path $bundleDirectory -PathType Container) (
        "Bundled migration directory not found: $bundleDirectory")
    $bundleNames = @(Get-ChildItem $bundleDirectory -Filter '*.md' -File |
        Sort-Object Name |
        ForEach-Object Name)
    $nameDifferences = @(Compare-Object @($sourceFiles.Name) $bundleNames)
    Assert-Condition ($nameDifferences.Count -eq 0) (
        "Bundled migration document set differs from docs/migrate/2.0.x: " +
        ($nameDifferences | Out-String))
}

foreach ($sourceFile in $sourceFiles)
{
    $expected = Convert-MigrationDocument $sourceFile
    $bundleFile = Join-Path $bundleDirectory $sourceFile.Name
    if ($Update)
    {
        $nativeText = $expected.Replace("`n", [Environment]::NewLine)
        [IO.File]::WriteAllText($bundleFile, $nativeText, [Text.UTF8Encoding]::new($false))
        continue
    }

    Assert-Condition (Test-Path $bundleFile -PathType Leaf) (
        "Bundled migration document not found: $bundleFile")
    $actual = ConvertTo-NormalizedText ([IO.File]::ReadAllText($bundleFile))
    Assert-Condition ([string]::Equals($expected, $actual, [StringComparison]::Ordinal)) (
        "'$($sourceFile.Name)' is out of sync. Run " +
        "'./.azurepipelines/validate-migration-plugin.ps1 -Update'.")
}

$pluginManifestPath = Join-Path $pluginRoot 'plugin.json'
$marketplaceManifestPath = Join-Path $repositoryRoot '.github/plugin/marketplace.json'
$plugin = Get-Content $pluginManifestPath -Raw | ConvertFrom-Json
$marketplace = Get-Content $marketplaceManifestPath -Raw | ConvertFrom-Json

foreach ($property in @('name', 'description', 'version', 'homepage', 'repository', 'license'))
{
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string] $plugin.$property)) (
        "Plugin property '$property' must not be empty.")
}
Assert-Condition (-not [string]::IsNullOrWhiteSpace([string] $plugin.author.name)) (
    'Plugin author name must not be empty.')
Assert-Condition (-not [string]::IsNullOrWhiteSpace([string] $plugin.author.url)) (
    'Plugin author URL must not be empty.')
Assert-Condition (@($plugin.keywords).Count -gt 0) 'Plugin must declare at least one keyword.'
Assert-Condition (
    @($plugin.skills).Count -eq 1 -and $plugin.skills[0] -eq './skills/'
) "Plugin must expose its skills through './skills/'."
Assert-Condition (-not [string]::IsNullOrWhiteSpace([string] $marketplace.name)) (
    'Marketplace name must not be empty.')
Assert-Condition (-not [string]::IsNullOrWhiteSpace([string] $marketplace.owner.name)) (
    'Marketplace owner name must not be empty.')

$entries = @($marketplace.plugins | Where-Object { $_.name -eq $plugin.name })
Assert-Condition ($entries.Count -eq 1) (
    "Marketplace must contain exactly one entry for plugin '$($plugin.name)'.")
$entry = $entries[0]
Assert-Condition ($entry.source -eq '.agents') "Plugin '$($plugin.name)' must use source '.agents'."

foreach ($property in @('description', 'version', 'homepage', 'repository', 'license'))
{
    Assert-Condition ($entry.$property -eq $plugin.$property) (
        "Plugin property '$property' differs between plugin.json and marketplace.json.")
}
Assert-Condition ($entry.author.name -eq $plugin.author.name) 'Plugin author names differ.'
Assert-Condition ($entry.author.url -eq $plugin.author.url) 'Plugin author URLs differ.'
Assert-Condition (
    [string]::Join("`0", @($entry.keywords)) -eq [string]::Join("`0", @($plugin.keywords))
) 'Plugin keyword lists differ.'

$verb = if ($Update) { 'Updated and validated' } else { 'Validated' }
Write-Host "$verb $($sourceFiles.Count) bundled migration docs; plugin manifests are synchronized."
