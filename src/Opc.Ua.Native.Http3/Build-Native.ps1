# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ToolchainRoot,
    [string]$VisualStudioRoot = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise',
    [string]$WindowsSdkRoot = 'C:\Program Files (x86)\Windows Kits\10',
    [ValidateSet('All', 'Test', 'Build', 'Check')][string]$Action = 'All',
    [switch]$Format
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$pins = Get-Content (Join-Path $root 'build\public-inputs.json') -Raw | ConvertFrom-Json
$toolchain = (Resolve-Path $ToolchainRoot).Path
foreach ($file in $pins.windows.files) {
    $path = Join-Path $toolchain $file.path
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Public Rust input hash mismatch: $($file.path)"
    }
}
$vc = Join-Path $VisualStudioRoot "VC\Tools\MSVC\$($pins.windows.msvc_directory)"
$sdkVersion = $pins.windows.sdk_version
foreach ($file in $pins.windows.native_files) {
    $base = if ($file.base -eq 'msvc') { $vc } else { $WindowsSdkRoot }
    if ((Get-FileHash (Join-Path $base $file.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Native tool/runtime input hash mismatch: $($file.path)"
    }
}
$bin = Join-Path $toolchain 'bin'
$linker = Join-Path $vc 'bin\Hostx64\x64\link.exe'
$artifacts = Join-Path $root 'artifacts'
foreach ($relative in 'work','cargo-home','target','logs','link','runtimes\win-x64\native','receipts') {
    New-Item -ItemType Directory -Force (Join-Path $artifacts $relative) | Out-Null
}
$env:TEMP = Join-Path $artifacts 'work'
$env:TMP = $env:TEMP
$env:TMPDIR = $env:TEMP
$env:PYTHONDONTWRITEBYTECODE = '1'
$env:CARGO_HOME = Join-Path $artifacts 'cargo-home'
$env:CARGO_TARGET_DIR = Join-Path $artifacts 'target'
$env:RUSTC = Join-Path $bin 'rustc.exe'
$env:RUSTDOC = Join-Path $bin 'rustdoc.exe'
$env:RUSTFMT = Join-Path $bin 'rustfmt.exe'
$env:PATH = "$bin;$vc\bin\Hostx64\x64;$env:PATH"
$env:LIB = "$vc\lib\x64;$WindowsSdkRoot\Lib\$sdkVersion\ucrt\x64;$WindowsSdkRoot\Lib\$sdkVersion\um\x64"
Remove-Item Env:RUSTC_WRAPPER,Env:RUSTC_WORKSPACE_WRAPPER,Env:RUSTUP_TOOLCHAIN,Env:RUSTFLAGS -ErrorAction SilentlyContinue
$repo = (Resolve-Path (Join-Path $root '..\..')).Path
$env:CARGO_ENCODED_RUSTFLAGS = @('-C','target-feature=+crt-static',"--remap-path-prefix=$repo=.",
    "--remap-path-prefix=$toolchain=public-rust-1.97.0") -join [char]31
$env:OPCUA_HTTP3_LINKER = $linker
$env:OPCUA_HTTP3_LINK_LOG = Join-Path $artifacts 'link'
$commands = [Collections.Generic.List[object]]::new()
function Invoke-Checked([string]$file, [string[]]$arguments, [string]$name) {
    & $file @arguments 2>&1 | Tee-Object (Join-Path $artifacts "logs\$name.log")
    $code = $LASTEXITCODE
    $commands.Add([ordered]@{name=$name;arguments=$arguments;exit_code=$code})
    $commands | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $artifacts 'receipts\windows-commands.json')
    if ($code -ne 0) { throw "$name failed: $code" }
}
Push-Location $root
try {
    Invoke-Checked python @('build\verify.py','source') 'source'
    if ($Format) {
        Invoke-Checked $env:RUSTFMT @('--edition','2024','src\lib.rs','src\ffi.rs','src\config.rs','src\error.rs',
            'src\send.rs','build\link-capture.rs','..\..\tests\Opc.Ua.Native.Http3.Tests\native\regressions.rs',
            '..\..\tests\Opc.Ua.Native.Http3.Tests\native\policy.rs','..\..\tests\Opc.Ua.Native.Http3.Tests\native\abi.rs') 'format'
    }
    $capture = Join-Path $artifacts 'link-capture.exe'
    Invoke-Checked $env:RUSTC @('--edition','2024','-C','target-feature=+crt-static','-C',"linker=$linker",
        'build\link-capture.rs','-o',$capture) 'link-launcher'
    $env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_LINKER = $capture
    $cargo = Join-Path $bin 'cargo.exe'
    if ($Action -in 'Test','All') {
        Invoke-Checked $cargo @('test','--offline','--locked','--workspace','--target','x86_64-pc-windows-msvc','--quiet') 'windows-tests'
    }
    if ($Action -in 'Check','All') {
        Invoke-Checked $env:RUSTFMT @('--check','--edition','2024','src\lib.rs','build\link-capture.rs',
            '..\..\tests\Opc.Ua.Native.Http3.Tests\native\regressions.rs',
            '..\..\tests\Opc.Ua.Native.Http3.Tests\native\policy.rs','..\..\tests\Opc.Ua.Native.Http3.Tests\native\abi.rs') 'windows-fmt'
        Invoke-Checked $cargo @('clippy','--offline','--locked','--workspace','--all-targets',
            '--target','x86_64-pc-windows-msvc','--','-D','warnings') 'windows-clippy'
    }
    if ($Action -in 'Build','All') {
        Invoke-Checked $cargo @('build','--offline','--locked','--release','--lib',
            '-p','opcua-native-http3','--target','x86_64-pc-windows-msvc','--quiet') 'windows-release'
        Copy-Item (Join-Path $env:CARGO_TARGET_DIR 'x86_64-pc-windows-msvc\release\opcua_http3.dll') `
            (Join-Path $artifacts 'runtimes\win-x64\native\opcua_http3.dll') -Force
        Invoke-Checked python @('build\verify.py','binary','--rid','win-x64') 'windows-binary'
    }
}
finally {
    Pop-Location
}
