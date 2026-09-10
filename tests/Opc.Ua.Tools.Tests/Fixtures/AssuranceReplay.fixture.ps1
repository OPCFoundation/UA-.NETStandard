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
    $a = 'sha256:' + ('a' * 64)
    $b = 'sha256:' + ('b' * 64)
    $c = 'sha256:' + ('c' * 64)
    $d = 'sha256:' + ('d' * 64)
    $inputs = @(@{ id=$a; digest=$b; category='good' })
    if ($Scenario -ne 'empty-regressions') { $inputs += @{ id=$c; digest=$d; category='crash' } }
    if ($Scenario -eq 'empty-good') { $inputs = @($inputs | Where-Object category -ne good) }
    $manifest = @{schemaVersion=1;inventoryDigest=$a;inputs=$inputs;goodInputs=1;regressionInputs=$inputs.Count-1}
    $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'public-inputs.json')
    if ($Scenario -eq 'changed-copy') { $manifest.inventoryDigest = $b }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'copied-inputs.json')
    $inputXml = ($inputs | ForEach-Object { "<input id='$($_.id)' digest='$($_.digest)' category='$($_.category)' />" }) -join ''
    $executionXml = ''
    foreach ($target in @($a, $b)) {
        foreach ($input in $inputs) {
            if ($Scenario -eq 'omitted-target' -and $target -eq $b) { continue }
            if ($Scenario -eq 'omitted-input' -and $input.id -eq $c) { continue }
            $executionXml += "<execution target='$target' input='$($input.id)' />"
        }
    }
    "<replay schemaVersion='1'><targets><target id='$a'/><target id='$b'/></targets><inputs>$inputXml</inputs><executions>$executionXml</executions></replay>" |
        Set-Content (Join-Path $fixture 'fuzz-replay.xml')
    $skipped = if ($Scenario -eq 'unrelated-skip') { 1 } else { 0 }
    $extra = if ($skipped) { "<UnitTestResult testId='2' testName='OtherTest' outcome='NotExecuted'/>" } else { '' }
    @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult testId="1" outcome="Passed"/>$extra</Results>
<ResultSummary outcome="Completed"><Counters total="$(1+$skipped)" executed="1" passed="1" failed="0" notExecuted="$skipped"/></ResultSummary>
</TestRun>
"@ | Set-Content (Join-Path $fixture 'run.trx')
    $output = Join-Path $fixture 'summary.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/assurance/results.ps1') `
        -ResultsPath $fixture -Kind fuzz-replay -OutputPath $output -Enforce
    $code = $LASTEXITCODE
    if (-not (Test-Path $output)) { throw 'Replay proof not produced.' }
    $actual = Get-Content $output -Raw | ConvertFrom-Json
    $complete = $Scenario -in @('complete', 'empty-regressions')
    if (($actual.status -eq 'completed') -ne $complete -or ($code -eq 0) -ne $complete) {
        throw 'Replay execution coverage was incorrectly credited.'
    }
    if ($complete -and ($actual.counts.expectedTargets -ne 2 -or
        $actual.counts.executedInputs -ne $inputs.Count -or
        $actual.counts.expectedInputs -ne $inputs.Count)) { throw 'Wrong observed replay counters.' }
}
finally { Remove-Item -LiteralPath $fixture -Recurse -Force }
