# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$fixture = Join-Path $PSScriptRoot "obj/assurance-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
try {
    $project = 'fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj'
    $profiles = Get-Content (Join-Path $root '.azurepipelines/assurance-profiles.json') -Raw | ConvertFrom-Json
    $job = $profiles.profiles.jobs | Where-Object { $_.project -eq $project }
    foreach ($bucket in $job.corpusBuckets) {
        if ($Scenario -eq 'missing-seeds') { continue }
        $src = Join-Path $fixture "$($job.corpusRoot)/$bucket"
        $dst = Join-Path $fixture ('output/Testcases/' + $bucket.Substring(10))
        $null = New-Item -ItemType Directory -Path $src, $dst -Force
        if ($Scenario -eq 'empty-seeds') { continue }
        # Identical basenames in DIFFERENT buckets must survive copying.
        Set-Content (Join-Path $src 'seed') $bucket
        Set-Content (Join-Path $dst 'seed') $bucket
    }
    if ($Scenario -eq 'output-collision') {
        Set-Content (Join-Path $fixture 'output/Testcases/Tcp/seed') 'overwritten-by-other-bucket'
    }
    $output = Join-Path $fixture 'manifest.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/assurance-fuzz-inputs.ps1') `
        -RepoRoot $fixture -Project $project -ProfilesPath (Join-Path $root '.azurepipelines/assurance-profiles.json') `
        -BuildOutput (Join-Path $fixture 'output') -OutputPath $output
    $expected = $Scenario -in @('bucket-identity', 'empty-regressions')
    if (($LASTEXITCODE -eq 0) -ne $expected) { throw "Unexpected input validation result: $Scenario" }
    if ($expected) {
        $manifest = Get-Content $output -Raw | ConvertFrom-Json
        if ($manifest.goodInputs -ne 8 -or $manifest.regressionInputs -ne 0) {
            throw 'Bucket identity or explicitly empty regression inventory was lost.'
        }
    }
    exit 0
}
finally { Remove-Item $fixture -Recurse -Force }
