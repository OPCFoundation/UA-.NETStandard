# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario, [string] $FixtureOutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$fixture = Join-Path $PSScriptRoot "obj/assurance-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
$responses = @{}
$prefix = 'repos/OPCFoundation/UA-.NETStandard'
$sha = 'a' * 40
$definitionSha = $sha
$sourceBranch = if ($Scenario -eq 'release-branch') { 'release/2.0.0' } else { 'master' }
$sourceRef = "refs/heads/$sourceBranch"
$queryBranch = [Uri]::EscapeDataString($sourceBranch)
$profilePath = Join-Path $root '.azurepipelines/assurance-profiles.json'
$profiles = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
$profileDigest = 'sha256:' + (Get-FileHash -LiteralPath $profilePath).Hash.ToLowerInvariant()
$now = [DateTimeOffset]::UtcNow.AddHours(-1)
$savedPath = $env:PATH
. (Join-Path $PSScriptRoot 'AssuranceProofData.fixture.ps1')

function Add-Response([string] $Endpoint, $Value) {
    $path = Join-Path $fixture ([guid]::NewGuid().ToString('N') + '.json')
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $path -Encoding utf8
    $responses[$Endpoint] = $path
}

try {
    Add-Response "$prefix/branches/master" @{name='master'; protected=$true; commit=@{sha=$sha}}
    foreach ($name in @('release-policy.json', 'assurance-profiles.json')) {
        Add-Response "$prefix/contents/.azurepipelines/${name}?ref=$sha" @{
            path=".azurepipelines/$name"; encoding='base64'
            content=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $root ".azurepipelines/$name")))
        }
    }
    $count = 0
    foreach ($workflow in @(
        @{id=101; path='.github/workflows/buildandtest.yml'; run=501; profiles=@('security-net10','fuzz-replay-net10','aot-net10')},
        @{id=102; path='.github/workflows/codeql-analysis.yml'; run=502; profiles=@('codeql-net10')}
    )) {
        $filename = [IO.Path]::GetFileName($workflow.path)
        Add-Response "$prefix/actions/workflows/$filename" @{id=$workflow.id;path=$workflow.path;state='active'}
        $run = @{
            id=$workflow.run; workflow_id=$workflow.id; path=$workflow.path; head_sha=$sha; run_attempt=2
            repository=@{full_name='OPCFoundation/UA-.NETStandard'}
            head_repository=@{full_name='OPCFoundation/UA-.NETStandard'}
            head_branch=$sourceBranch;event='push';pull_requests=@();status='completed';conclusion='success'
            created_at=$now.AddMinutes(-10).ToString('o');run_started_at=$now.ToString('o')
            updated_at=$now.AddMinutes(10).ToString('o')
            referenced_workflows=@(@{
                path="OPCFoundation/UA-.NETStandard/$($workflow.path)@$sourceRef"
                ref=$sourceRef;sha=$definitionSha
            })
        }
        if ($workflow.id -eq 101) {
            switch ($Scenario) {
                'wrong-repository' { $run.repository.full_name='other/repository' }
                'wrong-sha' { $run.head_sha='c'*40 }
                'wrong-event' { $run.event='pull_request' }
                'wrong-ref' { $run.head_branch='master378' }
                'wrong-workflow' { $run.workflow_id=999 }
                'definition-unverified' { $run.referenced_workflows=@(); $run.event='workflow_dispatch' }
                'push-definition' { $run.referenced_workflows=@() }
                'push-definition-missing' { $run.referenced_workflows=@() }
                'push-definition-contradiction' { $run.referenced_workflows[0].sha='b'*40 }
                'run-in-progress' { $run.status='in_progress';$run.conclusion=$null }
                { $_ -in @('failed-result', 'native-crash') } { $run.conclusion='failure' }
            }
        }
        if ($Scenario -eq 'push-definition' -and $workflow.id -eq 101) {
            $definitionBytes = [Text.Encoding]::UTF8.GetBytes("name: Synthetic assurance`non: push`njobs: {}`n")
            Add-Response "$prefix/contents/$($workflow.path)?ref=$sha" @{
                type='file'; path=$workflow.path; encoding='base64'; size=$definitionBytes.Length
                content=[Convert]::ToBase64String($definitionBytes); sha=('c'*40)
            }
        }
        $listed = @{id=$workflow.run;run_attempt=2}
        if ($Scenario -eq 'wrong-attempt' -and $workflow.id -eq 101) { $listed.run_attempt=1 }
        $list = @{total_count=1;workflow_runs=@($listed)}
        if ($Scenario -eq 'missing-current-run') { $list=@{total_count=0;workflow_runs=@()} }
        Add-Response "$prefix/actions/workflows/$($workflow.id)/runs?head_sha=$sha&branch=$queryBranch&per_page=100" $list
        Add-Response "$prefix/actions/runs/$($workflow.run)" $run
        Add-Response "$prefix/actions/runs/$($workflow.run)/attempts/2" $run
        $jobs = @()
        $artifacts = @()
        foreach ($profile in $profiles.profiles | Where-Object { $_.id -in $workflow.profiles }) {
            foreach ($definition in $profile.jobs) {
                $count++
                $producerJob = 'assurance-windows'
                $jobName = "assurance-$($definition.id)"
                $artifactName = "assurance-$($definition.id)-$($workflow.run)-2-$producerJob"
                if ($definition.id -eq 'codeql-csharp') {
                    $producerJob='analyze';$jobName='Analyze (csharp)'
                    $artifactName="assurance-codeql-csharp-$($workflow.run)-2-analyze"
                }
                if ($definition.id -eq 'native-aot') {
                    $producerJob='aot-test';$jobName='aot-windows-2025'
                    $artifactName="assurance-native-aot-$($workflow.run)-2-aot-test-windows-2025"
                }
                $job = @{
                    id=$count; name=$jobName; run_id=$workflow.run;run_attempt=2;head_sha=$sha
                    status='completed';conclusion='success'
                    started_at=$now.AddMinutes(1).ToString('o');completed_at=$now.AddMinutes(8).ToString('o')
                }
                if ($Scenario -ne 'missing-selected-project' -or $count -ne 1) { $jobs += $job }
                $directory = Join-Path $fixture "record-$count"
                $null = New-Item -ItemType Directory -Path $directory
                $kind = if ($profile.id -eq 'fuzz-replay-net10') { 'fuzz-replay' } else { 'trx' }
                if ($definition.id -eq 'native-aot') { $kind='mtp-trx' }
                if ($definition.id -eq 'codeql-csharp') { $kind='sarif' }
                $proof = @{
                    schemaVersion=1;kind=$kind;status='completed'
                    documents=@(@{digest='sha256:' + ('d'*64)})
                    counts=@{total=2;executed=2;passed=2;failed=0;skipped=0}
                }
                if ($kind -eq 'fuzz-replay') {
                    $proof.counts.expectedTargets=2;$proof.counts.executedTargets=2
                    $proof.counts.expectedInputs=2;$proof.counts.executedInputs=2
                    $proof.replay=@{
                        inventoryDigest='sha256:' + [Convert]::ToHexString(
                            [Security.Cryptography.SHA256]::HashData(
                                [Text.Encoding]::UTF8.GetBytes($definition.id))).ToLowerInvariant()
                        targetDigest='sha256:' + ('e'*64)
                        executionDigest='sha256:' + ('f'*64);expectedPairs=4;executedPairs=4;allowedEmptyRegressionSkips=0
                    }
                }
                if ($kind -eq 'sarif') { $proof.counts=@{findings=0} }
                if ($definition.id -eq 'native-aot' -and
                    ($Scenario.StartsWith('native-') -or $Scenario.StartsWith('codeql-') -or $Scenario -eq 'verified-seven')) {
                    $proof = New-AssuranceFixtureNative $directory $sha $workflow.run 2
                    switch ($Scenario) {
                        'native-image-mismatch' { $proof.native.observedDigest = 'sha256:' + ('1' * 64) }
                        'native-dynamic-code' { $proof.native.runtime.isDynamicCodeSupported = $true }
                        'native-source-mismatch' { $proof.native.sourceSha = 'b' * 40 }
                        'native-attempt-mismatch' { $proof.native.attempt = 1 }
                        'native-missing-report' { $proof.native.runtime.Remove('reportPhase') }
                        'native-crash' {
                            $proof.status = 'failed'; $proof.documents = @(); $proof.Remove('counts')
                            $proof.reasons = @('NATIVE_CRASHED_BEFORE_REPORT'); $job.conclusion = 'failure'
                        }
                    }
                }
                if ($definition.id -eq 'codeql-csharp' -and
                    ($Scenario.StartsWith('codeql-') -or $Scenario -eq 'verified-seven')) {
                    $proof = New-AssuranceFixtureCodeql $sha $workflow.run 2
                    switch ($Scenario) {
                        'codeql-partial-extraction' { $proof.analysis.projects[0].extractedSources = 1 }
                        'codeql-extraction-mismatch' { $proof.analysis.projects[0].extractedSourceDigest = 'sha256:' + ('1' * 64) }
                        'codeql-partial-queries' { $proof.analysis.completedQueries = 1 }
                        'codeql-missing-config' { $proof.analysis.Remove('configurationDigest') }
                        'codeql-source-mismatch' { $proof.analysis.sourceSha = 'b' * 40 }
                        'codeql-attempt-mismatch' { $proof.analysis.attempt = 1 }
                        'codeql-missing-review' {
                            $proof.analysis.findings = 1; $proof.analysis.distinctAlerts = 1
                            $proof.counts.findings = 1
                        }
                    }
                }
                if ($Scenario -eq 'partial-fuzz-proof' -and $definition.id -eq 'fuzz-network') {
                    $proof.replay.executedPairs=3
                }
                if ($Scenario -eq 'manifest-only-fuzz' -and $kind -eq 'fuzz-replay') { $proof.Remove('replay') }
                if ($Scenario -eq 'zero-result' -and $count -eq 1) {
                    $proof.counts=@{total=0;executed=0;passed=0;failed=0;skipped=0}
                }
                if ($Scenario -eq 'failed-result' -and $count -eq 1) {
                    $proof.status='failed'
                    $proof.counts=@{total=2;executed=2;passed=1;failed=1;skipped=0}
                    $job.conclusion='failure'
                }
                $proofFile = Join-Path $directory 'evidence.job.results.json'
                $proof | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $proofFile
                $record = @{
                    id=$definition.id;profile=$profile.id;project=$definition.project;configuration='Release'
                    host='windows';hostTfm='net10.0';libraryTfm='net10.0';platform='windows/amd64'
                    shard='all';filter='';selected=$true;status='completed';sourceSha=$sha;profileDigest=$profileDigest
                    producer=@{
                        system='github-actions';workflow=$workflow.path;definitionSha=$definitionSha
                        runId=[string]$workflow.run;attempt=2;job=$producerJob;tools=@(@{id='powershell';version='7.6.5'})
                    }
                    resultDocument='evidence.job.results.json'
                    resultDigest='sha256:' + (Get-FileHash -LiteralPath $proofFile).Hash.ToLowerInvariant()
                    privateField='RESTRICTED_SENTINEL'
                }
                if ($proof.kind -eq 'native-aot') {
                    $record.producer.tools += @{ id = 'dotnet'; version = $proof.native.tools.sdkVersion }
                    $record.producer.tools += @{
                        id = 'ilc'; version = $proof.native.tools.compilerVersion
                        digest = $proof.native.tools.compilerDigest
                    }
                    if ($Scenario -eq 'native-crash') { $record.status = 'failed' }
                }
                if ($proof.kind -eq 'codeql') {
                    $record.producer.tools += @{
                        id = 'codeql'; version = $proof.analysis.tool.version; digest = $proof.analysis.tool.digest
                    }
                }
                if ($count -eq 1) {
                    switch ($Scenario) {
                        'failed-result' { $record.status='failed' }
                        'forged-na' { $record.status='not-applicable';$record.notApplicableRule='network-unsupported-tfm' }
                        'record-old-attempt' { $record.producer.attempt=1 }
                        'producer-local-path' { $record.producer.job='C:\private\RESTRICTED_SENTINEL' }
                        'record-wrong-source' { $record.sourceSha='c'*40 }
                    }
                }
                $record | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $directory 'evidence.job.json')
                $zipPath = Join-Path $fixture "$count.zip"
                [IO.Compression.ZipFile]::CreateFromDirectory($directory, $zipPath)
                if ($Scenario -eq 'zip-traversal' -and $count -eq 1) {
                    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Update)
                    try {
                        $entry = $zip.CreateEntry('../RESTRICTED_SENTINEL.json')
                        $stream = $entry.Open()
                        try { $stream.WriteByte(65) } finally { $stream.Dispose() }
                    }
                    finally { $zip.Dispose() }
                }
                $artifact = @{
                    id=700+$count;name=$artifactName;expired=$false
                    digest='sha256:' + (Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant()
                    size_in_bytes=(Get-Item -LiteralPath $zipPath).Length
                    created_at=$now.AddMinutes(5).ToString('o');updated_at=$now.AddMinutes(5).ToString('o')
                    expires_at=$now.AddDays(1).ToString('o')
                    workflow_run=@{id=$workflow.run;head_sha=$sha;head_branch=$sourceBranch}
                }
                if ($Scenario -eq 'stale-artifact' -and $count -eq 1) {
                    $artifact.created_at=$now.AddDays(-1).ToString('o')
                }
                if ($Scenario -eq 'hash-mismatch' -and $count -eq 1) { $artifact.digest='sha256:' + ('0'*64) }
                $artifacts += $artifact
                $responses["$prefix/actions/artifacts/$($artifact.id)/zip"] = $zipPath
            }
        }
        Add-Response "$prefix/actions/runs/$($workflow.run)/attempts/2/jobs?per_page=100&page=1" @{total_count=$jobs.Count;jobs=$jobs}
        Add-Response "$prefix/actions/runs/$($workflow.run)/artifacts?per_page=100&page=1" @{total_count=$artifacts.Count;artifacts=$artifacts}
    }
    $env:ASSURANCE_STUB_RESPONSES = Join-Path $fixture 'responses.json'
    $waitArguments = @{}
    if ($Scenario.StartsWith('wait-')) {
        $waitArguments.WaitTimeoutSeconds = if ($Scenario -eq 'wait-timeout') { 1 } else { 30 }
        $runEndpoint = "$prefix/actions/runs/501"
        $completePath = $responses[$runEndpoint]
        $pending = Get-Content -LiteralPath $completePath -Raw | ConvertFrom-Json
        $pending.status = 'in_progress'; $pending.conclusion = $null
        Add-Response $runEndpoint $pending
        $pendingPath = $responses[$runEndpoint]
        if ($Scenario -eq 'wait-cancelled') {
            $waitArguments.CancellationPath = Join-Path $fixture 'cancel.signal'
            'cancel' | Set-Content -LiteralPath $waitArguments.CancellationPath
        }
        if ($Scenario -in @('wait-success', 'wait-attempt-changed')) {
            if ($Scenario -eq 'wait-attempt-changed') {
                $changed = Get-Content -LiteralPath $completePath -Raw | ConvertFrom-Json
                $changed.run_attempt = 3
                Add-Response $runEndpoint $changed
                $completePath = $responses[$runEndpoint]
            }
            $responses[$runEndpoint] = @($pendingPath, $completePath)
        }
        if ($Scenario -eq 'wait-superseded') {
            $listEndpoint = "$prefix/actions/workflows/101/runs?head_sha=$sha&branch=$queryBranch&per_page=100"
            $initial = $responses[$listEndpoint]
            Add-Response $listEndpoint @{ total_count = 1; workflow_runs = @(@{ id = 999; run_attempt = 1 }) }
            $responses[$listEndpoint] = @($initial, $responses[$listEndpoint])
        }
    }
    $responses | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $env:ASSURANCE_STUB_RESPONSES
    $stub = Join-Path $PSScriptRoot 'AssuranceGh.fixture.ps1'
    if ($IsWindows) {
        "@echo off`r`n`"$(Get-Process -Id $PID | Select-Object -ExpandProperty Path)`" -NoProfile -File `"$stub`" %*`r`n" |
            Set-Content -LiteralPath (Join-Path $fixture 'gh.cmd') -Encoding ascii
    }
    else {
        "#!/bin/sh`nexec pwsh -NoProfile -File '$stub' `"`$@`"`n" |
            Set-Content -LiteralPath (Join-Path $fixture 'gh') -Encoding utf8
        & chmod +x (Join-Path $fixture 'gh')
    }
    $env:PATH = $fixture + [IO.Path]::PathSeparator + $savedPath
    if ((Get-Command gh -CommandType Application | Select-Object -First 1).Source -notlike "$fixture*") {
        throw 'Transport stub not selected; no live API call is allowed.'
    }
    $output = Join-Path $fixture 'public/assurance.json'
    & pwsh -NoProfile -File (Join-Path $root '.azurepipelines/get-release-assurance.ps1') `
        -RepositoryRoot $root -ExpectedSourceSha $sha -ExpectedSourceRef $sourceRef `
        -OutputPath $output -WorkDirectory (Join-Path $fixture 'work') @waitArguments
    if ($LASTEXITCODE -ne 0) { throw 'Retrieval process failed.' }
    $actual = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $receiptText = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($output, 'metadata.json')) -Raw
    $receipt = $receiptText | ConvertFrom-Json
    $completed = switch ($Scenario) {
        { $_ -in @('wrong-repository','wrong-sha','wrong-event','wrong-ref','wrong-workflow','wrong-attempt',
            'definition-unverified','push-definition-missing','push-definition-contradiction',
            'run-in-progress','missing-current-run') } { 0; break }
        'manifest-only-fuzz' { 2; break }
        { $_ -in @('complete', 'release-branch', 'push-definition') } { 5; break }
        'verified-seven' { 7; break }
        'native-positive' { 6; break }
        'wait-success' { 5; break }
        { $_.StartsWith('wait-') } { 0; break }
        { $_.StartsWith('native-') } { 5; break }
        { $_.StartsWith('codeql-') } { 6; break }
        default { 4 }
    }
    $failed = if ($Scenario -in @('failed-result', 'native-crash')) { 1 } else { 0 }
    if ($actual.expected -ne 7 -or $actual.completed -ne $completed -or
        $actual.missing -ne 7-$completed-$failed -or $actual.failed -ne $failed -or $actual.notApplicable -ne 0) {
        throw "Unexpected assurance result in $Scenario. Expected completed=$completed. Receipt: $receiptText"
    }
    if ($receipt.mode -ne 'github-api' -or $receipt.componentDigest -cne
        ('sha256:' + (Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant())) {
        throw 'Production retrieval mode or receipt binding was lost.'
    }
    if ($Scenario.StartsWith('wait-') -and
        (@($receipt.pending).Count -ne 1 -or $receipt.pending[0].runId -cne '501' -or $receipt.pending[0].attempt -ne 2)) {
        throw 'Pending run identity was not preserved.'
    }
    if (@($actual.inputIdentities.id | Select-Object -Unique).Count -ne $actual.inputIdentities.Count) {
        throw 'Cross-workflow input identities were duplicated.'
    }
    foreach ($identity in $actual.inputIdentities | Where-Object { $_.id -ne 'assurance-profile' }) {
        $proofPath = Join-Path (Split-Path -Parent $output) $identity.path
        if (-not (Test-Path -LiteralPath $proofPath) -or $identity.digest -cne
            ('sha256:' + (Get-FileHash -LiteralPath $proofPath).Hash.ToLowerInvariant())) {
            throw 'Referenced sanitized proof was not persisted with its exact digest.'
        }
        $completedJobs = @($actual.jobs | Where-Object status -EQ completed)
        if (@($completedJobs.resultDocument | Select-Object -Unique).Count -ne $completedJobs.Count -or
            @($completedJobs | Where-Object { $_.resultDocument -cne "$($_.id).proof.json" }).Count -ne 0) {
            throw 'Completed jobs must retain separate, job-bound proof filenames.'
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath (Split-Path -Parent $output) -File) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if ($text.Contains('RESTRICTED_SENTINEL') -or $text.Contains($fixture) -or $text -match '"trusted"\s*:\s*true') {
            throw 'Unreviewed public field or manufactured trust leaked.'
        }
        foreach ($job in $actual.jobs | Where-Object { $_.status -in @('completed', 'failed') }) {
            if (-not $job.resultDocument -or
                -not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $output) $job.resultDocument))) {
                throw 'Completed or observed-failed proof sidecar was lost.'
            }
        }
    }
    if ($Scenario -eq 'complete' -and ($actual.selected -ne 7 -or
        ($actual.jobs | Where-Object id -eq 'native-aot').status -ne 'missing' -or
        ($actual.jobs | Where-Object id -eq 'codeql-csharp').status -ne 'missing')) {
        throw 'Native counts or zero SARIF findings were falsely credited.'
    }
    if ($FixtureOutputDirectory) {
        $destination = [IO.Path]::GetFullPath($FixtureOutputDirectory)
        if (Test-Path -LiteralPath $destination) { throw 'Fixture export directory must be fresh.' }
        $null = New-Item -ItemType Directory -Path $destination
        foreach ($file in Get-ChildItem -LiteralPath (Split-Path -Parent $output) -File -Filter '*.json') {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name)
        }
        @{ schemaVersion = 1; mode = 'synthetic-test-fixture-not-production'; scenario = $Scenario } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'fixture-context.json') -Encoding utf8
    }
}
finally {
    $env:PATH = $savedPath
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
