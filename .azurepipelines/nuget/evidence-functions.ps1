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

function Read-NugetJson {
    param([string]$Path)
    if ((Get-Item -LiteralPath $Path).Length -gt 64MB) {
        Stop-NugetEvidence 'JSON input exceeds the bounded document limit.' 2
    }
    $text = [IO.File]::ReadAllText($Path)
    return ConvertFrom-NugetJsonText $text
}

function ConvertFrom-NugetJsonText {
    param([string]$text)
    try { $document = [System.Text.Json.JsonDocument]::Parse($text) }
    catch { Stop-NugetEvidence 'Malformed JSON input.' 2 }
    try {
        function Test-JsonFields($Element) {
            if ($Element.ValueKind -eq 'Object') {
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($property in $Element.EnumerateObject()) {
                    if (-not $names.Add($property.Name)) { Stop-NugetEvidence 'Duplicate JSON field.' 2 }
                    Test-JsonFields $property.Value
                }
            }
            elseif ($Element.ValueKind -eq 'Array') {
                foreach ($item in $Element.EnumerateArray()) { Test-JsonFields $item }
            }
        }
        Test-JsonFields $document.RootElement
    }
    finally { $document.Dispose() }
    return ConvertFrom-Json -InputObject $text -AsHashtable -Depth 100
}

function Stop-NugetEvidence {
    param([string]$Message, [ValidateSet(1, 2)][int]$ExitCode = 2)
    $exception = [IO.InvalidDataException]::new($Message)
    $exception.Data['NugetExitCode'] = $ExitCode
    throw $exception
}

function Write-NugetJson {
    param($Value, [string]$Path)
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($Path)))
    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

function Get-NugetDigest {
    param([string]$Path)
    return 'sha256:' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-NugetIndependentTrust {
    param([string]$Path, [string[]]$CandidateRoots)
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Stop-NugetEvidence 'Protected trust configuration must be an existing absolute controller input.' 2
    }
    $full = [IO.Path]::GetFullPath($Path)
    foreach ($root in $CandidateRoots) {
        $prefix = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if ($full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-NugetEvidence 'Protected trust configuration cannot be supplied inside candidate or evaluation inputs.' 2
        }
    }
    $null = Resolve-NugetPath (Split-Path -Parent $full) (Split-Path -Leaf $full)
}

function Resolve-NugetPath {
    param([string]$Root, [string]$Relative)
    if ($Relative -notmatch '^[A-Za-z0-9_. /-]+$' -or
        $Relative -match '(^|/)(\.{1,2}|)(/|$)' -or [IO.Path]::IsPathRooted($Relative)) {
        Stop-NugetEvidence 'Unsafe evidence path.' 2
    }
    $fullRoot = [IO.Path]::GetFullPath($Root)
    $path = $fullRoot
    foreach ($segment in $Relative.Split('/')) {
        $path = Join-Path $path $segment
        if ((Test-Path -LiteralPath $path) -and
            (Get-Item -LiteralPath $path -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            Stop-NugetEvidence 'Evidence paths cannot alias a reparse point.' 2
        }
    }
    $parent = Get-Item -LiteralPath $fullRoot -Force
    while ($null -ne $parent) {
        if ($parent.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            Stop-NugetEvidence 'Unsafe evidence root.' 2
        }
        $parent = $parent.Parent
    }
    return $path
}

function Assert-NugetPublicJson {
    param([string]$Path)
    $value = Read-NugetJson $Path
    function Test-PublicValue($Item) {
        if ($Item -is [Collections.IDictionary]) {
            if ($Item.Contains('payloadType') -and $Item.Contains('payload')) {
                try {
                    $payload = [Convert]::FromBase64String($Item.payload)
                    if ($payload.Length -gt 16MB) { throw 'Oversized signed payload.' }
                    $decoded = ConvertFrom-NugetJsonText ([Text.Encoding]::UTF8.GetString($payload))
                }
                catch { Stop-NugetEvidence 'A public signed payload is malformed or oversized.' 2 }
                Test-PublicValue $decoded
            }
            foreach ($key in $Item.Keys) {
                if ($key -match '(?i)password|clientSecret|apiKey|packageFolders|restoreSources') {
                    throw 'Restricted field in candidate public evidence.'
                }
                Test-PublicValue $Item[$key]
            }
        }
        elseif ($Item -is [string]) {
            if ($Item.StartsWith('/') -or $Item -match '(?i)([a-z]:[\\/]|\\\\|file://|/home/|/Users/|/agent/|/runner/)' -or
                $Item -match '(?i)https?://[^/ ]+@|[?&](token|sig|key|password)=') {
                throw 'Local path or credential-shaped value in candidate public evidence.'
            }
        }
        elseif ($Item -is [Collections.IEnumerable]) {
            foreach ($child in $Item) { Test-PublicValue $child }
        }
    }
    Test-PublicValue $value
}

function Get-NugetPolicy {
    param([string]$RepositoryRoot)
    $policy = Read-NugetJson (Join-Path $RepositoryRoot '.azurepipelines\release\policy.json')
    if ($policy.schemaVersion -ne 1 -or $policy.stage -notin @('pilot', 'required') -or
        $policy.currentMajor -ne 2 -or $policy.requiredChannel -cne 'stable') {
        Stop-NugetEvidence 'Unsupported protected NuGet release policy.' 2
    }
    return $policy
}

function Get-NugetChannel {
    param([string]$Version)
    if ($Version -notmatch '^(0|[1-9][0-9]*)\.[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$') {
        Stop-NugetEvidence 'Invalid actual package version.' 2
    }
    if ($Matches[2]) { return 'preview' }
    return 'stable'
}

function New-NugetContext {
    param([string]$RepositoryRoot, [string]$Version)
    $sha = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot observe the actual checkout.' }
    $refSha = (& git -C $RepositoryRoot rev-parse --verify --end-of-options "$env:GITHUB_REF^{commit}").Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the actual source ref.' }
    $status = & git -C $RepositoryRoot status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0) { throw 'Cannot observe tracked source cleanliness.' }
    if ($sha -cne $env:GITHUB_SHA -or $sha -cne $refSha -or $sha -notmatch '^[0-9a-f]{40}([0-9a-f]{24})?$' -or
        $env:GITHUB_WORKFLOW_SHA -notmatch '^[0-9a-f]{40}([0-9a-f]{24})?$' -or
        $env:GITHUB_REPOSITORY -cne 'OPCFoundation/UA-.NETStandard' -or
        $env:GITHUB_RUN_ID -notmatch '^[1-9][0-9]*$' -or $env:GITHUB_RUN_ATTEMPT -notmatch '^[1-9][0-9]*$' -or
        [string]::IsNullOrWhiteSpace($env:GITHUB_JOB) -or @($status).Count -ne 0) {
        throw 'Missing, contradictory or dirty producer identity.'
    }
    $workflow = '.github/workflows/nuget-publish.yml'
    if (-not $env:GITHUB_WORKFLOW_REF.StartsWith("$env:GITHUB_REPOSITORY/$workflow@", [StringComparison]::Ordinal)) {
        throw 'Unexpected NuGet producer definition.'
    }
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $sdk = (& $dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') { throw 'The evidence producer requires .NET 10.' }
    return [ordered]@{
        source = [ordered]@{
            repository = $env:GITHUB_REPOSITORY; actualSha = $sha; actualRef = $env:GITHUB_REF; trackedClean = $true
        }
        producer = [ordered]@{
            system = 'github-actions'; workflow = $workflow; definitionSha = $env:GITHUB_WORKFLOW_SHA
            runId = $env:GITHUB_RUN_ID; attempt = [int]$env:GITHUB_RUN_ATTEMPT; job = $env:GITHUB_JOB
            tools = @(@{ id = 'dotnet'; version = $sdk; digest = Get-NugetDigest $dotnet })
        }
        release = @{ group = 'nuget'; version = $Version; channel = (Get-NugetChannel $Version) }
        policyDigest = Get-NugetDigest (Join-Path $RepositoryRoot '.azurepipelines\release\policy.json')
        artifacts = @()
    }
}

function New-NugetCaptureRequest {
    param($Context, [string]$Configuration, [string]$RepositoryRoot, [string]$Solution)
    [xml]$sln = Get-Content -LiteralPath $Solution -Raw
    $projects = @($sln.SelectNodes('//Project') | ForEach-Object {
        $relative = $_.GetAttribute('Path').Replace('\', '/')
        $null = Resolve-NugetPath $RepositoryRoot $relative
        $relative
    } | Sort-Object -Unique)
    if ($projects.Count -eq 0) { throw 'The built solution has no projects.' }
    return [ordered]@{
        source = $Context.source; producer = $Context.producer; version = $Context.release.version
        configuration = $Configuration; projects = $projects
    }
}

function Invoke-NugetEvidenceTool {
    param([string]$Tool, [string[]]$Arguments, [string]$Log)
    if (-not (Test-Path -LiteralPath $Tool -PathType Leaf)) { throw 'Evidence tool is unavailable.' }
    # Tool diagnostics can contain cache/feed paths. Only sanitized findings leave this runner.
    & dotnet $Tool @Arguments *> $Log
    $code = $LASTEXITCODE
    if ($code -notin @(0, 1)) {
        Stop-NugetEvidence 'Evidence tool rejected its inputs; diagnostic log retained locally.' 2
    }
    return $code
}

function Write-NugetFinding {
    param([string]$Output, [string]$Operation, [string]$Code, [bool]$Blocking = $false,
        [int]$ExitCode = 0)
    Write-NugetJson ([ordered]@{
        schemaVersion = 1; operation = $Operation; status = 'incomplete'; blocking = $Blocking
        baselineFailed = ($ExitCode -eq 1); exitCode = $ExitCode
        unmetControls = @($Code); detail = 'The new collector did not finish; no verification credit was awarded.'
    }) $Output
    Write-Host "::warning::NuGet evidence $Operation incomplete ($Code)."
}

function Copy-NugetDocument {
    param([string]$SourceRoot, [string]$DestinationRoot, [string]$Relative, [string]$Digest)
    $source = Resolve-NugetPath $SourceRoot $Relative
    if ((Get-NugetDigest $source) -cne $Digest) { throw 'Sidecar bytes changed.' }
    Assert-NugetPublicJson $source
    $destination = Resolve-NugetPath $DestinationRoot $Relative
    if (Test-Path -LiteralPath $destination) { throw 'Duplicate sidecar path, including identical bytes.' }
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)
    Copy-Item -LiteralPath $source -Destination $destination
}

function Assert-NugetPublicProjection {
    param([string]$Root, [string]$Index = 'release-evidence.json')
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($Index)
    while ($queue.Count -gt 0) {
        $relative = $queue.Dequeue()
        if (-not $visited.Add($relative)) { continue }
        if ($visited.Count -gt 4096) { Stop-NugetEvidence 'Public document graph exceeds its traversal bound.' 2 }
        $path = Resolve-NugetPath $Root $relative
        Assert-NugetPublicJson $path
        $document = Read-NugetJson $path
        $parent = Split-Path -Parent $relative
        if ($document.Contains('documents')) {
            Assert-NugetPublicFields $document @('schemaVersion', 'source', 'producer', 'release', 'policy',
                'artifacts', 'documents', 'assurance', 'assessment')
            foreach ($link in $document.documents) {
                Assert-NugetPublicFields $link @('type', 'format', 'version', 'path', 'digest', 'subject')
                $targetRelative = if ($parent) { $parent.Replace('\', '/') + '/' + $link.path } else { $link.path }
                $target = Resolve-NugetPath $Root $targetRelative
                if ((Get-NugetDigest $target) -cne $link.digest) {
                    Stop-NugetEvidence 'A transitive public document no longer matches its index digest.' 2
                }
                if ($link.type -ceq 'sbom') {
                    $matching = @($document.artifacts | Where-Object {
                        $_.id -ceq $link.subject.id -and $_.kind -ceq $link.subject.kind -and
                        $_.digest -ceq $link.subject.digest
                    })
                    if ($matching.Count -ne 1) {
                        Stop-NugetEvidence 'A package BOM does not bind exactly one indexed package subject.' 2
                    }
                    Assert-NugetPublicBom $target $matching[0]
                }
                elseif ($link.type -ceq 'inventory') {
                    $inventory = Read-NugetJson $target
                    Assert-NugetPublicFields $inventory @('schemaVersion', 'artifact', 'licenses',
                        'consumerDependencies', 'externalPrerequisites', 'resolvedGraphs', 'payloads', 'unmetControls')
                    Assert-NugetPublicFields $inventory.artifact @('kind', 'id', 'version', 'configuration',
                        'digest', 'scopes', 'size')
                    Assert-NugetPublicFields $inventory.artifact.scopes @('tfms', 'rids', 'roslyn', 'platforms')
                    foreach ($consumer in $inventory.consumerDependencies) {
                        Assert-NugetPublicFields $consumer @('id', 'range', 'target')
                    }
                    $licenses = @($inventory.licenses)
                    foreach ($payload in $inventory.payloads) {
                        Assert-NugetPublicFields $payload @('path', 'digest', 'classification', 'targets',
                            'roslyn', 'owner', 'version')
                    }
                    foreach ($graph in $inventory.resolvedGraphs) {
                        Assert-NugetPublicFields $graph @('id', 'version', 'type', 'target',
                            'dependencies', 'licenses', 'project')
                        $licenses += @($graph.licenses)
                    }
                    foreach ($license in $licenses | Where-Object { $null -ne $_ }) {
                        Assert-NugetPublicFields $license @('kind', 'value', 'digest')
                    }
                }
                elseif ($link.type -ceq 'input-manifest') {
                    $inputs = Read-NugetJson $target
                    Assert-NugetPublicFields $inputs @('schemaVersion', 'source', 'buildInputDigests', 'mappings')
                    Assert-NugetPublicFields $inputs.source @('repository', 'actualSha', 'actualRef', 'trackedClean',
                        'eventSha', 'pullRequestHeadSha', 'syntheticMergeSha')
                    foreach ($mapping in $inputs.mappings) {
                        Assert-NugetPublicFields $mapping @('project', 'configuration', 'packageId', 'version',
                            'isPackable', 'includeSymbols', 'symbolPackageFormat', 'hasPdb', 'graphDigest')
                    }
                }
                $queue.Enqueue($targetRelative)
            }
        }
    }
}

function Assert-NugetPublicFields {
    param($Value, [string[]]$Allowed)
    if ($Value -isnot [Collections.IDictionary]) { Stop-NugetEvidence 'Expected a public JSON object.' 2 }
    foreach ($field in $Value.Keys) {
        if ($field -cnotin $Allowed) { Stop-NugetEvidence 'An undeclared field is outside the public projection.' 2 }
    }
}

function Assert-NugetAssessment {
    param($Assessment, [switch]$Companion)
    $allowed = @('status', 'unmetControls')
    if ($Companion) { $allowed += @('pendingControls', 'observedControls') }
    Assert-NugetPublicFields $Assessment $allowed
    if ($Assessment.status -cnotin @('complete', 'incomplete') -or
        $Assessment.unmetControls -isnot [Array]) {
        Stop-NugetEvidence 'Malformed producer assessment.' 2
    }
    $hasPending = $Assessment.Contains('pendingControls')
    $hasObserved = $Assessment.Contains('observedControls')
    if ($hasPending -xor $hasObserved) {
        Stop-NugetEvidence 'A classified assessment requires both pending and observed controls.' 2
    }
    if ($hasPending) {
        $sets = @{}
        foreach ($name in @('unmetControls', 'pendingControls', 'observedControls')) {
            if ($Assessment[$name] -isnot [Array]) {
                Stop-NugetEvidence 'Assessment classifications must be arrays.' 2
            }
            $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($control in $Assessment[$name]) {
                if ($control -isnot [string] -or -not $set.Add($control)) {
                    Stop-NugetEvidence 'Assessment classifications contain invalid or duplicate controls.' 2
                }
            }

            $sets[$name] = $set
        }
        if ($sets.pendingControls.Overlaps($sets.observedControls)) {
            Stop-NugetEvidence 'Pending and observed control classifications overlap.' 2
        }
        $sets.pendingControls.UnionWith($sets.observedControls)
        if (-not $sets.pendingControls.SetEquals($sets.unmetControls)) {
            Stop-NugetEvidence 'Assessment classifications do not exactly cover unmet controls.' 2
        }
    }
}

function Get-NugetProducerClassification {
    param([string]$Root, $Envelope)
    $found = $null
    foreach ($document in $Envelope.documents | Where-Object {
        $_.type -ceq 'producer-record' -and $_.version -ceq '1'
    }) {
        $path = Resolve-NugetPath $Root $document.path
        $record = Read-NugetJson $path
        if ($record.kind -cne 'producer-assessment') { continue }
        if ((Get-NugetDigest $path) -cne $document.digest -or $record.schemaVersion -ne 1 -or $found) {
            Stop-NugetEvidence 'Invalid producer classification document binding.' 2
        }
        Assert-NugetPublicFields $record @('schemaVersion', 'kind', 'source', 'producer', 'release',
            'artifacts', 'unmetControls', 'pendingControls', 'observedControls')
        if ($record.source.actualSha -cne $Envelope.source.actualSha -or
            $record.producer.runId -cne $Envelope.producer.runId -or
            $record.producer.attempt -ne $Envelope.producer.attempt -or
            $record.release.group -cne $Envelope.release.group -or
            $record.release.version -cne $Envelope.release.version) {
            Stop-NugetEvidence 'Producer classification scope differs from its envelope.' 2
        }
        $found = @{
            status = $Envelope.assessment.status; unmetControls = $record.unmetControls
        }
        if ($record.Contains('pendingControls')) { $found.pendingControls = $record.pendingControls }
        if ($record.Contains('observedControls')) { $found.observedControls = $record.observedControls }
        if (-not $found.Contains('pendingControls') -or -not $found.Contains('observedControls')) {
            Stop-NugetEvidence 'Producer classification is missing its complete control partition.' 2
        }
        Assert-NugetAssessment $found -Companion
    }
    return $found
}

function Assert-NugetPublicBom {
    param([string]$Path, $Artifact)
    $bom = Read-NugetJson $Path
    Assert-NugetPublicFields $bom @('$schema', 'bomFormat', 'specVersion', 'version',
        'metadata', 'components', 'dependencies', 'properties')
    if ($bom.bomFormat -cne 'CycloneDX' -or $bom.specVersion -cne '1.6') {
        Stop-NugetEvidence 'Public NuGet BOM must use the supported native CycloneDX 1.6 format.' 2
    }
    Assert-NugetPublicFields $bom.metadata @('component')
    $subject = $bom.metadata.component
    if ($subject.name -cne $Artifact.id -or $subject.version -cne $Artifact.version -or
        @($subject.hashes | Where-Object {
            $_.alg -ceq 'SHA-256' -and "sha256:$($_.content)" -ceq $Artifact.digest
        }).Count -ne 1) {
        Stop-NugetEvidence 'The public BOM metadata does not bind its actual package digest and version.' 2
    }
    $properties = @($bom.properties)
    foreach ($component in @($subject) + @($bom.components)) {
        Assert-NugetPublicFields $component @('type', 'bom-ref', 'name', 'version', 'purl',
            'hashes', 'licenses', 'properties')
        foreach ($hash in $component.hashes) { Assert-NugetPublicFields $hash @('alg', 'content') }
        foreach ($license in $component.licenses) {
            Assert-NugetPublicFields $license @('license', 'expression')
            if ($license.license) {
                Assert-NugetPublicFields $license.license @('name', 'url', 'properties')
                $properties += @($license.license.properties)
            }
        }
        $properties += @($component.properties)
    }
    foreach ($dependency in $bom.dependencies) {
        Assert-NugetPublicFields $dependency @('ref', 'dependsOn')
    }
    $names = @('producer', 'reconciliation', 'artifact-kind', 'configuration', 'ownership', 'target',
        'project', 'classification', 'owner', 'owner-version', 'targets', 'roslyn', 'declared-version-range',
        'license-file-digest')
    foreach ($property in $properties | Where-Object { $null -ne $_ }) {
        Assert-NugetPublicFields $property @('name', 'value')
        if ($property.name -cnotin @($names | ForEach-Object { "opcua:$_" })) {
            Stop-NugetEvidence 'A BOM property is outside the declared public producer projection.' 2
        }
    }
    Assert-NugetPublicJson $Path
}

function Copy-NugetPublicAttachment {
    param([string]$Packages, [string]$Output)
    $root = Join-Path $Packages 'evidence'
    Assert-NugetPublicProjection $root
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $Output 'evidence')
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue('release-evidence.json')
    while ($queue.Count -gt 0) {
        $relative = $queue.Dequeue()
        if (-not $visited.Add($relative)) { continue }
        if ($visited.Count -gt 4096) { Stop-NugetEvidence 'Public attachment exceeds its document bound.' 2 }
        $path = Resolve-NugetPath $root $relative
        Copy-NugetDocument $root (Join-Path $Output 'evidence') $relative (Get-NugetDigest $path)
        $document = Read-NugetJson $path
        $parent = Split-Path -Parent $relative
        foreach ($link in $document.documents) {
            $target = if ($parent) { $parent.Replace('\', '/') + '/' + $link.path } else { $link.path }
            $queue.Enqueue($target)
        }
    }
    foreach ($relative in @('producer-context.json', 'Release/pack-provenance.json', 'Debug/pack-provenance.json')) {
        $path = Resolve-NugetPath $root $relative
        if (Test-Path -LiteralPath $path) {
            Copy-NugetDocument $root (Join-Path $Output 'evidence') $relative (Get-NugetDigest $path)
        }
    }
    $proofIndex = Join-Path $Packages 'proof-results.json'
    if (Test-Path -LiteralPath $proofIndex) {
        $index = Read-NugetJson $proofIndex
        Copy-NugetDocument $Packages $Output 'proof-results.json' (Get-NugetDigest $proofIndex)
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $Output 'proofs')
        foreach ($proof in $index.proofs | Where-Object bundle) {
            Copy-NugetDocument (Join-Path $Packages 'proofs') (Join-Path $Output 'proofs') `
                $proof.bundle $proof.bundleDigest
        }
    }
    $verificationPath = Join-Path $Packages 'verification-bundle.json'
    if (Test-Path -LiteralPath $verificationPath) {
        $verification = Read-NugetJson $verificationPath
        # Approval, boundary and finding-review records remain controlled; only native producer bundles are public.
        $publicVerification = @{ schemaVersion = 1; proofs = @(); nativeNuget = @($verification.nativeNuget) }
        foreach ($proof in $publicVerification.nativeNuget) {
            Assert-NugetPublicFields $proof @('role', 'authorityId', 'bundlePath', 'digest', 'indexPath')
            if ($proof.Contains('indexPath') -and $proof.indexPath -cne 'evidence/release-evidence.json') {
                Stop-NugetEvidence 'The public native index must reference the immutable producer envelope.' 2
            }
            $source = Resolve-NugetPath $Packages $proof.bundlePath
            $destination = Resolve-NugetPath $Output $proof.bundlePath
            if ((Get-NugetDigest $source) -cne $proof.digest) {
                Stop-NugetEvidence 'A native proof changed before public projection.' 2
            }
            if (Test-Path -LiteralPath $destination) {
                if ((Get-NugetDigest $destination) -cne $proof.digest) {
                    Stop-NugetEvidence 'Public verification paths have conflicting bytes.' 2
                }
            }
            else { Copy-NugetDocument $Packages $Output $proof.bundlePath $proof.digest }
        }
        Write-NugetJson $publicVerification (Join-Path $Output 'verification-bundle.json')
    }
}

function New-NugetAssurance {
    param([string]$RepositoryRoot)
    $profiles = Read-NugetJson (Join-Path $RepositoryRoot '.azurepipelines\assurance\profiles.json')
    $catalog = Read-NugetJson (Join-Path $RepositoryRoot '.azurepipelines\release\artifacts.json')
    $group = @($catalog.groups | Where-Object id -CEQ 'nuget')[0]
    $jobs = @($profiles.profiles | Where-Object { $_.id -cin $group.profiles } | ForEach-Object {
        $profile = $_
        foreach ($job in $profile.jobs) {
            [ordered]@{
                id = $job.id; profile = $profile.id; project = $job.project
                configuration = $profile.configuration; host = $profile.host; hostTfm = $profile.hostTfm
                libraryTfm = $profile.libraryTfm; platform = $profile.platform; shard = 'all'; filter = $profile.filter
                selected = $false; status = 'missing'; inputIds = @()
            }
        }
    })
    return [ordered]@{
        profiles = @($group.profiles); expected = $jobs.Count; selected = 0; completed = 0; failed = 0
        missing = $jobs.Count; notApplicable = 0; jobs = $jobs; inputIdentities = @()
    }
}

function Merge-NugetSidecars {
    param([string]$RepositoryRoot, [string]$Inputs, [string]$Packages, [string]$Output, $Context)
    $manifestPath = Join-Path $Packages 'release-manifest.json'
    $manifest = Read-NugetJson $manifestPath
    if ($manifest.schemaVersion -ne 1 -or $manifest.commit -cne $Context.source.actualSha -or
        $manifest.repository -cne $Context.source.repository -or $manifest.runId -cne $Context.producer.runId -or
        $manifest.ref -cne $Context.source.actualRef -or $manifest.workflow -cne $Context.producer.workflow -or
        $manifest.packageVersion -cne $Context.release.version) {
        Stop-NugetEvidence 'V1/source/context identity mismatch.' 1
    }
    $null = New-Item -ItemType Directory -Force -Path $Output
    if (@(Get-ChildItem -LiteralPath $Output -Force).Count -ne 0) { throw 'Aggregate output must be empty.' }
    $policy = Get-NugetPolicy $RepositoryRoot
    $artifacts = @()
    $documents = @()
    $mappings = @()
    $inputDigests = @()
    $controls = @()
    $pendingControls = @()
    $observedControls = @()
    $classifiedControls = $true
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $digests = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($configuration in @('Release', 'Debug')) {
        $root = Join-Path $Inputs "opcua-$configuration-$($Context.producer.runId)\evidence"
        $envelopePath = Join-Path $root 'release-evidence.json'
        $envelope = Read-NugetJson $envelopePath
        Assert-NugetAssessment $envelope.assessment
        if ($envelope.schemaVersion -ne 2 -or $envelope.source.actualSha -cne $Context.source.actualSha -or
            $envelope.source.repository -cne $Context.source.repository -or
            $envelope.source.actualRef -cne $Context.source.actualRef -or -not $envelope.source.trackedClean -or
            $envelope.producer.runId -cne $Context.producer.runId -or
            $envelope.producer.attempt -ne $Context.producer.attempt -or
            $envelope.producer.workflow -cne $Context.producer.workflow -or
            $envelope.producer.definitionSha -cne $Context.producer.definitionSha -or
            $envelope.release.version -cne $Context.release.version -or
            $envelope.policy.digest -cne $Context.policyDigest) {
            Stop-NugetEvidence 'Configuration evidence identity mismatch.' 1
        }
        $destinationRoot = Join-Path $Output $configuration
        $null = New-Item -ItemType Directory -Force -Path $destinationRoot
        Copy-NugetDocument $root $destinationRoot 'release-evidence.json' (Get-NugetDigest $envelopePath)
        foreach ($artifact in $envelope.artifacts) {
            if ($artifact.configuration -cne $configuration -or
                -not $seen.Add("$($artifact.kind)|$($artifact.id)") -or -not $digests.Add($artifact.digest)) {
                throw 'Duplicate/colliding artifact or wrong configuration.'
            }
            $archive = @($manifest.archives | Where-Object {
                $_.id -ceq $artifact.id -and $_.version -ceq $artifact.version -and
                $_.type -ceq $(if ($artifact.kind -eq 'nuget-symbols') { 'symbols' } else { 'package' })
            })
            if ($archive.Count -ne 1 -or "sha256:$($archive[0].sha256)" -cne $artifact.digest -or
                (Get-NugetDigest (Resolve-NugetPath $Packages $archive[0].file)) -cne $artifact.digest) {
                Stop-NugetEvidence 'Signed archive changed or has no unique v1 identity.' 1
            }
            $artifacts += $artifact
        }
        $classification = Get-NugetProducerClassification $root $envelope
        foreach ($document in $envelope.documents) {
            if ($document.type -cnotin @('sbom', 'inventory', 'input-manifest', 'policy',
                'artifact-catalog', 'profile', 'producer-record')) {
                Stop-NugetEvidence 'Unexpected producer document type.' 2
            }
            if ($document.type -ceq 'producer-record' -and (
                $document.format -cne 'json' -or $document.version -cne '1' -or
                (Read-NugetJson (Resolve-NugetPath $root $document.path)).kind -cne 'producer-assessment')) {
                Stop-NugetEvidence 'Unexpected producer classification format.' 2
            }
            Copy-NugetDocument $root $destinationRoot $document.path $document.digest
            $rebasedDocument = $document.Clone()
            $rebasedDocument.path = "$configuration/$($document.path)"
            $documents += $rebasedDocument
        }
        $summary = Read-NugetJson (Join-Path $root 'source-inputs.json')
        if ($summary.source.actualSha -cne $Context.source.actualSha -or
            @($summary.mappings | Where-Object configuration -CNE $configuration).Count -ne 0) {
            Stop-NugetEvidence 'Mapping source/configuration mismatch.' 1
        }
        $mappings += @($summary.mappings)
        $inputDigests += @($summary.buildInputDigests)
        $controls += @($envelope.assessment.unmetControls)
        if ($classification) {
            $pendingControls += @($classification.pendingControls)
            $observedControls += @($classification.observedControls)
        }
        else { $classifiedControls = $false }
    }
    if ($artifacts.Count -ne @($manifest.archives).Count) {
        Stop-NugetEvidence 'Archive inventory and sidecars differ.' 1
    }
    if (@($mappings | Group-Object { "$($_.project)|$($_.configuration)" } | Where-Object Count -GT 1).Count) {
        throw 'Duplicate evaluated project/configuration mapping.'
    }
    # Reconcile both halves against expectations expanded from the catalog, not observed archives.
    $catalog = Read-NugetJson (Join-Path $RepositoryRoot '.azurepipelines\release\artifacts.json')
    $group = @($catalog.groups | Where-Object id -CEQ 'nuget')[0]
    $modern = @(Get-Content -LiteralPath (Resolve-NugetPath $RepositoryRoot $group.modernCatalog) |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    if (@($modern | Sort-Object -Unique).Count -ne $modern.Count) { throw 'Duplicate catalog ID.' }
    $metadataIds = @($group.variants | Where-Object id -CEQ 'metapackages' | ForEach-Object {
        foreach ($source in $_.sources) {
            [xml]$nuspec = Get-Content -LiteralPath (Resolve-NugetPath $RepositoryRoot $source) -Raw
            [string]$nuspec.package.metadata.id
        }
    })
    $debug = @($mappings | Where-Object {
        $_.isPackable -and $_.configuration -ceq 'Debug' -and $_.packageId.EndsWith('.Debug') -and
        $_.packageId.Substring(0, $_.packageId.Length - 6) -in $modern
    } | ForEach-Object packageId)
    $expected = @($modern) + @($metadataIds) + @($debug)
    $actual = @($artifacts | Where-Object kind -CEQ 'nuget-package' | ForEach-Object id)
    $membership = $debug.Count -gt 0 -and $expected.Count -eq $actual.Count -and
        @($expected | Where-Object { $_ -cnotin $actual }).Count -eq 0 -and
        @($modern | Where-Object {
            $id = $_
            @($mappings | Where-Object {
                $_.isPackable -and $_.configuration -ceq 'Release' -and $_.packageId -ceq $id
            }).Count -ne 1
        }).Count -eq 0
    foreach ($mapping in $mappings | Where-Object {
        $_.isPackable -and $_.packageId -cin $actual -and $_.includeSymbols -and
        $_.symbolPackageFormat -ceq 'snupkg' -and $_.hasPdb
    }) {
        if (@($artifacts | Where-Object { $_.kind -ceq 'nuget-symbols' -and $_.id -ceq $mapping.packageId }).Count -ne 1) {
            $membership = $false
        }
    }
    if ($membership) { $controls = @($controls | Where-Object { $_ -cne 'ARTIFACT_MEMBERSHIP' }) }
    else { $controls += 'ARTIFACT_MEMBERSHIP' }
    Write-NugetJson (@{
        schemaVersion = 1; source = $Context.source; buildInputDigests = @($inputDigests | Sort-Object -Unique)
        mappings = @($mappings | Sort-Object project, configuration)
    }) (Join-Path $Output 'source-inputs.json')
    $sourceDigest = Get-NugetDigest (Join-Path $Output 'source-inputs.json')
    $sourceSubject = @{ kind = 'source'; id = "$($Context.source.repository)@$($Context.source.actualSha)"; digest = $sourceDigest }
    $documents += @{ type = 'input-manifest'; format = 'json'; version = '1'; path = 'source-inputs.json'
        digest = $sourceDigest; subject = $sourceSubject }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $Output 'release-manifest.json')
    $manifestDigest = Get-NugetDigest $manifestPath
    $documents += @{ type = 'archive-manifest'; format = 'json'; version = '1'; path = 'release-manifest.json'
        digest = $manifestDigest; subject = @{ kind = 'artifact-set'; id = 'nuget'; digest = $manifestDigest } }
    $assessment = [ordered]@{ status = 'incomplete'; unmetControls = @($controls | Sort-Object -Unique) }
    if ($classifiedControls) {
        $observedControls = @($observedControls | Where-Object { $_ -cin $controls })
        $observedControls += @($controls | Where-Object { $_ -cnotin $pendingControls -and $_ -cnotin $observedControls })
        $observedControls = @($observedControls | Sort-Object -Unique)
        $pendingControls = @($pendingControls | Where-Object {
            $_ -cin $controls -and $_ -cnotin $observedControls
        } | Sort-Object -Unique)
        $classification = [ordered]@{
            schemaVersion = 1; kind = 'producer-assessment'
            source = $Context.source; producer = $Context.producer; release = $Context.release
            artifacts = @($artifacts | Sort-Object id, kind)
            unmetControls = $assessment.unmetControls; pendingControls = $pendingControls
            observedControls = $observedControls
        }
        Write-NugetJson $classification (Join-Path $Output 'producer-assessment.json')
        $documents += @{
            type = 'producer-record'; format = 'json'; version = '1'; path = 'producer-assessment.json'
            digest = Get-NugetDigest (Join-Path $Output 'producer-assessment.json'); subject = $sourceSubject
        }
    }
    $envelope = [ordered]@{
        schemaVersion = 2; source = $Context.source; producer = $Context.producer; release = $Context.release
        policy = @{ id = $policy.id; version = $policy.version; digest = $Context.policyDigest; stage = $policy.stage }
        artifacts = @($artifacts | Sort-Object id, kind); documents = $documents
        assurance = New-NugetAssurance $RepositoryRoot
        assessment = $assessment
    }
    Write-NugetJson $envelope (Join-Path $Output 'release-evidence.json')
    return $envelope
}

function New-NugetProvenance {
    param($Context, [string]$Configuration)
    return [ordered]@{
        buildDefinition = @{
            buildType = 'https://github.com/OPCFoundation/UA-.NETStandard/nuget-pack/v1'
            externalParameters = @{ configuration = $Configuration; version = $Context.release.version }
            internalParameters = @{}
            resolvedDependencies = @(@{
                uri = "git+https://github.com/$($Context.source.repository)@$($Context.source.actualRef)"
                digest = @{ gitCommit = $Context.source.actualSha }
            })
        }
        runDetails = @{
            builder = @{ id = "https://github.com/$($Context.source.repository)/$($Context.producer.workflow)@$($Context.producer.definitionSha)" }
            metadata = @{
                invocationId = "https://github.com/$($Context.source.repository)/actions/runs/$($Context.producer.runId)/attempts/$($Context.producer.attempt)"
            }
        }
    }
}

function Get-NugetEvaluationEvidence {
    param([string]$RepositoryRoot, [string]$Packages, [string]$Work, $Context)
    $sourceRoot = Join-Path $Packages 'evidence'
    $original = Join-Path $sourceRoot 'release-evidence.json'
    $assuranceRoot = Join-Path $Packages 'assurance'
    $componentPath = Join-Path $assuranceRoot 'assurance.json'
    if (-not (Test-Path -LiteralPath $componentPath)) { return $original }

    $receipt = Read-NugetJson (Join-Path $assuranceRoot 'assurance.metadata.json')
    if ($receipt.kind -cne 'assurance-metadata-receipt' -or $receipt.mode -cne 'github-api' -or
        $receipt.repository -cne $Context.source.repository -or
        $receipt.sourceSha -cne $Context.source.actualSha -or
        $receipt.ref -cne $Context.source.actualRef -or
        $receipt.policyDigest -cne $Context.policyDigest -or
        $receipt.profileDigest -cne (Get-NugetDigest (Join-Path $RepositoryRoot '.azurepipelines/assurance/profiles.json')) -or
        $receipt.componentDigest -cne (Get-NugetDigest $componentPath)) {
        throw 'Assurance metadata does not bind the selected source, policy and component.'
    }
    $component = Read-NugetJson $componentPath
    $schema = Read-NugetJson (Join-Path $RepositoryRoot '.azurepipelines/release/evidence.schema.json')
    $schema['$ref'] = '#/$defs/assurance'
    foreach ($key in @('required', 'properties', 'additionalProperties')) { $schema.Remove($key) }
    if (-not (Test-Json -Json ($component | ConvertTo-Json -Depth 40) `
        -Schema ($schema | ConvertTo-Json -Depth 100))) {
        throw 'The assurance component does not satisfy the contract.'
    }
    $evaluationRoot = Join-Path $Work 'evaluation'
    $null = New-Item -ItemType Directory -Path $evaluationRoot
    $envelope = Read-NugetJson $original
    foreach ($document in $envelope.documents) {
        Copy-NugetDocument $sourceRoot $evaluationRoot $document.path $document.digest
    }
    $originalDigest = Get-NugetDigest $original
    Copy-NugetDocument $sourceRoot $evaluationRoot 'release-evidence.json' $originalDigest
    Move-Item -LiteralPath (Join-Path $evaluationRoot 'release-evidence.json') `
        -Destination (Join-Path $evaluationRoot 'producer-release-evidence.json')
    $envelope.documents += @{
        type = 'producer-record'; format = 'json'; version = '2'; path = 'producer-release-evidence.json'
        digest = $originalDigest
        subject = @{ kind = 'source'; id = "$($Context.source.repository)@$($Context.source.actualSha)"; digest = $originalDigest }
    }
    $copied = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($input in $component.inputIdentities) {
        $destination = 'assurance/' + $input.path
        if ($copied.Add($destination)) {
            $inputRoot = if ($input.kind -ceq 'profile') { $RepositoryRoot } else { $assuranceRoot }
            $source = Resolve-NugetPath $inputRoot $input.path
            if ((Get-NugetDigest $source) -cne $input.digest) { throw 'Assurance input bytes changed.' }
            Assert-NugetPublicJson $source
            $target = Resolve-NugetPath $evaluationRoot $destination
            $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
            Copy-Item -LiteralPath $source -Destination $target
        }
        $input.path = $destination
    }
    foreach ($job in $component.jobs | Where-Object resultDocument) {
        $relative = $job.resultDocument
        $destination = 'assurance/' + $relative
        $digest = Get-NugetDigest (Resolve-NugetPath $assuranceRoot $relative)
        if ($copied.Add($destination)) {
            Copy-NugetDocument $assuranceRoot (Join-Path $evaluationRoot 'assurance') $relative $digest
        }
        $job.resultDocument = $destination
        $envelope.documents += @{
            type = 'assurance-summary'; format = 'json'; version = '1'; path = $destination; digest = $digest
            subject = @{ kind = 'source'; id = "$($Context.source.repository)@$($Context.source.actualSha)"; digest = $digest }
        }
    }
    # Re-evaluation never rewrites the immutable, attested producer envelope.
    $envelope.assurance = $component
    $path = Join-Path $evaluationRoot 'release-evidence.json'
    Write-NugetJson $envelope $path
    return $path
}

function Write-NugetReceipt {
    param([string]$Path, [string]$Packages, [string]$Destination, [string]$File, [string]$State,
        [string]$DeliveredDigest, [string]$Verification = 'not-performed',
        [string]$PolicyDigest = 'unavailable', [string]$ContentReportDigest)
    $manifest = Read-NugetJson (Join-Path $Packages 'release-manifest.json')
    $evidence = Join-Path $Packages 'evidence\release-evidence.json'
    $manifestDigest = Get-NugetDigest (Join-Path $Packages 'release-manifest.json')
    $evidenceDigest = if (Test-Path -LiteralPath $evidence) { Get-NugetDigest $evidence } else { 'unavailable' }
    $events = $Path + '.events'
    if (Test-Path -LiteralPath $Path) { $receipt = Read-NugetJson $Path }
    else {
        $receipt = [ordered]@{
            schemaVersion = 1; repository = $manifest.repository; sourceSha = $manifest.commit
            version = $manifest.packageVersion; destination = $Destination; status = 'incomplete'
            evidenceDigest = $evidenceDigest; manifestDigest = $manifestDigest
            producerRunId = $manifest.runId
            producerAttempt = if ($env:SOURCE_ATTEMPT) { $env:SOURCE_ATTEMPT } else { $env:GITHUB_RUN_ATTEMPT }
            deliveryRunId = $env:GITHUB_RUN_ID; deliveryAttempt = $env:GITHUB_RUN_ATTEMPT
            symbols = @{
                expected = $manifest.symbolPackageCount
                status = if ($manifest.symbolPackageCount -gt 0) { 'unresolved' } else { 'not-present' }
                verification = 'not-performed'
            }
            packages = @($manifest.archives | Where-Object type -CEQ 'package' | ForEach-Object {
                @{ file = $_.file; id = $_.id; version = $_.version; authorDigest = "sha256:$($_.sha256)"
                    state = 'not-attempted'; verification = 'not-performed' }
            })
        }
    }
    if ($receipt.destination -cne $Destination -or $receipt.sourceSha -cne $manifest.commit -or
        $receipt.manifestDigest -cne $manifestDigest -or $receipt.evidenceDigest -cne $evidenceDigest -or
        $receipt.producerRunId -cne $manifest.runId) {
        Stop-NugetEvidence 'Receipt destination/source/immutable evidence mismatch.' 1
    }
    if ($File) {
        $entry = @($receipt.packages | Where-Object file -CEQ $File)
        if ($entry.Count -ne 1) { throw 'Unknown receipt package.' }
        $entry[0].state = $State
        $entry[0].verification = $Verification
        if ($DeliveredDigest) { $entry[0].deliveredDigest = $DeliveredDigest }
    }
    $null = New-Item -ItemType Directory -Force -Path $events
    $event = [ordered]@{
        schemaVersion = 1; kind = 'nuget-delivery-event'; eventId = [guid]::NewGuid().ToString('N')
        observedAt = [DateTimeOffset]::UtcNow.ToString('O')
        manifestDigest = $manifestDigest; evidenceDigest = $evidenceDigest
        policyDigest = $PolicyDigest; contentReportDigest = $ContentReportDigest
        sourceSha = $manifest.commit; producerRunId = $receipt.producerRunId
        producerAttempt = $receipt.producerAttempt; deliveryRunId = $env:GITHUB_RUN_ID
        deliveryAttempt = $env:GITHUB_RUN_ATTEMPT; destination = $Destination
        file = $File; state = $State; verification = $Verification
        authorDigest = if ($File) { $entry[0].authorDigest } else { $null }
        deliveredDigest = $DeliveredDigest
    }
    $eventPath = Join-Path $events "$($event.eventId).json"
    $bytes = [Text.Encoding]::UTF8.GetBytes(($event | ConvertTo-Json -Depth 10))
    $stream = [IO.File]::Open($eventPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes); $stream.Flush($true) }
    finally { $stream.Dispose() }
    # The first snapshot is immutable. Later observations are independent append-only events.
    if (-not (Test-Path -LiteralPath $Path)) { Write-NugetJson $receipt $Path }
}

function Get-NugetDeliveryState {
    param([string]$Path)
    $receipt = Read-NugetJson $Path
    $events = @(Get-ChildItem -LiteralPath ($Path + '.events') -File -Filter '*.json' |
        ForEach-Object { Read-NugetJson $_.FullName } | Sort-Object observedAt, eventId)
    if ($events.Count -gt 16384) { Stop-NugetEvidence 'Delivery journal exceeds its operation bound.' 2 }
    foreach ($event in $events) {
        if ($event.kind -cne 'nuget-delivery-event' -or $event.manifestDigest -cne $receipt.manifestDigest -or
            $event.evidenceDigest -cne $receipt.evidenceDigest -or $event.sourceSha -cne $receipt.sourceSha -or
            $event.destination -cne $receipt.destination -or $event.producerRunId -cne $receipt.producerRunId -or
            $event.producerAttempt -cne $receipt.producerAttempt) {
            Stop-NugetEvidence 'Delivery journal event has a conflicting binding.' 1
        }
        if ($event.file) {
            $entry = @($receipt.packages | Where-Object file -CEQ $event.file)
            if ($entry.Count -ne 1) { Stop-NugetEvidence 'Delivery event names an unknown archive.' 2 }
            $entry[0].state = $event.state
            $entry[0].verification = $event.verification
            if ($event.deliveredDigest) { $entry[0].deliveredDigest = $event.deliveredDigest }
        }
    }
    return $receipt
}
