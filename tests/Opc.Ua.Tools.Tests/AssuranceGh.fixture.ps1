# Copyright (c) OPC Foundation. Licensed under the MIT License.
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
