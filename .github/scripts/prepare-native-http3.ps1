# Copyright (c) OPC Foundation. Licensed under the MIT License.
<#
.SYNOPSIS
    Resolves and verifies the public Rust toolchain used by the native HTTP3 build.
.DESCRIPTION
    An existing isolated toolchain can be supplied without downloading anything.
    Otherwise rustup installs the exact public release into build-private homes.
    Executed compiler and runtime files are checked against the native package's
    accepted public-input manifest. No default or system toolchain is changed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ToolRoot,
    [string] $ExistingToolchainRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) {
    throw 'This prepares Windows tools. Linux uses the separately pinned public Rust container.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$pins = Get-Content -LiteralPath (
    Join-Path $repository 'src\Opc.Ua.Native.Http3\build\public-inputs.json') -Raw | ConvertFrom-Json
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)

if (-not [string]::IsNullOrWhiteSpace($ExistingToolchainRoot)) {
    $toolchain = (Resolve-Path -LiteralPath $ExistingToolchainRoot).Path
}
else {
    $rustup = (Get-Command rustup -CommandType Application -ErrorAction Stop).Source
    New-Item -ItemType Directory -Path $ToolRoot -Force | Out-Null
    $manifest = Join-Path $ToolRoot 'public-release-manifest.toml'
    Invoke-WebRequest -Uri $pins.release_manifest_url -OutFile $manifest -TimeoutSec 120
    if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $pins.release_manifest_sha256) {
        throw 'The public Rust release manifest does not match the accepted hash.'
    }

    $environment = @{
        RUSTUP_HOME = Join-Path $ToolRoot 'rustup'
        CARGO_HOME = Join-Path $ToolRoot 'cargo'
        RUSTUP_DIST_SERVER = 'https://static.rust-lang.org'
        RUSTUP_UPDATE_ROOT = 'https://static.rust-lang.org/rustup'
        RUSTUP_TOOLCHAIN = ''
    }
    $previous = @{}
    foreach ($entry in $environment.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    try {
        $selected = "$($pins.rust_version)-$($pins.windows.target)"
        & $rustup toolchain install $selected --no-self-update --profile minimal `
            --component rustfmt --component clippy --component rust-src | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not install the exact public Rust release into the private build home.'
        }
        $toolchain = & $rustup run $selected rustc --print sysroot
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($toolchain)) {
            throw 'The explicitly selected Rust toolchain did not report its root.'
        }
        $toolchain = [IO.Path]::GetFullPath($toolchain.Trim())
    }
    finally {
        foreach ($entry in $previous.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
        }
    }
}

foreach ($file in $pins.windows.files) {
    $path = Join-Path $toolchain $file.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The selected public Rust toolchain is missing '$($file.path)'."
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "The selected public Rust toolchain differs at '$($file.path)'."
    }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'Visual Studio discovery is unavailable; the pinned MSVC/SDK prerequisites must be installed.'
}
$installations = @(& $vswhere -all -products '*' -property installationPath)
if ($LASTEXITCODE -ne 0) {
    throw 'Visual Studio discovery failed.'
}
$visualStudio = $installations | Where-Object {
    Test-Path -LiteralPath (Join-Path $_ "VC\Tools\MSVC\$($pins.windows.msvc_directory)") -PathType Container
} | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw "The accepted MSVC $($pins.windows.msvc_directory) toolset is not installed."
}
$windowsSdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$msvc = Join-Path $visualStudio "VC\Tools\MSVC\$($pins.windows.msvc_directory)"
$nativeInputMismatches = [Collections.Generic.List[string]]::new()
foreach ($file in $pins.windows.native_files) {
    $base = switch ($file.base) {
        'msvc' { $msvc }
        'sdk' { $windowsSdk }
        default { throw "Unknown native-tool input base '$($file.base)'." }
    }
    $path = Join-Path $base $file.path
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualHash -ne $file.sha256) {
        $nativeInputMismatches.Add(
            "'$($file.path)' expected '$($file.sha256)', found '$actualHash'.")
    }
}
if ($nativeInputMismatches.Count -gt 0) {
    throw "The accepted MSVC/SDK inputs differ: $($nativeInputMismatches -join ' ')"
}

[pscustomobject]@{
    ToolchainRoot = $toolchain
    VisualStudioRoot = $visualStudio
    WindowsSdkRoot = $windowsSdk
    RustVersion = $pins.rust_version
}
