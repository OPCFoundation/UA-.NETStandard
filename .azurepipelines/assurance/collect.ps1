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

<#
.SYNOPSIS
Collects a contract-compatible assurance component, not a publication approval.
.DESCRIPTION
Expected scope is expanded from the committed profiles before looking at records.
Trust and freshness inputs must come from the controlling pipeline, not records.
Security requires observed TRX counts; fuzz requires observed target/input pairs
reconciled with both frozen and copied inventories. Native and CodeQL credit
requires their complete source/tool-bound proof, not baseline success or counters.
ReviewAuthenticator is a protected in-process core-verifier hook, never an
artifact or dispatch input. Without it, nonzero findings remain missing.
Producer/tool authority must still be authenticated by the trusted core.
Output is the release/evidence.schema.json $defs/assurance object.
#>
param(
    [Parameter(Mandatory)][string] $RecordsPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}([0-9a-f]{24})?$')][string] $ExpectedSourceSha,
    [Parameter(Mandatory)][string] $ExpectedRunId,
    [Parameter(Mandatory)][int] $ExpectedAttempt,
    [Parameter(Mandatory)][string] $ExpectedWorkflow,
    [AllowEmptyString()][ValidatePattern('^(?:[0-9a-f]{40}(?:[0-9a-f]{24})?)?$')][string] $ExpectedDefinitionSha = '',
    [string] $ProfileIds = 'security-net10,fuzz-replay-net10,codeql-net10,aot-net10',
    [string] $ProfilesPath = (Join-Path $PSScriptRoot 'profiles.json'),
    [scriptblock] $ReviewAuthenticator
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'proofs.ps1')
if ($ExpectedAttempt -lt 1) { throw 'Expected attempt must be positive.' }
$catalog = Get-Content -LiteralPath $ProfilesPath -Raw | ConvertFrom-Json
$ids = @($ProfileIds.Split(',') | ForEach-Object { $_.Trim() } | Select-Object -Unique)
if ($ids.Count -eq 0 -or @($ids | Where-Object { $_ -notin $catalog.profiles.id }).Count -gt 0) {
    throw 'Unknown or empty assurance profile selection.'
}
$records = @()
$profileDigest = 'sha256:' + (Get-FileHash -LiteralPath $ProfilesPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (Test-Path -LiteralPath $RecordsPath) {
    foreach ($file in Get-ChildItem -LiteralPath $RecordsPath -Recurse -Filter '*.job.json' -File) {
        try {
            $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $records += @{ job = $record; directory = $file.DirectoryName }
        }
        catch { Write-Host 'Unparseable assurance record ignored; its expected job remains missing.' }
    }
}
$assurance = [ordered]@{
    profiles = $ids; expected = 0; selected = 0; completed = 0; failed = 0; missing = 0
    notApplicable = 0; jobs = @()
    inputIdentities = @(@{
        id = 'assurance-profile'; kind = 'profile'; path = '.azurepipelines/assurance/profiles.json'
        digest = $profileDigest
    })
}
foreach ($profile in $catalog.profiles | Where-Object { $_.id -in $ids }) {
    foreach ($definition in $profile.jobs) {
        $job = [ordered]@{
            id = $definition.id; profile = $profile.id; project = $definition.project
            configuration = $profile.configuration; host = $profile.host; hostTfm = $profile.hostTfm
            libraryTfm = $profile.libraryTfm; platform = $profile.platform; shard = 'all'; filter = $profile.filter
            selected = $false; status = 'missing'; inputIds = @('assurance-profile')
        }
        $candidates = @($records | Where-Object {
            $candidate = $_.job
            $matches = $candidate.id -ceq $definition.id -and $candidate.profile -ceq $profile.id -and
                $candidate.sourceSha -ceq $ExpectedSourceSha -and
                $candidate.producer.runId -ceq $ExpectedRunId -and
                $candidate.producer.attempt -eq $ExpectedAttempt -and
                $candidate.producer.workflow -ceq $ExpectedWorkflow -and
                $candidate.producer.definitionSha -ceq $ExpectedDefinitionSha -and
                $candidate.profileDigest -ceq $profileDigest
            foreach ($field in @('project', 'configuration', 'host', 'hostTfm', 'libraryTfm', 'platform', 'shard', 'filter')) {
                $matches = $matches -and $candidate.$field -ceq $job[$field]
            }
            $matches
        })
        if ($candidates.Count -eq 1) {
            $record = $candidates[0].job
            $valid = $ExpectedDefinitionSha -cmatch '^[0-9a-f]{40}([0-9a-f]{24})?$' -and
                $record.sourceSha -ceq $ExpectedSourceSha -and
                $record.producer.runId -ceq $ExpectedRunId -and
                $record.producer.attempt -eq $ExpectedAttempt -and
                $record.producer.workflow -ceq $ExpectedWorkflow -and
                $record.producer.definitionSha -ceq $ExpectedDefinitionSha -and
                $record.producer.system -in @('github-actions', 'azure-pipelines') -and
                $record.producer.job -cmatch '^[A-Za-z0-9_-]+$' -and
                $record.profileDigest -ceq $profileDigest
            foreach ($field in @('project', 'configuration', 'host', 'hostTfm', 'libraryTfm', 'platform', 'shard', 'filter')) {
                $valid = $valid -and $record.$field -ceq $job[$field]
            }
            if ($valid -and $record.selected -is [bool] -and $record.selected) {
                $job.selected = $true
                $job.sourceSha = $ExpectedSourceSha
                # Reconstruct the whitelist; do not forward arbitrary record fields.
                $job.producer = @{
                    system = $record.producer.system; workflow = $ExpectedWorkflow
                    definitionSha = $ExpectedDefinitionSha; runId = $ExpectedRunId
                    attempt = $ExpectedAttempt; job = $record.producer.job
                    tools = @($record.producer.tools | Where-Object {
                        $_.id -cin @('powershell', 'dotnet', 'ilc', 'codeql') -and
                        $_.version -cmatch '^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$' -and
                        ($_.id -cin @('powershell', 'dotnet') -or $_.digest -cmatch '^sha256:[0-9a-f]{64}$')
                    } | ForEach-Object {
                        $tool = @{ id = $_.id; version = $_.version }
                        if ($_.digest -cmatch '^sha256:[0-9a-f]{64}$') { $tool.digest = $_.digest }
                        $tool
                    })
                }
                $proofName = [string] $record.resultDocument
                if ($proofName -match '^[A-Za-z0-9._-]+\.json$' -and $proofName -notmatch '^\.\.') {
                    $proofFile = Join-Path $candidates[0].directory $proofName
                    if (Test-Path -LiteralPath $proofFile -PathType Leaf) {
                        try {
                            $proof = Get-Content -LiteralPath $proofFile -Raw | ConvertFrom-Json
                            $digest = 'sha256:' + (Get-FileHash -LiteralPath $proofFile -Algorithm SHA256).Hash.ToLowerInvariant()
                            $nativeFailure = $profile.id -ceq 'aot-net10' -and $proof.kind -ceq 'native-aot' -and
                                $proof.status -ceq 'failed' -and $record.status -ceq 'failed'
                            if ($record.resultDigest -cne $digest -or $proof.schemaVersion -ne 1 -or
                                (-not $nativeFailure -and @($proof.documents).Count -eq 0) -or @($proof.documents | Where-Object {
                                    $_.digest -cnotmatch '^sha256:[0-9a-f]{64}$'
                                }).Count -gt 0) { throw 'Unbound result proof.' }
                            if ($proof.status -eq 'failed' -and $record.status -eq 'failed') { $job.status = 'failed' }
                            $validCounts = $true
                            foreach ($name in @('total', 'executed', 'passed', 'failed', 'skipped')) {
                                $value = $proof.counts.$name
                                if (($value -isnot [long] -and $value -isnot [int]) -or $value -lt 0) {
                                    $validCounts = $false
                                }
                            }
                            if ($job.status -eq 'failed' -and $validCounts) {
                                $job.counts = @{}
                                foreach ($name in @('total', 'executed', 'passed', 'failed', 'skipped')) {
                                    $job.counts[$name] = [long] $proof.counts.$name
                                }
                            }
                            $completeTests = $validCounts -and
                                $proof.status -eq 'completed' -and $record.status -eq 'completed' -and
                                $proof.counts.executed -gt 0 -and
                                $proof.counts.passed -eq $proof.counts.executed -and
                                $proof.counts.total -eq $proof.counts.executed + $proof.counts.skipped -and
                                $proof.counts.failed -eq 0
                            $complete = $completeTests -and $profile.id -eq 'security-net10' -and
                                $proof.kind -eq 'trx' -and $proof.counts.skipped -eq 0
                            $native = $null
                            $analysis = $null
                            if ($completeTests -and $profile.id -eq 'aot-net10' -and
                                $proof.kind -ceq 'native-aot' -and $proof.counts.skipped -eq 0) {
                                $native = Get-AssuranceNativeProof $proof $record
                                $complete = $true
                            }
                            if ($profile.id -ceq 'codeql-net10' -and $proof.kind -ceq 'codeql' -and
                                $proof.status -cin @('completed', 'missing') -and
                                $record.status -cin @('completed', 'missing')) {
                                $analysis = Get-AssuranceCodeqlProof $proof $record $ReviewAuthenticator
                                $complete = $true
                            }
                            if ($completeTests -and $profile.id -eq 'fuzz-replay-net10' -and $proof.kind -eq 'fuzz-replay') {
                                $complete = $proof.counts.expectedTargets -gt 0 -and
                                    $proof.counts.expectedTargets -eq $proof.counts.executedTargets -and
                                    $proof.counts.expectedInputs -gt 0 -and
                                    $proof.counts.expectedInputs -eq $proof.counts.executedInputs -and
                                    $proof.replay.expectedPairs -eq $proof.counts.expectedTargets * $proof.counts.expectedInputs -and
                                    $proof.replay.expectedPairs -eq $proof.replay.executedPairs -and
                                    $proof.counts.skipped -eq $proof.replay.allowedEmptyRegressionSkips
                                foreach ($name in @('inventoryDigest', 'targetDigest', 'executionDigest')) {
                                    $complete = $complete -and $proof.replay.$name -cmatch '^sha256:[0-9a-f]{64}$'
                                }
                                foreach ($name in @('expectedTargets', 'executedTargets', 'expectedInputs', 'executedInputs')) {
                                    $value = $proof.counts.$name
                                    $complete = $complete -and ($value -is [long] -or $value -is [int]) -and $value -gt 0
                                }
                                foreach ($name in @('expectedPairs', 'executedPairs', 'allowedEmptyRegressionSkips')) {
                                    $value = $proof.replay.$name
                                    $complete = $complete -and ($value -is [long] -or $value -is [int]) -and $value -ge 0
                                }
                            }
                            if ($complete) {
                                $job.status = 'completed'
                                $job.counts = @{
                                    total = [long] $proof.counts.total; executed = [long] $proof.counts.executed
                                    passed = [long] $proof.counts.passed; failed = 0; skipped = [long] $proof.counts.skipped
                                }
                                if ($null -ne $analysis) {
                                    $job.counts = @{
                                        analyzedProjects = @($analysis.projects).Count
                                        findings = $analysis.findings; unresolvedFindings = 0
                                    }
                                }
                                $proofName = "$($job.id).proof.json"
                                $sanitized = @{
                                    schemaVersion = 1; kind = $proof.kind; status = 'completed'
                                    counts = $job.counts
                                    documents = @($proof.documents | ForEach-Object { @{ digest = $_.digest } })
                                }
                                if ($null -ne $native) { $sanitized.native = $native }
                                if ($null -ne $analysis) { $sanitized.analysis = $analysis }
                                if ($proof.kind -eq 'fuzz-replay') {
                                    foreach ($countName in @('expectedTargets', 'executedTargets', 'expectedInputs', 'executedInputs')) {
                                        $job.counts[$countName] = [long] $proof.counts.$countName
                                    }
                                    # The persisted sanitized proof binds the target and public corpus digests.
                                    $sanitized.replay = @{
                                        inventoryDigest = $proof.replay.inventoryDigest
                                        targetDigest = $proof.replay.targetDigest
                                        executionDigest = $proof.replay.executionDigest
                                        expectedPairs = $proof.replay.expectedPairs
                                        executedPairs = $proof.replay.executedPairs
                                        allowedEmptyRegressionSkips = $proof.replay.allowedEmptyRegressionSkips
                                    }
                                }
                                $proofOutput = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))) $proofName
                                $null = New-Item -ItemType Directory -Path (Split-Path -Parent $proofOutput) -Force
                                $sanitized | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $proofOutput -Encoding utf8
                                $proofDigest = 'sha256:' + (Get-FileHash -LiteralPath $proofOutput -Algorithm SHA256).Hash.ToLowerInvariant()
                                if ($null -ne $analysis -or $null -ne $native) {
                                    $kind = if ($null -ne $analysis) { 'analysis-configuration' } else { 'build-graph' }
                                    $id = "$($job.id)-$kind"
                                    $job.inputIds += $id
                                    $assurance.inputIdentities += @{
                                        id = $id; kind = $kind; path = $proofName; digest = $proofDigest
                                    }
                                }
                                if ($proof.kind -eq 'fuzz-replay') {
                                    foreach ($kind in @('target-manifest', 'sanitized-corpus-manifest')) {
                                        $id = "$($job.id)-$kind"
                                        $job.inputIds += $id
                                        $assurance.inputIdentities += @{ id = $id; kind = $kind; path = $proofName; digest = $proofDigest }
                                    }
                                }
                                $job.resultDocument = $proofName
                            }
                            elseif ($job.status -eq 'failed') {
                                $proofName = "$($job.id).proof.json"
                                $sanitized = @{
                                    schemaVersion = 1; kind = $proof.kind; status = 'failed'
                                    documents = @($proof.documents | ForEach-Object { @{ digest = $_.digest } })
                                }
                                if ($validCounts) { $sanitized.counts = $job.counts }
                                $proofOutput = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))) $proofName
                                $null = New-Item -ItemType Directory -Path (Split-Path -Parent $proofOutput) -Force
                                $sanitized | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $proofOutput -Encoding utf8
                                $job.resultDocument = $proofName
                            }
                        }
                        catch {
                            $job.status = 'missing'
                            Write-Host 'Profile proof rejected; no execution credit granted.'
                        }
                    }
                }
            }
        }
        $assurance.jobs += $job
        $assurance.expected++
        if ($job.selected) { $assurance.selected++ }
        $assurance[$job.status]++
    }
}
$parent = Split-Path -Parent $OutputPath
if ($parent) { $null = New-Item -ItemType Directory -Path $parent -Force }
$assurance | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Release assurance: expected=$($assurance.expected) selected=$($assurance.selected) completed=$($assurance.completed) failed=$($assurance.failed) missing=$($assurance.missing) N/A=0."
