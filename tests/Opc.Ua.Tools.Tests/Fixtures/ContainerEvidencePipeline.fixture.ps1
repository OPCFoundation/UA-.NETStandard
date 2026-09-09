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

param([Parameter(Mandatory)][string]$Scenario)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
. (Join-Path $PSScriptRoot 'FixtureWorkspace.ps1')
$fixture = Join-Path (Get-FixturePhysicalTempDirectory) "container-fixture-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path (Join-Path $fixture '.azurepipelines')

function Write-Json($Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
}

function Add-Blob($Value, [string]$Type) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 100 -Compress))
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    [IO.File]::WriteAllBytes((Join-Path $fixture "layout/blobs/sha256/$hash"), $bytes)
    @{ digest = "sha256:$hash"; size = $bytes.Length; mediaType = $Type }
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "$Scenario : $Message" }
}

try {
    foreach ($name in @('release-policy.json', 'release-artifacts.json')) {
        Copy-Item -LiteralPath (Join-Path $root ".azurepipelines/$name") -Destination (Join-Path $fixture '.azurepipelines')
    }
    $policyPath = Join-Path $fixture '.azurepipelines/release-policy.json'
    $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json -AsHashtable
    if ($Scenario -in @('required-stable', 'required-four-part-version', 'required-context-version', 'required-preview')) {
        $policy.stage = 'required'
        Write-Json $policy $policyPath
    }
    $group = if ($Scenario -in @('missing-platform', 'valid-dual-platform', 'catalog-membership')) {
        'containers'
    } else { 'pump' }
    $image = if ($group -eq 'containers') { 'refserver' } else { 'pumpdeviceintegrationserver' }
    $id = if ($group -eq 'containers') {
        "ghcr.io/opcfoundation/uanetstandard/$image"
    } else { "ghcr.io/opcfoundation/$image" }
    $sha = 'a' * 40
    $version = if ($Scenario -eq 'required-preview') { '2.0.0-preview.1' }
        elseif ($Scenario -eq 'required-four-part-version') { '2.0.0.42' } else { '2.0.0' }
    $workflow = if ($group -eq 'containers') {
        '.github/workflows/docker-image.yml'
    } else { '.github/workflows/pump-device-integration-server-docker.yml' }
    $context = @{
        source = @{ repository = 'OPCFoundation/UA-.NETStandard'; actualSha = $sha; actualRef = 'refs/heads/master'; trackedClean = $true }
        producer = @{
            system = 'github-actions'; workflow = $workflow; definitionSha = ('b' * 40)
            runId = '42'; attempt = 1; job = 'fixture'; tools = @()
        }
        release = @{ group = $group; version = $version; channel = 'development' }
        policyDigest = 'sha256:' + (Get-FileHash -LiteralPath $policyPath).Hash.ToLowerInvariant()
        artifacts = @()
    }
    Write-Json $context (Join-Path $fixture 'context.json')
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'layout/blobs/sha256')
    $manifestType = 'application/vnd.oci.image.manifest.v1+json'
    $config = Add-Blob @{
        architecture = 'amd64'; os = 'linux'
        config = @{ Labels = @{ 'org.opencontainers.image.revision' = $sha; 'org.opencontainers.image.version' = $version } }
    } 'application/vnd.oci.image.config.v1+json'
    $layer = Add-Blob @{ synthetic = 'fixture layer, not a runnable container' } 'application/vnd.oci.image.layer.v1.tar'
    $runnable = Add-Blob @{
        schemaVersion = 2; mediaType = $manifestType; config = $config; layers = @($layer)
    } $manifestType
    $runnable.platform = @{ os = 'linux'; architecture = 'amd64' }
    $predicateSubject = if ($Scenario -eq 'wrong-attestation-subject') { 'c' * 64 } else { $runnable.digest.Substring(7) }
    $sbom = Add-Blob @{
        _type = 'https://in-toto.io/Statement/v0.1'; predicateType = 'https://spdx.dev/Document'
        subject = @(@{ name = $id; digest = @{ sha256 = $predicateSubject } })
        predicate = @{
            spdxVersion = $(if ($Scenario -eq 'unsupported-spdx') { 'SPDX-2.2' } else { 'SPDX-2.3' })
            SPDXID = 'SPDXRef-DOCUMENT'; dataLicense = 'CC0-1.0'
            name = 'synthetic'; documentNamespace = 'https://example.invalid/sbom'
            creationInfo = @{ created = '2026-09-08T00:00:00Z'; creators = @('Tool: synthetic-fixture') }
            packages = @(@{ SPDXID = 'SPDXRef-Package'; name = 'synthetic'; downloadLocation = 'NOASSERTION' })
            documentDescribes = @('SPDXRef-Package')
        }
    } 'application/vnd.in-toto+json'
    $sbom.annotations = @{ 'in-toto.io/predicate-type' = 'https://spdx.dev/Document' }
    $nativePath = Join-Path $fixture "layout/blobs/sha256/$($sbom.digest.Substring(7))"
    $nativeBefore = (Get-FileHash -LiteralPath $nativePath).Hash
    $attestation = Add-Blob @{
        schemaVersion = 2; mediaType = $manifestType; config = $config; layers = @($sbom)
    } $manifestType
    $attestation.platform = @{ os = 'unknown'; architecture = 'unknown' }
    $attestation.annotations = @{
        'vnd.docker.reference.type' = 'attestation-manifest'
        'vnd.docker.reference.digest' = $runnable.digest
    }
    if ($Scenario -eq 'attestation-descriptor') { $attestation.platform = @{ os = 'linux'; architecture = 'amd64' } }
    $children = @($runnable, $attestation)
    if ($Scenario -eq 'valid-dual-platform') {
        $armConfig = Add-Blob @{
            architecture = 'arm64'; os = 'linux'
            config = @{ Labels = @{ 'org.opencontainers.image.revision' = $sha; 'org.opencontainers.image.version' = $version } }
        } 'application/vnd.oci.image.config.v1+json'
        $arm = Add-Blob @{
            schemaVersion = 2; mediaType = $manifestType; config = $armConfig; layers = @($layer)
        } $manifestType
        $arm.platform = @{ os = 'linux'; architecture = 'arm64'; variant = 'v8' }
        $children += $arm
    }
    $index = Add-Blob @{
        schemaVersion = 2; mediaType = 'application/vnd.oci.image.index.v1+json'
        manifests = $children
    } 'application/vnd.oci.image.index.v1+json'
    $layout = if ($Scenario -eq 'path-traversal') { '../private-layout' } else { 'layout' }
    Write-Json @{ images = @(@{ id = $id; layout = $layout; rootDigest = $index.digest }) } (Join-Path $fixture 'request.json')
    if ($Scenario -eq 'wrong-digest') {
        [IO.File]::AppendAllText((Join-Path $fixture "layout/blobs/sha256/$($runnable.digest.Substring(7))"), ' ')
    }
    $operation = switch ($Scenario) {
        { $_ -in @('active-incomplete', 'required-stable', 'required-four-part-version', 'required-preview') } {
            'Preflight'; break
        }
        'record-request' { 'Record'; break }
        { $_ -in @('private-path-filter', 'private-field-filter', 'baseline-failure') } { 'Status'; break }
        'pr-no-publish' { 'Collect'; break }
        { $_ -in @('catalog-membership', 'pump-membership') } { 'Aggregate'; break }
        default { 'VerifyLocal' }
    }
    $work = Join-Path $fixture 'work'
    $null = New-Item -ItemType Directory -Path $work
    if ($Scenario -eq 'recorded-root-mismatch') {
        Write-Json @{
            collectorState = 'unavailable'; signingState = 'not-attempted'; rootDigest = ('sha256:' + ('e' * 64))
            artifacts = @(); attestations = @()
        } (Join-Path $work 'state.json')
    }
    if ($Scenario -in @('private-path-filter', 'private-field-filter')) {
        $privateState = @{
            collectorState = 'unavailable'; signingState = 'not-attempted'; artifacts = @(); attestations = @()
            rawLog = 'C:\private\confidential-corpus\secret.log'; restrictedDigest = ('d' * 64)
        }
        if ($Scenario -eq 'private-field-filter') {
            $privateState.signingState = 'C:\private\confidential-corpus\secret.log'
            $privateState.toolState = 'C:\private\confidential-corpus\secret.log'
        }
        Write-Json $privateState (Join-Path $work 'state.json')
    }
    if ($Scenario -eq 'pr-no-publish') {
        foreach ($name in @('docker-image.yml', 'pump-device-integration-server-docker.yml')) {
            $workflowText = Get-Content -LiteralPath (Join-Path $root ".github/workflows/$name") -Raw
            Assert-True ($workflowText.Contains("push: `${{ github.event_name != 'pull_request' }}")) `
                'A workflow can push on a PR.'
            Assert-True (-not $workflowText.Contains('provenance: false')) 'Workflow disabled native provenance.'
        }
        $env:GITHUB_ACTIONS = 'true'
        $env:GITHUB_EVENT_NAME = 'pull_request'
        $env:GITHUB_REPOSITORY = 'OPCFoundation/UA-.NETStandard'
        $env:GITHUB_REF = 'refs/pull/1/merge'
        # No credential is supplied, and Collect must reject before contacting a registry.
        Write-Json @{
            collectorState = 'unavailable'; signingState = 'not-attempted'; rootDigest = $index.digest
            artifacts = @(); attestations = @()
        } (Join-Path $work 'state.json')
    }
    $buildState = if ($Scenario -eq 'baseline-failure') { 'failure' } else { 'success' }
    $output = Join-Path $fixture 'status.json'
    $versionArgument = if ($Scenario -eq 'required-context-version') { '' } else { $version }
    & pwsh -NoLogo -NoProfile -File (Join-Path $root '.azurepipelines/container-evidence.ps1') `
        -Operation $operation -RepositoryRoot $fixture -Group $group -Image $image -Version $versionArgument `
        -RootDigest $index.digest `
        -Work $work -Output $output -Request (Join-Path $fixture 'request.json') `
        -Context (Join-Path $fixture 'context.json') -Tool (Join-Path $fixture 'deliberately-absent-tool.dll') `
        -BuildState $buildState
    $code = $LASTEXITCODE
    $expected = if ($Scenario -in @(
        'required-stable', 'required-four-part-version', 'required-context-version', 'baseline-failure')) { 1 }
        elseif ($Scenario -in @('wrong-digest', 'missing-platform', 'attestation-descriptor',
            'wrong-attestation-subject', 'pr-no-publish', 'path-traversal', 'recorded-root-mismatch',
            'private-field-filter', 'unsupported-spdx')) { 2 }
        elseif ($operation -in @('Preflight', 'Status', 'VerifyLocal', 'Record') -and $Scenario -ne 'required-preview') { 1 }
        else { 0 }
    Assert-True ($code -eq $expected) "Exit $code; expected $expected."
    $text = Get-Content -LiteralPath $output -Raw
    $status = $text | ConvertFrom-Json
    Assert-True ($status.status -ceq 'incomplete') 'No fixture can establish complete release evidence.'
    Assert-True (-not $status.externalAuthorizationVerified) 'Offline fixtures cannot verify authorization.'
    if ($Scenario -eq 'valid-pump') {
        Assert-True ($status.observedSubjects.Count -eq 2) 'Expected root index and one runnable manifest.'
        Assert-True (@($status.observedSubjects | Where-Object kind -EQ 'oci-manifest').Count -eq 1) 'Attestation counted as platform.'
        Assert-True ($status.observedAttestations.Count -eq 1) 'Native SBOM descriptor not recorded.'
        Assert-True ($status.observedAttestations[0].subjectDigest -ceq $runnable.digest) 'SBOM subject is not the runnable digest.'
        Assert-True ($nativeBefore -ceq (Get-FileHash -LiteralPath $nativePath).Hash) 'Native SPDX bytes were modified.'
    }
    if ($Scenario -eq 'valid-dual-platform') {
        $platforms = @($status.observedSubjects | Where-Object kind -EQ 'oci-manifest' | ForEach-Object platforms)
        Assert-True ($platforms.Count -eq 2 -and $platforms -contains 'linux/arm64/v8') `
            'OCI default arm64/v8 variant was not reconciled.'
        Assert-True ($status.unmetControls -contains 'INVENTORY_COMPLETE') `
            'A missing arm64 SBOM must not establish complete inventory.'
    }
    if ($Scenario -eq 'record-request') {
        $recorded = Get-Content -LiteralPath (Join-Path $work 'oci-request.json') -Raw | ConvertFrom-Json
        $identity = Get-Content -LiteralPath (Join-Path $work 'oci-context.json') -Raw | ConvertFrom-Json
        Assert-True ($recorded.images.Count -eq 1 -and $recorded.images[0].id -ceq $id -and
            $recorded.images[0].rootDigest -ceq $index.digest -and $recorded.images[0].layout -ceq 'layout') `
            'Record did not produce the tool OCI request shape.'
        Assert-True ($identity.source.actualSha -ceq $sha -and
            $identity.producer.definitionSha -ceq ('b' * 40)) 'Source and workflow SHAs were conflated.'
    }
    if ($Scenario -eq 'aggregate-observed') {
        $env:GITHUB_SHA = $sha
        $env:GITHUB_RUN_ID = '42'
        $env:GITHUB_RUN_ATTEMPT = '1'
        & pwsh -NoLogo -NoProfile -File (Join-Path $root '.azurepipelines/container-evidence.ps1') `
            -Operation Status -RepositoryRoot $fixture -Group $group -Image $image -Version $version `
            -Work $work -Output $output -BuildState success
        Assert-True ($LASTEXITCODE -eq 1) 'Incomplete stable evidence must remain blocking after serialization.'
        $status = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        Assert-True ($status.attempt -eq 1) 'Serialized run attempt was lost.'
        & pwsh -NoLogo -NoProfile -File (Join-Path $root '.azurepipelines/container-evidence.ps1') `
            -Operation Aggregate -RepositoryRoot $fixture -Group $group -Inputs $fixture `
            -Work $work -Output (Join-Path $fixture 'aggregate.json')
        Assert-True ($LASTEXITCODE -eq 0) 'Aggregate failed.'
        $aggregate = Get-Content -LiteralPath (Join-Path $fixture 'aggregate.json') -Raw | ConvertFrom-Json
        $observed = @($aggregate.images | Where-Object observation -EQ 'reported')
        Assert-True ($observed.Count -eq 1 -and $observed[0].image -ceq $id -and
            $observed[0].observedSubjects.Count -eq 2) 'Aggregate lost the observed Pump subjects.'
    }
    if ($Scenario -in @('private-path-filter', 'private-field-filter')) {
        Assert-True ($text -notmatch 'confidential|secret\.log|restrictedDigest|rawLog|dddddddd') 'Private capture leaked.'
    }
    if ($Scenario -eq 'catalog-membership') {
        Assert-True ($status.expectedImages -eq 9 -and $status.expectedRunnablePlatforms -eq 18) 'Main group membership changed.'
        Assert-True ($status.images.Count -eq 9) 'Missing images disappeared from the main group manifest.'
        Assert-True (@($status.images | Where-Object group -CNE 'containers').Count -eq 0) 'Main group depends on Pump.'
        Assert-True (-not $status.eligibilityEvidence) 'Descriptor observations claimed eligibility.'
        Assert-True (@($status.images | Where-Object { $_.observedSubjects.Count -ne 0 }).Count -eq 0) `
            'Absent outputs fabricated platforms.'
    }
    if ($Scenario -eq 'pump-membership') {
        Assert-True ($status.expectedImages -eq 1 -and $status.expectedRunnablePlatforms -eq 1) 'Pump is not independent.'
        Assert-True ($status.images.Count -eq 1 -and $status.images[0].group -ceq 'pump') 'Pump depends on main images.'
    }
    if ($Scenario -in @('required-stable', 'required-four-part-version', 'required-context-version')) {
        Assert-True ($status.blocking -and -not $status.publisherBoundaryVerified) 'Required stable bypassed boundary.'
    }
    Write-Output "PASS $Scenario"
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
