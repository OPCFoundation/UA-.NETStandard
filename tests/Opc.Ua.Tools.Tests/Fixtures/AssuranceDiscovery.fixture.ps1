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
    $projects = @('tests/Opc.Ua.ReleaseEvidence.Tests/Opc.Ua.ReleaseEvidence.Tests.csproj')
    foreach ($area in @('Encoders', 'Certificates', 'Network')) {
        if ($Scenario -eq 'missing-fuzz-project' -and $area -eq 'Network') { continue }
        $projects += "fuzzing/Opc.Ua.$area.Fuzz.Tests/Opc.Ua.$area.Fuzz.Tests.csproj"
    }
    foreach ($project in $projects) {
        $path = Join-Path $fixture $project
        $null = New-Item -ItemType Directory -Path (Split-Path $path) -Force
        Set-Content $path '<Project />'
    }
    $change = switch ($Scenario) {
        'corpus-change' { 'fuzzing/Opc.Ua.Network.Fuzz.Corpus/Testcases.Tcp/seed' }
        'dictionary-change' { 'fuzzing/Opc.Ua.Network.Fuzz/ua.dict' }
        'shared-dictionary-change' { 'fuzzing/Dictionaries/encoders.dict' }
        'fuzz-script-change' { 'fuzzing/Scripts/test-fuzzing.ps1' }
        'fuzz-manifest-change' { 'fuzzing/fuzz-targets.json' }
        'onefuzz-change' { 'fuzzing/OneFuzz/README.md' }
        'helper-change' { '.azurepipelines/assurance/results.ps1' }
        'documentation-only' { 'docs/README.md' }
        default { 'src/Subject.cs' }
    }
    Set-Content (Join-Path $fixture 'changed.txt') $change
    $output = & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/assurance/discovery.ps1') `
        -RepoRoot $fixture -ChangedFilesPath (Join-Path $fixture 'changed.txt')
    if ($Scenario -eq 'missing-fuzz-project') {
        if ($LASTEXITCODE -eq 0) { throw 'Missing requested fuzz project was accepted.' }
        exit 0
    }
    if ($LASTEXITCODE -ne 0) { throw 'Discovery failed.' }
    $result = $output | ConvertFrom-Json
    if ($result.projects.Count -ne 4 -or $result.relevantChanges -ne ($Scenario -ne 'documentation-only')) {
        throw 'Relevant assurance selection was lost.'
    }
    if ('fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj' -notin $result.projects.path) {
        throw 'Network replay was resolved beneath tests instead of fuzzing.'
    }
    exit 0
}
finally { Remove-Item $fixture -Recurse -Force }
