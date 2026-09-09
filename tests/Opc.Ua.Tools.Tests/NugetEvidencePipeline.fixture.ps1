# Copyright (c) OPC Foundation, Inc. All rights reserved.
# Licensed under the MIT License. See LICENSE.txt in the project root for license information.
param([Parameter(Mandatory)][string]$Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$fixture = Join-Path ([IO.Path]::GetTempPath()) "nuget-pipeline-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path (Join-Path $fixture '.azurepipelines')
$packages = Join-Path $fixture 'packages'
$null = New-Item -ItemType Directory -Path $packages

function Write-Json($Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "$Scenario : $Message" }
}

# The wrapper's external reader boundary is replaced, not the policy or identity checks.
# Real CLI/artifact verification has separate ReleaseEvidenceCliTests.
function global:dotnet {
    if ($args[0] -ceq '--version') { $global:LASTEXITCODE = 0; return '10.0.100' }
    if ($args[0] -ceq 'nuget') {
        $global:LASTEXITCODE = if ($Scenario -eq 'feed-native-failed') { 1 } else { 0 }
        return
    }
    $global:ReaderInvoked = $true
    $arguments = @($args)
    if ($Scenario -eq 'saved-bundle-forwarded') {
        Assert-True ($arguments -contains '--verification-bundle') 'Saved cryptographic bundle was not forwarded.'
        Assert-True ($arguments -notcontains '--trust-receipt') 'A caller receipt must not become trust authority.'
        $bundlePath = $arguments[[Array]::IndexOf($arguments, '--verification-bundle') + 1]
        Assert-True ((Split-Path -Leaf $bundlePath) -ceq 'verification-bundle.json') 'The actual bundle input changed.'
    }
    $output = $arguments[[Array]::IndexOf($arguments, '--output') + 1]
    if ($arguments -contains 'verify-delivery') {
        $author = $arguments[[Array]::IndexOf($arguments, '--author') + 1]
        $delivered = $arguments[[Array]::IndexOf($arguments, '--delivered') + 1]
        $matches = $Scenario -ne 'feed-content-mismatch'
        Write-Json @{
            schemaVersion = 1; status = if ($matches) { 'content-matched' } else { 'content-mismatch' }
            contentPreserved = $matches; authorSignaturePreserved = $matches
            authorArchiveDigest = 'sha256:' + (Get-FileHash $author -Algorithm SHA256).Hash.ToLowerInvariant()
            deliveredArchiveDigest = 'sha256:' + (Get-FileHash $delivered -Algorithm SHA256).Hash.ToLowerInvariant()
            signatureVerificationPerformed = $false
        } $output
        $global:LASTEXITCODE = if ($Scenario -eq 'feed-malformed-reader') { 2 }
            elseif ($matches) { 0 } else { 1 }
        return
    }
    $blocking = $Scenario -in @('required-incomplete', 'baseline-failed')
    $complete = $Scenario -eq 'required-complete-reader'
    Write-Json @{
        schemaVersion = 1
        status = $(if ($complete) { 'complete' } else { 'incomplete' })
        blocking = $blocking
        baselineFailed = ($Scenario -eq 'baseline-failed')
        unmetControls = @($(if (-not $complete) { 'PRODUCER_NOT_READY' }))
    } $output
    $global:LASTEXITCODE = if ($Scenario -eq 'invalid-reader') { 2 } elseif ($blocking) { 1 } else { 0 }
}

function global:Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, [int]$TimeoutSec, [int]$MaximumRetryCount)
    if ($Uri -notlike 'https://api.nuget.org/v3-flatcontainer/example/*') {
        throw 'Unexpected fixture feed request.'
    }
    Copy-Item -LiteralPath $global:FeedAuthorPath -Destination $OutFile
}

function global:git {
    $global:LASTEXITCODE = 0
    if ($args -contains 'rev-parse') { return $env:GITHUB_SHA }
    if ($args -contains 'status') { return }
    throw 'Unexpected fixture git operation.'
}

function global:gh {
    $global:LASTEXITCODE = 0
    if ($args[0] -ceq 'api' -and $args[1] -match '/releases/tags/(.+)$') {
        $tag = $Matches[1]
        $wanted = if ($Scenario -eq 'attach-prefixed-version-tag') { 'v2.0.0' } else { '2.0.0' }
        if ($tag -cne $wanted) { $global:LASTEXITCODE = 1; return }
        return @{ draft = $false; tag_name = $tag } | ConvertTo-Json -Compress
    }
    if ($args[0] -ceq 'api' -and $args[1] -match '/commits/') {
        return @{ sha = $(if ($Scenario -eq 'attach-wrong-source') { 'f' * 40 } else { 'a' * 40 }) } |
            ConvertTo-Json -Compress
    }
    if ($args[0] -ceq 'release' -and $args[1] -ceq 'upload') {
        $global:AttachmentInvoked = $true
        return
    }
    throw 'Unexpected fixture GitHub operation.'
}

try {
    $policyPath = Join-Path $fixture '.azurepipelines/release-policy.json'
    $policy = Get-Content -LiteralPath (Join-Path $root '.azurepipelines/release-policy.json') -Raw |
        ConvertFrom-Json -AsHashtable
    if ($Scenario.StartsWith('required-') -or $Scenario -eq 'deferred-major') { $policy.stage = 'required' }
    Write-Json $policy $policyPath
    $version = if ($Scenario -eq 'required-preview') { '2.0.0-preview.1' }
        elseif ($Scenario -eq 'deferred-major') { '1.5.378.1' } else { '2.0.0' }
    $source = @{
        repository = 'OPCFoundation/UA-.NETStandard'; actualSha = ('a' * 40)
        actualRef = 'refs/heads/release/2.0.0'; trackedClean = $true
    }
    $context = @{
        source = $source
        producer = @{
            system = 'github-actions'; workflow = '.github/workflows/nuget-publish.yml'
            definitionSha = ('b' * 40); runId = '42'; attempt = 2; job = 'publish'; tools = @()
        }
        release = @{ group = 'nuget'; version = $version; channel = 'stable' }
        policyDigest = 'sha256:' + (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        artifacts = @()
    }
    $manifest = @{
        schemaVersion = 1; repository = $source.repository; workflow = $context.producer.workflow
        runId = '42'; ref = $source.actualRef; commit = $source.actualSha
        packageVersion = $version; packageCount = 1; symbolPackageCount = 0; debugPackageCount = 0
        archives = @(@{ id = 'Example'; version = $version; type = 'package'
            file = "Example.$version.nupkg"; sha256 = ('c' * 64) })
    }
    Write-Json $manifest (Join-Path $packages 'release-manifest.json')
    Write-Json @{ schemaVersion = 1; artifacts = @() } (Join-Path $packages 'archive-metadata.json')
    if ($Scenario -eq 'saved-bundle-forwarded') {
        $null = New-Item -ItemType Directory -Path (Join-Path $packages 'evidence')
        Write-Json @{ schemaVersion = 2; documents = @() } (Join-Path $packages 'evidence/release-evidence.json')
        Write-Json @{ schemaVersion = 1; proofs = @() } (Join-Path $packages 'verification-bundle.json')
    }
    $expected = Join-Path $fixture 'expected.json'
    Write-Json $context $expected
    if ($Scenario -eq 'malformed-expected') { [IO.File]::WriteAllText($expected, '{"source":') }
    if ($Scenario -eq 'malformed-manifest') {
        [IO.File]::WriteAllText((Join-Path $packages 'release-manifest.json'), '{"schemaVersion":1,"schemaVersion":2}')
    }
    $tool = Join-Path $fixture 'reader.dll'
    [IO.File]::WriteAllBytes($tool, [byte[]]@())
    $output = Join-Path $fixture 'report.json'
    $script = Join-Path $root '.azurepipelines/nuget-evidence.ps1'

    $env:SOURCE_SHA = $source.actualSha
    $env:SOURCE_REF = $source.actualRef
    $env:SOURCE_RUN_ID = '42'
    $env:SOURCE_ATTEMPT = '2'
    $env:SOURCE_DEFINITION_SHA = $context.producer.definitionSha
    if ($Scenario -eq 'source-mismatch') { $env:SOURCE_SHA = 'd' * 40 }
    if ($Scenario -eq 'attempt-mismatch') { $env:SOURCE_ATTEMPT = '3' }
    if ($Scenario -eq 'definition-mismatch') { $env:SOURCE_DEFINITION_SHA = 'e' * 40 }
    if ($Scenario -eq 'foreign-source') {
        $context.source.repository = 'foreign/repository'
        $manifest.repository = $context.source.repository
        Write-Json $context $expected
        Write-Json $manifest (Join-Path $packages 'release-manifest.json')
    }
    if ($Scenario -eq 'missing-source-authority') { $env:SOURCE_RUN_ID = '' }

    if ($Scenario.StartsWith('public-bom-')) {
        . (Join-Path $root '.azurepipelines\nuget-evidence-functions.ps1')
        $artifact = @{ id = 'Example'; version = $version; digest = 'sha256:' + ('c' * 64) }
        $bom = @{
            bomFormat = 'CycloneDX'; specVersion = '1.6'; version = 1
            metadata = @{ component = @{
                type = 'library'; name = 'Example'; version = $version
                hashes = @(@{ alg = 'SHA-256'; content = 'c' * 64 })
                properties = @(@{ name = 'opcua:configuration'; value = 'Release' })
            } }
            components = @(); dependencies = @()
        }
        if ($Scenario -eq 'public-bom-wrong-subject') { $bom.metadata.component.name = 'Other' }
        if ($Scenario -eq 'public-bom-unknown-field') { $bom.metadata.rawFindings = 'restricted-detail' }
        if ($Scenario -eq 'public-bom-unapproved-property') {
            $bom.metadata.component.properties = @(@{ name = 'internal-details'; value = 'restricted-detail' })
        }
        Write-Json $bom $output
        $rejected = $false
        try { Assert-NugetPublicBom $output $artifact }
        catch { $rejected = $true }
        Assert-True ($rejected -eq ($Scenario -ne 'public-bom-exact-subject')) `
            'Public native BOM projection did not enforce exact subject and field scope.'
    }
    elseif ($Scenario.StartsWith('attach-')) {
        $null = New-Item -ItemType Directory -Path (Join-Path $packages 'evidence')
        Write-Json @{ status = 'incomplete' } (Join-Path $packages 'evidence/report.json')
        Write-Json @{ schemaVersion = 2; documents = @(@{
            path = 'report.json'
            digest = 'sha256:' + (Get-FileHash -LiteralPath (Join-Path $packages 'evidence/report.json') `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        }) } (Join-Path $packages 'evidence/release-evidence.json')
        Write-Json @{ arbitraryUnindexedData = 'not-approved-for-publication' } `
            (Join-Path $packages 'evidence/unindexed.json')
        $global:AttachmentInvoked = $false
        & $script -Operation Attach -RepositoryRoot $fixture -Packages $packages -Output $output `
            -Work (Join-Path $fixture 'work')
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        if ($Scenario -eq 'attach-wrong-source') {
            Assert-True (-not $global:AttachmentInvoked) 'A release for another source must not receive evidence.'
            Assert-True ($report.status -ceq 'incomplete') 'Rejected attachment must remain explicit.'
        }
        else {
            Assert-True $global:AttachmentInvoked 'Existing version-tagged release was not selected.'
            Assert-True ($report.status -ceq 'attached') 'Attachment was not recorded.'
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture 'work/public-attachment/evidence/unindexed.json'))) `
                'An arbitrary unindexed file was included in the attachment.'
        }
    }
    elseif ($Scenario.StartsWith('aggregate-')) {
        $env:GITHUB_REPOSITORY = $source.repository
        $env:GITHUB_SHA = $source.actualSha
        $env:GITHUB_REF = $source.actualRef
        $env:GITHUB_WORKFLOW_SHA = $context.producer.definitionSha
        $env:GITHUB_WORKFLOW_REF = "$($source.repository)/$($context.producer.workflow)@$($source.actualRef)"
        $env:GITHUB_RUN_ID = '42'
        $env:GITHUB_RUN_ATTEMPT = '2'
        $env:GITHUB_JOB = 'publish'
        [IO.File]::WriteAllText((Join-Path $fixture '.azurepipelines/expected-packages.txt'), "Example`n")
        Write-Json @{ profiles = @() } (Join-Path $fixture '.azurepipelines/assurance-profiles.json')
        Write-Json @{ groups = @(@{
            id = 'nuget'; profiles = @(); modernCatalog = '.azurepipelines/expected-packages.txt'
            variants = @(@{ id = 'metapackages'; sources = @('nuget/Meta.nuspec') })
        }) } (Join-Path $fixture '.azurepipelines/release-artifacts.json')
        $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'nuget')
        [IO.File]::WriteAllText((Join-Path $fixture 'nuget/Meta.nuspec'),
            '<package><metadata><id>Meta</id><version>2.0.0</version></metadata></package>')
        $inputs = Join-Path $fixture 'inputs'
        $manifest.archives = @()
        foreach ($configuration in @('Release', 'Debug')) {
            $local = Join-Path $inputs "opcua-$configuration-42/evidence"
            $null = New-Item -ItemType Directory -Force -Path $local
            $ids = @(if ($configuration -eq 'Release') { 'Example'; 'Meta' } else { 'Example.Debug' })
            $artifacts = @()
            foreach ($id in $ids) {
                $file = "$id.$version.nupkg"
                [IO.File]::WriteAllText((Join-Path $packages $file), "Synthetic already-validated archive identity: $id")
                $hash = (Get-FileHash -LiteralPath (Join-Path $packages $file) -Algorithm SHA256).Hash.ToLowerInvariant()
                $manifest.archives += @{ id = $id; version = $version; type = 'package'; file = $file; sha256 = $hash }
                $artifacts += @{
                    kind = 'nuget-package'; id = $id; version = $version; configuration = $configuration
                    digest = "sha256:$hash"; scopes = @{ tfms = @(); rids = @(); roslyn = @(); platforms = @() }
                }
            }
            $mapping = @{
                project = 'src/Example/Example.csproj'; configuration = $configuration
                packageId = $ids[0]; isPackable = $true; includeSymbols = $false; hasPdb = $false
            }
            $summary = @{ source = $source; mappings = @($mapping); buildInputDigests = @('sha256:' + ('d' * 64)) }
            if ($Scenario -eq 'aggregate-restricted-field' -and $configuration -eq 'Debug') {
                $summary.clientSecret = 'synthetic-private-value'
            }
            $summaryPath = Join-Path $local 'source-inputs.json'
            Write-Json $summary $summaryPath
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $producer = $context.producer.Clone()
            if ($Scenario -eq 'aggregate-wrong-attempt' -and $configuration -eq 'Debug') { $producer.attempt = 1 }
            if ($Scenario -eq 'aggregate-wrong-definition' -and $configuration -eq 'Debug') {
                $producer.definitionSha = 'f' * 40
            }
            $envelope = @{
                schemaVersion = 2; source = $source; producer = $producer; release = $context.release
                policy = @{ digest = $context.policyDigest }; artifacts = $artifacts
                documents = @(@{ type = 'input-manifest'; format = 'json'; version = '1'
                    path = 'source-inputs.json'; digest = $digest })
                assessment = @{ status = 'incomplete'; unmetControls = @('ARTIFACT_MEMBERSHIP', 'PRODUCER_NOT_READY') }
            }
            if ($Scenario -in @('aggregate-classified-controls', 'aggregate-partial-classification',
                'aggregate-overlapping-classification', 'aggregate-missing-classification')) {
                $classification = @{
                    schemaVersion = 1; kind = 'producer-assessment'; source = $source
                    producer = $producer; release = $context.release; artifacts = $artifacts
                    unmetControls = $envelope.assessment.unmetControls
                    pendingControls = @('PRODUCER_NOT_READY')
                    observedControls = @('ARTIFACT_MEMBERSHIP')
                }
                if ($Scenario -eq 'aggregate-partial-classification') {
                    $classification.observedControls = @()
                }
                if ($Scenario -eq 'aggregate-overlapping-classification') {
                    $classification.pendingControls = @('PRODUCER_NOT_READY', 'ARTIFACT_MEMBERSHIP')
                }
                if ($Scenario -eq 'aggregate-missing-classification') {
                    $classification.Remove('observedControls')
                }
                $classificationPath = Join-Path $local 'producer-assessment.json'
                Write-Json $classification $classificationPath
                $classificationDigest = 'sha256:' + (
                    Get-FileHash -LiteralPath $classificationPath -Algorithm SHA256).Hash.ToLowerInvariant()
                $envelope.documents += @{
                    type = 'producer-record'; format = 'json'; version = '1'; path = 'producer-assessment.json'
                    digest = $classificationDigest
                    subject = @{ kind = 'source'; id = $source.repository; digest = $digest }
                }
            }
            if ($Scenario -eq 'aggregate-hidden-payload' -and $configuration -eq 'Debug') {
                $envelope.payloadType = 'application/vnd.in-toto+json'
                $envelope.payload = [Convert]::ToBase64String(
                    [Text.Encoding]::UTF8.GetBytes('{"predicate":{"clientSecret":"private-fixture"}}'))
            }
            if ($Scenario -eq 'aggregate-duplicate-payload' -and $configuration -eq 'Debug') {
                $envelope.payloadType = 'application/vnd.in-toto+json'
                $envelope.payload = [Convert]::ToBase64String(
                    [Text.Encoding]::UTF8.GetBytes('{"predicate":{},"predicate":{"safe":true}}'))
            }
            Write-Json $envelope (Join-Path $local 'release-evidence.json')
            Write-Json @{ buildDefinition = @{
                resolvedDependencies = @(@{ digest = @{
                    gitCommit = if ($Scenario -eq 'aggregate-provenance-source-mismatch' -and
                        $configuration -eq 'Debug') { 'f' * 40 } else { $source.actualSha }
                } })
            } } (Join-Path $local 'pack-provenance.json')
        }
        $manifest.packageCount = 3
        $manifest.debugPackageCount = 1
        Write-Json $manifest (Join-Path $packages 'release-manifest.json')
        if ($Scenario -eq 'aggregate-changed-archive') {
            [IO.File]::AppendAllText((Join-Path $packages "Example.$version.nupkg"), 'altered')
        }
        if ($Scenario -eq 'aggregate-missing-debug') {
            Remove-Item -LiteralPath (Join-Path $inputs 'opcua-Debug-42/evidence/release-evidence.json')
        }
        $output = Join-Path $fixture 'aggregate'
        & $script -Operation Aggregate -RepositoryRoot $fixture -Packages $packages -Inputs $inputs `
            -Output $output -Work (Join-Path $fixture 'work')
        $aggregateCode = $LASTEXITCODE
        if ($Scenario -in @('aggregate-configurations', 'aggregate-classified-controls')) {
            Assert-True ($LASTEXITCODE -eq 0) 'Configuration aggregation failed.'
            $result = Get-Content -LiteralPath (Join-Path $output 'release-evidence.json') -Raw | ConvertFrom-Json
            Assert-True ($result.artifacts.Count -eq 3) 'Release, Debug and metapackage membership was not retained.'
            Assert-True ($result.assessment.unmetControls -notcontains 'ARTIFACT_MEMBERSHIP') 'Complete membership was lost.'
            Assert-True ($result.assessment.status -ceq 'incomplete') 'Aggregate membership is not full release assurance.'
            Assert-True ((Test-Path -LiteralPath (Join-Path $output 'Release/subjects.sha256'))) 'Release subjects missing.'
            Assert-True ((Test-Path -LiteralPath (Join-Path $output 'Debug/subjects.sha256'))) 'Debug subjects missing.'
            if ($Scenario -eq 'aggregate-classified-controls') {
                $classification = Get-Content -LiteralPath (Join-Path $output 'producer-assessment.json') -Raw |
                    ConvertFrom-Json
                Assert-True ($classification.pendingControls -contains 'PRODUCER_NOT_READY' -and
                    $classification.observedControls.Count -eq 0) `
                    'Pending proof requirements must not become observed failures during aggregation.'
                Assert-True (@($result.assessment.PSObject.Properties).Count -eq 2) `
                    'The v2 assessment must retain its original closed shape.'
                Assert-True (@($result.documents | Where-Object path -CEQ 'producer-assessment.json').Count -eq 1) `
                    'The classified companion must be bound by the resulting index.'
            }
        }
        else {
            Assert-True ((Test-Path -LiteralPath "$output.incomplete.json")) 'Rejected aggregation needs an explicit report.'
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $output 'producer-context.json'))) 'Invalid aggregate cannot be attested.'
            if ($Scenario -in @('aggregate-changed-archive', 'aggregate-wrong-attempt',
                'aggregate-wrong-definition', 'aggregate-provenance-source-mismatch')) {
                Assert-True ($aggregateCode -eq 1) 'Observed baseline identity/content contradictions must block in pilot.'
            }
            elseif ($Scenario -eq 'aggregate-missing-debug') {
                Assert-True ($aggregateCode -eq 0) 'Missing new evidence remains advisory during pilot.'
            }
            elseif ($Scenario -in @('aggregate-duplicate-payload', 'aggregate-partial-classification',
                'aggregate-overlapping-classification', 'aggregate-missing-classification')) {
                Assert-True ($aggregateCode -eq 2) 'Malformed embedded signed JSON must remain an invalid-input failure.'
            }
        }
    }
    elseif ($Scenario.StartsWith('feed-')) {
        $global:FeedAuthorPath = Join-Path $packages "Example.$version.nupkg"
        $archive = [IO.Compression.ZipFile]::Open($global:FeedAuthorPath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $archive.CreateEntry('Example.nuspec')
            $writer = [IO.StreamWriter]::new($entry.Open())
            try {
                $writer.Write("<package><metadata><id>Example</id><version>$version</version></metadata></package>")
            }
            finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
        $manifest.archives[0].sha256 = (Get-FileHash $global:FeedAuthorPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Json $manifest (Join-Path $packages 'release-manifest.json')
        & $script -Operation Receipt -RepositoryRoot $fixture -Packages $packages -Output $output `
            -Work (Join-Path $fixture 'work') -Destination NugetOrg `
            -File "Example.$version.nupkg" -State push-accepted-or-duplicate
        $before = [IO.File]::ReadAllBytes($output)
        & $script -Operation VerifyFeed -RepositoryRoot $fixture -Packages $packages -Output $output `
            -Work (Join-Path $fixture 'work') -Destination NugetOrg -Tool $tool
        $code = $LASTEXITCODE
        $expectedCode = if ($Scenario -eq 'feed-malformed-reader') { 2 }
            elseif ($Scenario -in @('feed-content-mismatch', 'feed-native-failed')) { 1 } else { 0 }
        Assert-True ($code -eq $expectedCode) "Expected feed exit $expectedCode, got $code."
        Assert-True ([Convert]::ToBase64String($before) -ceq
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Feed observation mutated the initial receipt.'
        . (Join-Path $root '.azurepipelines\nuget-evidence-functions.ps1')
        $receipt = Get-NugetDeliveryState $output
        Assert-True ($receipt.status -ceq 'incomplete') 'Feed identity or transport success cannot grant eligibility.'
        if ($Scenario -eq 'feed-content-matched-unapproved') {
            Assert-True ($receipt.packages[0].state -ceq 'feed-content-matched-signer-approval-pending') `
                'Approved signer constraints must remain explicit and unresolved.'
        }
    }
    elseif ($Scenario.StartsWith('receipt-')) {
        if ($Scenario -eq 'receipt-symbols-unresolved') {
            $manifest.symbolPackageCount = 1
            $manifest.archives += @{
                id = 'Example'; version = $version; type = 'symbols'
                file = "Example.$version.snupkg"; sha256 = 'd' * 64
            }
            Write-Json $manifest (Join-Path $packages 'release-manifest.json')
        }
        & $script -Operation Receipt -RepositoryRoot $fixture -Packages $packages -Output $output `
            -Work (Join-Path $fixture 'work') -Destination NugetOrg
        Assert-True ($LASTEXITCODE -eq 0) 'Receipt creation failed.'
        $before = [IO.File]::ReadAllBytes($output)
        if ($Scenario -eq 'receipt-source-mismatch') {
            $manifest.commit = 'e' * 40
            Write-Json $manifest (Join-Path $packages 'release-manifest.json')
        }
        & $script -Operation Receipt -RepositoryRoot $fixture -Packages $packages -Output $output `
            -Work (Join-Path $fixture 'work') -Destination NugetOrg `
            -File "Example.$version.nupkg" -State push-accepted-or-duplicate
        . (Join-Path $root '.azurepipelines\nuget-evidence-functions.ps1')
        $receipt = Get-NugetDeliveryState $output
        if ($Scenario -eq 'receipt-source-mismatch') {
            Assert-True ((Test-Path -LiteralPath "$output.incomplete.json")) 'A conflicting receipt must be reported.'
            Assert-True ([Convert]::ToBase64String($before) -ceq
                [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Existing receipt was overwritten.'
        }
        else {
            Assert-True ([Convert]::ToBase64String($before) -ceq
                [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Initial delivery snapshot was modified.'
            Assert-True (@(Get-ChildItem -LiteralPath "$output.events" -File).Count -eq 2) 'Append-only events missing.'
            Assert-True ($receipt.packages[0].authorDigest -ceq ('sha256:' + ('c' * 64))) 'Author identity changed.'
            Assert-True ($receipt.packages[0].state -ceq 'push-accepted-or-duplicate') 'Push outcome was lost.'
            Assert-True ($receipt.packages[0].verification -ceq 'not-performed') 'Duplicate push is not feed verification.'
            Assert-True ($receipt.status -ceq 'incomplete') 'Push acceptance is not delivery completeness.'
            if ($Scenario -eq 'receipt-symbols-unresolved') {
                Assert-True ($receipt.symbols.expected -eq 1 -and $receipt.symbols.status -ceq 'unresolved' -and
                    $receipt.symbols.verification -ceq 'not-performed') 'Normal-package checks cannot verify symbols.'
            }
        }
    }
    else {
        $global:ReaderInvoked = $false
        & $script -Operation Preflight -RepositoryRoot $fixture -Packages $packages -Expected $expected `
            -Output $output -Work (Join-Path $fixture 'work') -Tool $tool -Destination NugetOrg
        $code = $LASTEXITCODE
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        if ($Scenario -in @('source-mismatch', 'attempt-mismatch', 'definition-mismatch',
            'foreign-source', 'missing-source-authority')) {
            Assert-True ($code -eq 1) 'Authenticated source contradictions must block even during pilot.'
            Assert-True (-not $global:ReaderInvoked) 'Do not evaluate with contradicted source expectations.'
        }
        else {
            $expectedCode = if ($Scenario -in @('invalid-reader', 'malformed-expected', 'malformed-manifest')) { 2 }
                elseif ($Scenario -in @('required-incomplete', 'baseline-failed')) { 1 } else { 0 }
            Assert-True ($code -eq $expectedCode) "Expected exit $expectedCode, received $code."
            if ($Scenario -notin @('malformed-expected', 'malformed-manifest')) {
                Assert-True $global:ReaderInvoked 'The offline reader was not invoked.'
            }
            Assert-True ($report.status -ceq $(if ($Scenario -eq 'required-complete-reader') {
                'complete'
            } else { 'incomplete' })) 'The reader outcome was hidden.'
        }
    }
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
