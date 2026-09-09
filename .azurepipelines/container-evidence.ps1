# Copyright (c) OPC Foundation, Inc. All rights reserved.
# Licensed under the MIT License. See LICENSE.txt in the project root for license information.
<#
.SYNOPSIS
Container pilot: record identities, fetch immutable OCI blobs, reconcile offline and report gaps.
.DESCRIPTION
Only Collect and Sign contact GHCR, and only in the official authenticated branch workflow.
Record and VerifyLocal accept offline OCI requests/context. No operation builds or publishes images.
Public output is a fixed status projection, never a copy of capture files, tool output or predicates.
Exit 0 means processed/nonblocking, NOT complete; 1 means blocked/baseline failure; 2 means collection error.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Preflight', 'Record', 'Collect', 'VerifyLocal', 'Sign', 'Status', 'Aggregate', 'Assemble', 'Evaluate')]
    [string]$Operation,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet('containers', 'pump')][string]$Group = 'containers',
    [string]$Image,
    [string]$RootDigest,
    [string]$Version,
    [string]$Work,
    [string]$Output,
    [string]$Request,
    [string]$Context,
    [string]$Inputs,
    [string]$Tool,
    [string]$EvidenceDirectory,
    [string]$Evidence,
    [string]$Assurance,
    [string]$ReferrerContext,
    [string]$VerificationBundle,
    [string]$TrustPolicy,
    [ValidateSet('unknown', 'success', 'failure', 'cancelled', 'skipped')]
    [string]$BuildState = 'unknown'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Read-Json([string]$Path) {
    if ((Get-Item -LiteralPath $Path).Length -gt 64MB) { throw 'Evidence document exceeds the input limit.' }
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -Depth 100
}

function Write-Json($Value, [string]$Path) {
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($Path)))
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 100) + "`n", [Text.UTF8Encoding]::new($false))
}

function Get-Digest([string]$Path) {
    'sha256:' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Digest([string]$Digest) {
    if ($Digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'Invalid OCI digest.' }
}

function Invoke-BoundedTool(
    [string]$Executable, [string[]]$Arguments, [string]$Stdout, [string]$Stderr,
    [int[]]$AllowedExitCodes = @(0)
) {
    if (@($Arguments | Where-Object { $_ -match '["\r\n]' }).Count -gt 0) {
        throw 'Unsupported tool argument.'
    }
    $quoted = @($Arguments | ForEach-Object { '"' + $_ + '"' })
    $process = Start-Process -FilePath $Executable -ArgumentList $quoted -PassThru -NoNewWindow `
        -RedirectStandardOutput $Stdout -RedirectStandardError $Stderr
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(6)
    try {
        while (-not $process.WaitForExit(200)) {
            if ([DateTimeOffset]::UtcNow -gt $deadline -or
                (Get-Item -LiteralPath $Stdout).Length -gt 8MB -or
                (Get-Item -LiteralPath $Stderr).Length -gt 8MB) {
                $process.Kill($true)
                throw 'Evidence tool exceeded its time or output limit.'
            }
        }
        if ((Get-Item -LiteralPath $Stdout).Length -gt 8MB -or
            (Get-Item -LiteralPath $Stderr).Length -gt 8MB -or $process.ExitCode -notin $AllowedExitCodes) {
            throw 'Evidence tool failed or exceeded its output limit.'
        }
        $process.ExitCode
    }
    finally { $process.Dispose() }
}

function Resolve-Confined([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative.Contains('\') -or
        $Relative -match '(^/|:|(^|/)\.\.?(/|$)|//|/$)') { throw 'Unsafe bundle path.' }
    $current = [IO.Path]::GetFullPath($Root)
    foreach ($part in @('') + $Relative.Split('/')) {
        if ($part) { $current = Join-Path $current $part }
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Reparse points are not evidence inputs.'
            }
        }
    }
    $current
}

function Get-Group([string]$Id) {
    $matches = @($script:catalog.groups | Where-Object id -CEQ $Id)
    if ($matches.Count -ne 1) { throw 'Unknown artifact group.' }
    $matches[0]
}

function Get-ImageId([string]$Name, $Definition) {
    $ids = @($Definition.images | ForEach-Object { "$($Definition.upstreamRepositoryPrefix)/$($_.id)" })
    $id = if ($Name -in $ids) { $Name } else { "$($Definition.upstreamRepositoryPrefix)/$Name" }
    if ($id -cnotin $ids) { throw 'Image is outside the approved group.' }
    $id
}

function Test-RequiredStable([string]$ActualVersion) {
    # A stable-looking official publication cannot bypass this guard by claiming "development".
    $script:policy.stage -ceq 'required' -and
        $ActualVersion -cmatch "^$($script:policy.currentMajor)\.\d+\.\d+(\.\d+)?(\+[0-9A-Za-z.-]+)?$"
}

function Assert-RegistryBranch {
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'OPCFoundation/UA-.NETStandard' -or
        $env:GITHUB_EVENT_NAME -cnotin @('push', 'workflow_dispatch') -or
        $env:GITHUB_REF -cnotmatch '^refs/heads/(master|release/.+|docker.*)$' -or
        $env:GITHUB_HEAD_REF -or [string]::IsNullOrWhiteSpace($env:GHCR_TOKEN)) {
        throw 'Registry operations require the official authenticated branch workflow.'
    }
}

function New-Context {
    $head = (& git -C $RepositoryRoot rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cnotmatch '^[0-9a-f]{40,64}$' -or $head -cne $env:GITHUB_SHA -or
        $env:GITHUB_WORKFLOW_SHA -cnotmatch '^[0-9a-f]{40,64}$' -or
        $env:GITHUB_RUN_ID -cnotmatch '^\d+$' -or $env:GITHUB_RUN_ATTEMPT -cnotmatch '^[1-9]\d*$' -or
        $env:GITHUB_WORKFLOW_REF -cne "$($env:GITHUB_REPOSITORY)/$($script:definition.producer)@$($env:GITHUB_REF)") {
        throw 'Actual checkout/workflow identity is unavailable or mismatched.'
    }
    & git -C $RepositoryRoot diff --quiet HEAD --
    $clean = $LASTEXITCODE -eq 0
    if ($LASTEXITCODE -gt 1) { throw 'Tracked cleanliness check failed.' }
    if ($Version -cnotmatch '^\d+\.\d+\.\d+(\.\d+)?([+-][0-9A-Za-z.+-]+)?$') {
        throw 'Actual version is unavailable.'
    }
    @{
        source = @{
            repository = $env:GITHUB_REPOSITORY; actualSha = $head; actualRef = $env:GITHUB_REF
            trackedClean = $clean
        }
        producer = @{
            system = 'github-actions'; workflow = $script:definition.producer
            definitionSha = $env:GITHUB_WORKFLOW_SHA; runId = $env:GITHUB_RUN_ID
            attempt = [int]$env:GITHUB_RUN_ATTEMPT; job = $env:GITHUB_JOB
            tools = @(@{ id = 'pwsh'; version = $PSVersionTable.PSVersion.ToString() })
        }
        release = @{ group = $Group; version = $Version; channel = 'development' }
        policyDigest = Get-Digest (Join-Path $RepositoryRoot '.azurepipelines/release-policy.json')
        artifacts = @()
    }
}

function Get-LocalContext {
    $value = if ($Context) { Read-Json $Context } else { New-Context }
    if ($value.release.group -cne $Group -or
        $value.source.actualSha -cnotmatch '^[0-9a-f]{40,64}$' -or
        $value.producer.definitionSha -cnotmatch '^[0-9a-f]{40,64}$' -or
        $value.producer.workflow -cne $script:definition.producer -or
        $value.release.version -cnotmatch '^\d+\.\d+\.\d+(\.\d+)?([+-][0-9A-Za-z.+-]+)?$' -or
        $value.policyDigest -cne (Get-Digest (Join-Path $RepositoryRoot '.azurepipelines/release-policy.json')) -or
        ($Version -and $value.release.version -cne $Version)) {
        throw 'OCI context does not match the current group/policy.'
    }
    $script:Version = $value.release.version
    $value
}

function Get-Blob([string]$Layout, [string]$Digest, $Size = $null) {
    Assert-Digest $Digest
    $path = Resolve-Confined $Layout "blobs/sha256/$($Digest.Substring(7))"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Missing OCI blob.' }
    $length = (Get-Item -LiteralPath $path).Length
    if ($length -gt 4GB -or
        ($null -ne $Size -and ($length -ne $Size -or $Size -lt 0 -or $Size -gt 4GB)) -or
        (Get-Digest $path) -cne $Digest) {
        throw 'Missing or altered OCI blob.'
    }
    $path
}

function Get-Platform($Value) {
    $platform = "$($Value.os)/$($Value.architecture)"
    if ($Value.variant) { $platform += "/$($Value.variant)" }
    $platform
}

function Read-OciGraph($Entry, [string]$Layout, $Identity) {
    $artifacts = [Collections.Generic.List[hashtable]]::new()
    $attestations = [Collections.Generic.List[hashtable]]::new()
    $visited = [Collections.Generic.HashSet[string]]::new()
    function Visit($Digest, $MediaType, $Size, $Platform, $Subject) {
        if (-not $visited.Add($Digest) -or $visited.Count -gt 1024) { throw 'Duplicate/excessive OCI graph.' }
        $path = Get-Blob $Layout $Digest $Size
        $document = Read-Json $path
        if ($document.schemaVersion -ne 2 -or ($MediaType -and $MediaType -cne $document.mediaType)) {
            throw 'Descriptor media type/schema mismatch.'
        }
        if ($document.mediaType -cin @(
            'application/vnd.oci.image.index.v1+json', 'application/vnd.docker.distribution.manifest.list.v2+json')) {
            if ($Subject -or @($document.manifests).Count -eq 0) { throw 'Invalid OCI index.' }
            $artifacts.Add(@{ kind = 'oci-index'; digest = $Digest; platforms = @() })
            foreach ($child in $document.manifests) {
                $isAttestation = $child.annotations.'vnd.docker.reference.type' -ceq 'attestation-manifest'
                $childSubject = $null
                if ($isAttestation) {
                    $childSubject = $child.annotations.'vnd.docker.reference.digest'
                    $siblings = @($document.manifests | Where-Object {
                        $_.digest -ceq $childSubject -and
                        $_.annotations.'vnd.docker.reference.type' -cne 'attestation-manifest' -and
                        (Get-Platform $_.platform) -cin $script:definition.platforms
                    })
                    if ((Get-Platform $child.platform) -cne 'unknown/unknown' -or $siblings.Count -ne 1) {
                        throw 'Attestation descriptor is not a runnable platform subject.'
                    }
                }
                Visit $child.digest $child.mediaType $child.size (Get-Platform $child.platform) $childSubject
            }
            return
        }
        if ($document.mediaType -cnotin @(
            'application/vnd.oci.image.manifest.v1+json', 'application/vnd.docker.distribution.manifest.v2+json')) {
            throw 'Unsupported OCI manifest.'
        }
        $config = $document.config
        if ($config.mediaType -cnotin @(
            'application/vnd.oci.image.config.v1+json', 'application/vnd.docker.container.image.v1+json')) {
            throw 'Unsupported OCI config.'
        }
        $configuration = Read-Json (Get-Blob $Layout $config.digest $config.size)
        if ($Subject) {
            if ($document.subject -and $document.subject.digest -cne $Subject) { throw 'Wrong manifest subject.' }
            if (@($document.layers).Count -eq 0) { throw 'Empty attestation manifest.' }
            foreach ($layer in $document.layers) {
                $statement = Read-Json (Get-Blob $Layout $layer.digest $layer.size)
                if ($layer.mediaType -cne 'application/vnd.in-toto+json' -or
                    $statement._type -cnotin @('https://in-toto.io/Statement/v0.1', 'https://in-toto.io/Statement/v1') -or
                    $statement.predicateType -cne $layer.annotations.'in-toto.io/predicate-type' -or
                    @($statement.subject).Count -eq 0) { throw 'Invalid attestation statement.' }
                foreach ($item in $statement.subject) {
                    if (-not $item.name -or "sha256:$($item.digest.sha256)" -cne $Subject) {
                        throw 'Wrong predicate subject.'
                    }
                }
                $type = switch -CaseSensitive ($statement.predicateType) {
                    'https://spdx.dev/Document' { 'sbom' }
                    'https://slsa.dev/provenance/v0.2' { 'provenance' }
                    'https://slsa.dev/provenance/v1' { 'provenance' }
                    default { throw 'Unsupported predicate type.' }
                }
                $format = if ($type -ceq 'sbom') { $statement.predicate.spdxVersion } else { $statement.predicateType }
                # This is a descriptor observation. Full schema/inventory/provenance validation belongs to the CLI.
                if ($type -ceq 'sbom' -and $format -cne 'SPDX-2.3') { throw 'Unsupported native SPDX.' }
                $attestations.Add(@{
                    type = $type; digest = $layer.digest; subjectDigest = $Subject
                    manifestDigest = $Digest; formatVersion = $format
                })
            }
            return
        }
        $actualPlatform = Get-Platform $configuration
        # OCI image config permits arm64's default v8 to be omitted.
        if ($actualPlatform -ceq 'linux/arm64' -and $Platform -ceq 'linux/arm64/v8') {
            $actualPlatform = $Platform
        }
        if (($Platform -and $Platform -cne $actualPlatform) -or $actualPlatform -cnotin $script:definition.platforms) {
            throw 'Runnable platform disagrees with config/catalog.'
        }
        $labels = $configuration.config.Labels
        if ($labels.'org.opencontainers.image.revision' -cne $Identity.source.actualSha -or
            $labels.'org.opencontainers.image.version' -cne $Identity.release.version) {
            throw 'Image version/source label mismatch.'
        }
        foreach ($layer in $document.layers) { $null = Get-Blob $Layout $layer.digest $layer.size }
        $artifacts.Add(@{ kind = 'oci-manifest'; digest = $Digest; platforms = @($actualPlatform) })
    }
    Visit $Entry.rootDigest $null $null $null $null
    foreach ($platform in $script:definition.platforms) {
        if (@($artifacts | Where-Object { $_.kind -ceq 'oci-manifest' -and $_.platforms[0] -ceq $platform }).Count -ne 1) {
            throw 'Missing or duplicate required runnable platform.'
        }
    }
    @{ artifacts = @($artifacts.ToArray()); attestations = @($attestations.ToArray()) }
}

function Save-Status($State) {
    $unmet = @(
        'SOURCE_IDENTITY', 'PRODUCER_IDENTITY', 'POLICY_IDENTITY', 'RELEASE_INTENT',
        'INVENTORY_COMPLETE', 'PROVENANCE_VERIFIED', 'SIGNATURE_VERIFIED', 'ASSURANCE_COMPLETE',
        'INPUT_IDENTITY', 'EVIDENCE_FRESHNESS', 'PUBLIC_EVIDENCE_SAFE', 'PRODUCER_NOT_READY', 'PUBLISHER_BOUNDARY'
    )
    if ($State.collectorState -cne 'locally-verified') { $unmet += 'ARTIFACT_INTEGRITY' }
    if ($Group -ceq 'containers' -or $State.collectorState -cne 'locally-verified') { $unmet += 'ARTIFACT_MEMBERSHIP' }
    foreach ($code in $State.toolUnmetControls) {
        if ($code -cnotin $script:policy.controls.code) { throw 'Unknown OCI tool control.' }
        $unmet += $code
    }
    $status = @{
        schemaVersion = 1; status = 'incomplete'; group = $Group; image = $script:imageId
        stage = $script:policy.stage; baselineFailed = $BuildState -cin @('failure', 'cancelled')
        blocking = (Test-RequiredStable $Version); publisherBoundaryVerified = $script:policy.publisherBoundaryVerified
        externalAuthorizationVerified = $false; assuranceVerified = $false
        buildState = $BuildState; collectorState = $State.collectorState; signingState = $State.signingState
        toolState = $State.toolState
        unmetControls = @($unmet | Sort-Object -Unique)
        observedSubjects = @(); observedAttestations = @()
    }
    if ($State.rootDigest -cmatch '^sha256:[0-9a-f]{64}$') { $status.rootDigest = $State.rootDigest }
    if ($State.sourceSha -cmatch '^[0-9a-f]{40,64}$') { $status.sourceSha = $State.sourceSha }
    if ($State.runId -cmatch '^\d+$') { $status.runId = $State.runId }
    if (($State.attempt -is [int] -or $State.attempt -is [long]) -and $State.attempt -gt 0) {
        $status.attempt = $State.attempt
    }
    foreach ($subject in $State.artifacts) {
        Assert-Digest $subject.digest
        if ($subject.kind -cnotin @('oci-index', 'oci-manifest') -or
            @($subject.platforms | Where-Object { $_ -cnotin $script:definition.platforms }).Count -gt 0) {
            throw 'Invalid public subject projection.'
        }
        $status.observedSubjects += @{
            kind = $subject.kind; digest = $subject.digest; platforms = @($subject.platforms)
        }
    }
    foreach ($attestation in $State.attestations) {
        Assert-Digest $attestation.digest
        Assert-Digest $attestation.subjectDigest
        Assert-Digest $attestation.manifestDigest
        if ($attestation.type -cnotin @('sbom', 'provenance') -or
            $attestation.formatVersion -cnotin @(
                'SPDX-2.3', 'https://slsa.dev/provenance/v0.2', 'https://slsa.dev/provenance/v1')) {
            throw 'Invalid public attestation projection.'
        }
        $status.observedAttestations += @{
            type = $attestation.type; digest = $attestation.digest; subjectDigest = $attestation.subjectDigest
            manifestDigest = $attestation.manifestDigest; formatVersion = $attestation.formatVersion
        }
    }
    Write-Json $status $Output
    $status
}

function Fetch-Layout([string]$Id, [string]$Digest, [string]$Layout) {
    Assert-RegistryBranch
    Assert-Digest $Digest
    $repository = $Id.Substring('ghcr.io/'.Length)
    $basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($env:GITHUB_ACTOR):$($env:GHCR_TOKEN)"))
    $token = Invoke-RestMethod -Uri "https://ghcr.io/token?service=ghcr.io&scope=repository:${repository}:pull" `
        -Headers @{ Authorization = "Basic $basic" } -MaximumRedirection 0
    if (-not $token.token) { throw 'GHCR did not issue a read token.' }
    $headers = @{
        Authorization = "Bearer $($token.token)"
        Accept = 'application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, ' +
            'application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json'
    }
    $seen = [Collections.Generic.HashSet[string]]::new()
    function Fetch($Hash, $Size, [bool]$Manifest) {
        Assert-Digest $Hash
        if ($null -ne $Size -and ($Size -lt 0 -or $Size -gt 4GB -or ($Manifest -and $Size -gt 64MB))) {
            throw 'OCI descriptor exceeds the acquisition limit.'
        }
        if (-not $seen.Add($Hash)) { return }
        if ($seen.Count -gt 8192) { throw 'Excessive OCI graph.' }
        $path = Resolve-Confined $Layout "blobs/sha256/$($Hash.Substring(7))"
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path)
        $endpoint = if ($Manifest) { 'manifests' } else { 'blobs' }
        # PowerShell strips Authorization on redirects; never enable PreserveAuthorizationOnRedirect.
        Invoke-WebRequest -Uri "https://ghcr.io/v2/$repository/$endpoint/$Hash" -Headers $headers `
            -OutFile $path -MaximumRedirection 5 -ConnectionTimeoutSeconds 60 -OperationTimeoutSeconds 600
        $null = Get-Blob $Layout $Hash $Size
        if (-not $Manifest) { return }
        $value = Read-Json $path
        if ($value.manifests) {
            foreach ($child in $value.manifests) { Fetch $child.digest $child.size $true }
        }
        elseif ($value.config -and $null -ne $value.layers) {
            Fetch $value.config.digest $value.config.size $false
            foreach ($layer in $value.layers) { Fetch $layer.digest $layer.size $false }
        }
        else { throw 'Registry returned an unsupported manifest.' }
    }
    Fetch $Digest $null $true
    Write-Json @{ imageLayoutVersion = '1.0.0' } (Join-Path $Layout 'oci-layout')
    $root = Read-Json (Get-Blob $Layout $Digest)
    Write-Json @{
        schemaVersion = 2
        manifests = @(@{ mediaType = $root.mediaType; digest = $Digest; size = (Get-Item (Get-Blob $Layout $Digest)).Length })
    } (Join-Path $Layout 'index.json')
}

if (-not $Work) { $Work = Join-Path ([IO.Path]::GetTempPath()) "container-evidence-$PID" }
if (-not $Output) {
    $Output = Join-Path $Work $(if ($Operation -ceq 'Evaluate') { 'evaluation.json' } else { 'public/status.json' })
}
$null = New-Item -ItemType Directory -Force -Path $Work
$statePath = Join-Path $Work 'state.json'
$state = @{
    collectorState = 'unavailable'; signingState = 'not-attempted'; toolState = 'unavailable'
    artifacts = @(); attestations = @()
}
$exitCode = 0
try {
    $script:policy = Read-Json (Join-Path $RepositoryRoot '.azurepipelines/release-policy.json')
    $script:catalog = Read-Json (Join-Path $RepositoryRoot '.azurepipelines/release-artifacts.json')
    $script:definition = Get-Group $Group
    if ($Image) { $script:imageId = Get-ImageId $Image $script:definition }
    if ($Operation -cin @('Assemble', 'Evaluate')) {
        if (-not $Context) { throw 'Group assembly/evaluation requires an explicit expected context.' }
        $identity = Get-LocalContext
        if (-not $Tool) {
            $Tool = Join-Path $RepositoryRoot 'tools/Opc.Ua.ReleaseEvidence/bin/Release/net10.0/Opc.Ua.ReleaseEvidence.dll'
        }
        if (-not (Test-Path -LiteralPath $Tool -PathType Leaf)) { throw 'The protected evidence tool is unavailable.' }
        $arguments = @($Tool)
        if ($Operation -ceq 'Assemble') {
            if (-not $Request) { throw 'Group assembly requires the complete group OCI layout request.' }
            if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $Work 'evidence-v2' }
            $arguments += @('oci-assemble', '--repository-root', $RepositoryRoot, '--request', $Request,
                '--context', $Context, '--output', $EvidenceDirectory)
            if ($Assurance) { $arguments += @('--assurance', $Assurance) }
            if ($ReferrerContext) { $arguments += @('--referrers', $ReferrerContext) }
        }
        else {
            if (-not $Evidence) { throw 'Evaluation requires an immutable v2 evidence envelope.' }
            $arguments += @('evaluate', '--repository-root', $RepositoryRoot, '--evidence', $Evidence,
                '--expected', $Context, '--output', $Output)
            if ($VerificationBundle) { $arguments += @('--verification-bundle', $VerificationBundle) }
            if ($TrustPolicy) { $arguments += @('--trust-policy', $TrustPolicy) }
        }
        $code = Invoke-BoundedTool (Get-Command dotnet -CommandType Application).Source $arguments `
            (Join-Path $Work "$Operation.stdout.log") (Join-Path $Work "$Operation.stderr.log") @(0, 1, 2)
        # The shared evaluator owns classification and authorization. Do not turn required success into failure,
        # or advisory/incomplete processing into a complete release assertion.
        exit $code
    }
    if ($Operation -ceq 'Aggregate') {
        $entries = @()
        foreach ($definition in @($script:definition)) {
            foreach ($member in $definition.images) {
                $id = "$($definition.upstreamRepositoryPrefix)/$($member.id)"
                $observed = @()
                if ($Inputs -and (Test-Path -LiteralPath $Inputs)) {
                    foreach ($file in Get-ChildItem -LiteralPath $Inputs -Filter status.json -File -Recurse) {
                        $record = Read-Json $file.FullName
                        if ($record.group -ceq $Group -and $record.image -ceq $id -and
                            $record.sourceSha -ceq $env:GITHUB_SHA -and
                            $record.runId -ceq $env:GITHUB_RUN_ID -and $record.attempt -eq [int]$env:GITHUB_RUN_ATTEMPT) {
                            $observed += $record
                        }
                    }
                }
                $subjects = @()
                if ($observed.Count -eq 1) {
                    foreach ($subject in $observed[0].observedSubjects) {
                        Assert-Digest $subject.digest
                        if ($subject.kind -cnotin @('oci-index', 'oci-manifest') -or
                            @($subject.platforms | Where-Object { $_ -cnotin $definition.platforms }).Count -gt 0) {
                            throw 'Unsafe aggregate subject.'
                        }
                        $subjects += @{ kind = $subject.kind; digest = $subject.digest; platforms = @($subject.platforms) }
                    }
                }
                $entries += @{
                    group = $definition.id; image = $id; expectedPlatforms = @($definition.platforms)
                    status = 'incomplete'; observation = $(if ($observed.Count -eq 1) { 'reported' } else { 'missing-or-duplicate' })
                    observedSubjects = $subjects
                }
            }
        }
        Write-Json @{
            schemaVersion = 1; status = 'incomplete'; workflowGroup = $Group; group = $Group
            eligibilityEvidence = $false
            expectedImages = $entries.Count; expectedRunnablePlatforms = @($entries.expectedPlatforms).Count
            images = $entries; externalAuthorizationVerified = $false
            unmetControls = @('ARTIFACT_MEMBERSHIP', 'ASSURANCE_COMPLETE', 'PRODUCER_NOT_READY', 'PUBLISHER_BOUNDARY')
        } $Output
        exit 0
    }
    if (Test-Path -LiteralPath $statePath) { $state = Read-Json $statePath }
    if (-not $state.toolState) { $state.toolState = 'unavailable' }
    if ($Operation -ceq 'Preflight') {
        if (Test-RequiredStable $Version) {
            $exitCode = 1
            Write-Warning 'Required stable publication blocked: protected controller/promotion and publisher boundary are pending.'
        }
    }
    elseif ($Operation -ceq 'Record') {
        $identity = Get-LocalContext
        $state.sourceSha = $identity.source.actualSha
        $state.runId = $identity.producer.runId
        $state.attempt = [int]$identity.producer.attempt
        Write-Json $identity (Join-Path $Work 'oci-context.json')
        if ($RootDigest) {
            Assert-Digest $RootDigest
            $state.rootDigest = $RootDigest
            Write-Json @{ images = @(@{ id = $script:imageId; layout = 'layout'; rootDigest = $RootDigest }) } `
                (Join-Path $Work 'oci-request.json')
        }
    }
    elseif ($Operation -ceq 'Collect') {
        if (-not $state.rootDigest) { throw 'Build root digest was not recorded.' }
        Fetch-Layout $script:imageId $state.rootDigest (Join-Path $Work 'layout')
        $state.collectorState = 'downloaded'
    }
    elseif ($Operation -ceq 'VerifyLocal') {
        if (-not $Request) { $Request = Join-Path $Work 'oci-request.json' }
        if (-not $Context) { $Context = Join-Path $Work 'oci-context.json' }
        $identity = Get-LocalContext
        $requestValue = Read-Json $Request
        $ids = @($requestValue.images.id)
        if ($ids.Count -eq 0 -or @($ids | Sort-Object -Unique).Count -ne $ids.Count) { throw 'Empty/duplicate OCI request.' }
        $state.artifacts = @()
        $state.attestations = @()
        foreach ($entry in $requestValue.images) {
            $null = Get-ImageId $entry.id $script:definition
            if ($Image -and $entry.id -cne $script:imageId) { throw 'Request image differs from this matrix member.' }
            if ($Image -and $state.rootDigest -and $entry.rootDigest -cne $state.rootDigest) {
                throw 'Request root differs from the recorded build action digest.'
            }
            $layout = Resolve-Confined (Split-Path -Parent ([IO.Path]::GetFullPath($Request))) $entry.layout
            $result = Read-OciGraph $entry $layout $identity
            $state.artifacts += $result.artifacts
            $state.attestations += $result.attestations
            if ($ids.Count -eq 1) { $state.rootDigest = $entry.rootDigest }
        }
        $state.sourceSha = $identity.source.actualSha
        $state.runId = $identity.producer.runId
        $state.attempt = [int]$identity.producer.attempt
        $state.collectorState = 'locally-verified'
        if (-not $Tool) {
            $Tool = Join-Path $RepositoryRoot 'tools/Opc.Ua.ReleaseEvidence/bin/Release/net10.0/Opc.Ua.ReleaseEvidence.dll'
        }
        if (Test-Path -LiteralPath $Tool -PathType Leaf) {
            $state.toolState = 'rejected'
            & dotnet $Tool oci --repository-root $RepositoryRoot --request $Request --context $Context `
                --output (Join-Path $Work 'oci-result.json') *> (Join-Path $Work 'oci-tool.log')
            if ($LASTEXITCODE -notin @(0, 1)) { throw 'OCI tool rejected the input; diagnostics remain local.' }
            $report = Read-Json (Join-Path $Work 'oci-result.json')
            if ($report.status -cne 'incomplete') { throw 'Unsupported OCI report.' }
            $state.toolState = 'processed-incomplete'
            $state.toolUnmetControls = @($report.unmetControls)
        }
        else {
            Write-Warning 'OCI collector tool unavailable; local descriptor checks do not complete release evidence.'
        }
    }
    elseif ($Operation -ceq 'Sign') {
        Assert-RegistryBranch
        if ($state.collectorState -cne 'locally-verified') { throw 'Signing requires verified local descriptors.' }
        $identity = Get-LocalContext
        if ($state.sourceSha -cne $identity.source.actualSha) { throw 'Signing source mismatch.' }
        $certificate = "https://github.com/$($env:GITHUB_WORKFLOW_REF)"
        $state.signingState = 'failed'
        $proofDirectory = Join-Path $Work 'subject-proofs'
        $null = New-Item -ItemType Directory -Force -Path $proofDirectory
        $cosign = (Get-Command cosign -CommandType Application).Source
        $null = Invoke-BoundedTool $cosign @('version', '--json') `
            (Join-Path $Work 'cosign-version.json') (Join-Path $Work 'cosign-version.log')
        $toolVersion = Read-Json (Join-Path $Work 'cosign-version.json')
        if ($toolVersion.gitVersion -cne 'v3.1.3') { throw 'Only approved cosign 3.1.3 is supported.' }
        $proofRecords = @()
        $digests = @(@($state.rootDigest) + @($state.artifacts.digest) | Sort-Object -Unique)
        foreach ($digest in $digests) {
            Assert-Digest $digest
            $subject = "$($script:imageId)@$digest"
            $stem = $digest.Substring(7)
            $bundlePath = Join-Path $proofDirectory "$stem.bundle.json"
            $proofPath = Join-Path $proofDirectory "$stem.verify.json"
            if ((Test-Path -LiteralPath $bundlePath) -or (Test-Path -LiteralPath $proofPath)) {
                throw 'Per-subject proof output already exists; immutable receipts cannot be overwritten.'
            }
            $null = Invoke-BoundedTool $cosign @(
                'sign', '--yes', '--timeout', '5m', '--bundle', $bundlePath,
                '-a', "sourceSha=$($identity.source.actualSha)",
                '-a', "workflowSha=$($identity.producer.definitionSha)", $subject
            ) (Join-Path $Work "$stem.sign.log") (Join-Path $Work "$stem.sign-error.log")
            $null = Invoke-BoundedTool $cosign @(
                'verify', '--timeout', '5m', '--certificate-identity', $certificate,
                '--certificate-oidc-issuer', 'https://token.actions.githubusercontent.com',
                '--certificate-github-workflow-repository', $identity.source.repository,
                '--certificate-github-workflow-ref', $identity.source.actualRef,
                '--certificate-github-workflow-sha', $identity.source.actualSha,
                '--certificate-github-workflow-trigger', $env:GITHUB_EVENT_NAME,
                '--output', 'json', $subject
            ) $proofPath (Join-Path $Work "$stem.verify-error.log")
            $proofs = @(Read-Json $proofPath)
            if ($proofs.Count -eq 0 -or @($proofs | Where-Object {
                $_.critical.image.'docker-manifest-digest' -cne $digest -or
                $_.critical.identity.'docker-reference' -cne $script:imageId
            }).Count -ne 0) {
                throw 'Cosign verification returned an unexpected subject/repository.'
            }
            $null = Read-Json $bundlePath
            $proofRecords += @{
                subject = $subject; rootDigest = $state.rootDigest
                bundle = @{ path = "$stem.bundle.json"; digest = Get-Digest $bundlePath }
                verification = @{ path = "$stem.verify.json"; digest = Get-Digest $proofPath }
                tool = @{ id = 'cosign'; version = '3.1.3'; digest = Get-Digest $cosign }
            }
        }
        Write-Json @{
            schemaVersion = 1; kind = 'oci-signature-observations'; group = $Group
            sourceSha = $identity.source.actualSha; definitionSha = $identity.producer.definitionSha
            runId = $identity.producer.runId; attempt = $identity.producer.attempt
            authenticatedProducer = $false; releaseAuthorization = $false
            subjects = $proofRecords
        } (Join-Path $proofDirectory 'index.json')
        $state.signingState = 'verified-identity-boundary-pending'
    }
    elseif ($Operation -ceq 'Status') {
        if ($state.collectorState -cnotin @('unavailable', 'downloaded', 'locally-verified', 'failed') -or
            $state.signingState -cnotin @('not-attempted', 'failed', 'verified-identity-boundary-pending') -or
            $state.toolState -cnotin @('unavailable', 'rejected', 'processed-incomplete')) {
            throw 'Unknown collector/signing state.'
        }
    }
    Write-Json $state $statePath
    $status = Save-Status $state
    if ($status.baselineFailed -or $status.blocking) { $exitCode = 1 }
    if ($status.status -ceq 'incomplete') { Write-Warning 'Container evidence is incomplete; no release authorization is established.' }
}
catch {
    # Exception text can contain credentials, registry responses and local/private paths. Never project it.
    Write-Warning "Container evidence operation $Operation failed; sanitized status remains incomplete."
    if ($Operation -ceq 'Sign') {
        $state.signingState = 'failed'
        if ($state.collectorState -cne 'locally-verified') { $state.collectorState = 'failed' }
    }
    else {
        $state.collectorState = 'failed'
        $state.artifacts = @()
        $state.attestations = @()
    }
    if ($state.signingState -cnotin @('not-attempted', 'failed', 'verified-identity-boundary-pending')) {
        $state.signingState = 'failed'
    }
    if ($state.toolState -cnotin @('unavailable', 'rejected', 'processed-incomplete')) {
        $state.toolState = 'unavailable'
    }
    Write-Json $state $statePath
    Write-Json @{
        schemaVersion = 1; status = 'incomplete'; group = $Group; image = $script:imageId
        collectorState = $state.collectorState; signingState = $state.signingState
        toolState = $state.toolState; buildState = $BuildState
        observedSubjects = @(); observedAttestations = @(); externalAuthorizationVerified = $false
        unmetControls = @(
            'ARTIFACT_INTEGRITY', 'ARTIFACT_MEMBERSHIP', 'ASSURANCE_COMPLETE',
            'PROVENANCE_VERIFIED', 'SIGNATURE_VERIFIED', 'PRODUCER_NOT_READY', 'PUBLISHER_BOUNDARY'
        )
    } $Output
    $exitCode = 2
}
exit $exitCode
