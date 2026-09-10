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
Fetches sanitized assurance from authenticated, read-only GitHub API records.
.DESCRIPTION
Writes the component ($defs/assurance), plus <OutputPath without .json>.metadata.json.
Completed and observed-failed jobs write <job-id>.proof.json beside the component;
retain every referenced sidecar with its exact bytes.
Requires gh on PATH and a runner-provided read token (actions:read, contents:read).
Never logs credentials, API response bodies, findings, local paths or raw results.
Only current master / major 2 policy is supported. No dispatch input can override
identity, freshness, source, policy or producer checks. The default is one-shot:
an absent/in-progress run remains missing (including a publish/CI race).
WaitTimeoutSeconds optionally waits for the same run/attempt with a fixed overall
deadline and capped backoff. CancellationPath is a caller-owned cancellation
signal. A new attempt, superseding run, cancellation or policy change invalidates
the snapshot; there is no older-success fallback.

Workflow head_sha is not a general substitute for definition SHA. An explicit
matching referenced_workflows entry supplies it. For a same-repository push,
GitHub uses the workflow from the event commit; additionally retrieve that exact
file from the authenticated Contents API. Other unproven event/definition cases
remain DEFINITION_UNVERIFIED. This is not a publication approval.
Native and CodeQL sidecars are locally validated; approved controller/tool and
review authority remain separate trusted-core requirements.

OfflineMetadataPath is for local fixtures only, never a publisher input: it is
rejected on CI, is marked offline-fixture, and cannot award any job credit.
.EXAMPLE
pwsh -NoProfile -File .azurepipelines\assurance\get-release.ps1 `
  -ExpectedSourceSha <checkout-sha> -OutputPath out\assurance.json -WorkDirectory scratch
#>
param(
    [string] $RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot)),
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}([0-9a-f]{24})?$')][string] $ExpectedSourceSha,
    [ValidatePattern('^refs/heads/(master|release/2\.[0-9A-Za-z._/-]+)$')]
    [string] $ExpectedSourceRef = 'refs/heads/master',
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $WorkDirectory,
    [string] $OfflineMetadataPath = '',
    [ValidateRange(0, 900)][int] $WaitTimeoutSeconds = 0,
    [string] $CancellationPath = '',
    [scriptblock] $ReviewAuthenticator,
    [string] $ReviewVerificationBundle,
    [string] $TrustPolicy = $env:OPCUA_RELEASE_TRUST_POLICY
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'github.ps1')
$repository = 'OPCFoundation/UA-.NETStandard'
$prefix = "repos/$repository"
$sourceBranch = $ExpectedSourceRef.Substring('refs/heads/'.Length)
$queryBranch = [Uri]::EscapeDataString($sourceBranch)
$profilePath = Join-Path $RepositoryRoot '.azurepipelines/assurance/profiles.json'
$policyPath = Join-Path $RepositoryRoot '.azurepipelines/release/policy.json'
$profiles = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$profileDigest = 'sha256:' + (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash.ToLowerInvariant()
$policyDigest = 'sha256:' + (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash.ToLowerInvariant()
$workspace = Join-Path ([IO.Path]::GetFullPath($WorkDirectory)) ('assurance-' + [guid]::NewGuid().ToString('N'))
$waitDeadline = [DateTimeOffset]::UtcNow.AddSeconds($WaitTimeoutSeconds)
$null = New-Item -ItemType Directory -Path $workspace
if ($ReviewVerificationBundle -and $TrustPolicy -and $null -eq $ReviewAuthenticator) {
    . (Join-Path $PSScriptRoot 'review.ps1')
    $ReviewAuthenticator = New-AssuranceReviewAuthenticator -RepositoryRoot $RepositoryRoot `
        -VerificationBundle $ReviewVerificationBundle -TrustPolicy $TrustPolicy `
        -WorkDirectory (Join-Path $workspace 'review-verification')
}
$receipt = [ordered]@{
    schemaVersion = 1; kind = 'assurance-metadata-receipt'; repository = $repository
    sourceSha = $ExpectedSourceSha; ref = $ExpectedSourceRef
    mode = 'github-api'; assessedAt = [DateTimeOffset]::UtcNow.ToString('o')
    policyDigest = $policyDigest; profileDigest = $profileDigest
    producerApprovalStatus = 'unverified'
    status = 'incomplete'; reasons = @(); workflows = @(); pending = @()
}
$workflowDefinitions = @(
    @{ path = '.github/workflows/buildandtest.yml'; profiles = @('security-net10', 'fuzz-replay-net10', 'aot-net10') },
    @{ path = '.github/workflows/codeql-analysis.yml'; profiles = @('codeql-net10') }
)

function Assert-AssuranceTime($Value) {
    if ($Value -is [DateTime] -or $Value -is [DateTimeOffset]) { return [DateTimeOffset] $Value }
    $time = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string] $Value, [ref] $time)) { throw 'TIMESTAMP_UNVERIFIED' }
    return $time
}

function Assert-AssuranceRun($Run, $Workflow, [switch] $AllowPending) {
    if ($Run.repository.full_name -cne $repository -or $Run.head_repository.full_name -cne $repository) {
        throw 'WRONG_REPOSITORY'
    }
    if ($Run.head_sha -cne $ExpectedSourceSha) { throw 'WRONG_SOURCE' }
    if ($Run.head_branch -cne $sourceBranch -or $Run.event -cnotin @('push', 'schedule', 'workflow_dispatch') -or
        @($Run.pull_requests | Where-Object { $null -ne $_ }).Count -ne 0) { throw 'WRONG_EVENT_OR_REF' }
    if ($Run.workflow_id -ne $Workflow.id -or $Run.path -cne $Workflow.path -or
        [string] $Run.id -notmatch '^[1-9][0-9]*$' -or $Run.run_attempt -lt 1) { throw 'WRONG_WORKFLOW_OR_ATTEMPT' }
    $pending = $Run.status -cin @('queued', 'in_progress', 'waiting', 'pending', 'requested')
    if ($pending -and $AllowPending) {
        $created = Assert-AssuranceTime $Run.created_at
        $updated = Assert-AssuranceTime $Run.updated_at
        if ($created -gt $updated -or $updated -gt [DateTimeOffset]::UtcNow) { throw 'STALE_RUN' }
        return
    }
    if ($Run.status -cne 'completed' -or $Run.conclusion -cnotin @('success', 'failure')) {
        throw 'RUN_NOT_COMPLETED'
    }
    $created = Assert-AssuranceTime $Run.created_at
    $started = Assert-AssuranceTime $Run.run_started_at
    $updated = Assert-AssuranceTime $Run.updated_at
    if ($created -gt $started -or $started -gt $updated -or $updated -gt [DateTimeOffset]::UtcNow) {
        throw 'STALE_RUN'
    }
}

try {
    # Expand every expected job before querying CI; nothing discovered can shrink scope.
    $componentPath = Join-Path $workspace 'empty.json'
    & (Join-Path $PSScriptRoot 'collect.ps1') -RecordsPath (Join-Path $workspace 'absent') `
        -OutputPath $componentPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedRunId 'unavailable' `
        -ExpectedAttempt 1 -ExpectedWorkflow '.github/workflows/buildandtest.yml' `
        -ProfilesPath $profilePath -ReviewAuthenticator $ReviewAuthenticator
    $component = Get-Content -LiteralPath $componentPath -Raw | ConvertFrom-Json -AsHashtable
    if ($OfflineMetadataPath) {
        if ($env:GITHUB_ACTIONS -eq 'true' -or $env:TF_BUILD -eq 'True') {
            throw 'OFFLINE_METADATA_FORBIDDEN_ON_CI'
        }
        $null = Get-Content -LiteralPath $OfflineMetadataPath -Raw | ConvertFrom-Json
        $receipt.mode = 'offline-fixture'
        $receipt.reasons += 'OFFLINE_METADATA_HAS_NO_RELEASE_AUTHORITY'
    }
    else {
        try {
            if ($policy.schemaVersion -ne 1 -or $policy.currentMajor -ne 2 -or
                $policy.requiredChannel -cne 'stable' -or $policy.stage -notin @('pilot', 'required') -or
                $profiles.schemaVersion -ne 1) { throw 'UNSUPPORTED_CURRENT_POLICY' }
            $branch = Invoke-AssuranceApi "$prefix/branches/master"
            if ($branch.name -cne 'master' -or $branch.protected -ne $true) { throw 'POLICY_REF_UNVERIFIED' }
            $policySha = [string] $branch.commit.sha
            if ($policySha -notmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') { throw 'POLICY_REF_UNVERIFIED' }
            foreach ($file in @(
                @{ path = '.azurepipelines/release/policy.json'; digest = $policyDigest },
                @{ path = '.azurepipelines/assurance/profiles.json'; digest = $profileDigest }
            )) {
                $remote = Invoke-AssuranceApi "$prefix/contents/$($file.path)?ref=$policySha"
                if ($remote.encoding -cne 'base64' -or $remote.path -cne $file.path) { throw 'POLICY_INPUT_UNVERIFIED' }
                $digest = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
                    [Convert]::FromBase64String($remote.content))).ToLowerInvariant()
                if ($digest -cne $file.digest) { throw 'POLICY_INPUT_CHANGED' }
            }
            $receipt.policySourceSha = $policySha
            foreach ($definition in $workflowDefinitions) {
                $entry = [ordered]@{ workflow = $definition.path; status = 'missing'; reasons = @(); artifacts = @() }
                $receipt.workflows += $entry
                try {
                    $filename = [IO.Path]::GetFileName($definition.path)
                    $workflow = Invoke-AssuranceApi "$prefix/actions/workflows/$filename"
                    if ($workflow.path -cne $definition.path -or $workflow.state -cne 'active' -or
                        [string] $workflow.id -notmatch '^[1-9][0-9]*$') { throw 'WORKFLOW_UNVERIFIED' }
                    $entry.workflowId = [string] $workflow.id
                    $runs = Invoke-AssuranceApi "$prefix/actions/workflows/$($workflow.id)/runs?head_sha=$ExpectedSourceSha&branch=$queryBranch&per_page=100"
                    if ($runs.total_count -gt 100) { throw 'RUN_QUERY_LIMIT' }
                    $candidates = @($runs.workflow_runs | Where-Object { $null -ne $_ } |
                        Sort-Object { [long] $_.id } -Descending)
                    if ($candidates.Count -eq 0) { throw 'CURRENT_RUN_MISSING' }
                    # Never fall back to an older successful run or attempt.
                    $latest = $candidates[0]
                    $run = Invoke-AssuranceApi "$prefix/actions/runs/$($latest.id)"
                    if ($WaitTimeoutSeconds -gt 0 -and $run.status -cne 'completed') {
                        $AssuranceApiDeadline = $waitDeadline
                        Assert-AssuranceRun $run $workflow -AllowPending
                        if ($run.id -ne $latest.id -or $run.run_attempt -ne $latest.run_attempt) {
                            throw 'RUN_ATTEMPT_CHANGED'
                        }
                        $receipt.pending += @{
                            workflow = $definition.path; runId = [string] $run.id
                            attempt = [int] $run.run_attempt; status = $run.status
                        }
                        $delay = 2
                        while ($run.status -cne 'completed') {
                            if ($CancellationPath -and (Test-Path -LiteralPath $CancellationPath)) {
                                throw 'WAIT_CANCELLED'
                            }
                            $remaining = ($waitDeadline - [DateTimeOffset]::UtcNow).TotalMilliseconds
                            if ($remaining -le 0) { throw 'WAIT_DEADLINE_EXCEEDED' }
                            Start-Sleep -Milliseconds ([int] [Math]::Min($delay * 1000, $remaining))
                            $currentPolicy = Invoke-AssuranceApi "$prefix/branches/master"
                            if ($currentPolicy.commit.sha -cne $policySha -or $currentPolicy.protected -ne $true) {
                                throw 'POLICY_CHANGED_DURING_WAIT'
                            }
                            $currentRuns = Invoke-AssuranceApi "$prefix/actions/workflows/$($workflow.id)/runs?head_sha=$ExpectedSourceSha&branch=$queryBranch&per_page=100"
                            $newest = @($currentRuns.workflow_runs | Sort-Object { [long] $_.id } -Descending)
                            if ($currentRuns.total_count -gt 100 -or $newest.Count -eq 0 -or
                                $newest[0].id -ne $latest.id -or $newest[0].run_attempt -ne $latest.run_attempt) {
                                throw 'RUN_SUPERSEDED_DURING_WAIT'
                            }
                            $run = Invoke-AssuranceApi "$prefix/actions/runs/$($latest.id)"
                            Assert-AssuranceRun $run $workflow -AllowPending
                            if ($run.id -ne $latest.id -or $run.run_attempt -ne $latest.run_attempt) {
                                throw 'RUN_ATTEMPT_CHANGED'
                            }
                            $delay = [Math]::Min(15, $delay * 2)
                        }
                    }
                    Assert-AssuranceRun $run $workflow
                    if ($run.id -ne $latest.id -or $run.run_attempt -ne $latest.run_attempt) {
                        throw 'RUN_ATTEMPT_CHANGED'
                    }
                    $attempt = Invoke-AssuranceApi "$prefix/actions/runs/$($run.id)/attempts/$($run.run_attempt)"
                    Assert-AssuranceRun $attempt $workflow
                    if ($attempt.id -ne $run.id -or $attempt.run_attempt -ne $run.run_attempt -or
                        $attempt.updated_at -cne $run.updated_at -or $attempt.conclusion -cne $run.conclusion) {
                        throw 'RUN_ATTEMPT_CHANGED'
                    }
                    $entry.runId = [string] $run.id
                    $entry.attempt = [int] $run.run_attempt
                    $entry.event = $run.event
                    $entry.conclusion = $run.conclusion
                    $entry.startedAt = $run.run_started_at
                    $entry.completedAt = $run.updated_at
                    $references = @($attempt.referenced_workflows | Where-Object {
                        $_.path -ceq "$repository/$($definition.path)@$ExpectedSourceRef" -and
                        $_.ref -ceq $ExpectedSourceRef -and $_.sha -cmatch '^[0-9a-f]{40}([0-9a-f]{24})?$'
                    })
                    $definitionSha = $null
                    if ($references.Count -eq 1) {
                        if ($attempt.event -ceq 'push' -and $references[0].sha -cne $attempt.head_sha) {
                            throw 'CONTRADICTORY_DEFINITION'
                        }
                        $definitionSha = $references[0].sha
                        $entry.definitionSha = $definitionSha
                        $entry.definitionStatus = 'api-recorded'
                    }
                    elseif ($references.Count -gt 1) {
                        throw 'CONTRADICTORY_DEFINITION'
                    }
                    elseif ($attempt.event -ceq 'push') {
                        # Push definitions come from the event commit, unlike arbitrary dispatch/reusable definitions.
                        $file = Invoke-AssuranceApi "$prefix/contents/$($definition.path)?ref=$($attempt.head_sha)"
                        if ($file.type -cne 'file' -or $file.path -cne $definition.path -or
                            $file.encoding -cne 'base64' -or $file.sha -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') {
                            throw 'DEFINITION_UNVERIFIED'
                        }
                        $bytes = [Convert]::FromBase64String($file.content)
                        if ($bytes.Length -eq 0 -or $bytes.Length -ne $file.size) { throw 'DEFINITION_UNVERIFIED' }
                        $definitionSha = $attempt.head_sha
                        $entry.definitionSha = $definitionSha
                        $entry.definitionStatus = 'push-event-commit'
                        $entry.definitionBlobSha = $file.sha
                        $entry.definitionDigest = 'sha256:' + [Convert]::ToHexString(
                            [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
                    }
                    else {
                        $entry.definitionStatus = 'unverified'
                        $entry.reasons += 'DEFINITION_UNVERIFIED'
                    }
                    $runJobs = Get-AssuranceApiCollection "$prefix/actions/runs/$($run.id)/attempts/$($run.run_attempt)/jobs" 'jobs'
                    $runArtifacts = Get-AssuranceApiCollection "$prefix/actions/runs/$($run.id)/artifacts" 'artifacts'
                    $records = Join-Path $workspace ([string] $run.id)
                    $null = New-Item -ItemType Directory -Path $records
                    $observedSelection = @()
                    foreach ($profile in $profiles.profiles | Where-Object { $_.id -in $definition.profiles }) {
                        foreach ($expected in $profile.jobs) {
                            $producerJob = if ($profile.id -eq 'codeql-net10') { 'analyze' } else { 'assurance-windows' }
                            $jobName = if ($producerJob -eq 'analyze') { 'Analyze (csharp)' } else { "assurance-$($expected.id)" }
                            $name = "assurance-$($expected.id)-$($run.id)-$($run.run_attempt)-$producerJob"
                            if ($profile.id -eq 'aot-net10') {
                                $producerJob = 'aot-test'
                                $jobName = 'aot-windows-2025'
                                $name = "assurance-native-aot-$($run.id)-$($run.run_attempt)-aot-test-windows-2025"
                            }
                            $artifactReceipt = [ordered]@{ job = $expected.id; name = $name; status = 'missing'; reasons = @() }
                            $entry.artifacts += $artifactReceipt
                            try {
                                $jobs = @($runJobs | Where-Object { $_.name -ceq $jobName })
                                $artifacts = @($runArtifacts | Where-Object { $_.name -ceq $name })
                                if ($jobs.Count -ne 1 -or $artifacts.Count -ne 1) { throw 'JOB_OR_ARTIFACT_MISSING' }
                                $job = $jobs[0]
                                $artifact = $artifacts[0]
                                if ($job.run_id -ne $run.id -or $job.run_attempt -ne $run.run_attempt -or
                                    $job.head_sha -cne $ExpectedSourceSha -or $job.status -cne 'completed' -or
                                    $job.conclusion -cnotin @('success', 'failure')) { throw 'JOB_IDENTITY_MISMATCH' }
                                if ($artifact.workflow_run.id -ne $run.id -or
                                    $artifact.workflow_run.head_sha -cne $ExpectedSourceSha -or
                                    $artifact.workflow_run.head_branch -cne $sourceBranch -or $artifact.expired -ne $false -or
                                    [string] $artifact.id -notmatch '^[1-9][0-9]*$' -or
                                    $artifact.digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
                                    $artifact.size_in_bytes -le 0 -or $artifact.size_in_bytes -gt 4194304) {
                                    throw 'ARTIFACT_IDENTITY_MISMATCH'
                                }
                                $created = Assert-AssuranceTime $artifact.created_at
                                $updated = Assert-AssuranceTime $artifact.updated_at
                                if ($created -lt (Assert-AssuranceTime $job.started_at) -or
                                    $created -gt (Assert-AssuranceTime $job.completed_at) -or $updated -lt $created -or
                                    $updated -gt (Assert-AssuranceTime $run.updated_at) -or
                                    (Assert-AssuranceTime $artifact.expires_at) -le [DateTimeOffset]::UtcNow -or
                                    (Assert-AssuranceTime $job.started_at) -lt (Assert-AssuranceTime $run.run_started_at)) {
                                    throw 'STALE_ARTIFACT'
                                }
                                $zip = Join-Path $workspace "$($artifact.id).zip"
                                Invoke-AssuranceApiFile "$prefix/actions/artifacts/$($artifact.id)/zip" $zip
                                $hash = 'sha256:' + (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
                                if ($hash -cne $artifact.digest -or (Get-Item -LiteralPath $zip).Length -ne $artifact.size_in_bytes) {
                                    throw 'ARTIFACT_HASH_MISMATCH'
                                }
                                $destination = Join-Path $records $expected.id
                                $artifactReceipt.files = @(Expand-AssuranceArchive $zip $destination @('evidence.job.json', 'evidence.job.results.json'))
                                $artifactReceipt.id = [string] $artifact.id
                                $artifactReceipt.digest = $hash
                                $record = Get-Content -LiteralPath (Join-Path $destination 'evidence.job.json') -Raw | ConvertFrom-Json
                                if ($record.id -cne $expected.id -or $record.profile -cne $profile.id -or
                                    $record.producer.system -cne 'github-actions' -or $record.producer.job -cne $producerJob -or
                                    $record.producer.workflow -cne $definition.path -or
                                    $record.producer.runId -cne [string] $run.id -or $record.producer.attempt -ne $run.run_attempt -or
                                    $record.sourceSha -cne $ExpectedSourceSha -or $record.profileDigest -cne $profileDigest) {
                                    throw 'RECORD_IDENTITY_MISMATCH'
                                }
                                foreach ($field in @('configuration', 'host', 'hostTfm', 'libraryTfm', 'platform', 'filter')) {
                                    if ($record.$field -cne $profile.$field) { throw 'RECORD_SCOPE_MISMATCH' }
                                }
                                if ($record.project -cne $expected.project -or $record.shard -cne 'all' -or
                                    $record.selected -isnot [bool] -or -not $record.selected) { throw 'RECORD_SCOPE_MISMATCH' }
                                if ($record.status -eq 'completed' -and $job.conclusion -ne 'success') {
                                    throw 'JOB_CONCLUSION_MISMATCH'
                                }
                                $artifactReceipt.status = 'authenticated'
                                $observedSelection += $expected.id
                            }
                            catch {
                                # Reject the whole artifact; a sibling job cannot supply its proof.
                                $rejected = Join-Path $records $expected.id
                                if (Test-Path -LiteralPath $rejected) { Remove-Item -LiteralPath $rejected -Recurse -Force }
                                $code = [string] $_.Exception.Message
                                $artifactReceipt.reasons += $(if ($code -cmatch '^[A-Z][A-Z_]+$') { $code } else { 'ARTIFACT_UNVERIFIED' })
                            }
                        }
                    }
                    $fresh = Invoke-AssuranceApi "$prefix/actions/runs/$($run.id)"
                    Assert-AssuranceRun $fresh $workflow
                    if ($fresh.run_attempt -ne $run.run_attempt -or $fresh.updated_at -cne $run.updated_at -or
                        $fresh.conclusion -cne $run.conclusion) { throw 'RUN_ATTEMPT_CHANGED' }
                    $latestRuns = Invoke-AssuranceApi "$prefix/actions/workflows/$($workflow.id)/runs?head_sha=$ExpectedSourceSha&branch=$queryBranch&per_page=100"
                    $latestRun = @($latestRuns.workflow_runs | Sort-Object { [long] $_.id } -Descending)
                    if ($latestRuns.total_count -gt 100 -or $latestRun.Count -eq 0 -or
                        $latestRun[0].id -ne $run.id -or $latestRun[0].run_attempt -ne $run.run_attempt) {
                        throw 'RUN_SUPERSEDED_DURING_COLLECTION'
                    }
                    if ($definitionSha) {
                        $collected = Join-Path $workspace "$($run.id).component.json"
                        & (Join-Path $PSScriptRoot 'collect.ps1') -RecordsPath $records -OutputPath $collected `
                            -ExpectedSourceSha $ExpectedSourceSha -ExpectedRunId ([string] $run.id) `
                            -ExpectedAttempt $run.run_attempt -ExpectedWorkflow $definition.path `
                            -ExpectedDefinitionSha $definitionSha -ProfileIds ($definition.profiles -join ',') `
                            -ProfilesPath $profilePath
                        $part = Get-Content -LiteralPath $collected -Raw | ConvertFrom-Json -AsHashtable
                        foreach ($job in $part.jobs) {
                            for ($i = 0; $i -lt $component.jobs.Count; $i++) {
                                if ($component.jobs[$i].id -ceq $job.id) { $component.jobs[$i] = $job }
                            }
                        }
                        $component.inputIdentities += @($part.inputIdentities | Where-Object { $_.id -ne 'assurance-profile' })
                        $entry.status = 'authenticated'
                    }
                    else {
                        foreach ($job in $component.jobs | Where-Object { $_.id -in $observedSelection }) {
                            # API-proven selection is distinct from verified execution/definition identity.
                            $job.selected = $true
                            $job.sourceSha = $ExpectedSourceSha
                        }
                    }
                }
                catch {
                    $code = [string] $_.Exception.Message
                    $entry.status = 'missing'
                    $entry.reasons += $(if ($code -cmatch '^[A-Z][A-Z_]+$') { $code } else { 'WORKFLOW_UNVERIFIED' })
                }
            }
            $current = Invoke-AssuranceApi "$prefix/branches/master"
            if ($current.commit.sha -cne $policySha -or $current.protected -ne $true) {
                throw 'POLICY_CHANGED_DURING_COLLECTION'
            }
        }
        catch {
            $code = [string] $_.Exception.Message
            $receipt.reasons += $(if ($code -cmatch '^[A-Z][A-Z_]+$') { $code } else { 'METADATA_UNAVAILABLE' })
            # A policy/API failure cannot leave previously collected credit behind.
            $component = Get-Content -LiteralPath $componentPath -Raw | ConvertFrom-Json -AsHashtable
        }
    }
    foreach ($key in @('selected', 'completed', 'failed', 'missing', 'notApplicable')) { $component[$key] = 0 }
    foreach ($job in $component.jobs) {
        if ($job.selected) { $component.selected++ }
        $component[$job.status]++
    }
    $receipt.counts = @{
        expected = $component.expected; selected = $component.selected; completed = $component.completed
        failed = $component.failed; missing = $component.missing; notApplicable = $component.notApplicable
    }
    $schema = Get-Content -LiteralPath (Join-Path $RepositoryRoot '.azurepipelines/release/evidence.schema.json') -Raw |
        ConvertFrom-Json -AsHashtable
    $schema['$ref'] = '#/$defs/assurance'
    foreach ($key in @('required', 'properties', 'additionalProperties')) { $schema.Remove($key) }
    $json = $component | ConvertTo-Json -Depth 30
    if (-not (Test-Json -Json $json -Schema ($schema | ConvertTo-Json -Depth 100))) {
        throw 'ASSURANCE_SCHEMA_INVALID'
    }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
    $null = New-Item -ItemType Directory -Path $parent -Force
    $json | Set-Content -LiteralPath $OutputPath -Encoding utf8
    foreach ($job in $component.jobs | Where-Object { $_.resultDocument }) {
        Copy-Item -LiteralPath (Join-Path $workspace $job.resultDocument) -Destination (Join-Path $parent $job.resultDocument)
    }
    $receipt.componentDigest = 'sha256:' + (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath ([IO.Path]::ChangeExtension($OutputPath, 'metadata.json')) -Encoding utf8
    Write-Host "Release assurance: expected=$($component.expected) completed=$($component.completed) failed=$($component.failed) missing=$($component.missing) N/A=$($component.notApplicable)."
}
finally {
    Remove-Item -LiteralPath $workspace -Recurse -Force
}
