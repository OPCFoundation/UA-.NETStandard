# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
# OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
<#
.SYNOPSIS
Replays every declared callback in an existing relocated OneFuzz drop.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DropDirectory,
    [string]$ValidatorPath,
    [string]$ResultsDirectory,
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$DropDirectory = (Get-Item -LiteralPath $DropDirectory).FullName
if (-not $ValidatorPath) {
    $work = Join-Path ([IO.Path]::GetTempPath()) ('opcua-onefuzz-validator-' + [guid]::NewGuid().ToString('N'))
    $project = Join-Path $root 'fuzzing\OneFuzz\Opc.Ua.OneFuzz.Validator\Opc.Ua.OneFuzz.Validator.csproj'
    $published = Join-Path $work 'published'
    & dotnet publish $project -c Release -p:CustomTestTarget=net10.0 -p:UseSharedCompilation=false `
        -maxcpucount:1 --artifacts-path (Join-Path $work 'sdk') --output $published `
        "-p:RestoreConfigFile=$(Join-Path $root 'NuGet.config')" --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Validator publication failed with exit $LASTEXITCODE."
    }
    $ValidatorPath = Join-Path $published 'Opc.Ua.OneFuzz.Validator.dll'
}
$ValidatorPath = (Get-Item -LiteralPath $ValidatorPath).FullName
$arguments = @(
    $ValidatorPath, 'validate', '--drop', $DropDirectory,
    '--timeout-seconds', $TimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
)
if ($ResultsDirectory) {
    $arguments += '--results', [IO.Path]::GetFullPath($ResultsDirectory)
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Published OneFuzz validation failed with exit $LASTEXITCODE."
}
