# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ToolRoot,
    [Parameter(Mandatory)][string]$CacheRoot,
    [Parameter(Mandatory)][string]$QuicSourceRoot,
    [Parameter(Mandatory)][string]$AssetRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [switch]$Linux,
    [switch]$StageOnly
)
$ErrorActionPreference = 'Stop'
$rid = if ($Linux) { 'linux-x64' } else { 'win-x64' }
$assets = Join-Path $AssetRoot $rid
$out = Join-Path $OutputRoot $rid
New-Item -ItemType Directory -Force $out,(Join-Path $out 'work') | Out-Null
$production = Join-Path $PSScriptRoot '..\..\src\Opc.Ua.Native.QuicGo'
$go = Join-Path $ToolRoot 'go\bin\go.exe'
$env:GOROOT = Join-Path $ToolRoot 'go'
$env:GOMODCACHE = Join-Path $CacheRoot 'modules'
$env:GOCACHE = Join-Path $CacheRoot 'compiled'
$env:GOTMPDIR = Join-Path $out 'work'
$env:GOENV = 'off'
$env:GOTOOLCHAIN = 'local'
$env:GOTELEMETRY = 'off'
$env:GOOS = if ($Linux) { 'linux' } else { 'windows' }
$env:GOARCH = 'amd64'
$env:CGO_ENABLED = '0'
$env:PYTHONDONTWRITEBYTECODE = '1'
$fixture = Join-Path $out $(if ($Linux) { 'identity-fixture' } else { 'identity-fixture.exe' })
& $go build -trimpath -buildvcs=false -o $fixture (Join-Path $PSScriptRoot 'native\identity-fixture\main.go')
if ($LASTEXITCODE -ne 0) { throw 'Test-only identity fixture compilation failed.' }
Push-Location (Join-Path $PSScriptRoot 'native\wire')
try {
    & $go mod download all
    if ($LASTEXITCODE -ne 0) { throw 'Wire test dependency verification failed.' }
    $modfile = Join-Path $out 'wire.mod'
    Copy-Item '.\go.mod' $modfile -Force
    Copy-Item '.\go.sum' (Join-Path $out 'wire.sum') -Force
    $sourceAlias = Join-Path $assets 'source\quic-go'
    & $go mod edit "-modfile=$modfile" "-replace=github.com/quic-go/quic-go=$sourceAlias"
    if ($LASTEXITCODE -ne 0) { throw 'Wire test source selection failed.' }
    $wire = Join-Path $out $(if ($Linux) { 'wire-tests' } else { 'wire-tests.exe' })
    & $go test "-modfile=$modfile" -mod=readonly -overlay (Join-Path $assets 'overlay\overlay.json') `
        -tags quic_trace09 -c -o $wire .
    if ($LASTEXITCODE -ne 0) { throw 'Exact wire test compilation failed.' }
    $peer = Join-Path $out $(if ($Linux) { 'capability-server' } else { 'capability-server.exe' })
    & $go build "-modfile=$modfile" -mod=readonly -overlay (Join-Path $assets 'overlay\overlay.json') `
        -trimpath -o $peer (Join-Path $PSScriptRoot 'native\capability-server\main.go')
    if ($LASTEXITCODE -ne 0) { throw 'Independent capability-negative peer compilation failed.' }
}
finally {
    Pop-Location
}
if ($StageOnly) {
    Write-Host "Prepared $rid native test executables in '$out'; no tests were executed."
    return
}
if (!$Linux) {
    & $wire '-test.v' '-test.timeout=30s' | Tee-Object -FilePath (Join-Path $out 'wire-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native exact09 wire tests failed.' }
    python (Join-Path $PSScriptRoot 'native\generic_network_test.py') `
        --library (Join-Path $assets 'opcua_quic_go.dll') --fixture $fixture `
        --peer-server $peer `
        --output (Join-Path $out 'network-tests.json') | Tee-Object -FilePath (Join-Path $out 'network-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Generic production QUIC tests failed.' }
    return
}
$pins = Get-Content (Join-Path $production 'build\native-pins.json') -Raw | ConvertFrom-Json
$image = $pins.linux_test_image
podman image exists $image
if ($LASTEXITCODE -ne 0) { throw 'Pinned existing Linux image unavailable; no image will be pulled.' }
$stage = Join-Path $out 'container-input'
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $fixture,$wire,$peer,(Join-Path $assets 'libopcua_quic_go.so'),`
    (Join-Path $PSScriptRoot 'native\generic_network_test.py'),`
    (Join-Path $PSScriptRoot 'native\peer_stop_regression.py') $stage -Force
$command = 'mkdir -p /validation/results /validation/work && chmod +x /validation/identity-fixture /validation/wire-tests /validation/capability-server && timeout -k 5s 40s /validation/wire-tests -test.v -test.timeout=30s > /validation/results/wire-tests.log && timeout -k 5s 120s python3 /validation/generic_network_test.py --library /validation/libopcua_quic_go.so --fixture /validation/identity-fixture --peer-server /validation/capability-server --output /validation/results/network-tests.json'
$container = podman create --pull=never --network=none --cap-drop=all --security-opt=no-new-privileges `
    --ulimit core=0 --memory=512m --cpus=1 --env PYTHONDONTWRITEBYTECODE=1 --env TMPDIR=/validation/work `
    --entrypoint /bin/sh $image -c $command
if ($LASTEXITCODE -ne 0) { throw 'Could not create the bounded test container.' }
$container = $container.Trim()
try {
    podman cp $stage "${container}:/validation"
    if ($LASTEXITCODE -ne 0) { throw 'Copying generic native test inputs failed.' }
    podman start --attach $container | Tee-Object -FilePath (Join-Path $out 'network-tests.log')
    $code = $LASTEXITCODE
    podman cp "${container}:/validation/results/." $out
    if ($LASTEXITCODE -ne 0 -or $code -ne 0) { throw "Linux generic native tests failed: $code" }
}
finally {
    podman rm --force $container | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Owned native test container $container could not be removed." }
}
