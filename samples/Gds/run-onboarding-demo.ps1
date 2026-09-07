#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and runs the OPC 10000-21 onboarding registrar/client demo.

.PARAMETER Port
    Local OPC UA TCP port used by the registrar.

.PARAMETER Keep
    Keeps the isolated PKI and captured server logs after the run.

.EXAMPLE
    pwsh samples/Gds/run-onboarding-demo.ps1
#>
[CmdletBinding()]
param(
    [int]$Port = 62560,
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverProject = Join-Path $here 'OnboardingRegistrar\OnboardingRegistrar.csproj'
$clientProject = Join-Path $here 'OnboardingClient\OnboardingClient.csproj'
$runRoot = Join-Path ([IO.Path]::GetTempPath()) "opcua-onboarding-demo-$PID"
$serverPki = Join-Path $runRoot 'registrar-pki'
$clientPki = Join-Path $runRoot 'client-pki'
$serverOut = Join-Path $runRoot 'registrar.out.log'
$serverErr = Join-Path $runRoot 'registrar.err.log'
$endpoint = "opc.tcp://localhost:$Port/OnboardingRegistrar"
$serverDll = Join-Path $here 'OnboardingRegistrar\bin\Debug\net10.0\OnboardingRegistrar.dll'
$clientDll = Join-Path $here 'OnboardingClient\bin\Debug\net10.0\OnboardingClient.dll'
$server = $null
$previousUser = $env:ONBOARDING_DEMO_USER
$previousPassword = $env:ONBOARDING_DEMO_PASSWORD

function Step([string]$message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

try {
    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
    $env:ONBOARDING_DEMO_USER = "onboarding-admin-$PID"
    $env:ONBOARDING_DEMO_PASSWORD = [Convert]::ToBase64String(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

    Step 'Building the onboarding registrar and client'
    dotnet build $serverProject -f net10.0 --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'The onboarding registrar build failed.'
    }
    dotnet build $clientProject -f net10.0 --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'The onboarding client build failed.'
    }

    Step "Starting the registrar at $endpoint"
    $server = Start-Process dotnet `
        -ArgumentList @(
            "`"$serverDll`"",
            '--port', $Port,
            '--pkiRoot', $serverPki
        ) `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($server.HasExited) {
            break
        }
        if ((Test-Path $serverOut) -and
            (Get-Content -Raw $serverOut) -match 'ONBOARDING_REGISTRAR_READY') {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        throw 'The onboarding registrar did not become ready within 60 seconds.'
    }

    Step 'Verifying anonymous ticket administration is denied'
    $anonymousOutput = & dotnet $clientDll `
        --endpoint $endpoint `
        --pkiRoot $clientPki `
        --anonymous true 2>&1
    $anonymousExitCode = $LASTEXITCODE
    Write-Host ($anonymousOutput | Out-String)
    if ($anonymousExitCode -eq 0 -or
        ($anonymousOutput | Out-String) -notmatch 'BadUserAccessDenied') {
        throw 'Anonymous ticket administration was not rejected.'
    }

    Step 'Running the onboarding client'
    & dotnet $clientDll `
        --endpoint $endpoint `
        --pkiRoot $clientPki 2>&1 |
        Tee-Object -Variable capturedClientOutput
    if ($LASTEXITCODE -ne 0) {
        throw 'The onboarding client failed.'
    }
    if (($capturedClientOutput | Out-String) -notmatch 'ONBOARDING_DEMO_OK') {
        throw 'The onboarding client did not report a successful round trip.'
    }

    Write-Host ''
    Write-Host 'Onboarding registrar/client round trip succeeded.' -ForegroundColor Green
}
catch {
    if (Test-Path $serverOut) {
        Write-Host ''
        Write-Host 'Registrar output:' -ForegroundColor Yellow
        Get-Content $serverOut
    }
    if (Test-Path $serverErr) {
        Write-Host ''
        Write-Host 'Registrar errors:' -ForegroundColor Yellow
        Get-Content $serverErr
    }
    throw
}
finally {
    if ($server -ne $null -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        $server.WaitForExit()
    }
    if ($previousUser -eq $null) {
        Remove-Item Env:\ONBOARDING_DEMO_USER -ErrorAction SilentlyContinue
    }
    else {
        $env:ONBOARDING_DEMO_USER = $previousUser
    }
    if ($previousPassword -eq $null) {
        Remove-Item Env:\ONBOARDING_DEMO_PASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:ONBOARDING_DEMO_PASSWORD = $previousPassword
    }
    if ($Keep) {
        Write-Host "Demo artifacts kept at $runRoot" -ForegroundColor Yellow
    }
    elseif (Test-Path $runRoot) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
