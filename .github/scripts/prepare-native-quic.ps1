# Copyright (c) OPC Foundation. Licensed under the MIT License.
<#
.SYNOPSIS
    Prepares the pinned, private-to-this-build Windows tools and QUIC source.
.DESCRIPTION
    Reuses verified tools when present. New archives are checked before extraction,
    and a mismatched existing installation is refused rather than overwritten.
    The source copy stays outside GOMODCACHE so Go can apply the reviewed overlay.
    No system installation or PATH configuration is changed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ToolRoot,
    [Parameter(Mandatory)][string] $CacheRoot,
    [Parameter(Mandatory)][string] $QuicSourceRoot,
    [Parameter(Mandatory)][string] $VerificationRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) {
    throw 'The accepted cross-compilation toolchain runs on Windows and produces both x64 RIDs.'
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $repository 'src\Opc.Ua.Native.QuicGo'
$pins = Get-Content -LiteralPath (Join-Path $project 'build\native-pins.json') -Raw | ConvertFrom-Json
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
$CacheRoot = [IO.Path]::GetFullPath($CacheRoot)
$QuicSourceRoot = [IO.Path]::GetFullPath($QuicSourceRoot)
$VerificationRoot = [IO.Path]::GetFullPath($VerificationRoot)

function Assert-FileHash([string] $Path, [string] $Expected)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required pinned file is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $Expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Pinned SHA256 mismatch for '$Path'; existing files were not overwritten."
    }
}

function Install-PinnedTool(
    [string] $Name,
    [string] $ArchiveDirectory,
    [string] $Executable,
    [psobject] $Pin)
{
    $destination = Join-Path $ToolRoot $Name
    $executablePath = Join-Path $destination $Executable
    if (Test-Path -LiteralPath $destination) {
        Assert-FileHash $executablePath $Pin.executable_sha256
        return $executablePath
    }

    $archives = Join-Path $ToolRoot 'archives'
    New-Item -ItemType Directory -Path $archives -Force | Out-Null
    $archive = Join-Path $archives ([IO.Path]::GetFileName(([Uri] $Pin.archive_url).AbsolutePath))
    if (-not (Test-Path -LiteralPath $archive)) {
        $partial = "$archive.$([Guid]::NewGuid().ToString('N')).partial"
        try {
            Invoke-WebRequest -Uri $Pin.archive_url -OutFile $partial -TimeoutSec 180
            Assert-FileHash $partial $Pin.archive_sha256
            Move-Item -LiteralPath $partial -Destination $archive
        }
        finally {
            if (Test-Path -LiteralPath $partial -PathType Leaf) {
                Remove-Item -LiteralPath $partial -Force
            }
        }
    }
    Assert-FileHash $archive $Pin.archive_sha256

    $stage = Join-Path $ToolRoot ".$Name-stage-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $stage | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $stage
    $extracted = Join-Path $stage $ArchiveDirectory
    Assert-FileHash (Join-Path $extracted $Executable) $Pin.executable_sha256
    Move-Item -LiteralPath $extracted -Destination $destination
    # A successful extraction moves its only top-level directory. Never recursively
    # delete a caller's tool root, including when extraction or validation fails.
    Remove-Item -LiteralPath $stage
    return $executablePath
}

New-Item -ItemType Directory -Path $ToolRoot,$CacheRoot,$VerificationRoot -Force | Out-Null
$go = Install-PinnedTool 'go' 'go' 'bin\go.exe' $pins.go
$zig = Install-PinnedTool 'zig' "zig-x86_64-windows-$($pins.zig.version)" 'zig.exe' $pins.zig

$savedEnvironment = @{}
$buildEnvironment = @{
    GOROOT = Join-Path $ToolRoot 'go'
    GOPATH = Join-Path $CacheRoot 'gopath'
    GOMODCACHE = Join-Path $CacheRoot 'modules'
    GOCACHE = Join-Path $CacheRoot 'compiled'
    GOENV = 'off'
    GOTOOLCHAIN = 'local'
    GOTELEMETRY = 'off'
    GOFLAGS = ''
}
foreach ($entry in $buildEnvironment.GetEnumerator()) {
    $savedEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
}
Push-Location (Join-Path $project 'native')
try {
    $moduleJson = & $go mod download -json "$($pins.module)@$($pins.version)"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not acquire the checksum-pinned QUIC module.'
    }
    $module = $moduleJson | ConvertFrom-Json
    if ($module.Sum -ne $pins.module_sum -or $module.GoModSum -ne $pins.module_mod_sum) {
        throw 'The acquired QUIC module does not match the accepted module checksums.'
    }
    if (-not (Test-Path -LiteralPath $QuicSourceRoot)) {
        $parent = Split-Path -Parent $QuicSourceRoot
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Copy-Item -LiteralPath $module.Dir -Destination $QuicSourceRoot -Recurse
    }
    & python (Join-Path $project 'build\prepare_overlay.py') `
        --source $QuicSourceRoot --verified-module $module.Dir --output $VerificationRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'The source inventory or accepted QUIC overlays do not match.'
    }
}
finally {
    Pop-Location
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}

[pscustomobject]@{
    ToolRoot = $ToolRoot
    CacheRoot = $CacheRoot
    QuicSourceRoot = $QuicSourceRoot
    VerificationRoot = $VerificationRoot
    Go = $go
    Zig = $zig
}
