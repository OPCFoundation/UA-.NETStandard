# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param(
    [switch]$ValidationOnly,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$PackageVersion = '0.0.0-native-validation'
)
$ErrorActionPreference = 'Stop'
if (!$ValidationOnly) {
    throw 'Product signing, final-hash SCA/VEX and minimum-host acceptance remain a required release gate.'
}
$Configuration = if ($Configuration -eq 'Debug') { 'Debug' } else { 'Release' }
if ($PackageVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+-[0-9A-Za-z][0-9A-Za-z.-]*$') {
    throw 'An explicitly prerelease version is required for unsigned validation packages.'
}
$root = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$env:PYTHONDONTWRITEBYTECODE = '1'
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts\work') | Out-Null
$env:TEMP = Join-Path $root 'artifacts\work'
$env:TMP = $env:TEMP
$env:TMPDIR = $env:TEMP
Push-Location $root
try {
    & python 'build\package.py' --version $PackageVersion --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Package allowlist preparation failed.' }
    if (!(Test-Path 'obj\project.assets.json')) {
        & dotnet restore 'Opc.Ua.Native.Http3.csproj' --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Native packaging project restore failed.' }
    }
    & dotnet pack 'Opc.Ua.Native.Http3.csproj' --configuration $Configuration --no-build `
        -p:NativeValidationOnly=true -p:PackageVersion=$PackageVersion `
        --output 'artifacts\packages' --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Native NuGet packaging failed.' }
    $packageId = 'OPCFoundation.NetStandard.Opc.Ua.Native.Http3'
    if ($Configuration -eq 'Debug') {
        $packageId += '.Debug'
    }
    & python 'build\verify.py' package --package "artifacts\packages\$packageId.$PackageVersion.nupkg"
    if ($LASTEXITCODE -ne 0) { throw 'Package content verification failed.' }
}
finally {
    Pop-Location
}
