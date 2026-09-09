# Copyright (c) OPC Foundation, Inc. All rights reserved.
# Licensed under the MIT License. See LICENSE.txt in the project root for license information.
<#
.SYNOPSIS
Runner-local NuGet capture, public sidecar aggregation and protected preflight.
.DESCRIPTION
Only new evidence failures are translated into explicit incomplete pilot reports.
Never invoke baseline build, signing, package validation or push inside this wrapper.
No stage override exists. Capture output and tool logs must not be uploaded.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('PrepareTool', 'Capture', 'Sidecars', 'Aggregate', 'Assurance', 'Preflight', 'Receipt', 'VerifyFeed', 'Attach')]
    [string]$Operation,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Packages,
    [string]$Inputs,
    [string]$Output,
    [string]$Work,
    [string]$Tool,
    [string]$Version,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$Solution,
    [string]$Expected,
    [string]$VerificationBundle,
    [string]$TrustPolicy = $env:OPCUA_RELEASE_TRUST_POLICY,
    [ValidateSet('GitHubPackages', 'NugetOrg')][string]$Destination = 'GitHubPackages',
    [string]$File,
    [ValidateSet('not-attempted', 'attempting', 'push-accepted-or-duplicate', 'push-failed')]
    [string]$State = 'not-attempted'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'nuget-evidence-functions.ps1')
if (-not $Work) { $Work = Join-Path ([IO.Path]::GetTempPath()) "nuget-evidence-$PID" }
$null = New-Item -ItemType Directory -Force -Path $Work
if (-not $Tool) {
    $Tool = Join-Path $RepositoryRoot 'tools\Opc.Ua.ReleaseEvidence\bin\Release\net10.0\Opc.Ua.ReleaseEvidence.dll'
}
if (-not $Output) { $Output = Join-Path $Work "$Operation.json" }
$blocking = $false
$policy = $null
$failureControl = 'EVIDENCE_SCHEMA'
try {
    $policy = Get-NugetPolicy $RepositoryRoot
    $policyDigest = Get-NugetDigest (Join-Path $RepositoryRoot '.azurepipelines\release-policy.json')
    if ($Operation -eq 'PrepareTool') {
        & dotnet build (Join-Path $RepositoryRoot 'tools\Opc.Ua.ReleaseEvidence\Opc.Ua.ReleaseEvidence.csproj') `
            -c Release -f net10.0 --nologo *> (Join-Path $Work 'tool-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Evidence tool build failed; diagnostics remain runner-local.' }
        Write-NugetJson @{ schemaVersion = 1; status = 'prepared'; verificationCredit = $false } $Output
    }
    elseif ($Operation -eq 'Capture') {
        $context = New-NugetContext $RepositoryRoot $Version
        if (-not $Solution) { $Solution = Join-Path $RepositoryRoot 'preview-pack.slnx' }
        $request = New-NugetCaptureRequest $context $Configuration $RepositoryRoot $Solution
        Write-NugetJson $request (Join-Path $Work 'capture-request.json')
        Write-NugetJson $context (Join-Path $Work 'context.json')
        $null = Invoke-NugetEvidenceTool $Tool @('capture', '--repository-root', $RepositoryRoot,
            '--request', (Join-Path $Work 'capture-request.json'), '--output', $Output) (Join-Path $Work 'capture.log')
    }
    elseif ($Operation -eq 'Sidecars') {
        $context = New-NugetContext $RepositoryRoot $Version
        Write-NugetJson $context (Join-Path $Work 'context.json')
        $local = Join-Path $Work 'sidecars'
        $null = Invoke-NugetEvidenceTool $Tool @('nuget', '--repository-root', $RepositoryRoot, '--packages', $Packages,
            '--inputs', $Inputs, '--context', (Join-Path $Work 'context.json'), '--output', $local) `
            (Join-Path $Work 'nuget.log')
        $envelope = Read-NugetJson (Join-Path $local 'release-evidence.json')
        Assert-NugetAssessment $envelope.assessment
        Assert-NugetPublicProjection $local
        $null = New-Item -ItemType Directory -Force -Path $Output
        foreach ($document in $envelope.documents) {
            Copy-NugetDocument $local $Output $document.path $document.digest
        }
        Copy-NugetDocument $local $Output 'release-evidence.json' (Get-NugetDigest (Join-Path $local 'release-evidence.json'))
        Write-NugetJson (New-NugetProvenance $context $Configuration) (Join-Path $Output 'pack-provenance.json')
        Write-NugetJson @{ schemaVersion = 1; status = 'incomplete'; scope = $Configuration
            detail = 'Configuration-local sidecars; full NuGet membership requires the aggregate.'
            unmetControls = @($envelope.assessment.unmetControls) } (Join-Path $Output 'pipeline-report.json')
    }
    elseif ($Operation -eq 'Aggregate') {
        $manifest = Read-NugetJson (Join-Path $Packages 'release-manifest.json')
        $context = New-NugetContext $RepositoryRoot $manifest.packageVersion
        $envelope = Merge-NugetSidecars $RepositoryRoot $Inputs $Packages $Output $context
        foreach ($configuration in @('Release', 'Debug')) {
            $root = Join-Path $Inputs "opcua-$configuration-$($context.producer.runId)\evidence"
            $predicate = Read-NugetJson (Join-Path $root 'pack-provenance.json')
            if ($predicate.buildDefinition.resolvedDependencies[0].digest.gitCommit -cne $context.source.actualSha) {
                Stop-NugetEvidence 'Pack provenance source differs from the producing source.' 1
            }
            Copy-NugetDocument $root (Join-Path $Output $configuration) 'pack-provenance.json' `
                (Get-NugetDigest (Join-Path $root 'pack-provenance.json'))
            $checksums = @($envelope.artifacts | Where-Object configuration -CEQ $configuration | ForEach-Object {
                $digest = $_.digest.Substring(7)
                $archive = @($manifest.archives | Where-Object sha256 -CEQ $digest)
                if ($archive.Count -ne 1) { throw 'Ambiguous attestation subject.' }
                "$digest  $($archive[0].file)"
            })
            if ($checksums.Count -eq 0 -or $checksums.Count -gt 1024) { throw 'Invalid attestation subject count.' }
            $checksums | Set-Content -LiteralPath (Join-Path $Output "$configuration\subjects.sha256") -Encoding utf8NoBOM
        }
        Assert-NugetPublicProjection $Output
        foreach ($configuration in @('Release', 'Debug')) {
            Assert-NugetPublicProjection $Output "$configuration/release-evidence.json"
        }
        # This readiness marker is created only after the complete transitive projection is checked.
        Write-NugetJson $context (Join-Path $Output 'producer-context.json')
    }
    elseif ($Operation -eq 'Assurance') {
        $manifest = Read-NugetJson (Join-Path $Packages 'release-manifest.json')
        $component = Join-Path $Output 'assurance.json'
        & (Join-Path $PSScriptRoot 'get-release-assurance.ps1') `
            -RepositoryRoot $RepositoryRoot -ExpectedSourceSha $manifest.commit -ExpectedSourceRef $manifest.ref `
            -OutputPath $component -WorkDirectory $Work -ReviewVerificationBundle $VerificationBundle `
            -TrustPolicy $TrustPolicy
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $component)) {
            throw 'Assurance collection did not produce its component.'
        }
        Assert-NugetPublicJson $component
        Assert-NugetPublicJson ([IO.Path]::ChangeExtension($component, 'metadata.json'))
    }
    elseif ($Operation -eq 'Preflight') {
        $blocking = $policy.stage -ceq 'required'
        $manifest = Read-NugetJson (Join-Path $Packages 'release-manifest.json')
        $channel = Get-NugetChannel $manifest.packageVersion
        $blocking = $policy.stage -ceq 'required' -and $channel -ceq 'stable' -and
            [int]$manifest.packageVersion.Split('.')[0] -eq $policy.currentMajor
        $context = if ($Expected) { Read-NugetJson $Expected } else {
            New-NugetContext $RepositoryRoot $manifest.packageVersion
        }
        if ($Destination -ceq 'NugetOrg' -and (
            $env:SOURCE_SHA -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$' -or
            $env:SOURCE_RUN_ID -cnotmatch '^[1-9][0-9]*$' -or
            $env:SOURCE_ATTEMPT -cnotmatch '^[1-9][0-9]*$' -or
            $env:SOURCE_DEFINITION_SHA -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$' -or
            $env:SOURCE_REF -cnotmatch '^refs/heads/release/.+$' -or
            $manifest.commit -cne $env:SOURCE_SHA -or $manifest.ref -cne $env:SOURCE_REF -or
            $manifest.runId -cne $env:SOURCE_RUN_ID -or
            $context.source.actualSha -cne $env:SOURCE_SHA -or
            $context.source.actualRef -cne $env:SOURCE_REF -or
            $context.producer.runId -cne $env:SOURCE_RUN_ID -or
            $context.producer.attempt -ne [int]$env:SOURCE_ATTEMPT -or
            $context.producer.definitionSha -cne $env:SOURCE_DEFINITION_SHA)) {
            $blocking = $true
            $failureControl = 'SOURCE_IDENTITY'
            throw 'Promotion expectations contradict the authenticated source run.'
        }
        if ($context.source.repository -cne 'OPCFoundation/UA-.NETStandard' -or
            $context.producer.workflow -cne '.github/workflows/nuget-publish.yml' -or
            $context.source.repository -cne $manifest.repository -or $context.source.actualSha -cne $manifest.commit -or
            $context.source.actualRef -cne $manifest.ref -or
            $context.producer.runId -cne $manifest.runId -or $context.producer.workflow -cne $manifest.workflow -or
            $context.release.version -cne $manifest.packageVersion) {
            $blocking = $true
            $failureControl = 'SOURCE_IDENTITY'
            throw 'Trusted expectations differ from v1.'
        }
        $context.policyDigest = Get-NugetDigest (Join-Path $RepositoryRoot '.azurepipelines\release-policy.json')
        $context.release.channel = $channel
        $metadata = Read-NugetJson (Join-Path $Packages 'archive-metadata.json')
        $context.artifacts = @($metadata.artifacts)
        $expectedPath = Join-Path $Work 'expected-release.json'
        Write-NugetJson $context $expectedPath
        $evidence = Join-Path $Packages 'evidence\release-evidence.json'
        if (-not (Test-Path -LiteralPath $evidence)) { $evidence = Join-Path $Packages 'release-manifest.json' }
        else {
            if (-not $VerificationBundle) {
                $savedBundle = Join-Path $Packages 'verification-bundle.json'
                if (Test-Path -LiteralPath $savedBundle) { $VerificationBundle = $savedBundle }
            }
            $evidence = Get-NugetEvaluationEvidence $RepositoryRoot $Packages $Work $context
        }
        $arguments = @('evaluate', '--repository-root', $RepositoryRoot,
            '--evidence', $evidence, '--expected', $expectedPath, '--artifacts-root', $Packages, '--output', $Output)
        if ($VerificationBundle) { $arguments += @('--verification-bundle', $VerificationBundle) }
        if ($TrustPolicy) {
            Assert-NugetIndependentTrust $TrustPolicy @($RepositoryRoot, $Packages, $Work)
            $arguments += @('--trust-policy', $TrustPolicy)
        }
        $code = Invoke-NugetEvidenceTool $Tool $arguments `
            (Join-Path $Work 'evaluate.log')
        $report = Read-NugetJson $Output
        if ($report.status -cnotin @('complete', 'incomplete') -or
            $report.baselineFailed -isnot [bool] -or $report.blocking -isnot [bool]) {
            Stop-NugetEvidence 'The reader did not produce a valid decision.' 2
        }
        if ($report.baselineFailed -or $report.blocking -or $code -eq 1 -or
            ($blocking -and $report.status -cne 'complete')) { exit 1 }
        Write-Host "NuGet preflight: $($report.status), stage=$($policy.stage), channel=$channel."
    }
    elseif ($Operation -eq 'Receipt') {
        Write-NugetReceipt $Output $Packages $Destination $File $State -PolicyDigest $policyDigest
    }
    elseif ($Operation -eq 'VerifyFeed') {
        if ($Destination -cne 'NugetOrg') { throw 'Only the public NuGet.org feed has a read-only delivery adapter.' }
        $receipt = Get-NugetDeliveryState $Output
        foreach ($entry in $receipt.packages | Where-Object state -EQ 'push-accepted-or-duplicate') {
            $id = $entry.id.ToLowerInvariant()
            $versionText = $entry.version.ToLowerInvariant()
            if ($id -notmatch '^[a-z0-9._-]+$' -or $versionText -notmatch '^[a-z0-9.+-]+$') {
                throw 'Invalid feed package identity.'
            }
            $download = Join-Path $Work "$id.$versionText.nupkg"
            try {
                Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$versionText/$id.$versionText.nupkg" `
                    -OutFile $download -TimeoutSec 120 -MaximumRetryCount 0
            }
            catch [Microsoft.PowerShell.Commands.HttpResponseException] {
                Write-NugetReceipt $Output $Packages $Destination $entry.file 'delivery-unverified' -PolicyDigest $policyDigest
                Write-Host '::warning::Feed retrieval unavailable; accepted push is not delivery verification.'
                continue
            }
            catch [System.Net.Http.HttpRequestException] {
                Write-NugetReceipt $Output $Packages $Destination $entry.file 'delivery-unverified' -PolicyDigest $policyDigest
                Write-Host '::warning::Feed retrieval unavailable; accepted push is not delivery verification.'
                continue
            }
            $author = Resolve-NugetPath $Packages $entry.file
            if ((Get-NugetDigest $author) -cne $entry.authorDigest) {
                Stop-NugetEvidence 'Author archive no longer matches immutable release evidence.' 1
            }
            $contentReport = Join-Path $Work "$id.content.json"
            $contentCode = Invoke-NugetEvidenceTool $Tool @(
                'verify-delivery', '--author', $author, '--delivered', $download, '--output', $contentReport) `
                (Join-Path $Work "$id.content.log")
            $content = Read-NugetJson $contentReport
            if ($content.schemaVersion -ne 1 -or $content.contentPreserved -isnot [bool] -or
                $content.authorSignaturePreserved -isnot [bool] -or
                $content.status -cnotin @('content-matched', 'content-mismatch')) {
                Stop-NugetEvidence 'The content reader returned a malformed decision.' 2
            }
            $contentEvidence = $Output + '.evidence'
            $null = New-Item -ItemType Directory -Force -Path $contentEvidence
            Copy-NugetDocument $Work $contentEvidence (Split-Path -Leaf $contentReport) (Get-NugetDigest $contentReport)
            if ($contentCode -eq 1 -or $content.status -cne 'content-matched' -or
                -not $content.contentPreserved -or -not $content.authorSignaturePreserved -or
                $content.authorArchiveDigest -cne $entry.authorDigest -or
                $content.deliveredArchiveDigest -cne (Get-NugetDigest $download)) {
                Write-NugetReceipt $Output $Packages $Destination $entry.file 'delivery-content-mismatch' `
                    -PolicyDigest $policyDigest -ContentReportDigest (Get-NugetDigest $contentReport)
                Stop-NugetEvidence 'Delivered content or original author-signature relationship differs.' 1
            }
            $feedDirectory = Join-Path $Work "feed-$id"
            $null = New-Item -ItemType Directory -Force -Path $feedDirectory
            Move-Item -LiteralPath $download -Destination (Resolve-NugetPath $feedDirectory $entry.file)
            try {
                & (Join-Path $PSScriptRoot 'validate-nuget-package-set.ps1') -PackageDirectory $feedDirectory `
                    -ManifestPath (Join-Path $feedDirectory 'verified.json') -ExpectedVersion $entry.version `
                    -VerifySignatures *> (Join-Path $Work "verify-$id.log")
            }
            catch { Stop-NugetEvidence 'Delivered NuGet package/signature baseline validation failed.' 1 }
            if ($LASTEXITCODE -ne 0) { Stop-NugetEvidence 'Delivered NuGet signature validation failed.' 1 }
            $verified = Read-NugetJson (Join-Path $feedDirectory 'verified.json')
            if ($verified.archives.Count -ne 1 -or $verified.archives[0].id -cne $entry.id) {
                Stop-NugetEvidence 'Delivered package identity differs.' 1
            }
            $savedBundle = if ($VerificationBundle) { $VerificationBundle } else {
                Join-Path $Packages 'verification-bundle.json'
            }
            if ($TrustPolicy -and (Test-Path -LiteralPath $savedBundle)) {
                Assert-NugetIndependentTrust $TrustPolicy @($RepositoryRoot, $Packages, $Work)
                $approvedReport = Join-Path $Work "$id.approved.json"
                $approvedCode = Invoke-NugetEvidenceTool $Tool @(
                    'verify-delivery-approved', '--repository-root', $RepositoryRoot,
                    '--evidence', (Join-Path $Packages 'evidence\release-evidence.json'),
                    '--verification-bundle', $savedBundle, '--trust-policy', $TrustPolicy,
                    '--author', $author, '--delivered', (Resolve-NugetPath $feedDirectory $entry.file),
                    '--output', $approvedReport) (Join-Path $Work "$id.approved.log")
                $approval = Read-NugetJson $approvedReport
                if ($approval.baselineFailed -isnot [bool] -or
                    $approval.signatureVerificationPerformed -isnot [bool]) {
                    Stop-NugetEvidence 'The approval verifier returned malformed evidence.' 2
                }
                Copy-NugetDocument $Work $contentEvidence (Split-Path -Leaf $approvedReport) (Get-NugetDigest $approvedReport)
                if ($approval.baselineFailed) {
                    Stop-NugetEvidence 'Delivered primary author signature failed its approved verification.' 1
                }
                if ($approvedCode -eq 0 -and $approval.status -ceq 'artifact-delivery-verified' -and
                    $approval.signatureVerificationPerformed -and $approval.authorDigest -ceq $entry.authorDigest -and
                    $approval.deliveredDigest -ceq $content.deliveredArchiveDigest) {
                    Write-NugetReceipt $Output $Packages $Destination $entry.file `
                        'feed-content-and-approved-signatures-verified' $content.deliveredArchiveDigest `
                        'independently-approved-primary-author-and-delivered-signatures' `
                        -PolicyDigest $policyDigest -ContentReportDigest (Get-NugetDigest $approvedReport)
                    continue
                }
            }
            Write-NugetReceipt $Output $Packages $Destination $entry.file `
                'feed-content-matched-signer-approval-pending' $content.deliveredArchiveDigest `
                'content-primary-signature-preserved-native-signatures-valid-not-approved' `
                -PolicyDigest $policyDigest -ContentReportDigest (Get-NugetDigest $contentReport)
            Write-Host '::warning::Delivered content matched; approved signer policy and symbol delivery remain unresolved.'
        }
    }
    elseif ($Operation -eq 'Attach') {
        $manifest = Read-NugetJson (Join-Path $Packages 'release-manifest.json')
        if ($manifest.repository -cne 'OPCFoundation/UA-.NETStandard' -or
            [int]$manifest.packageVersion.Split('.')[0] -ne $policy.currentMajor) { throw 'Not a current official release.' }
        $tag = $null
        $release = $null
        foreach ($candidateTag in @($manifest.packageVersion, "v$($manifest.packageVersion)")) {
            $json = & gh api "repos/$($manifest.repository)/releases/tags/$candidateTag" `
                2> (Join-Path $Work 'release-lookup.log')
            if ($LASTEXITCODE -eq 0) {
                $tag = $candidateTag
                $release = $json | ConvertFrom-Json -AsHashtable
                break
            }
        }
        if ($null -eq $release) {
            Write-NugetFinding $Output 'Attach' 'PUBLIC_EVIDENCE_SAFE'
            exit 0
        }
        $commitJson = & gh api "repos/$($manifest.repository)/commits/$tag" 2> (Join-Path $Work 'release-tag.log')
        if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve existing release tag.' }
        $commit = $commitJson | ConvertFrom-Json -AsHashtable
        if ($release.draft -or $release.tag_name -cne $tag -or $commit.sha -cne $manifest.commit) {
            throw 'Existing release is not bound to this source/version.'
        }
        $bundle = Join-Path $Work "nuget-evidence-$($manifest.packageVersion)-$($manifest.runId).zip"
        $public = Join-Path $Work 'public-attachment'
        Copy-NugetPublicAttachment $Packages $public
        Compress-Archive -LiteralPath $public -DestinationPath $bundle
        & gh release upload $tag $bundle --repo $manifest.repository *> (Join-Path $Work 'release-upload.log')
        if ($LASTEXITCODE -ne 0) { throw 'Release evidence attachment failed (existing assets are not overwritten).' }
        Write-NugetJson @{ schemaVersion = 1; status = 'attached'; tag = $tag; digest = Get-NugetDigest $bundle } $Output
    }
}
catch {
    # Fail closed if the current protected policy cannot be loaded. Never print exception input/path values.
    if ($Operation -eq 'Preflight' -and $null -eq $policy) { $blocking = $true }
    $reportPath = if ($Operation -in @('Capture', 'Sidecars', 'Aggregate', 'Assurance', 'Receipt', 'VerifyFeed')) {
        $Output + '.incomplete.json'
    } else { $Output }
    $exitCode = if ($_.Exception.Data.Contains('NugetExitCode')) {
        [int]$_.Exception.Data['NugetExitCode']
    } elseif ($blocking) { 1 } else { 0 }
    Write-NugetFinding $reportPath $Operation $failureControl ($blocking -or $exitCode -ne 0) $exitCode
    if ($exitCode -ne 0) { exit $exitCode }
}
exit 0
