# Copyright (c) OPC Foundation. Licensed under the MIT License.
<#
.SYNOPSIS
Discovers the Actions net10 test matrix and evaluates PR path relevance.
.DESCRIPTION
Ordinary projects are discovered under tests. The three public replay projects
are explicitly required under fuzzing. No legacy/alternate TFM is claimed by
this matrix; Azure uses evaluated MSBuild applicability in get-matrix.ps1.
#>
param(
    [string] $RepoRoot = (Split-Path $PSScriptRoot),
    [string] $ChangedFilesPath = ''
)
$ErrorActionPreference = 'Stop'
$projects = @(
    Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'tests') -Directory -Filter 'Opc.Ua.*.Tests' |
    Where-Object { $_.Name -notin @('Opc.Ua.Aot.Tests', 'Opc.Ua.Stress.Tests') } |
    Sort-Object Name |
    ForEach-Object {
        $path = "tests/$($_.Name)/$($_.Name).csproj"
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $path) -PathType Leaf)) {
            throw 'An expected ordinary test project is missing.'
        }
        @{ name = $_.Name -replace '^Opc\.Ua\.', '' -replace '\.Tests$', ''; path = $path }
    }
)
foreach ($area in @('Encoders', 'Certificates', 'Network')) {
    $path = "fuzzing/Opc.Ua.$area.Fuzz.Tests/Opc.Ua.$area.Fuzz.Tests.csproj"
    if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $path) -PathType Leaf)) {
        throw "Required public replay project is missing: $area."
    }
    $projects += @{ name = "$area.Fuzz"; path = $path }
}
$relevant = $true
if ($ChangedFilesPath) {
    $relevant = $false
    foreach ($file in Get-Content -LiteralPath $ChangedFilesPath) {
        $file = $file.Replace('\', '/')
        $name = [IO.Path]::GetFileName($file)
        if ($name -match '\.(cs|csproj|props|targets|slnx|sln|runsettings)$' -or
            $name -in @('global.json', 'nuget.config', 'coverage-thresholds.json') -or
            $file -like 'fuzzing/*' -or $file -like '.azurepipelines/*' -or
            $file -like '.github/workflows/*' -or $file -like 'tests/*.ps1' -or
            $file -like 'tests/*.runsettings*') {
            $relevant = $true
            break
        }
    }
}
@{ projects = $projects; relevantChanges = $relevant } | ConvertTo-Json -Depth 5 -Compress
