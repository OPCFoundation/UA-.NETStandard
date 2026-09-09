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

param([Parameter(Mandatory)][string] $Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$fixture = Join-Path (Split-Path $PSScriptRoot) "obj\assurance-native-producer-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
$savedPath = $env:PATH
$savedFixture = $env:ASSURANCE_NATIVE_FIXTURE
try {
    $apphost = (Get-Command dotnet -CommandType Application).Source
    $rid = if ($Scenario -eq 'process-crash') { 'linux-x64' } else { 'win-x64' }
    $assemblyName = switch ($Scenario) {
        'historian-apphost' { 'Opc.Ua.Aot.Tests.Historian' }
        'mcp-apphost' { 'Opc.Ua.Aot.Tests.Mcp' }
        default { 'Opc.Ua.Aot.Tests' }
    }
    $project = "tests\$assemblyName\$assemblyName.csproj"
    $env:ASSURANCE_NATIVE_FIXTURE = Join-Path $fixture 'fixture.json'
    $assets = Join-Path $fixture 'assets.json'
    @{ scenario = $Scenario; apphost = $apphost; assets = $assets; rid = $rid; assemblyName = $assemblyName } |
        ConvertTo-Json | Set-Content -LiteralPath $env:ASSURANCE_NATIVE_FIXTURE
    @{
        libraries = @{ "runtime.$rid.Microsoft.DotNet.ILCompiler/10.0.11" = @{ path = 'compiler' } }
        packageFolders = @{ $fixture = @{} }
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $assets
    if ($Scenario -ne 'compiler-missing') {
        $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'compiler\tools')
        'synthetic compiler identity, never executed' |
            Set-Content -LiteralPath (Join-Path $fixture 'compiler\tools\ilc.exe')
    }
    $pwsh = (Get-Process -Id $PID).Path
    "@echo off`r`n`"$pwsh`" -NoProfile -File `"$PSScriptRoot\AssuranceNativeCli.fixture.ps1`" %*`r`n" |
        Set-Content -LiteralPath (Join-Path $fixture 'dotnet.cmd') -Encoding ascii
    $env:PATH = $fixture + [IO.Path]::PathSeparator + $savedPath
    if ((Get-Command dotnet -CommandType Application | Select-Object -First 1).Source -notlike "$fixture*") {
        throw 'Fake publisher not selected; live publication is forbidden.'
    }
    $work = Join-Path $fixture 'work'
    $output = Join-Path $fixture 'proof.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines\assurance-native.ps1') `
        -Project $project -RuntimeIdentifier $rid `
        -WorkDirectory $work -OutputPath $output -NoRestore -TimeoutSeconds 5
    if ($LASTEXITCODE -eq 0) { throw 'Invalid native producer unexpectedly succeeded.' }
    $proof = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $reason = switch ($Scenario) {
        'apphost' { 'NATIVE_AOT_HEADER_MISSING' }
        'historian-apphost' { 'NATIVE_AOT_HEADER_MISSING' }
        'mcp-apphost' { 'NATIVE_AOT_HEADER_MISSING' }
        'publish-failure' { 'NATIVE_PUBLISH_FAILED' }
        'compiler-missing' { 'NATIVE_COMPILER_IDENTITY_MISSING' }
        'process-crash' { 'NATIVE_CRASHED_BEFORE_REPORT' }
        default { throw 'Unknown fixture scenario.' }
    }
    if ($proof.status -cne 'failed' -or $reason -cnotin $proof.reasons) {
        throw "Wrong native failure proof: $($proof | ConvertTo-Json -Depth 20)"
    }
    $original = (Get-FileHash -LiteralPath $output).Hash
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines\assurance-native.ps1') `
        -Project $project -RuntimeIdentifier $rid `
        -WorkDirectory $work -OutputPath $output -NoRestore -TimeoutSeconds 5 2>$null
    if ($LASTEXITCODE -eq 0 -or (Get-FileHash -LiteralPath $output).Hash -cne $original) {
        throw 'Reused native work directory was not rejected without overwriting evidence.'
    }
}
finally {
    $env:PATH = $savedPath
    $env:ASSURANCE_NATIVE_FIXTURE = $savedFixture
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
