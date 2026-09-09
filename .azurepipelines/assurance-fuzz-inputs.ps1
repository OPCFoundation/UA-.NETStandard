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
Freezes the public replay inventory and optionally verifies the built input copy.
.DESCRIPTION
Only committed public input roots from the profile are supported. Do not extract
private archives into these roots. Output contains counts and an aggregate digest,
never input names, content or exception messages. Public input hashes bind replay.
An absent or
empty good-seed bucket fails; a frozen zero regression inventory is legitimate.
#>
param(
    [string] $RepoRoot = (Split-Path $PSScriptRoot),
    [Parameter(Mandatory)][string] $Project,
    [string] $ProfilesPath = (Join-Path $PSScriptRoot 'assurance-profiles.json'),
    [string] $BuildOutput = '',
    [Parameter(Mandatory)][string] $OutputPath
)
$ErrorActionPreference = 'Stop'
try {
    $profiles = Get-Content -LiteralPath $ProfilesPath -Raw | ConvertFrom-Json
    $job = @($profiles.profiles.jobs | Where-Object { $_.project -eq $Project.Replace('\', '/') })
    if ($job.Count -ne 1 -or -not $job[0].corpusRoot) { throw 'Unknown corpus.' }
    $job = $job[0]
    $manifest = [ordered]@{
        schemaVersion = 1; project = $job.project; goodInputs = 0; regressionInputs = 0
        buckets = @(); inputs = @()
    }
    $inventory = [System.Collections.Generic.List[string]]::new()
    $destinations = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sources = [ordered]@{}
    foreach ($bucket in $job.corpusBuckets) {
        $source = Join-Path $RepoRoot "$($job.corpusRoot)/$bucket"
        if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Missing good seed bucket.' }
        $files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
        if ($files.Count -eq 0) { throw 'Empty good seed bucket.' }
        $manifest.goodInputs += $files.Count
        $manifest.buckets += @{ bucket = $bucket; count = $files.Count }
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
            $destination = 'Testcases/' + $bucket.Substring('Testcases.'.Length) + '/' + $relative
            if ($sources.Contains($destination)) { throw 'Case-insensitive output collision.' }
            $sources[$destination] = $file.FullName
        }
    }
    $regressionRoot = Join-Path $RepoRoot $job.regressionRoot
    if (Test-Path -LiteralPath $regressionRoot -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $regressionRoot -File -Recurse) {
            if ($file.Name -notmatch '^(crash|timeout|slow)') { continue }
            $relative = [IO.Path]::GetRelativePath($regressionRoot, $file.FullName).Replace('\', '/')
            $destination = 'Assets/' + $relative
            if ($sources.Contains($destination)) { throw 'Case-insensitive output collision.' }
            $sources[$destination] = $file.FullName
            $manifest.regressionInputs++
        }
    }
    foreach ($entry in $sources.GetEnumerator()) {
        if (-not $destinations.Add($entry.Key)) { throw 'Case-insensitive output collision.' }
        $hash = (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash.ToLowerInvariant()
        $id = 'sha256:' + [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($entry.Key))).ToLowerInvariant()
        $category = 'good'
        if ($entry.Key.StartsWith('Assets/')) {
            $null = [IO.Path]::GetFileName($entry.Value) -match '^(crash|timeout|slow)'
            $category = $Matches[1]
        }
        $manifest.inputs += @{ id = $id; digest = "sha256:$hash"; category = $category }
        $inventory.Add($entry.Key + ':' + $hash)
        if ($BuildOutput) {
            $copy = Join-Path $BuildOutput $entry.Key
            if (-not (Test-Path -LiteralPath $copy -PathType Leaf) -or
                (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) {
                throw 'Missing, stale, or overwritten input copy.'
            }
        }
    }
    if ($BuildOutput) {
        foreach ($folder in @('Testcases', 'Assets')) {
            $path = Join-Path $BuildOutput $folder
            if (-not (Test-Path -LiteralPath $path)) { continue }
            foreach ($file in Get-ChildItem -LiteralPath $path -File -Recurse) {
                if ($folder -eq 'Assets' -and $file.Name -notmatch '^(crash|timeout|slow)') { continue }
                if (-not $destinations.Contains([IO.Path]::GetRelativePath($BuildOutput, $file.FullName).Replace('\', '/'))) {
                    throw 'Unexpected copied input.'
                }
            }
        }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($inventory | Sort-Object -CaseSensitive) -join "`n")
        $manifest.inventoryDigest = 'sha256:' + [Convert]::ToHexString($sha.ComputeHash($bytes)).ToLowerInvariant()
    }
    finally { $sha.Dispose() }
    $parent = Split-Path -Parent $OutputPath
    if ($parent) { $null = New-Item -ItemType Directory -Path $parent -Force }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    Write-Host "Public replay inventory verified: good=$($manifest.goodInputs), regression=$($manifest.regressionInputs)."
    exit 0
}
catch {
    Write-Host 'Replay inventory incomplete: missing/empty seeds or invalid source-to-output mapping.'
    exit 1
}
