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
$fixture = Join-Path (Split-Path $PSScriptRoot) "obj/assurance-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
try {
    $pubSub = $Scenario.StartsWith('pubsub-', [StringComparison]::Ordinal)
    $case = if ($pubSub) { $Scenario.Substring('pubsub-'.Length) } else { $Scenario }
    $area = if ($pubSub) { 'PubSub' } else { 'Network' }
    $project = "fuzzing/Opc.Ua.$area.Fuzz.Tests/Opc.Ua.$area.Fuzz.Tests.csproj"
    $profiles = Get-Content (Join-Path $root '.azurepipelines/assurance/profiles.json') -Raw | ConvertFrom-Json
    $definitions = @($profiles.profiles.jobs) + @($profiles.additionalReplayProjects)
    $jobs = @($definitions | Where-Object { $_.project -eq $project })
    if ($jobs.Count -ne 1) { throw 'A selected public replay project has no unique input definition.' }
    $job = $jobs[0]
    if ($pubSub) {
        if ($project -in $profiles.profiles.jobs.project -or @($profiles.profiles.jobs).Count -ne 7) {
            throw 'Baseline PubSub replay must not change the seven-job release contract.'
        }
        if ((($job.corpusBuckets | Sort-Object) -join ',') -cne 'Testcases.Chunks,Testcases.Json,Testcases.Uadp') {
            throw 'PubSub must retain all three public seed buckets.'
        }
    }
    foreach ($bucket in $job.corpusBuckets) {
        if ($case -eq 'missing-seeds') { continue }
        $src = Join-Path $fixture "$($job.corpusRoot)/$bucket"
        $dst = Join-Path $fixture ('output/Testcases/' + $bucket.Substring(10))
        $null = New-Item -ItemType Directory -Path $src, $dst -Force
        if ($case -eq 'empty-seeds') { continue }
        # Identical basenames in DIFFERENT buckets must survive copying.
        Set-Content (Join-Path $src 'seed') $bucket
        Set-Content (Join-Path $dst 'seed') $bucket
    }
    if ($case -eq 'output-collision') {
        $bucket = if ($pubSub) { 'Json' } else { 'Tcp' }
        Set-Content (Join-Path $fixture "output/Testcases/$bucket/seed") 'overwritten-by-other-bucket'
    }
    $output = Join-Path $fixture 'manifest.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/assurance/fuzz-inputs.ps1') `
        -RepoRoot $fixture -Project $project -ProfilesPath (Join-Path $root '.azurepipelines/assurance/profiles.json') `
        -BuildOutput (Join-Path $fixture 'output') -OutputPath $output
    $expected = $case -in @('bucket-identity', 'empty-regressions')
    if (($LASTEXITCODE -eq 0) -ne $expected) { throw "Unexpected input validation result: $Scenario" }
    if ($expected) {
        $manifest = Get-Content $output -Raw | ConvertFrom-Json
        $expectedInputs = if ($pubSub) { 3 } else { 8 }
        if ($manifest.goodInputs -ne $expectedInputs -or $manifest.regressionInputs -ne 0 -or
            @($manifest.inputs | Select-Object -ExpandProperty id -Unique).Count -ne $expectedInputs) {
            throw 'Bucket identity or explicitly empty regression inventory was lost.'
        }
    }
    exit 0
}
finally { Remove-Item $fixture -Recurse -Force }
