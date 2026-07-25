# ========================================================================
# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person
# obtaining a copy of this software and associated documentation
# files (the "Software"), to deal in the Software without
# restriction, including without limitation the rights to use,
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the
# Software is furnished to do so, subject to the following
# conditions:
#
# The above copyright notice and this permission notice shall be
# included in all copies or substantial portions of the Software.
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
# EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
# OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
# NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
# HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/
# ========================================================================

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $ResultsDirectory,
    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$mcpGradeCommit = 'feff7162903ab9fbe92f182be9a082fcb24c9bbc'
$repositoryRoot = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$projectPath = Join-Path $PSScriptRoot 'Opc.Ua.Mcp.csproj'
$mcpGradeConfig = Join-Path $repositoryRoot '.mcpgraderc.json'
if ([string]::IsNullOrWhiteSpace($ResultsDirectory))
{
    $ResultsDirectory = Join-Path $repositoryRoot 'artifacts/mcpgrade'
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("opcua-mcpgrade-" + [Guid]::NewGuid().ToString('N'))
$graderRoot = Join-Path $temporaryRoot 'mcpgrade'

function Invoke-CheckedNative
{
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,
        [Parameter(ValueFromRemainingArguments)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "'$FilePath' exited with code $LASTEXITCODE."
    }
}

function Get-AvailableTcpPort
{
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try
    {
        return ([Net.IPEndPoint] $listener.LocalEndpoint).Port
    }
    finally
    {
        $listener.Stop()
    }
}

function Start-McpHttpServer
{
    param(
        [Parameter(Mandatory)]
        [string] $Profile,
        [Parameter(Mandatory)]
        [string] $AssemblyPath,
        [Parameter(Mandatory)]
        [string] $LogPrefix
    )

    $port = Get-AvailableTcpPort
    $stdoutPath = Join-Path $ResultsDirectory "$LogPrefix.stdout.log"
    $stderrPath = Join-Path $ResultsDirectory "$LogPrefix.stderr.log"
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        $AssemblyPath,
        '--transport',
        'http',
        '--profile',
        $Profile,
        '--port',
        $port
    ) -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline)
    {
        if ($process.HasExited)
        {
            throw "MCP $Profile server exited with code $($process.ExitCode). See $stderrPath."
        }

        $client = [Net.Sockets.TcpClient]::new()
        try
        {
            $client.Connect([Net.IPAddress]::Loopback, $port)
            return @{
                Process = $process
                Url = "http://127.0.0.1:$port/mcp"
            }
        }
        catch [Net.Sockets.SocketException]
        {
            Start-Sleep -Milliseconds 100
        }
        finally
        {
            $client.Dispose()
        }
    }

    Stop-Process -Id $process.Id
    throw "MCP $Profile server did not listen within 30 seconds. See $stderrPath."
}

function Stop-McpHttpServer
{
    param(
        [Parameter(Mandatory)]
        [Diagnostics.Process] $Process
    )

    if (-not $Process.HasExited)
    {
        Stop-Process -Id $Process.Id
        $Process.WaitForExit()
    }
    $Process.Dispose()
}

function Invoke-McpGrade
{
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,
        [Parameter(Mandatory)]
        [string] $OutputPath
    )

    & node (Join-Path $graderRoot 'dist/cli.js') @Arguments --json --config $mcpGradeConfig |
        Out-File -FilePath $OutputPath -Encoding utf8
    if ($LASTEXITCODE -ne 0)
    {
        throw "mcpgrade exited with code $LASTEXITCODE."
    }
}

function Get-McpGradeFindings
{
    param(
        [Parameter(Mandatory)]
        [object] $Report
    )

    return @($Report.categories | ForEach-Object { @($_.findings) })
}

New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try
{
    if (-not $SkipBuild)
    {
        Invoke-CheckedNative -FilePath 'dotnet' -Arguments @(
            'build',
            $projectPath,
            '--configuration',
            $Configuration,
            '--framework',
            'net10.0',
            '-p:CustomTestTarget=net10.0',
            '-p:NuGetAudit=false'
        )
    }

    $assemblyPath = Join-Path $PSScriptRoot "bin/$Configuration/net10.0/Opc.Ua.Mcp.dll"
    if (-not (Test-Path $assemblyPath -PathType Leaf))
    {
        throw "MCP assembly not found at '$assemblyPath'."
    }

    Invoke-CheckedNative -FilePath 'git' -Arguments @(
        'clone',
        '--quiet',
        'https://github.com/TengByte/mcpgrade.git',
        $graderRoot
    )
    Invoke-CheckedNative -FilePath 'git' -Arguments @(
        '-C',
        $graderRoot,
        'checkout',
        '--quiet',
        $mcpGradeCommit
    )
    $checkedOutCommit = (& git -C $graderRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $checkedOutCommit -ne $mcpGradeCommit)
    {
        throw "Expected mcpgrade commit $mcpGradeCommit, found '$checkedOutCommit'."
    }
    Push-Location $graderRoot
    try
    {
        Invoke-CheckedNative -FilePath 'npm' -Arguments @(
            'ci',
            '--ignore-scripts',
            '--no-audit',
            '--no-fund'
        )
        Invoke-CheckedNative -FilePath 'npm' -Arguments @('run', 'build', '--silent')
    }
    finally
    {
        Pop-Location
    }

    foreach ($profile in @('core', 'full'))
    {
        $server = Start-McpHttpServer -Profile $profile -AssemblyPath $assemblyPath `
            -LogPrefix "$profile-server"
        try
        {
            Invoke-McpGrade -Arguments @($server.Url) `
                -OutputPath (Join-Path $ResultsDirectory "$profile-static.json")
        }
        finally
        {
            Stop-McpHttpServer -Process $server.Process
        }
    }

    Push-Location (Split-Path $assemblyPath)
    try
    {
        Invoke-McpGrade -Arguments @(
            '--stdio',
            'dotnet Opc.Ua.Mcp.dll --profile full',
            '--probe'
        ) -OutputPath (Join-Path $ResultsDirectory 'full-probe.json')
    }
    finally
    {
        Pop-Location
    }

    $coreReport = Get-Content (Join-Path $ResultsDirectory 'core-static.json') -Raw |
        ConvertFrom-Json
    $coreBlocking = @(Get-McpGradeFindings $coreReport |
        Where-Object { $_.severity -in @('error', 'warn') })
    if ($coreReport.totalScore -lt 98 -or $coreBlocking.Count -ne 0)
    {
        throw "Core profile grade is $($coreReport.totalScore) with $($coreBlocking.Count) blocking finding(s)."
    }

    $fullReport = Get-Content (Join-Path $ResultsDirectory 'full-static.json') -Raw |
        ConvertFrom-Json
    $fullUnexpected = @(Get-McpGradeFindings $fullReport |
        Where-Object {
            $_.severity -in @('error', 'warn') -and
            $_.ruleId -notin @('T001', 'S007')
        })
    if ($fullReport.totalScore -lt 95 -or $fullUnexpected.Count -ne 0)
    {
        throw "Full profile grade is $($fullReport.totalScore) with $($fullUnexpected.Count) unexpected finding(s)."
    }

    $probeReport = Get-Content (Join-Path $ResultsDirectory 'full-probe.json') -Raw |
        ConvertFrom-Json
    $probeFailures = @(Get-McpGradeFindings $probeReport |
        Where-Object { $_.ruleId -in @('C003', 'C004') })
    if ($probeFailures.Count -ne 0)
    {
        throw "Full-profile probe reported $($probeFailures.Count) C003/C004 finding(s)."
    }

    Write-Host "Core grade: $($coreReport.grade) / $($coreReport.totalScore)"
    Write-Host "Full grade: $($fullReport.grade) / $($fullReport.totalScore)"
    Write-Host "Probe C003/C004 findings: 0"
}
finally
{
    if (Test-Path $temporaryRoot)
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
