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
$fixture = Get-Content -LiteralPath $env:ASSURANCE_CODEQL_FIXTURE -Raw | ConvertFrom-Json
$normalized = @()
for ($index = 0; $index -lt $args.Count; $index++) {
    $argument = $args[$index]
    if ($argument -cmatch '^--(database|output)=[A-Za-z]$' -and
        $index + 1 -lt $args.Count -and $args[$index + 1].StartsWith('\')) {
        $argument += ':' + $args[++$index]
    }
    $normalized += $argument
}
$args = $normalized
ConvertTo-Json -InputObject @($args) -Compress |
    Add-Content -LiteralPath (Join-Path (Split-Path $env:ASSURANCE_CODEQL_FIXTURE) 'commands.jsonl')
if ($fixture.scenario -eq 'command-timeout') { Start-Sleep -Seconds 30; exit 1 }
$command = $args[0..1] -join ' '
switch ($command) {
    'version --format=json' { @{ version = '2.23.0' } | ConvertTo-Json -Compress }
    'resolve database' { @{ sourceLocationPrefix = $fixture.root; finalized = $true } | ConvertTo-Json -Compress }
    'resolve queries' { ConvertTo-Json -InputObject @($fixture.query) -Compress }
    'query run' {
        if ('--threads=2' -notin $args -or '--ram=2048' -notin $args -or '--timeout=120' -notin $args) { exit 2 }
        $path = ($args | Where-Object { $_.StartsWith('--output=') }).Substring(9)
        [IO.File]::WriteAllText($path, 'synthetic-query-result')
    }
    'bqrs decode' {
        $path = ($args | Where-Object { $_.StartsWith('--output=') }).Substring(9)
        $status = if ($fixture.scenario -eq 'incomplete-extraction') { 'incomplete' } else { 'completed' }
        @{ '#select' = @{ tuples = ,@($fixture.root, "/out:$($fixture.assembly)", $fixture.source, $status, 0, 0) } } |
            ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path
    }
    default { exit 2 }
}
exit 0
