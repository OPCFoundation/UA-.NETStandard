# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$fixture = Join-Path $PSScriptRoot "obj/assurance-$([guid]::NewGuid().ToString('N'))"
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
        'helper-change' { '.azurepipelines/assurance-results.ps1' }
        'documentation-only' { 'docs/README.md' }
        default { 'src/Subject.cs' }
    }
    Set-Content (Join-Path $fixture 'changed.txt') $change
    $output = & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/assurance-discovery.ps1') `
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
