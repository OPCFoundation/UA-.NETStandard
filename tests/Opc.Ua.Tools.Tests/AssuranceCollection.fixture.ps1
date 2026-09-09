# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$fixture = Join-Path $PSScriptRoot "obj/assurance-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
try {
    $sha = 'a' * 40
    $profiles = Get-Content (Join-Path $root '.azurepipelines/assurance-profiles.json') -Raw | ConvertFrom-Json
    $profile = $profiles.profiles | Where-Object id -eq 'security-net10'
    $index = 0
    foreach ($definition in $profile.jobs) {
        $index++
        if ($index -eq 1 -and $Scenario -eq 'missing-job') { continue }
        $job = @{
            id=$definition.id; profile=$profile.id; project=$definition.project
            configuration='Release'; host='windows'; hostTfm='net10.0'; libraryTfm='net10.0'
            platform='windows/amd64'; shard='all'; filter=''; selected=$true; status='completed'
            inputIds=@('assurance-profile'); sourceSha=$sha; resultDocument="$index.results.json"
            profileDigest='sha256:' + (Get-FileHash (Join-Path $root '.azurepipelines/assurance-profiles.json')).Hash.ToLowerInvariant()
            producer=@{
                system='azure-pipelines';workflow='azure-pipelines.yml';definitionSha=$sha
                runId='123';attempt=2;job="job-$index";tools=@()
            }
        }
        if ($index -eq 1) {
            switch ($Scenario) {
                'wrong-source' { $job.sourceSha = 'b' * 40 }
                'stale-attempt' { $job.producer.attempt = 1 }
                'forged-na' { $job.status = 'not-applicable'; $job.notApplicableRule = 'network-unsupported-tfm' }
                'wrong-host' { $job.host = 'linux' }
            }
        }
        if ($index -ne 1 -or $Scenario -ne 'missing-proof') {
            @{
                schemaVersion=1;kind='trx';status='completed'
                documents=@(@{digest=('sha256:' + ('c' * 64))})
                counts=@{total=3;executed=3;passed=3;failed=0;skipped=0}
            } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture "$index.results.json")
            $job.resultDigest = 'sha256:' + (Get-FileHash (Join-Path $fixture "$index.results.json")).Hash.ToLowerInvariant()
        }
        $job | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture "$index.job.json")
        if ($index -eq 1 -and $Scenario -eq 'duplicate-job') {
            $job | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'duplicate.job.json')
        }
        if ($index -eq 1 -and $Scenario -in @('unrelated-os', 'unrelated-attempt')) {
            $unrelated = $job | ConvertTo-Json -Depth 10 | ConvertFrom-Json
            if ($Scenario -eq 'unrelated-os') { $unrelated.host = 'linux'; $unrelated.platform = 'linux/amd64' }
            else { $unrelated.producer.attempt = 1 }
            $unrelated | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'unrelated.job.json')
        }
    }
    $ids = if ($Scenario -eq 'full-profile') {
        'security-net10,fuzz-replay-net10,codeql-net10,aot-net10'
    } else { 'security-net10' }
    $output = Join-Path $fixture 'component.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/collect-assurance.ps1') `
        -RecordsPath $fixture -OutputPath $output -ProfileIds $ids `
        -ExpectedSourceSha $sha -ExpectedRunId '123' -ExpectedAttempt 2 `
        -ExpectedWorkflow 'azure-pipelines.yml' -ExpectedDefinitionSha $sha
    if ($LASTEXITCODE -ne 0) { throw 'Collector execution failed.' }
    $actual = Get-Content $output -Raw | ConvertFrom-Json
    $completed = if ($Scenario -in @('complete-security', 'full-profile', 'unrelated-os', 'unrelated-attempt')) { 2 } else { 1 }
    $expected = if ($Scenario -eq 'full-profile') { 7 } else { 2 }
    if ($actual.completed -ne $completed -or $actual.expected -ne $expected -or
        $actual.missing -ne ($expected - $completed) -or $actual.notApplicable -ne 0) {
        throw "Wrong completeness accounting in $Scenario."
    }
    $schema = Get-Content (Join-Path $root '.azurepipelines/release-evidence.schema.json') -Raw | ConvertFrom-Json -AsHashtable
    $schema['$ref'] = '#/$defs/assurance'
    $schema.Remove('required')
    $schema.Remove('properties')
    $schema.Remove('additionalProperties')
    if (-not (Test-Json -Json (Get-Content $output -Raw) -Schema ($schema | ConvertTo-Json -Depth 100))) {
        throw 'Collector output violated the release assurance contract.'
    }
    exit 0
}
finally { Remove-Item $fixture -Recurse -Force }
