# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$env:PYTHONDONTWRITEBYTECODE = '1'
$root = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $root '..\..')).Path
$artifacts = Join-Path $root 'artifacts'
$context = Join-Path $artifacts 'linux-context'
$target = Join-Path $context 'src\Opc.Ua.Native.Http3'
$tests = Join-Path $context 'tests\Opc.Ua.Native.Http3.Tests\native'
$pins = Get-Content (Join-Path $root 'build\public-inputs.json') -Raw | ConvertFrom-Json
$image = & podman image inspect $pins.linux.image --format '{{.Id}}'
if ($LASTEXITCODE -ne 0 -or $image -ne $pins.linux.image_id) {
    throw 'The accepted public Rust Linux image must already be cached; no implicit pull is allowed.'
}
Push-Location $root
try {
    & python (Join-Path (Join-Path $root 'build') 'verify.py') source
    if ($LASTEXITCODE -ne 0) { throw 'Source verification failed.' }
}
finally {
    Pop-Location
}
foreach ($path in $target,$tests,(Join-Path $artifacts 'logs'),(Join-Path $artifacts 'receipts'),
    (Join-Path $artifacts 'runtimes\linux-x64\native')) {
    New-Item -ItemType Directory -Force $path | Out-Null
}
# Explicit source-only snapshot: no caches, managed identity fixtures, DLLs or private reports.
foreach ($directory in 'src','upstream','patches','build','notices','include') {
    $destination = Join-Path $target $directory
    if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
    Copy-Item (Join-Path $root $directory) $destination -Recurse
    $bytecode = Join-Path $destination '__pycache__'
    if (Test-Path $bytecode) { Remove-Item $bytecode -Recurse -Force }
}
foreach ($file in 'Cargo.toml','Cargo.lock','rustfmt.toml','LICENSE.txt') {
    Copy-Item (Join-Path $root $file) (Join-Path $target $file) -Force
}
Get-ChildItem (Join-Path $repo 'tests\Opc.Ua.Native.Http3.Tests\native') -File |
    Where-Object { $_.Extension -in '.rs','.c' } |
    Copy-Item -Destination $tests -Force
$imageFile = Join-Path $artifacts 'receipts\linux-image-id.txt'
$arguments = @('build','--pull=never','--network=none','--layers','--jobs=1','--iidfile',$imageFile,
    '--file',(Join-Path $root 'build\Containerfile.linux'),$context)
& podman @arguments 2>&1 | Tee-Object (Join-Path $artifacts 'logs\linux-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Linux native tests/build failed.' }
$built = (Get-Content $imageFile -Raw).Trim()
$name = 'opcua-http3-export-' + [Guid]::NewGuid().ToString('N')
$created = $false
try {
    & podman create --pull=never --network=none --name $name $built /not-executed | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the owned asset extraction container.' }
    $created = $true
    & podman cp "${name}:/native/libopcua_http3.so" (Join-Path $artifacts 'runtimes\linux-x64\native')
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the production native library.' }
}
finally {
    if ($created) { & podman rm $name | Out-Null }
}
[ordered]@{
    schema_version=1
    public_image=$pins.linux.image
    public_image_id=$image
    result_image_id=$built
    tests_and_build_exit_code=0
    command=@('podman') + $arguments
    network='none'
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $artifacts 'receipts\linux-build.json')
Push-Location $root
try {
    & python (Join-Path (Join-Path $root 'build') 'verify.py') binary --rid linux-x64
    if ($LASTEXITCODE -ne 0) { throw 'Linux asset verification failed.' }
}
finally {
    Pop-Location
}
