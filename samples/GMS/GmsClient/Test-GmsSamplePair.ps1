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

param(
    [Parameter(Mandatory = $true)]
    [string] $ServerExecutable,
    [Parameter(Mandatory = $true)]
    [string] $ClientExecutable,
    [string] $EvidenceDirectory = 'artifacts\gms-sample-acceptance',
    [ValidateRange(20, 120)]
    [int] $ServerSeconds = 60,
    [switch] $Advanced,
    [switch] $AllChecks,
    [switch] $AssertNative
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$serverPath = (Resolve-Path $ServerExecutable).Path
$clientPath = (Resolve-Path $ClientExecutable).Path
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory, (Get-Location).Path)
$evidence = [IO.Path]::GetRelativePath($root, $evidencePath)
$pki = Join-Path 'artifacts\gms-sample-owned-pki' ([Guid]::NewGuid().ToString('N'))
$results = [Collections.Generic.List[object]]::new()
$server = $null
$port = 0
$forcedStops = 0
$failure = $null
$summary = [ordered]@{
    advanced = [bool]$Advanced
    nativeRequired = [bool]$AssertNative
    serverExitCode = $null
    naturalServerExit = $false
    listenersAfter = $null
    forcedOwnedProcessStops = 0
    pkiRemoved = $false
    cases = $results
    failure = $null
}

function Assert-Output([string] $Text, [string[]] $Patterns)
{
    foreach ($pattern in $Patterns)
    {
        if ($Text -notmatch $pattern)
        {
            throw "Required output not found: $pattern"
        }
    }
}

function Invoke-ClientCase([string] $Name, [string[]] $ClientArguments, [int] $Expected, [string[]] $Patterns)
{
    $out = Join-Path $evidence "$Name.out.log"
    $err = Join-Path $evidence "$Name.err.log"
    $quoted = @($ClientArguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' })
    "& '$clientPath' $($quoted -join ' ')" | Add-Content (Join-Path $evidence 'commands.txt')
    $client = Start-Process -FilePath $clientPath -ArgumentList $quoted -PassThru -NoNewWindow `
        -RedirectStandardOutput $out -RedirectStandardError $err
    try
    {
        if (-not $client.WaitForExit(130000))
        {
            throw "Client case '$Name' exceeded its cleanup deadline."
        }
        $client.Refresh()
        $text = (Get-Content $out -Raw) + (Get-Content $err -Raw)
        $results.Add([ordered]@{ name = $Name; expectedExit = $Expected; actualExit = $client.ExitCode })
        if ($client.ExitCode -ne $Expected)
        {
            throw "Client case '$Name' returned $($client.ExitCode), expected $Expected. See $out and $err."
        }
        Assert-Output $text $Patterns
        if ($AssertNative -and $Expected -eq 0)
        {
            Assert-Output $text @('RUNTIME dynamicCodeSupported=False')
        }
        Write-Output "CASE $Name exit=$($client.ExitCode) expected=$Expected"
    }
    finally
    {
        if (-not $client.HasExited)
        {
            Stop-Process -Id $client.Id -Force
            $script:forcedStops++
            $null = $client.WaitForExit(10000)
        }
        $client.Dispose()
    }
}

Push-Location $root
try
{
    New-Item -ItemType Directory -Force -Path $evidence | Out-Null
    if ($AllChecks)
    {
        Invoke-ClientCase 'remote-demo-refusal' `
            @('--endpoint', 'opc.tcp://example.invalid:4840/GmsServer', '--demo-trust') 1 `
            @('--demo-trust is restricted to loopback')
        Invoke-ClientCase 'missing-confirmation' `
            @('--system', 'Gms1', '--acknowledge', 'Gms1-F-0000', '--demo-trust') 1 `
            @('--acknowledge requires an explicit --system and --confirm-acknowledgement')
        Invoke-ClientCase 'missing-target' `
            @('--acknowledge', 'Gms1-F-0000', '--confirm-acknowledgement', '--demo-trust') 1 `
            @('--acknowledge requires an explicit --system and --confirm-acknowledgement')
        Invoke-ClientCase 'count-bound-refusal' @('--count', '65') 1 @('Expected an integer in \[1, 64\]')
    }
    $arguments = @('--port', '0', '--seconds', "$ServerSeconds", '--instances', '2',
        '--demo-trust', '--pki', (Join-Path $pki 'server'))
    if ($Advanced)
    {
        $arguments += '--advanced'
    }
    $quoted = @($arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' })
    "& '$serverPath' $($quoted -join ' ')" | Add-Content (Join-Path $evidence 'commands.txt')
    $serverOut = Join-Path $evidence 'server.out.log'
    $serverErr = Join-Path $evidence 'server.err.log'
    $server = Start-Process -FilePath $serverPath -ArgumentList $quoted -PassThru -NoNewWindow `
        -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr
    $startup = [Diagnostics.Stopwatch]::StartNew()
    $endpoint = $null
    while ($startup.Elapsed.TotalSeconds -lt 40)
    {
        if ($server.HasExited)
        {
            throw "The server exited before READY. See $serverOut and $serverErr."
        }
        $text = Get-Content $serverOut -Raw
        if ($text -match '(?m)^READY (opc\.tcp://127\.0\.0\.1:(\d+)/GmsServer)\r?$')
        {
            $endpoint = $Matches[1]
            $port = [int]$Matches[2]
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $endpoint)
    {
        throw 'No actual OPC UA readiness signal arrived within the startup bound.'
    }
    $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    if ($listeners.Count -ne 1 -or $listeners[0].LocalAddress -ne '127.0.0.1' -or
        $listeners[0].OwningProcess -ne $server.Id)
    {
        throw 'The ready sample must have exactly its own numeric loopback listener.'
    }
    Write-Output "READY_VERIFIED $endpoint pid=$($server.Id) loopbackListeners=1"
    $common = @('--endpoint', $endpoint, '--pki', (Join-Path $pki 'client'), '--demo-trust')
    $observe = $common + @('--seconds', '4', '--count', '2', '--events', '--download')
    $patterns = @('DISCOVERED 2 native GMS', 'SNAPSHOT .*Gms1', 'SNAPSHOT .*Gms2',
        'RESULT_QUERY .*count=[12]', 'RESULT_READ latest=', 'OBSERVED_DATA .*count=2',
        'OBSERVED_EVENTS count=[1-9]', 'OBSERVATION_CLEANUP before=0 after=0',
        'SESSION_CLOSED connected=False disposed=True', 'CLIENT_COMPLETE')
    if ($Advanced)
    {
        $observe += @('--characteristic', 'Bore')
        $patterns += @('ENVIRONMENTAL_SENSOR', 'SENSOR_WARNING .*input=', 'CHARACTERISTIC .*Bore .*value=',
            'DOWNLOAD Gms1-', 'DOWNLOAD Gms2-', 'CORRECTIONS count=1', 'INTERMEDIATE_READY')
    }
    else
    {
        $patterns += @('OPTIONAL LoadingMonitoring absent', 'OPTIONAL Corrections folder absent',
            'OPTIONAL file transfer')
    }
    Invoke-ClientCase 'observe' $observe 0 $patterns
    if ($Advanced -and $AllChecks)
    {
        Invoke-ClientCase 'changing-data' `
            ($common + @('--system', 'Gms1', '--seconds', '4', '--count', '32')) 0 `
            @('OBSERVED_DATA .*count=(2[0-9]|3[0-2])', 'OBSERVATION_CLEANUP before=0 after=0',
                'SESSION_CLOSED connected=False disposed=True')
        $data = Get-Content (Join-Path $evidence 'changing-data.out.log') -Raw
        $nodes = [regex]::Matches($data, '(?m)^DATA node=(\S+) sequence=\d+')
        $repeated = @($nodes | ForEach-Object { $_.Groups[1].Value } | Group-Object |
            Where-Object { $_.Count -gt 1 })
        if ($repeated.Count -lt 1)
        {
            throw 'The data observation must contain a later notification for an already monitored variable.'
        }
        Invoke-ClientCase 'cleanup' `
            ($common + @('--system', 'Gms1', '--seconds', '4', '--count', '2', '--download', '--verify-cleanup')) 0 `
            @('EXPECTED_MISSING_RESULT', 'EXPECTED_FILE_REFUSAL', 'EXPECTED_DOWNLOAD_CANCELLATION',
                'HANDLE_REUSE_VERIFIED', 'EXPECTED_OBSERVATION_CANCELLATION',
                'OBSERVATION_CLEANUP before=0 after=0', 'SESSION_CLOSED connected=False disposed=True')
        Invoke-ClientCase 'acknowledge' `
            ($common + @('--system', 'Gms1', '--seconds', '0', '--acknowledge', 'Gms1-F-0000',
                '--confirm-acknowledgement')) 0 `
            @('ACKNOWLEDGED system=.*Gms1 result=Gms1-F-0000 domainCode=0',
                'SESSION_CLOSED connected=False disposed=True')
        Invoke-ClientCase 'cross-instance-refusal' `
            ($common + @('--system', 'Gms2', '--seconds', '0', '--acknowledge', 'Gms1-F-0000',
                '--confirm-acknowledgement')) 1 `
            @('GmsResultDomainException', 'GetResultById', 'SESSION_CLOSED connected=False disposed=True')
        Invoke-ClientCase 'file-byte-refusal' `
            ($common + @('--system', 'Gms1', '--seconds', '0', '--download', '--max-file-bytes', '1')) 1 `
            @('BadEncodingLimitsExceeded', 'SESSION_CLOSED connected=False disposed=True')
    }
    if ($AllChecks)
    {
        Invoke-ClientCase 'certificate-trust-refusal' `
            @('--endpoint', $endpoint, '--pki', (Join-Path $pki 'untrusted-client'), '--seconds', '0') 1 `
            @('BadCertificateUntrusted|Certificate is not trusted')
    }
    if (-not $server.WaitForExit(($ServerSeconds + 30) * 1000))
    {
        throw 'The server did not exit naturally within its lifetime and cleanup bounds.'
    }
    $server.Refresh()
    $summary.serverExitCode = $server.ExitCode
    $summary.naturalServerExit = $true
    $text = (Get-Content $serverOut -Raw) + (Get-Content $serverErr -Raw)
    Assert-Output $text @('RELEASED Gms1', 'RELEASED Gms2', 'STOPPED: simulation awaited')
    if ($AssertNative)
    {
        Assert-Output $text @('RUNTIME dynamicCodeSupported=False')
    }
    if ($server.ExitCode -ne 0)
    {
        throw "Server exit code: $($server.ExitCode)"
    }
}
catch
{
    $failure = $_
    $summary.failure = $_.ToString()
}
finally
{
    if ($null -ne $server)
    {
        if (-not $server.HasExited)
        {
            Stop-Process -Id $server.Id -Force
            $forcedStops++
            $null = $server.WaitForExit(10000)
        }
        $server.Dispose()
    }
    if ($port -ne 0)
    {
        $summary.listenersAfter = @(Get-NetTCPConnection -LocalPort $port -State Listen `
            -ErrorAction SilentlyContinue).Count
    }
    if (Test-Path $pki)
    {
        Remove-Item -LiteralPath $pki -Recurse -Force
    }
    $summary.pkiRemoved = -not (Test-Path $pki)
    $summary.forcedOwnedProcessStops = $forcedStops
    $summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'summary.json')
    Pop-Location
}
if ($null -ne $failure)
{
    throw $failure
}
if ($summary.listenersAfter -ne 0 -or $forcedStops -ne 0 -or -not $summary.pkiRemoved)
{
    throw 'Acceptance failed: listeners, forced process termination or owned PKI cleanup.'
}
$summary | ConvertTo-Json -Depth 5
