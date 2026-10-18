# Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NativeLibrary,
    [string]$TestBinaryDirectory = (Join-Path $PSScriptRoot 'bin\Debug\net10.0'),
    [string]$ResultsDirectory = '.git\testagent\dc-native\linux-results',
    [string]$Filter = '',
    [string]$SdkImage = 'mcr.microsoft.com/dotnet/sdk@sha256:750c7bdfc4a67ed7d4f0affb908e046557f4f45e88a6a3c83f5d0d8f111957af'
)
$ErrorActionPreference = 'Stop'
$binary = [IO.Path]::GetFullPath($TestBinaryDirectory)
$native = [IO.Path]::GetFullPath($NativeLibrary)
$results = [IO.Path]::GetFullPath($ResultsDirectory)
if (!(Test-Path (Join-Path $binary 'Opc.Ua.Bindings.Quic.Tests.dll'))) {
    throw 'Build the portable managed test assembly first; this runner performs no restore or graph build.'
}
if (!(Test-Path $native) -or !(Test-Path ($native + '.sha256'))) {
    throw 'The reviewed Linux library and adjacent SHA256 receipt are required.'
}
if ($Filter.Contains("'")) {
    throw 'A test filter cannot contain a shell quote.'
}
podman image exists $SdkImage
if ($LASTEXITCODE -ne 0) {
    throw 'The existing qualified SDK image is required. This runner never pulls or installs an image.'
}
New-Item -ItemType Directory -Force $results | Out-Null
$command = 'cd /validation && mkdir -p results work dotnet-home && dotnet --version && timeout -k 5s 180s dotnet vstest Opc.Ua.Bindings.Quic.Tests.dll --TestAdapterPath:. --Logger:"trx;LogFileName=quic-binding-linux.trx" --ResultsDirectory:results'
if ($Filter) {
    $command += " --TestCaseFilter:'$Filter'"
}
$container = podman create --pull=never --cap-drop=all --security-opt=no-new-privileges `
    --ulimit core=0 --memory=2g --cpus=2 --env DOTNET_CLI_TELEMETRY_OPTOUT=1 `
    --env TMPDIR=/validation/work --env DOTNET_CLI_HOME=/validation/dotnet-home `
    --env OPCUA_QUIC_NATIVE_PATH=/validation/libopcua_quic_go.so `
    --entrypoint /bin/sh $SdkImage -c $command
if ($LASTEXITCODE -ne 0) {
    throw 'Could not create the bounded native binding Linux test container.'
}
$container = $container.Trim()
try {
    podman cp $binary "${container}:/validation"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not copy the owned portable managed test output.'
    }
    podman cp $native "${container}:/validation/libopcua_quic_go.so"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not copy the reviewed Linux native asset.'
    }
    podman cp ($native + '.sha256') "${container}:/validation/libopcua_quic_go.so.sha256"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not copy the matching native hash receipt.'
    }
    podman start --attach $container | Tee-Object -FilePath (Join-Path $results 'native-linux.log')
    $code = $LASTEXITCODE
    podman cp "${container}:/validation/results/." $results
    if ($LASTEXITCODE -ne 0 -or $code -ne 0) {
        throw "Native binding Linux tests failed: $code"
    }
}
finally {
    podman rm --force $container | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Owned Linux test container $container could not be removed."
    }
}
