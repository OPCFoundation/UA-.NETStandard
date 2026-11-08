#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and runs the OPC 10000-21 onboarding registrar/client demo.

.PARAMETER Port
    Local OPC UA TCP port used by the registrar.

.PARAMETER Keep
    Keeps the isolated PKI and captured server logs after the run.

.PARAMETER NoBuild
    Uses existing net10.0 Debug assemblies without invoking either build.

.PARAMETER ClientTimeoutSeconds
    Maximum duration of each client process, including certificate preparation.

.EXAMPLE
    pwsh samples/Gds/run-onboarding-demo.ps1
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 62560,
    [switch]$Keep,
    [switch]$NoBuild,
    [ValidateRange(1, 3600)]
    [int]$ClientTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverProject = Join-Path $here 'OnboardingRegistrar\OnboardingRegistrar.csproj'
$clientProject = Join-Path $here 'OnboardingClient\OnboardingClient.csproj'
$runRoot = Join-Path ([IO.Path]::GetTempPath()) "opcua-onboarding-demo-$([Guid]::NewGuid().ToString('N'))"
$serverPki = Join-Path $runRoot 'registrar-pki'
$clientPki = Join-Path $runRoot 'client-pki'
$untrustedClientPki = Join-Path $runRoot 'untrusted-client-pki'
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

function Invoke-DemoClient([string]$name, [string[]]$clientArguments) {
    $stdout = Join-Path $runRoot "$name.out.log"
    $stderr = Join-Path $runRoot "$name.err.log"
    $processArguments = @("`"$clientDll`"") + @(
        $clientArguments | ForEach-Object { "`"$_`"" }
    )
    $client = Start-Process dotnet `
        -ArgumentList $processArguments `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru
    try {
        if (-not $client.WaitForExit($ClientTimeoutSeconds * 1000)) {
            Stop-Process -Id $client.Id -Force
            $client.WaitForExit()
            $output = @(
                Get-Content -LiteralPath $stdout
                Get-Content -LiteralPath $stderr
            ) -join [Environment]::NewLine
            throw "Client '$name' exceeded $ClientTimeoutSeconds seconds.`n$output"
        }
        $client.WaitForExit()
        $output = @(
            Get-Content -LiteralPath $stdout
            Get-Content -LiteralPath $stderr
        ) -join [Environment]::NewLine
        Write-Host $output
        return [pscustomobject]@{
            ExitCode = $client.ExitCode
            Output = $output
        }
    }
    finally {
        try {
            if (-not $client.HasExited) {
                Stop-Process -Id $client.Id -Force
                $client.WaitForExit()
            }
        }
        finally {
            $client.Dispose()
        }
    }
}

try {
    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
    $env:ONBOARDING_DEMO_USER = "onboarding-admin-$PID"
    $env:ONBOARDING_DEMO_PASSWORD = [Convert]::ToBase64String(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

    if (-not $NoBuild) {
        Step 'Building the onboarding registrar and client'
        dotnet build $serverProject -f net10.0 -p:CustomTestTarget=net10.0 `
            -m:1 -nr:false -p:UseSharedCompilation=false --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw 'The onboarding registrar build failed.'
        }
        dotnet build $clientProject -f net10.0 -p:CustomTestTarget=net10.0 `
            -m:1 -nr:false -p:UseSharedCompilation=false --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw 'The onboarding client build failed.'
        }
    }
    if (-not (Test-Path -LiteralPath $serverDll -PathType Leaf) -or
        -not (Test-Path -LiteralPath $clientDll -PathType Leaf)) {
        throw 'Both net10.0 Debug sample assemblies must exist before running the demo.'
    }

    Step 'Preparing application certificates, signed tickets and explicit local trust'
    $preparation = Invoke-DemoClient 'preparation' @(
        '--prepareTickets', 'true',
        '--pkiRoot', $clientPki,
        '--registrarPkiRoot', $serverPki,
        '--untrustedClientPkiRoot', $untrustedClientPki,
        '--ticketAuthorityTrustPath', (Join-Path $serverPki 'ticket-authorities\trusted')
    )
    if ($preparation.ExitCode -ne 0) {
        throw 'Signed onboarding ticket preparation failed.'
    }

    Step "Starting the registrar at $endpoint"
    $server = Start-Process dotnet `
        -ArgumentList @(
            "`"$serverDll`"",
            '--port', $Port,
            '--pkiRoot', "`"$serverPki`""
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

    Step 'Verifying a client without configured registrar trust rejects the connection'
    $untrusted = Invoke-DemoClient 'untrusted' @(
        '--endpoint', $endpoint,
        '--pkiRoot', $untrustedClientPki
    )
    if ($untrusted.ExitCode -eq 0 -or
        $untrusted.Output -notmatch 'BadCertificateUntrusted') {
        throw 'A client without registrar trust did not reject the registrar certificate.'
    }

    Step 'Verifying anonymous ticket administration is denied'
    $anonymous = Invoke-DemoClient 'anonymous' @(
        '--endpoint', $endpoint,
        '--pkiRoot', $clientPki,
        '--anonymous', 'true'
    )
    if ($anonymous.ExitCode -eq 0 -or
        $anonymous.Output -notmatch 'BadUserAccessDenied') {
        throw 'Anonymous ticket administration was not rejected.'
    }

    Step 'Running the onboarding client'
    $roundTrip = Invoke-DemoClient 'roundtrip' @(
        '--endpoint', $endpoint,
        '--pkiRoot', $clientPki
    )
    if ($roundTrip.ExitCode -ne 0) {
        throw 'The onboarding client failed.'
    }
    if ($roundTrip.Output -notmatch 'ONBOARDING_DEMO_OK') {
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
    if ($server -ne $null) {
        $server.Dispose()
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
