# Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.
<#
.SYNOPSIS
Runs the existing portable sample executables and process tests on the cached qualified Linux SDK image.
.DESCRIPTION
No image pull, source mirror, NuGet restore, SDK install or build is performed. Only portable output
closures, the existing coverage collector and independently reviewed Linux native assets are copied.
The exact owned container and staging directory are removed even when validation fails.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$QuicLibrary,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$QuicSha256,
    [Parameter(Mandatory)][string]$Http3Library,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$Http3Sha256,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ResultsDirectory = '.git\testagent\dc-samples\linux-results',
    [string]$SdkImage =
        'mcr.microsoft.com/dotnet/sdk@sha256:750c7bdfc4a67ed7d4f0affb908e046557f4f45e88a6a3c83f5d0d8f111957af'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$quic = [IO.Path]::GetFullPath($QuicLibrary)
$http3 = [IO.Path]::GetFullPath($Http3Library)
if ((Test-Path -LiteralPath $results -PathType Leaf) -or
    ((Test-Path -LiteralPath $results -PathType Container) -and
        (Get-ChildItem -LiteralPath $results -Force | Select-Object -First 1))) {
    throw 'Choose a new or empty ResultsDirectory; stale TRX or coverage cannot satisfy this run.'
}

function Assert-NativeAsset([string]$Path, [string]$ExpectedHash) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw "The reviewed Linux native asset is absent or has the wrong SHA256: $Path"
    }
    $stream = [IO.File]::OpenRead($Path)
    try {
        $header = [byte[]]::new(20)
        if ($stream.Read($header, 0, $header.Length) -ne 20 -or
            $header[0] -ne 127 -or $header[1] -ne 69 -or $header[2] -ne 76 -or
            $header[3] -ne 70 -or $header[4] -ne 2 -or $header[5] -ne 1 -or
            $header[18] -ne 62 -or $header[19] -ne 0) {
            throw "A reviewed Linux x64 ELF image is required: $Path"
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Copy-PortableOutput([string]$Source, [string]$Destination) {
    if (!(Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Build the owned portable managed output first; this runner will not build it: $Source"
    }
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse |
        Where-Object Extension -In '.dll', '.json') {
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

Assert-NativeAsset $quic $QuicSha256
Assert-NativeAsset $http3 $Http3Sha256
podman image exists $SdkImage
if ($LASTEXITCODE -ne 0) {
    throw 'The existing qualified SDK image is required. This runner never pulls or installs an image.'
}

$testRelative = "tests\Opc.Ua.DataChannel.Samples.Tests\bin\$Configuration\net10.0"
$streamingRelative = "samples\Core\ConsoleDataChannelStreaming\bin\$Configuration\net10.0"
$audioRelative = "samples\Core\ConsoleDataChannelAudio\bin\$Configuration\net10.0"
foreach ($item in @(
    @($testRelative, 'Opc.Ua.DataChannel.Samples.Tests.dll'),
    @($streamingRelative, 'ConsoleDataChannelStreaming.dll'),
    @($audioRelative, 'ConsoleDataChannelAudio.dll'))) {
    if (!(Test-Path -LiteralPath (Join-Path (Join-Path $repository $item[0]) $item[1]))) {
        throw "The portable managed prerequisite is missing: $($item[0])\$($item[1])"
    }
}

$assetsPath = Join-Path $PSScriptRoot 'obj\project.assets.json'
if (!(Test-Path -LiteralPath $assetsPath)) {
    throw 'The existing restored test project/coverage collector is required; no restore is performed here.'
}
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$package = $assets.libraries.PSObject.Properties |
    Where-Object Name -Like 'coverlet.collector/*' | Select-Object -First 1
if (!$package) {
    throw 'Restore the existing centralized coverlet.collector reference before this portable run.'
}
$collector = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $packagePath = $package.Value.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $candidate = Join-Path (Join-Path $folder $packagePath) 'build\net10.0'
    if (Test-Path -LiteralPath (Join-Path $candidate 'coverlet.collector.dll')) {
        $collector = $candidate
        break
    }
}
if (!$collector) {
    throw 'The existing net10.0 coverage collector output is missing; no package download is attempted.'
}

New-Item -ItemType Directory -Force -Path $results | Out-Null
$stage = Join-Path ([IO.Path]::GetDirectoryName($results)) ('linux-stage-' + [Guid]::NewGuid().ToString('N'))
$container = $null
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository 'global.json') -Destination (Join-Path $stage 'global.json')
    Copy-PortableOutput (Join-Path $repository $testRelative) (Join-Path $stage $testRelative)
    Copy-PortableOutput (Join-Path $repository $streamingRelative) (Join-Path $stage $streamingRelative)
    Copy-PortableOutput (Join-Path $repository $audioRelative) (Join-Path $stage $audioRelative)
    Copy-PortableOutput $collector (Join-Path $stage 'collector')
    $native = Join-Path $stage 'native'
    New-Item -ItemType Directory -Path $native | Out-Null
    Copy-Item -LiteralPath $quic -Destination (Join-Path $native 'libopcua_quic_go.so')
    Copy-Item -LiteralPath $http3 -Destination (Join-Path $native 'libopcua_http3.so')
    $bytes = (Get-ChildItem -LiteralPath $stage -File -Recurse | Measure-Object Length -Sum).Sum
    $testLinux = $testRelative.Replace('\', '/')
    $command = "cd /validation && mkdir -p results work dotnet-home && " +
        "dotnet --version && timeout -k 5s 240s dotnet vstest " +
        "$testLinux/Opc.Ua.DataChannel.Samples.Tests.dll " +
        "--TestAdapterPath:`"$testLinux;collector`" " +
        '--Collect:"XPlat Code Coverage" --Logger:"trx;LogFileName=samples-linux.trx" --ResultsDirectory:results'
    $container = podman create --pull=never --network=none --cap-drop=all `
        --security-opt=no-new-privileges --ulimit core=0 --memory=2g --cpus=2 `
        --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true `
        --env DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 --env DOTNET_NOLOGO=1 --env DOTNET_EnableDiagnostics=0 `
        --env TMPDIR=/validation/work --env TEMP=/validation/work --env TMP=/validation/work `
        --env DOTNET_CLI_HOME=/validation/dotnet-home --env "OPCUA_SAMPLE_CONFIGURATION=$Configuration" `
        --env OPCUA_QUIC_NATIVE_PATH=/validation/native/libopcua_quic_go.so `
        --env "OPCUA_QUIC_NATIVE_SHA256=$QuicSha256" `
        --env OPCUA_HTTP3_NATIVE_PATH=/validation/native/libopcua_http3.so `
        --env "OPCUA_HTTP3_NATIVE_SHA256=$Http3Sha256" --entrypoint /bin/sh $SdkImage -c $command
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the bounded Linux sample validation container.'
    }
    $container = $container.Trim()
    @{
        Image = $SdkImage
        Container = $container
        Configuration = $Configuration
        PortableBytes = $bytes
        QuicSha256 = $QuicSha256
        Http3Sha256 = $Http3Sha256
        Command = $command
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'run-receipt.json') -Encoding utf8
    Write-Host ("Portable closure: {0:N2} MiB; no restore, build, source mirror or image pull." -f ($bytes / 1MB))
    podman cp $stage "${container}:/validation"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not copy the bounded owned portable closure.'
    }
    podman start --attach $container 2>&1 | Tee-Object -FilePath (Join-Path $results 'samples-linux.log')
    $code = $LASTEXITCODE
    podman cp "${container}:/validation/results/." $results
    if ($LASTEXITCODE -ne 0 -or $code -ne 0) {
        throw "Linux sample execution failed with exit code $code; inspect the retained output."
    }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $results 'samples-linux.trx') -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.total -le 0 -or [int]$counts.executed -ne [int]$counts.total -or
        [int]$counts.passed -ne [int]$counts.total -or [int]$counts.failed -ne 0 -or
        [int]$counts.notExecuted -ne 0) {
        throw 'Linux acceptance requires actual executed tests with no failure or skip.'
    }
    if (!(Get-ChildItem -LiteralPath $results -Recurse -Filter 'coverage.cobertura.xml' -File)) {
        throw 'The common CI coverage collector did not produce its required attachment.'
    }
    Write-Host "Linux sample acceptance: passed=$($counts.passed), failed=$($counts.failed), skipped=0."
}
finally {
    try {
        if ($container) {
            podman rm --force $container | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "The exact owned Linux container $container could not be removed."
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}
