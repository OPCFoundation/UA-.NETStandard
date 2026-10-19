# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to
# deal in the Software without restriction, including without limitation the
# rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
# sell copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included
# in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
# THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
# IN THE SOFTWARE.
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/


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
        'Scan: SUCCESS, \d+ tag\(s\)',
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
