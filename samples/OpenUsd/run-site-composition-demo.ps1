#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the federated pump and generator OpenUSD site-composition demo.

.DESCRIPTION
    Builds and starts the pump, generator, and site OPC UA servers, publishes the
    connector and optional viewport side by side, and opens the composed live site.
    Closing the viewer stops the servers and removes the isolated run directory unless
    -Keep is supplied.

.PARAMETER PumpCount
    Number of simulated pumps.

.PARAMETER GeneratorCount
    Number of simulated generator sets.

.PARAMETER PumpPort
    Local OPC UA TCP port used by the pump server.

.PARAMETER GeneratorPort
    Local OPC UA TCP port used by the generator server.

.PARAMETER SitePort
    Local OPC UA TCP port used by the site-composition server.

.PARAMETER Renderer
    Viewer renderer preference: Auto, Storm, D3D12, Vulkan, or Metal.

.PARAMETER Configuration
    Build configuration used for the sample servers and locally published viewer.

.PARAMETER ViewerBundlePath
    Optional existing directory containing Opc.Ua.OpenUsd.Connector.dll and
    Opc.Ua.OpenUsd.Connector.Viewer.dll. When omitted, both projects are published
    side by side into the isolated run directory.

.PARAMETER Seconds
    Closes the viewer automatically after this many seconds. Zero waits until the
    viewer window is closed.

.PARAMETER Keep
    Leaves the three servers running and keeps logs, stage assets, and publish output.

.EXAMPLE
    pwsh samples/OpenUsd/run-site-composition-demo.ps1

.EXAMPLE
    pwsh samples/OpenUsd/run-site-composition-demo.ps1 -Renderer D3D12 -Keep
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$PumpCount = 3,
    [ValidateRange(1, 100)]
    [int]$GeneratorCount = 2,
    [ValidateRange(1, 65535)]
    [int]$PumpPort = 62542,
    [ValidateRange(1, 65535)]
    [int]$GeneratorPort = 62543,
    [ValidateRange(1, 65535)]
    [int]$SitePort = 62544,
    [ValidateSet('Auto', 'Storm', 'D3D12', 'Vulkan', 'Metal')]
    [string]$Renderer = 'Auto',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ViewerBundlePath,
    [ValidateRange(0, 86400)]
    [int]$Seconds = 0,
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $here '..' '..')).Path
$runRoot = Join-Path ([IO.Path]::GetTempPath()) "opcua-openusd-site-demo-$PID"
$viewerPublish = Join-Path $runRoot 'viewer'
$stageCache = Join-Path $runRoot 'stage'
$previousCustomTestTarget = $env:CustomTestTarget
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()

$pumpProject = Join-Path $repoRoot 'samples\DI\PumpDeviceIntegrationServer\PumpDeviceIntegrationServer.csproj'
$generatorProject = Join-Path $here 'GeneratorServer\GeneratorServer.csproj'
$siteProject = Join-Path $here 'SiteCompositionServer\SiteCompositionServer.csproj'
$connectorProject = Join-Path $repoRoot 'tools\Opc.Ua.OpenUsd.Connector\Opc.Ua.OpenUsd.Connector.csproj'
$viewerProject = Join-Path $repoRoot 'tools\Opc.Ua.OpenUsd.Connector.Viewer\Opc.Ua.OpenUsd.Connector.Viewer.csproj'

$pumpEndpoint = "opc.tcp://localhost:$PumpPort/PumpDeviceIntegrationServer"
$generatorEndpoint = "opc.tcp://localhost:$GeneratorPort/GeneratorServer"
$siteEndpoint = "opc.tcp://localhost:$SitePort/SiteCompositionServer"

function Step([string]$message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Require([string]$tool) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is required and was not found on PATH."
    }
}

function Assert-PortAvailable([int]$port) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    try {
        $listener.Start()
    }
    catch {
        throw "TCP port $port is already in use."
    }
    finally {
        $listener.Stop()
    }
}

function Invoke-DotNet([string[]]$arguments, [string]$failure) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw $failure
    }
}

function Start-Server(
    [string]$name,
    [string]$dll,
    [string[]]$arguments,
    [string]$readyMarker
) {
    $stdout = Join-Path $runRoot "$name.out.log"
    $stderr = Join-Path $runRoot "$name.err.log"
    $processArguments = @("`"$dll`"") + $arguments
    $process = Start-Process dotnet `
        -ArgumentList $processArguments `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru
    $processes.Add($process)

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            throw "$name exited before becoming ready."
        }
        if ((Test-Path $stdout) -and
            (Get-Content -Raw $stdout) -match [regex]::Escape($readyMarker)) {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "$name did not become ready within 90 seconds."
}

function Show-ServerLogs {
    foreach ($name in @('pump', 'generator', 'site')) {
        foreach ($stream in @('out', 'err')) {
            $path = Join-Path $runRoot "$name.$stream.log"
            if (Test-Path $path) {
                Write-Host ''
                Write-Host "$name $stream log:" -ForegroundColor Yellow
                Get-Content $path
            }
        }
    }
}

function Get-RuntimeIdentifier {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    if ($IsWindows -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) {
        return 'win-x64'
    }
    if ($IsLinux -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) {
        return 'linux-x64'
    }
    if ($IsMacOS -and $architecture -eq [Runtime.InteropServices.Architecture]::Arm64) {
        return 'osx-arm64'
    }
    throw "The OpenUSD viewport does not provide a runtime for this platform and architecture."
}

Require dotnet

try {
    Assert-PortAvailable $PumpPort
    Assert-PortAvailable $GeneratorPort
    Assert-PortAvailable $SitePort
    New-Item -ItemType Directory -Force -Path $runRoot, $stageCache | Out-Null
    $env:CustomTestTarget = 'net10.0'

    Step 'Building the pump, generator, and site servers'
    Invoke-DotNet -Arguments @(
        'build', $pumpProject,
        '-c', $Configuration,
        '-f', 'net10.0',
        '--nologo',
        '--verbosity', 'quiet'
    ) -Failure 'The pump server build failed.'
    Invoke-DotNet -Arguments @(
        'build', $generatorProject,
        '-c', $Configuration,
        '-f', 'net10.0',
        '--nologo',
        '--verbosity', 'quiet'
    ) -Failure 'The generator server build failed.'
    Invoke-DotNet -Arguments @(
        'build', $siteProject,
        '-c', $Configuration,
        '-f', 'net10.0',
        '--nologo',
        '--verbosity', 'quiet'
    ) -Failure 'The site-composition server build failed.'

    if ([string]::IsNullOrWhiteSpace($ViewerBundlePath)) {
        $rid = Get-RuntimeIdentifier
        New-Item -ItemType Directory -Force -Path $viewerPublish | Out-Null
        Step "Publishing the connector and viewport for $rid"
        Invoke-DotNet -Arguments @(
            'publish', $connectorProject,
            '-c', $Configuration,
            '-f', 'net10.0',
            '-r', $rid,
            '--self-contained', 'false',
            '-o', $viewerPublish,
            '--nologo',
            '--verbosity', 'quiet'
        ) -Failure 'The OpenUSD connector publish failed.'
        Invoke-DotNet -Arguments @(
            'publish', $viewerProject,
            '-c', $Configuration,
            '-f', 'net10.0',
            '-r', $rid,
            '--self-contained', 'false',
            '-o', $viewerPublish,
            '--nologo',
            '--verbosity', 'quiet'
        ) -Failure 'The OpenUSD viewport publish failed.'
    }
    else {
        $viewerPublish = [IO.Path]::GetFullPath($ViewerBundlePath)
    }

    $connectorDll = Join-Path $viewerPublish 'Opc.Ua.OpenUsd.Connector.dll'
    $viewportDll = Join-Path $viewerPublish 'Opc.Ua.OpenUsd.Connector.Viewer.dll'
    if (-not (Test-Path $connectorDll) -or -not (Test-Path $viewportDll)) {
        throw "The viewer bundle is incomplete: $viewerPublish"
    }

    $pumpDll = Join-Path `
        (Split-Path $pumpProject -Parent) `
        "bin\$Configuration\net10.0\PumpDeviceIntegrationServer.dll"
    $generatorDll = Join-Path `
        (Split-Path $generatorProject -Parent) `
        "bin\$Configuration\net10.0\GeneratorServer.dll"
    $siteDll = Join-Path `
        (Split-Path $siteProject -Parent) `
        "bin\$Configuration\net10.0\SiteCompositionServer.dll"

    Step "Starting the pump server at $pumpEndpoint"
    Start-Server -Name 'pump' -Dll $pumpDll -Arguments @(
        '--host', 'localhost',
        '--port', $PumpPort,
        '--pumps', $PumpCount
    ) -ReadyMarker "OPC UA server listening at $pumpEndpoint"

    Step "Starting the generator server at $generatorEndpoint"
    Start-Server -Name 'generator' -Dll $generatorDll -Arguments @(
        '--host', 'localhost',
        '--port', $GeneratorPort,
        '--generators', $GeneratorCount
    ) -ReadyMarker "OPC UA server listening at $generatorEndpoint"

    Step "Starting the site-composition server at $siteEndpoint"
    Start-Server -Name 'site' -Dll $siteDll -Arguments @(
        '--host', 'localhost',
        '--port', $SitePort,
        '--pump-server', $pumpEndpoint,
        '--generator-server', $generatorEndpoint
    ) -ReadyMarker "OPC UA server listening at $siteEndpoint"

    Step 'Opening the federated live site'
    $viewerArguments = @(
        $connectorDll,
        '--server', $siteEndpoint,
        '--insecure',
        '--federate',
        '--view',
        '--renderer', $Renderer,
        '--camera', '/Site/SiteCamera',
        '--fetch-assets', $stageCache
    )
    if ($Seconds -gt 0) {
        $viewerArguments += @('--seconds', $Seconds)
    }
    & dotnet @viewerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The OpenUSD connector exited with code $LASTEXITCODE."
    }

    Write-Host ''
    Write-Host 'The OpenUSD site-composition demo completed.' -ForegroundColor Green
}
catch {
    Show-ServerLogs
    throw
}
finally {
    if ($previousCustomTestTarget -eq $null) {
        Remove-Item Env:\CustomTestTarget -ErrorAction SilentlyContinue
    }
    else {
        $env:CustomTestTarget = $previousCustomTestTarget
    }

    if ($Keep) {
        Write-Host ''
        Write-Host "Demo artifacts kept at $runRoot" -ForegroundColor Yellow
        foreach ($process in $processes) {
            if (-not $process.HasExited) {
                Write-Host "  PID $($process.Id): $($process.StartInfo.Arguments)"
            }
        }
    }
    else {
        for ($index = $processes.Count - 1; $index -ge 0; $index--) {
            $process = $processes[$index]
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                $process.WaitForExit()
            }
        }
        if (Test-Path $runRoot) {
            Remove-Item -LiteralPath $runRoot -Recurse -Force
        }
    }
}
