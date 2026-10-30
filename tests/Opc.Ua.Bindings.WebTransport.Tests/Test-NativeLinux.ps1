# Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $QuicLibrary,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string] $QuicSha256,
    [Parameter(Mandatory)]
    [string] $Http3Library,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string] $Http3Sha256,
    [string] $ManagedDirectory = (Join-Path $PSScriptRoot 'bin\Debug\net10.0'),
    [string] $OutputRoot = '.git\testagent\dc-webtransport\linux',
    [string] $Image = 'mcr.microsoft.com/dotnet/sdk@sha256:750c7bdfc4a67ed7d4f0affb908e046557f4f45e88a6a3c83f5d0d8f111957af'
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath((Join-Path $repository $OutputRoot))
if (-not $output.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
{
    throw 'Validation output must be beneath the repository.'
}
$managed = (Resolve-Path $ManagedDirectory).Path
$assembly = Join-Path $managed 'Opc.Ua.Bindings.WebTransport.Tests.dll'
if (-not (Test-Path $assembly))
{
    throw 'Build the portable test assembly against coordinated fresh dependencies first.'
}
foreach ($asset in @(@($QuicLibrary, $QuicSha256), @($Http3Library, $Http3Sha256)))
{
    if ((Get-FileHash $asset[0] -Algorithm SHA256).Hash -ne $asset[1])
    {
        throw 'A native asset does not match its explicitly reviewed hash.'
    }
}
$stage = Join-Path $output 'stage'
if (Test-Path $stage)
{
    throw 'The owned staging directory already exists; use a new OutputRoot for this bounded run.'
}
New-Item -ItemType Directory (Join-Path $stage 'tests'), (Join-Path $stage 'native') -Force | Out-Null
$container = $null
try
{
    foreach ($file in Get-ChildItem $managed -File -Recurse)
    {
        if ($file.Extension -notin '.dll', '.json')
        {
            continue
        }
        $relative = [IO.Path]::GetRelativePath($managed, $file.FullName)
        $destination = Join-Path (Join-Path $stage 'tests') $relative
        New-Item -ItemType Directory ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item $file.FullName $destination
    }
    Copy-Item $QuicLibrary (Join-Path $stage 'native\libopcua_quic_go.so')
    Copy-Item $Http3Library (Join-Path $stage 'native\libopcua_http3.so')
    $QuicSha256.ToLowerInvariant() | Set-Content (Join-Path $stage 'native\libopcua_quic_go.so.sha256')
    $command = 'cd /validation && mkdir -p results work dotnet-home && ' +
        'timeout -k 5s 180s dotnet vstest tests/Opc.Ua.Bindings.WebTransport.Tests.dll ' +
        '--Logger:"trx;LogFileName=webtransport-linux.trx" --ResultsDirectory:results'
    $created = & podman create --pull=never --network=none --cap-drop=all `
        --security-opt=no-new-privileges --ulimit core=0 --memory=2g --cpus=2 `
        --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 `
        --env DOTNET_CLI_HOME=/validation/dotnet-home --env TMPDIR=/validation/work `
        --env OPCUA_QUIC_NATIVE_PATH=/validation/native/libopcua_quic_go.so `
        --env OPCUA_HTTP3_NATIVE_PATH=/validation/native/libopcua_http3.so `
        --env "OPCUA_HTTP3_NATIVE_SHA256=$Http3Sha256" `
        --entrypoint /bin/sh $Image -c $command
    if ($LASTEXITCODE -ne 0)
    {
        throw 'The already-cached qualified Linux SDK image is unavailable; no download was attempted.'
    }
    $container = ($created | Select-Object -Last 1).Trim()
    & podman cp $stage "${container}:/validation"
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Copying the bounded validation inputs failed.'
    }
    & podman start --attach $container 2>&1 | Tee-Object (Join-Path $output 'console.log')
    $exitCode = $LASTEXITCODE
    New-Item -ItemType Directory (Join-Path $output 'results') -Force | Out-Null
    & podman cp "${container}:/validation/results/." (Join-Path $output 'results')
    if ($LASTEXITCODE -ne 0)
    {
        throw 'The Linux run did not produce a retrievable results directory.'
    }
    if ($exitCode -ne 0)
    {
        throw "Actual Linux WebTransport validation failed with exit code $exitCode."
    }
    [xml] $report = Get-Content (Join-Path $output 'results\webtransport-linux.trx')
    $counters = $report.TestRun.ResultSummary.Counters
    if ([int]$counters.total -eq 0 -or [int]$counters.failed -ne 0 -or
        [int]$counters.passed -ne [int]$counters.total)
    {
        throw 'Native Linux validation must execute the complete suite without failures or skips.'
    }
    Write-Output "Actual Linux WebTransport: passed $($counters.passed), failed 0, skipped 0."
}
finally
{
    if ($container)
    {
        & podman rm --force $container | Out-Null
    }
    if (Test-Path $stage)
    {
        Remove-Item $stage -Recurse -Force
    }
}
