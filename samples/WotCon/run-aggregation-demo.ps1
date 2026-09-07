#!/usr/bin/env pwsh
# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/

<#
.SYNOPSIS
    Builds and runs the WoT aggregation client/server demo.

.PARAMETER AggregationPort
    Local OPC UA TCP port used by the aggregation server.

.PARAMETER SourceAPort
    Local OPC UA TCP port used by Source A.

.PARAMETER SourceBPort
    Local OPC UA TCP port used by Source B.

.PARAMETER Keep
    Keeps the isolated PKI stores and captured server logs after the run.

.EXAMPLE
    pwsh samples/WotCon/run-aggregation-demo.ps1
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$AggregationPort = 62550,

    [ValidateRange(1, 65535)]
    [int]$SourceAPort = 62551,

    [ValidateRange(1, 65535)]
    [int]$SourceBPort = 62552,

    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$flatTagProject = Join-Path $here 'FlatTagServer\FlatTagServer.csproj'
$aggregationProject = Join-Path $here 'AggregationServer\AggregationServer.csproj'
$clientProject = Join-Path $here 'AggregationClient\AggregationClient.csproj'
$flatTagDll = Join-Path $here 'FlatTagServer\bin\Debug\net10.0\FlatTagServer.dll'
$aggregationDll = Join-Path $here 'AggregationServer\bin\Debug\net10.0\AggregationServer.dll'
$clientDll = Join-Path $here 'AggregationClient\bin\Debug\net10.0\AggregationClient.dll'
$documentsDirectory = Join-Path $here 'AggregationClient\Documents'
$runRoot = Join-Path ([IO.Path]::GetTempPath()) "opcua-wot-aggregation-demo-$PID"
$sourceAPki = Join-Path $runRoot 'source-a-pki'
$sourceBPki = Join-Path $runRoot 'source-b-pki'
$aggregationPki = Join-Path $runRoot 'aggregation-pki'
$clientPki = Join-Path $runRoot 'client-pki'
$sourceAOut = Join-Path $runRoot 'source-a.out.log'
$sourceAErr = Join-Path $runRoot 'source-a.err.log'
$sourceBOut = Join-Path $runRoot 'source-b.out.log'
$sourceBErr = Join-Path $runRoot 'source-b.err.log'
$aggregationOut = Join-Path $runRoot 'aggregation.out.log'
$aggregationErr = Join-Path $runRoot 'aggregation.err.log'
$sourceAEndpoint = "opc.tcp://localhost:$SourceAPort/SourceA"
$sourceBEndpoint = "opc.tcp://localhost:$SourceBPort/SourceB"
$aggregationEndpoint = "opc.tcp://localhost:$AggregationPort/AggregationServer"
$servers = [Collections.Generic.List[object]]::new()

function Step([string]$message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Assert-PortAvailable([int]$port, [string]$name) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    try {
        $listener.Start()
    }
    catch [Net.Sockets.SocketException] {
        throw "$name port $port is not available."
    }
    finally {
        $listener.Stop()
    }
}

function Start-DemoServer(
    [string]$name,
    [string]$dll,
    [string[]]$arguments,
    [string]$standardOutput,
    [string]$standardError) {
    $process = Start-Process dotnet `
        -ArgumentList (@("`"$dll`"") + $arguments) `
        -RedirectStandardOutput $standardOutput `
        -RedirectStandardError $standardError `
        -PassThru
    $servers.Add([pscustomobject]@{
        Name = $name
        Process = $process
        StandardOutput = $standardOutput
        StandardError = $standardError
    })
    return $process
}

function Wait-Endpoint(
    [Diagnostics.Process]$process,
    [string]$standardOutput,
    [int]$port,
    [string]$name) {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            throw "$name exited before its OPC UA endpoint became ready."
        }

        if (Test-Path $standardOutput) {
            $output = Get-Content -Raw $standardOutput
            if ($output -match "OPC UA server listening at .+:$port/") {
                return
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "$name did not become ready within 60 seconds."
}

function Show-ServerLogs([object]$server) {
    if (Test-Path $server.StandardOutput) {
        Write-Host ''
        Write-Host "$($server.Name) output:" -ForegroundColor Yellow
        Get-Content $server.StandardOutput
    }
    if (Test-Path $server.StandardError) {
        Write-Host ''
        Write-Host "$($server.Name) errors:" -ForegroundColor Yellow
        Get-Content $server.StandardError
    }
}

try {
    $ports = @($AggregationPort, $SourceAPort, $SourceBPort)
    if (($ports | Select-Object -Unique).Count -ne $ports.Count) {
        throw 'The aggregation, Source A, and Source B ports must be distinct.'
    }
    Assert-PortAvailable $AggregationPort 'Aggregation server'
    Assert-PortAvailable $SourceAPort 'Source A'
    Assert-PortAvailable $SourceBPort 'Source B'
    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

    Step 'Building the WoT aggregation servers and client'
    dotnet build $flatTagProject -f net10.0 --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'The flat-tag server build failed.'
    }
    dotnet build $aggregationProject -f net10.0 --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'The aggregation server build failed.'
    }
    dotnet build $clientProject -f net10.0 --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'The aggregation client build failed.'
    }

    Step "Starting Source A at $sourceAEndpoint"
    $sourceA = Start-DemoServer `
        'Source A' `
        $flatTagDll `
        @(
            '--port', "$SourceAPort",
            '--instanceName', 'SourceA',
            '--applicationName', "WotAggregationSourceA-$PID",
            '--namespace', 'urn:opcfoundation.org:UA:WotAggregation:SourceA',
            '--pkiRoot', "`"$sourceAPki`"",
            '--differentialPressure', '111.25',
            '--fluidTemperature', '301.15',
            '--massFlow', '0.42',
            '--level', '4.25',
            '--cavitation', 'true',
            '--pump2DifferentialPressure', '211.25',
            '--pump2FluidTemperature', '304.15',
            '--pump2MassFlow', '0.52',
            '--pump2Level', '4.75',
            '--pump2Cavitation', 'false'
        ) `
        $sourceAOut `
        $sourceAErr

    Step "Starting Source B at $sourceBEndpoint"
    $sourceB = Start-DemoServer `
        'Source B' `
        $flatTagDll `
        @(
            '--port', "$SourceBPort",
            '--instanceName', 'SourceB',
            '--applicationName', "WotAggregationSourceB-$PID",
            '--namespace', 'urn:opcfoundation.org:UA:WotAggregation:SourceB',
            '--pkiRoot', "`"$sourceBPki`"",
            '--bearingTemperature', '333.15',
            '--pumpPowerInput', '17.75',
            '--pumpEfficiency', '91.5',
            '--numberOfStarts', '23',
            '--motorOverheat', 'true',
            '--pump2BearingTemperature', '337.15',
            '--pump2PumpPowerInput', '19.75',
            '--pump2PumpEfficiency', '89.5',
            '--pump2NumberOfStarts', '31',
            '--pump2MotorOverheat', 'false'
        ) `
        $sourceBOut `
        $sourceBErr

    Step "Starting the aggregation server at $aggregationEndpoint"
    $aggregation = Start-DemoServer `
        'Aggregation server' `
        $aggregationDll `
        @(
            '--port', "$AggregationPort",
            '--applicationName', "WotAggregationServer-$PID",
            '--pkiRoot', "`"$aggregationPki`""
        ) `
        $aggregationOut `
        $aggregationErr

    Wait-Endpoint $sourceA $sourceAOut $SourceAPort 'Source A'
    Wait-Endpoint $sourceB $sourceBOut $SourceBPort 'Source B'
    Wait-Endpoint $aggregation $aggregationOut $AggregationPort 'Aggregation server'

    Step 'Loading the WoT documents and reading the materialized Pump'
    $capturedClientOutput = @(
        & dotnet $clientDll `
            --aggregationEndpoint $aggregationEndpoint `
            --sourceAEndpoint $sourceAEndpoint `
            --sourceBEndpoint $sourceBEndpoint `
            --applicationName "WotAggregationClient-$PID" `
            --pkiRoot $clientPki `
            --documentsDirectory $documentsDirectory 2>&1
    )
    $clientExitCode = $LASTEXITCODE
    $capturedClientOutput | ForEach-Object { Write-Host $_ }
    if ($clientExitCode -ne 0) {
        throw 'The aggregation client failed.'
    }

    $clientText = $capturedClientOutput | Out-String
    $loadedCount = [regex]::Matches($clientText, '(?m)^Loaded ').Count
    if ($loadedCount -ne 16) {
        throw "The aggregation client loaded $loadedCount resources instead of 16."
    }

    $expectedValues = @(
        'DifferentialPressure',
        'FluidTemperature',
        'MassFlow',
        'Level',
        'Cavitation',
        'BearingTemperature',
        'PumpPowerInput',
        'PumpEfficiency',
        'NumberOfStarts',
        'MotorOverheat'
    )
    foreach ($name in $expectedValues) {
        $pattern =
            '(?m)^  {0}: .+ \[Good \[0x00000000\]\]\r?$' -f [regex]::Escape($name)
        if ($clientText -notmatch $pattern) {
            throw "The aggregation client did not report a Good $name value."
        }
    }

    Write-Host ''
    Write-Host 'WoT aggregation client/server round trip succeeded.' -ForegroundColor Green
}
catch {
    foreach ($server in $servers) {
        Show-ServerLogs $server
    }
    throw
}
finally {
    for ($ii = $servers.Count - 1; $ii -ge 0; $ii--) {
        $process = $servers[$ii].Process
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit()
        }
    }
    if ($Keep) {
        Write-Host "Demo artifacts kept at $runRoot" -ForegroundColor Yellow
    }
    elseif (Test-Path $runRoot) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
