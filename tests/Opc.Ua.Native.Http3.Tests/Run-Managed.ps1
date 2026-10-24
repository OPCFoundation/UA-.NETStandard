# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param(
    [switch]$NativeAot,
    [string]$RuntimeFrameworkVersion = '10.0.12',
    [string]$VisualStudioRoot = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise',
    [string]$WindowsSdkRoot = 'C:\Program Files (x86)\Windows Kits\10'
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force 'artifacts','artifacts\work' | Out-Null
    $env:TEMP = Join-Path $PSScriptRoot 'artifacts\work'
    $env:TMP = $env:TEMP
    $env:TMPDIR = $env:TEMP
    if ($NativeAot) {
        if (!$IsWindows) { throw 'Use Run-LinuxManaged.ps1 for the cached Linux NativeAOT builder.' }
        $vc = Join-Path $VisualStudioRoot 'VC\Tools\MSVC\14.51.36231'
        $env:PATH = "$vc\bin\Hostx64\x64;$env:PATH"
        $env:LIB = "$vc\lib\x64;$WindowsSdkRoot\Lib\10.0.26100.0\ucrt\x64;$WindowsSdkRoot\Lib\10.0.26100.0\um\x64"
        $arguments = @('publish','Opc.Ua.Native.Http3.Tests.csproj','--configuration','Release','--runtime','win-x64',
            '-p:PublishAot=true',"-p:RuntimeFrameworkVersion=$RuntimeFrameworkVersion",'-p:IlcUseEnvironmentalTools=true',
            "-p:CppLinker=$vc\bin\Hostx64\x64\link.exe","-p:CppLibCreator=$vc\bin\Hostx64\x64\lib.exe",
            '--output','artifacts\win-aot','--verbosity','quiet')
        & dotnet @arguments 2>&1 | Tee-Object 'artifacts\managed-win-aot-build.log'
        if ($LASTEXITCODE -ne 0) { throw 'Managed NativeAOT build failed.' }
        & '.\artifacts\win-aot\Opc.Ua.Native.Http3.Tests.exe' --report-trx `
            --results-directory artifacts\managed-win-aot 2>&1 | Tee-Object 'artifacts\managed-win-aot.log'
    }
    else {
        & dotnet run --project 'Opc.Ua.Native.Http3.Tests.csproj' --configuration Release -- `
            --report-trx --results-directory artifacts\managed-jit 2>&1 | Tee-Object 'artifacts\managed-jit.log'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Managed ABI tests failed.' }
}
finally {
    Pop-Location
}
