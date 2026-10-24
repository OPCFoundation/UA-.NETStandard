# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ToolRoot,
    [Parameter(Mandatory)][string]$CacheRoot,
    [Parameter(Mandatory)][string]$QuicSourceRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateSet('win-x64', 'linux-x64')][string]$RuntimeIdentifier = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
$CacheRoot = [IO.Path]::GetFullPath($CacheRoot)
$QuicSourceRoot = [IO.Path]::GetFullPath($QuicSourceRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$project = Split-Path -Parent $PSScriptRoot
$pins = Get-Content (Join-Path $PSScriptRoot 'native-pins.json') -Raw | ConvertFrom-Json
$go = Join-Path $ToolRoot 'go\bin\go.exe'
$zig = Join-Path $ToolRoot 'zig\zig.exe'
if ((Get-FileHash $go -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pins.go.executable_sha256) {
    throw 'Go executable is not the accepted public Go1.27.2 compiler.'
}
if ((Get-FileHash $zig -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pins.zig.executable_sha256) {
    throw 'Zig executable is not the accepted public Zig0.17.0 compiler.'
}
$rid = $pins.rids.$RuntimeIdentifier
$out = [IO.Path]::GetFullPath((Join-Path $OutputRoot $RuntimeIdentifier))
$env:GOROOT = Join-Path $ToolRoot 'go'
$env:GOPATH = Join-Path $CacheRoot 'gopath'
$env:GOMODCACHE = Join-Path $CacheRoot 'modules'
$env:GOCACHE = Join-Path $CacheRoot 'compiled'
$env:GOTMPDIR = Join-Path $out 'work'
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $CacheRoot 'zig-global'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $CacheRoot 'zig-local'
$env:GOENV = 'off'
$env:GOTOOLCHAIN = 'local'
$env:GOTELEMETRY = 'off'
$env:GOFLAGS = ''
$env:CGO_ENABLED = '1'
$env:GOOS = $rid.goos
$env:GOARCH = 'amd64'
$env:CC = "`"$zig`" cc -target $($rid.target)"
$env:TEMP = $env:GOTMPDIR
$env:TMP = $env:GOTMPDIR
$env:PYTHONDONTWRITEBYTECODE = '1'
New-Item -ItemType Directory -Force -Path $out,$env:GOTMPDIR | Out-Null
Push-Location (Join-Path $project 'native')
try {
    & $go mod download all
    if ($LASTEXITCODE -ne 0) { throw 'Pinned Go dependencies are unavailable.' }
    & $go mod verify
    if ($LASTEXITCODE -ne 0) { throw 'Go module checksum verification failed.' }
    $nativeRoot = (Get-Location).Path
    $nativeFiles = @(Get-ChildItem $nativeRoot -Recurse -File | ForEach-Object {
        [IO.Path]::GetRelativePath($nativeRoot, $_.FullName).Replace('\', '/')
    })
    if (Compare-Object ($nativeFiles | Sort-Object) ($pins.production_files | Sort-Object)) {
        throw 'Production source file allowlist differs. No fixture/helper export source may be compiled.'
    }
    $module = & $go list -m -json github.com/quic-go/quic-go | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $module.Version -ne $pins.version -or `
        $module.Sum -ne $pins.module_sum -or $module.GoModSum -ne $pins.module_mod_sum) {
        throw 'Unexpected QUIC module or checksum.'
    }
    foreach ($dependency in $pins.dependencies.PSObject.Properties) {
        $version = & $go list -m -f '{{.Version}}' $dependency.Name
        if ($LASTEXITCODE -ne 0 -or $version -ne $dependency.Value) {
            throw "Dependency pin changed: $($dependency.Name)"
        }
    }
    # A small output-local build tree gives module build-info a stable relative
    # replacement. The junction reuses the reviewed source; no full mirror is made.
    $buildSource = Join-Path $out 'source'
    $sourceAlias = Join-Path $buildSource 'quic-go'
    New-Item -ItemType Directory -Force $buildSource,(Join-Path $buildSource 'include') | Out-Null
    if (!(Test-Path $sourceAlias)) {
        New-Item -ItemType Junction -Path $sourceAlias -Target $QuicSourceRoot | Out-Null
    }
    Copy-Item '.\*.go','.\go.mod','.\go.sum' $buildSource -Force
    Copy-Item '.\include\opcua_quic.h','.\include\exports.map' (Join-Path $buildSource 'include') -Force
    $overlay = Join-Path $out 'overlay'
    python (Join-Path $PSScriptRoot 'prepare_overlay.py') --source $sourceAlias `
        --verified-module $module.Dir --output $overlay
    if ($LASTEXITCODE -ne 0) { throw 'Observation-only overlay verification failed.' }
    Set-Location $buildSource
    $exportMap = (Join-Path $buildSource 'include\exports.map').Replace('\', '/')
    $env:CGO_LDFLAGS_ALLOW = '^' + [regex]::Escape("-Wl,--version-script=$exportMap") + '$'
    & $go mod edit '-replace=github.com/quic-go/quic-go=./quic-go'
    if ($LASTEXITCODE -ne 0) { throw 'Verified source binding failed.' }
    $ldflags = '-buildid='
    $asset = Join-Path $out $rid.asset
    & $go build -mod=readonly -overlay (Join-Path $overlay 'overlay.json') -buildmode=c-shared `
        -trimpath -buildvcs=false -ldflags $ldflags -o $asset .
    if ($LASTEXITCODE -ne 0) { throw 'Production C ABI build failed.' }
    & $go vet -mod=readonly -overlay (Join-Path $overlay 'overlay.json') .
    if ($LASTEXITCODE -ne 0) { throw 'Production Go vet failed.' }
    & $go version -m $asset | Set-Content (Join-Path $out 'build-info.txt')
    & $go list -mod=readonly -overlay (Join-Path $overlay 'overlay.json') -deps `
        -f '{{.ImportPath}}' . | Set-Content (Join-Path $out 'linked-packages.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Native dependency closure enumeration failed.' }
    if (Select-String (Join-Path $out 'linked-packages.txt') -Pattern 'openpgp|native-quic-proof|identity-fixture') {
        throw 'Forbidden production dependency in the actual linked closure.'
    }
    python (Join-Path $PSScriptRoot 'verify_native.py') --asset $asset --rid $RuntimeIdentifier `
        --receipt (Join-Path $out 'native-receipt.json')
    if ($LASTEXITCODE -ne 0) { throw 'Production export policy failed.' }
}
finally {
    Pop-Location
}
