# SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
# SPDX-License-Identifier: MIT


#Requires -Version 7.0

<#
.SYNOPSIS
Starts the AutoID sample server on a free port, runs the sample client against it and checks what the
client prints.

.PARAMETER ServerExecutable
The built AutoIdServer executable or, with a .dll extension, assembly.

.PARAMETER ClientExecutable
The built AutoIdClient executable or, with a .dll extension, assembly.

.PARAMETER ServerSeconds
How long the server may run before it stops itself. Defaults to 60.

.PARAMETER WatchSeconds
How long the client watches scan events. Defaults to 6.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $ServerExecutable,
    [Parameter(Mandatory = $true)]
    [string] $ClientExecutable,
    [ValidateRange(20, 300)]
    [int] $ServerSeconds = 60,
    [ValidateRange(0, 60)]
    [int] $WatchSeconds = 6
)

$ErrorActionPreference = 'Stop'

function Get-FreePort
{
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try
    {
        return $listener.LocalEndpoint.Port
    }
    finally
    {
        $listener.Stop()
    }
}

function Get-StartInfo([string] $Path, [string[]] $Arguments)
{
    $resolved = (Resolve-Path $Path).Path
    $info = [Diagnostics.ProcessStartInfo]::new()
    if ($resolved.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase))
    {
        $info.FileName = 'dotnet'
        $info.ArgumentList.Add($resolved)
    }
    else
    {
        $info.FileName = $resolved
    }
    foreach ($argument in $Arguments)
    {
        $info.ArgumentList.Add($argument)
    }
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    return $info
}

$port = Get-FreePort
$pki = Join-Path ([IO.Path]::GetTempPath()) ('autoid-sample-pki-' + [Guid]::NewGuid().ToString('N'))
$server = $null
try
{
    $server = [Diagnostics.Process]::Start((Get-StartInfo $ServerExecutable @(
        '--port', $port, '--pki-root', $pki, '--run-seconds', $ServerSeconds)))
    $serverOutput = $server.StandardOutput.ReadToEndAsync()

    # The server creates its certificates on first start, so it may refuse sessions for a while.
    $text = $null
    for ($attempt = 1; $attempt -le 8 -and $null -eq $text; $attempt++)
    {
        if ($server.HasExited)
        {
            throw "The server exited early with code $($server.ExitCode)."
        }
        $client = [Diagnostics.Process]::Start((Get-StartInfo $ClientExecutable @(
            '--url', "opc.tcp://localhost:$port/AutoIdServer", '--insecure', '--seconds', $WatchSeconds)))
        $clientOutput = $client.StandardOutput.ReadToEndAsync()
        $clientError = $client.StandardError.ReadToEndAsync()
        if (-not $client.WaitForExit(($WatchSeconds + 60) * 1000))
        {
            $client.Kill($true)
            throw 'The client did not finish in time.'
        }
        if ($client.ExitCode -eq 0)
        {
            $text = $clientOutput.GetAwaiter().GetResult()
        }
        elseif ($attempt -eq 8)
        {
            throw "The client exited with code $($client.ExitCode): $($clientError.GetAwaiter().GetResult())"
        }
        else
        {
            Start-Sleep -Seconds 5
        }
    }
    Write-Host $text

    $patterns = @(
        'Rfid reader DockDoor1',
        'DockDoor1: Idle, Example Automation RFID portal R-100, serial R100-0001',
        'ScanActive: coherent data at .*antenna .*dBm',
        'Scan: SUCCESS, \d+ tag\(s\)',
        'OpticalVerifier reader LabelVerifier',
        'Verifier Scan: SUCCESS, ISO grade A',
        'Verifier image: image/x-portable-graymap, \d+ byte\(s\)',
        'ReadTag user memory: SUCCESS, "PALLET-\d{4}"',
        'WriteTag: SUCCESS; read back: "CHECKED"',
        'ScanStart: SUCCESS',
        'ScanStop: stopped')
    if ($WatchSeconds -gt 0)
    {
        $patterns += 'scan event at'
    }
    foreach ($pattern in $patterns)
    {
        if ($text -notmatch $pattern)
        {
            throw "Required client output not found: $pattern"
        }
    }
    Write-Host 'AutoID sample pair: OK'
}
finally
{
    if ($null -ne $server -and -not $server.HasExited)
    {
        $server.Kill($true)
    }
    if (Test-Path $pki)
    {
        Remove-Item $pki -Recurse -Force
    }
}
