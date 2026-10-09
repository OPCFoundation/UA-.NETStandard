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
    $published = $Scenario.StartsWith('published-', [StringComparison]::Ordinal)
    $case = if ($pubSub) { $Scenario.Substring('pubsub-'.Length) } else { $Scenario }
    $area = if ($pubSub) { 'PubSub' } elseif ($published) { 'Encoders' } else { 'Network' }
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
    $publishedArguments = @{}
    if ($published) {
        $stackSource = Join-Path $fixture "$($job.stackRoot)/deep.bin"
        $stackOutput = Join-Path $fixture 'output/StackTestcases/deep.bin'
        foreach ($file in @($stackSource, $stackOutput)) {
            $null = New-Item -ItemType Directory -Path (Split-Path $file) -Force
            Set-Content -LiteralPath $file 'synthetic-nested-input'
        }
        & git -C $fixture init --quiet
        & git -C $fixture add -- fuzzing
        & git -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m 'Synthetic seeds'
        if ($LASTEXITCODE -ne 0) { throw 'Could not freeze fixture seeds.' }
        $checkout = Join-Path $fixture 'published-corpus'
        $assets = Join-Path $checkout "Opc.Ua.$area.Fuzz.Tests/Assets"
        $null = New-Item -ItemType Directory -Path $assets -Force
        Set-Content -LiteralPath (Join-Path $assets 'crash-fixture') 'synthetic-crash'
        & git -C $checkout init --quiet
        & git -C $checkout add -- .
        & git -C $checkout -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m 'Synthetic corpus'
        if ($LASTEXITCODE -ne 0) { throw 'Could not freeze fixture corpus.' }
        $commit = (& git -C $checkout rev-parse HEAD).Trim()
        foreach ($target in @((Join-Path $fixture $job.regressionRoot), (Join-Path $fixture 'output/Assets'))) {
            $null = New-Item -ItemType Directory -Path $target -Force
            Copy-Item -LiteralPath (Join-Path $assets 'crash-fixture') -Destination $target
        }
        switch ($case) {
            'published-wrong-commit' { $commit = 'a' * 40 }
            'published-modified-checkout' { Set-Content (Join-Path $assets 'crash-fixture') 'modified' }
            'published-untracked-checkout' { Set-Content (Join-Path $assets 'crash-extra') 'untracked' }
            'published-missing-overlay' { Remove-Item (Join-Path $fixture "$($job.regressionRoot)/crash-fixture") }
            'published-altered-overlay' { Set-Content (Join-Path $fixture "$($job.regressionRoot)/crash-fixture") 'changed' }
            'published-unapproved-overlay' { Set-Content (Join-Path $fixture "$($job.regressionRoot)/crash-extra") 'untracked' }
            'published-wrong-copy' { Set-Content (Join-Path $fixture 'output/Assets/crash-fixture') 'changed' }
            'published-missing-stack' { Remove-Item -LiteralPath $stackOutput }
        }
        $publishedArguments = @{
            RequireCommitted = $true
            PublicCorpusCheckout = $checkout
            PublicCorpusCommit = $commit
        }
    }
    $output = Join-Path $fixture 'manifest.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/assurance/fuzz-inputs.ps1') `
        -RepoRoot $fixture -Project $project -ProfilesPath (Join-Path $root '.azurepipelines/assurance/profiles.json') `
        -BuildOutput (Join-Path $fixture 'output') -OutputPath $output @publishedArguments
    $expected = $case -in @('bucket-identity', 'empty-regressions', 'published-complete')
    if (($LASTEXITCODE -eq 0) -ne $expected) { throw "Unexpected input validation result: $Scenario" }
    if ($expected) {
        $manifest = Get-Content $output -Raw | ConvertFrom-Json
        $expectedInputs = if ($pubSub) { 3 } elseif ($published) { 5 } else { 8 }
        $regressions = if ($published) { 2 } else { 0 }
        if ($manifest.goodInputs -ne $expectedInputs -or $manifest.regressionInputs -ne $regressions -or
            @($manifest.inputs | Select-Object -ExpandProperty id -Unique).Count -ne $expectedInputs + $regressions) {
            throw 'Bucket identity or explicitly empty regression inventory was lost.'
        }
        if ($published -and ($manifest.publicCorpusCommit -cne $commit -or
            $manifest.publicCorpusInputs -ne 1 -or
            @($manifest.inputs | Where-Object category -eq stack).Count -ne 1)) {
            throw 'Published provenance or stack-input scope was lost.'
        }
    }
    exit 0
}
finally { Remove-Item $fixture -Recurse -Force }
