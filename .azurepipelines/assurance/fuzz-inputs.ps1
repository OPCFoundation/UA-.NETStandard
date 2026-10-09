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
Only committed public input roots from profile jobs or additional replay definitions
are supported. A separate clean checkout pinned by PublicCorpusCommit can supply
published nightly crash inputs; these are not release-assurance inputs.
Additional projects do not join the release profiles. Do not extract private
archives into these roots. Output contains counts and an aggregate digest,
never input names, content or exception messages. Public input hashes bind replay.
An absent or
empty good-seed bucket fails; a frozen zero regression inventory is legitimate.
CI uses RequireCommitted to reject untracked inputs and tracked changes relative
to HEAD. Local input-copy fixtures can validate their synthetic roots without Git.
#>
param(
    [string] $RepoRoot = (Split-Path (Split-Path $PSScriptRoot)),
    [Parameter(Mandatory)][string] $Project,
    [string] $ProfilesPath = (Join-Path $PSScriptRoot 'profiles.json'),
    [string] $BuildOutput = '',
    [switch] $RequireCommitted,
    [string] $PublicCorpusCheckout = '',
    [string] $PublicCorpusCommit = '',
    [Parameter(Mandatory)][string] $OutputPath
)
$ErrorActionPreference = 'Stop'
try {
    $profiles = Get-Content -LiteralPath $ProfilesPath -Raw | ConvertFrom-Json
    $definitions = @($profiles.profiles.jobs) + @($profiles.additionalReplayProjects)
    $job = @($definitions | Where-Object { $_.project -eq $Project.Replace('\', '/') })
    if ($job.Count -ne 1 -or -not $job[0].corpusRoot) { throw 'Unknown corpus.' }
    $job = $job[0]
    $manifest = [ordered]@{
        schemaVersion = 1; project = $job.project; goodInputs = 0; regressionInputs = 0
        buckets = @(); inputs = @()
    }
    $inventory = [System.Collections.Generic.List[string]]::new()
    $destinations = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sources = [ordered]@{}
    $publishedInputs = @{}
    if ($PublicCorpusCheckout -or $PublicCorpusCommit) {
        if (-not $RequireCommitted -or -not $PublicCorpusCheckout -or
            $PublicCorpusCommit -cnotmatch '^[0-9a-f]{40}$') {
            throw 'Published replay inputs require a committed source identity.'
        }
        $actualCommit = & git -C $PublicCorpusCheckout rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actualCommit -cne $PublicCorpusCommit) {
            throw 'Published replay checkout does not match the pinned commit.'
        }
        $changes = @(& git -C $PublicCorpusCheckout status --porcelain=v1 --untracked-files=all)
        if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) {
            throw 'Published replay checkout is not clean.'
        }
        $externalRoot = Join-Path $PublicCorpusCheckout (
            [IO.Path]::GetFileNameWithoutExtension($job.project) + '/Assets')
        $trackedOutput = @(& git -C $PublicCorpusCheckout ls-files -z -- (
            [IO.Path]::GetFileNameWithoutExtension($job.project) + '/Assets'))
        if ($LASTEXITCODE -ne 0) { throw 'Cannot verify published input paths.' }
        $externalTracked = [Collections.Generic.HashSet[string]]::new(
            [string[]](($trackedOutput -join "`n").Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)),
            [StringComparer]::Ordinal)
        foreach ($file in Get-ChildItem -LiteralPath $externalRoot -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($externalRoot, $file.FullName).Replace('\', '/')
            if ($relative.StartsWith('Repo/', [StringComparison]::OrdinalIgnoreCase) -or
                $file.Name -notmatch '^(crash|timeout|slow)' -or
                -not $externalTracked.Contains(
                    [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($PublicCorpusCheckout), $file.FullName).Replace('\', '/'))) {
                throw 'Published overlays may not replace curated inputs or add unrecognized files.'
            }
            $publishedInputs['Assets/' + $relative] = $file.FullName
        }
        if ($publishedInputs.Count -eq 0) { throw 'Published replay corpus is empty.' }
        $manifest.publicCorpusCommit = $PublicCorpusCommit
        $manifest.publicCorpusInputs = $publishedInputs.Count
    }
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
    if ($job.stackRoot) {
        $stackRoot = Join-Path $RepoRoot $job.stackRoot
        $files = @(Get-ChildItem -LiteralPath $stackRoot -File -Recurse)
        if ($files.Count -eq 0) { throw 'Required stack-regression inventory is empty.' }
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($stackRoot, $file.FullName).Replace('\', '/')
            $sources['StackTestcases/' + $relative] = $file.FullName
            $manifest.regressionInputs++
        }
    }
    foreach ($entry in $publishedInputs.GetEnumerator()) {
        if (-not $sources.Contains($entry.Key) -or
            (Get-FileHash -LiteralPath $sources[$entry.Key] -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash) {
            throw 'Published corpus input is missing or changed in the overlay.'
        }
    }
    if ($RequireCommitted) {
        $inputRoots = @($job.corpusBuckets | ForEach-Object { "$($job.corpusRoot)/$_" }) + @($job.regressionRoot)
        if ($job.stackRoot) { $inputRoots += $job.stackRoot }
        $trackedOutput = @(& git -C $RepoRoot ls-files -z -- @inputRoots 2>&1)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot verify committed public input paths.' }
        $tracked = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($path in (($trackedOutput -join "`n").Split([char] 0, [StringSplitOptions]::RemoveEmptyEntries))) {
            $null = $tracked.Add($path)
        }
        foreach ($entry in $sources.GetEnumerator()) {
            if (-not $tracked.Contains([IO.Path]::GetRelativePath($RepoRoot, $entry.Value).Replace('\', '/')) -and
                -not $publishedInputs.ContainsKey($entry.Key)) {
                throw 'An uncommitted overlay is not a public replay input.'
            }
        }
        & git -C $RepoRoot diff --quiet HEAD -- @inputRoots 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Public replay inputs differ from the checked-out commit.' }
    }
    foreach ($entry in $sources.GetEnumerator()) {
        if (-not $destinations.Add($entry.Key)) { throw 'Case-insensitive output collision.' }
        $hash = (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash.ToLowerInvariant()
        $id = 'sha256:' + [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($entry.Key))).ToLowerInvariant()
        $category = 'good'
        if ($entry.Key.StartsWith('StackTestcases/')) { $category = 'stack' }
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
        foreach ($folder in @('Testcases', 'Assets', 'StackTestcases')) {
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
