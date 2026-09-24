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

# Fake gh transport only. No publisher validation or collector is replaced.
$ErrorActionPreference = 'Stop'
if ($args.Count -ne 6 -or ($args[0..4] -join ' ') -cne 'api --hostname github.com --method GET') { exit 2 }
$responses = Get-Content -LiteralPath $env:ASSURANCE_STUB_RESPONSES -Raw | ConvertFrom-Json -AsHashtable
$endpoint = $args[5]
if (-not $responses.ContainsKey($endpoint)) { exit 3 }
$response = $responses[$endpoint]
if ($response -is [array]) {
    if ($response.Count -eq 0) { exit 4 }
    $path = $response[0]
    if ($response.Count -gt 1) {
        $responses[$endpoint] = @($response | Select-Object -Skip 1)
        $responses | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $env:ASSURANCE_STUB_RESPONSES -Encoding utf8
    }
}
else { $path = $response }
$bytes = [IO.File]::ReadAllBytes($path)
$stdout = [Console]::OpenStandardOutput()
$stdout.Write($bytes, 0, $bytes.Length)
$stdout.Flush()
exit 0
