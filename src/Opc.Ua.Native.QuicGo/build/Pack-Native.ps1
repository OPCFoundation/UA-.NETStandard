# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ToolRoot,
    [Parameter(Mandatory)][string]$CacheRoot,
    [Parameter(Mandatory)][string]$QuicSourceRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [switch]$UnsignedDevelopment
)
$ErrorActionPreference = 'Stop'
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (!$UnsignedDevelopment) {
    throw 'Distribution signing/final SBOM/SCA/minimum-host approval is a separate gate. Use -UnsignedDevelopment for local validation only.'
}
$assets = Join-Path $OutputRoot 'assets'
$packages = Join-Path $OutputRoot 'packages'
foreach ($rid in @('win-x64', 'linux-x64')) {
    & (Join-Path $PSScriptRoot 'Build-Native.ps1') -ToolRoot $ToolRoot -CacheRoot $CacheRoot `
        -QuicSourceRoot $QuicSourceRoot -OutputRoot $assets -RuntimeIdentifier $rid
}
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'Opc.Ua.Native.QuicGo.csproj'
$python = python -c 'import sys; print(sys.executable)'
if ($LASTEXITCODE -ne 0) { throw 'An installed Python3 interpreter is required for package verification.' }
dotnet pack $project -c Release "-p:NativeQuicAssetRoot=$assets" "-p:NativeQuicPython=$python" -o $packages --nologo
if ($LASTEXITCODE -ne 0) { throw 'Managed native-package build failed.' }
foreach ($package in Get-ChildItem $packages -Filter 'OPCFoundation.NetStandard.Opc.Ua.Native.QuicGo.*.nupkg') {
    python (Join-Path $PSScriptRoot 'verify_package.py') $package.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Package payload allowlist failed.' }
}
