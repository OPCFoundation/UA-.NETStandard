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
    Copy-Item -LiteralPath $fixture.apphost -Destination (Join-Path $output "$($fixture.assemblyName).exe")
    exit 0
}
if ($args[0] -eq 'msbuild') {
    @{
        Properties = @{
            PublishAot = 'true'; Configuration = 'Release'; TargetFramework = 'net10.0'
            RuntimeIdentifier = $fixture.rid; NETCoreSdkVersion = '10.0.303'; ProjectAssetsFile = $fixture.assets
            AssemblyName = $fixture.assemblyName
        }
    } | ConvertTo-Json -Depth 10
    exit 0
}
exit 16
