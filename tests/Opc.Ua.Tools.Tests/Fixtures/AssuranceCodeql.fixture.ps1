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
$fixture = Join-Path (Split-Path $PSScriptRoot) "obj\assurance-codeql-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
$savedEnvironment = @{}
foreach ($name in @('PATH', 'GITHUB_ACTIONS', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_REF',
    'ASSURANCE_CODEQL_FIXTURE', 'ASSURANCE_STUB_RESPONSES')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $work = Join-Path $fixture 'work'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines\assurance\codeql.ps1') -Mode Prepare -WorkDirectory $work
    if ($LASTEXITCODE -ne 0) { throw 'CodeQL preparation failed.' }
    [xml] $capture = Get-Content -LiteralPath (Join-Path $work 'capture.targets') -Raw
    if ($capture.Project.Target.BeforeTargets -cne 'CoreCompile') { throw 'Compilation input capture hook missing.' }
    $database = Join-Path $fixture 'database'
    $pack = Join-Path $fixture 'pack'
    $results = Join-Path $fixture 'results'
    $null = New-Item -ItemType Directory -Path $database, $pack, $results,
        (Join-Path $database 'results\codeql\csharp-queries'), (Join-Path $pack 'codeql-suites'),
        (Join-Path $database 'db-csharp')
    'synthetic dataset identity' | Set-Content -LiteralPath (Join-Path $database 'db-csharp\dataset')
    'finalized: true' | Set-Content -LiteralPath (Join-Path $database 'codeql-database.yml')
    $query = Join-Path $pack 'Query.ql'
    'select 1' | Set-Content -LiteralPath $query
    'name: codeql/csharp-queries' | Set-Content -LiteralPath (Join-Path $pack 'qlpack.yml')
    'queries: Query.ql' | Set-Content -LiteralPath (Join-Path $pack 'codeql-suites\csharp-code-scanning.qls')
    if ($Scenario -ne 'missing-query-result') {
        'observed synthetic BQRS' | Set-Content -LiteralPath (Join-Path $database 'results\codeql\csharp-queries\Query.bqrs')
    }
    $source = Join-Path $fixture 'Source.cs'
    'internal sealed class SyntheticSource {}' | Set-Content -LiteralPath $source
    $assembly = Join-Path $fixture 'Synthetic.dll'
    if ($Scenario -eq 'complete') {
        $smokeDirectory = Join-Path $fixture 'capture-smoke'
        $null = New-Item -ItemType Directory -Path $smokeDirectory
        $smokeProject = Join-Path $fixture 'CaptureSmoke.csproj'
        @"
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssuranceCodeqlDirectory>$smokeDirectory</AssuranceCodeqlDirectory>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$source" />
    <IntermediateAssembly Include="$assembly" />
  </ItemGroup>
  <Import Project="$work\capture.targets" />
  <Target Name="CoreCompile" />
</Project>
"@ | Set-Content -LiteralPath $smokeProject -Encoding utf8
        & dotnet msbuild $smokeProject -nologo -t:CoreCompile -verbosity:quiet
        if ($LASTEXITCODE -ne 0) { throw 'Real MSBuild rejected the compilation capture hook.' }
        $captured = @(Get-ChildItem -LiteralPath $smokeDirectory -Filter '*.compile.txt' -File)
        if ($captured.Count -ne 1 -or "assembly|$assembly" -cnotin (Get-Content -LiteralPath $captured[0].FullName)) {
            throw 'Real MSBuild did not capture the compiler output identity.'
        }
    }
    "project|$fixture\Synthetic.csproj`nassembly|$assembly`nsource|$source" |
        Set-Content -LiteralPath (Join-Path $work 'compilations\Synthetic.compile.txt')
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $database 'src.zip'), [IO.Compression.ZipArchiveMode]::Create)
    try {
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $source, $source.Replace('\', '/').Replace(':', '_').TrimStart('/'))
    }
    finally { $zip.Dispose() }
    if ($Scenario -eq 'changed-source') { 'changed source bytes' | Set-Content -LiteralPath $source }
    $finding = if ($Scenario -eq 'review-missing') { @(@{ ruleId = 'synthetic-rule'; message = @{ text = 'RESTRICTED_SENTINEL' } }) } else { @() }
    @{
        version = '2.1.0'; runs = @(@{
            tool = @{ driver = @{ name = 'CodeQL'; version = '2.23.0' } }
            invocations = @(@{ executionSuccessful = $true }); results = $finding
        })
    } | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $results 'analysis.sarif')
    $env:ASSURANCE_CODEQL_FIXTURE = Join-Path $fixture 'cli.json'
    @{ root = $root; query = $query; assembly = $assembly; source = $source; scenario = $Scenario } |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $env:ASSURANCE_CODEQL_FIXTURE
    $pwsh = (Get-Process -Id $PID).Path
    $cli = Join-Path $fixture 'codeql.cmd'
    "@echo off`r`n`"$pwsh`" -NoProfile -File `"$PSScriptRoot\AssuranceCodeqlCli.fixture.ps1`" %*`r`n" |
        Set-Content -LiteralPath $cli -Encoding ascii
    "@echo off`r`n`"$pwsh`" -NoProfile -File `"$PSScriptRoot\AssuranceGh.fixture.ps1`" %*`r`n" |
        Set-Content -LiteralPath (Join-Path $fixture 'gh.cmd') -Encoding ascii
    $env:PATH = $fixture + [IO.Path]::PathSeparator + $savedEnvironment.PATH
    $env:GITHUB_ACTIONS = 'true'; $env:GITHUB_RUN_ID = '501'; $env:GITHUB_RUN_ATTEMPT = '2'
    $env:GITHUB_REF = 'refs/heads/master'
    $sha = (& git -C $root rev-parse HEAD).Trim()
    $prefix = 'repos/OPCFoundation/UA-.NETStandard/code-scanning'
    $sarifId = [guid]::NewGuid().ToString()
    $uploadPath = Join-Path $fixture 'upload.json'
    @{ processing_status = 'complete'; analyses_url = "https://api.github.com/$prefix/analyses?sarif_id=$sarifId" } |
        ConvertTo-Json | Set-Content -LiteralPath $uploadPath
    $analysisPath = Join-Path $fixture 'analyses.json'
    $analysis = @{
        id = 800; commit_sha = $sha; ref = 'refs/heads/master'; category = 'assurance-csharp-net10'
        analysis_key = '.github/workflows/codeql-analysis.yml:analyze'; error = ''
        tool = @{ name = 'CodeQL'; version = '2.23.0' }; results_count = $finding.Count
        created_at = [DateTimeOffset]::UtcNow.ToString('o')
    }
    if ($Scenario -eq 'wrong-analysis-source') { $analysis.commit_sha = 'a' * 40 }
    ConvertTo-Json -InputObject @($analysis) -Depth 10 | Set-Content -LiteralPath $analysisPath
    $env:ASSURANCE_STUB_RESPONSES = Join-Path $fixture 'responses.json'
    @{
        "$prefix/sarifs/$sarifId" = $uploadPath
        "$prefix/analyses?sarif_id=$sarifId&per_page=100" = $analysisPath
    } | ConvertTo-Json | Set-Content -LiteralPath $env:ASSURANCE_STUB_RESPONSES
    if ((Get-Command gh -CommandType Application | Select-Object -First 1).Source -notlike "$fixture*") {
        throw 'Fake transport not selected.'
    }
    $output = Join-Path $fixture 'proof.json'
    & (Join-Path $root '.azurepipelines\assurance\codeql.ps1') `
        -Mode Collect -WorkDirectory $work -OutputPath $output -CodeqlPath $cli -CodeqlVersion '2.23.0' `
        -DatabaseLocations (@{ csharp = $database } | ConvertTo-Json -Compress) -SarifId $sarifId `
        -ResultsPath $results -CommandTimeoutSeconds 10
    if ($LASTEXITCODE -ne 0) { throw 'CodeQL producer failed to persist its honest status.' }
    $text = Get-Content -LiteralPath $output -Raw
    $proof = $text | ConvertFrom-Json
    $expected = if ($Scenario -eq 'complete') { 'completed' } else { 'missing' }
    if ($proof.status -cne $expected) {
        $commands = Get-Content -LiteralPath (Join-Path $fixture 'commands.jsonl') -Raw
        throw "Wrong CodeQL producer status for ${Scenario}: $text`n$($Error[0] | Out-String)`n$commands"
    }
    if ($text.Contains('RESTRICTED_SENTINEL') -or $text.Contains($fixture)) { throw 'Restricted CodeQL data leaked.' }
    if ($Scenario -eq 'complete' -and
        ($proof.counts.analyzedProjects -ne 1 -or $proof.analysis.projects[0].extractedSources -ne 1 -or
        $proof.analysis.expectedQueries -ne 1 -or $proof.analysis.completedQueries -ne 1)) {
        throw 'Actual compilation/extraction/query coverage was not recorded.'
    }
}
finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
