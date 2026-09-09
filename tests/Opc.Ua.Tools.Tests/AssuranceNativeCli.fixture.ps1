# Copyright (c) OPC Foundation. Licensed under the MIT License.
$ErrorActionPreference = 'Stop'
$fixture = Get-Content -LiteralPath $env:ASSURANCE_NATIVE_FIXTURE -Raw | ConvertFrom-Json
$normalized = @()
for ($index = 0; $index -lt $args.Count; $index++) {
    $argument = $args[$index]
    if ($argument -eq '-p' -and $index + 1 -lt $args.Count) { $argument += ':' + $args[++$index] }
    $normalized += $argument
}
$args = $normalized
if ($args[0] -eq 'publish') {
    if ($fixture.scenario -eq 'publish-failure') { exit 12 }
    if ('-r' -notin $args -or $fixture.rid -notin $args -or '-p:PublishAot=true' -notin $args -or
        '-f' -notin $args -or 'net10.0' -notin $args -or '--no-restore' -notin $args) { exit 13 }
    $index = [Array]::IndexOf($args, '-o')
    if ($index -lt 0) { exit 14 }
    $output = $args[$index + 1]
    if (Test-Path -LiteralPath $output) { exit 15 }
    $null = New-Item -ItemType Directory -Path $output
    Copy-Item -LiteralPath $fixture.apphost -Destination (Join-Path $output 'Opc.Ua.Aot.Tests.exe')
    exit 0
}
if ($args[0] -eq 'msbuild') {
    @{
        Properties = @{
            PublishAot = 'true'; Configuration = 'Release'; TargetFramework = 'net10.0'
            RuntimeIdentifier = $fixture.rid; NETCoreSdkVersion = '10.0.303'; ProjectAssetsFile = $fixture.assets
        }
    } | ConvertTo-Json -Depth 10
    exit 0
}
exit 16
