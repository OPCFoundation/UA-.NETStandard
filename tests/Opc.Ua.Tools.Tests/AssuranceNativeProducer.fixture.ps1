# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$fixture = Join-Path $PSScriptRoot "obj\assurance-native-producer-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
$savedPath = $env:PATH
$savedFixture = $env:ASSURANCE_NATIVE_FIXTURE
try {
    $apphost = (Get-Command dotnet -CommandType Application).Source
    $rid = if ($Scenario -eq 'process-crash') { 'linux-x64' } else { 'win-x64' }
    $env:ASSURANCE_NATIVE_FIXTURE = Join-Path $fixture 'fixture.json'
    $assets = Join-Path $fixture 'assets.json'
    @{ scenario = $Scenario; apphost = $apphost; assets = $assets; rid = $rid } |
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
        -Project 'tests\Opc.Ua.Aot.Tests\Opc.Ua.Aot.Tests.csproj' -RuntimeIdentifier $rid `
        -WorkDirectory $work -OutputPath $output -NoRestore -TimeoutSeconds 5
    if ($LASTEXITCODE -eq 0) { throw 'Invalid native producer unexpectedly succeeded.' }
    $proof = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $reason = switch ($Scenario) {
        'apphost' { 'NATIVE_AOT_HEADER_MISSING' }
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
        -Project 'tests\Opc.Ua.Aot.Tests\Opc.Ua.Aot.Tests.csproj' -RuntimeIdentifier $rid `
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
