# Copyright (c) OPC Foundation. Licensed under the MIT License.
# These structural checks supplement, and never replace, authenticated producer assessment.
function Get-AssuranceNativeProof($Proof, $Record) {
    $native = $Proof.native
    if ($Proof.kind -cne 'native-aot' -or $Proof.status -cne 'completed' -or
        $native.project -cne $Record.project -or $native.configuration -cne $Record.configuration -or
        $native.hostTfm -cne 'net10.0' -or $native.libraryTfm -cne 'net10.0' -or
        $native.runtimeIdentifier -cne 'win-x64' -or $native.publishAot -isnot [bool] -or -not $native.publishAot -or
        $native.sourceSha -cne $Record.sourceSha -or $native.runId -cne $Record.producer.runId -or
        $native.attempt -ne $Record.producer.attempt -or $native.nonce -cnotmatch '^[0-9a-f]{32}$' -or
        $native.processId -le 0 -or $native.exitCode -ne 0 -or
        $native.image.format -cne 'pe32plus' -or $native.image.machine -cne 'amd64' -or
        $native.image.nativeAotHeader -cne 'DNDH' -or $native.image.nativeAotHeaderMajor -ne 5 -or
        $native.image.nativeAotHeaderMinor -ne 0 -or $native.image.size -lt 512 -or $native.image.size -gt 1073741824 -or
        $native.runtime.architecture -cne 'x64' -or $native.runtime.framework -cnotmatch '^\.NET 10\.[0-9]+\.[0-9]+$' -or
        $native.runtime.isDynamicCodeSupported -isnot [bool] -or $native.runtime.isDynamicCodeSupported -or
        $native.runtime.isDynamicCodeCompiled -isnot [bool] -or $native.runtime.isDynamicCodeCompiled -or
        $native.runtime.reportPhase -cne 'completed' -or
        $native.tools.sdkVersion -cnotmatch '^10\.[0-9]+\.[0-9]+$' -or
        $native.tools.compilerVersion -cnotmatch '^10\.[0-9]+\.[0-9]+$' -or
        $native.tools.compilerDigest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'NATIVE_PROOF_INCOMPLETE' }
    if (@($Record.producer.tools | Where-Object {
        $_.id -ceq 'dotnet' -and $_.version -ceq $native.tools.sdkVersion
    }).Count -ne 1 -or @($Record.producer.tools | Where-Object {
        $_.id -ceq 'ilc' -and $_.version -ceq $native.tools.compilerVersion -and
        $_.digest -ceq $native.tools.compilerDigest
    }).Count -ne 1) { throw 'NATIVE_PRODUCER_TOOL_MISMATCH' }
    if ($native.producedDigest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'NATIVE_DIGEST_INVALID' }
    foreach ($name in @('attempt', 'processId', 'exitCode')) {
        if ($native.$name -isnot [int] -and $native.$name -isnot [long]) { throw 'NATIVE_COUNTER_INVALID' }
    }
    foreach ($name in @('nativeAotHeaderMajor', 'nativeAotHeaderMinor', 'size')) {
        if ($native.image.$name -isnot [int] -and $native.image.$name -isnot [long]) { throw 'NATIVE_COUNTER_INVALID' }
    }
    foreach ($name in @('launchedDigest', 'observedDigest', 'postRunDigest')) {
        if ($native.$name -cne $native.producedDigest) { throw 'NATIVE_IMAGE_IDENTITY_MISMATCH' }
    }

    if ($native.image.digest -cne $native.producedDigest) { throw 'NATIVE_IMAGE_IDENTITY_MISMATCH' }
    $start = [DateTimeOffset] $native.startedAt
    $end = [DateTimeOffset] $native.completedAt
    if ($end -le $start -or ($end - $start).TotalSeconds -gt 3600 -or $end -gt [DateTimeOffset]::UtcNow) {
        throw 'NATIVE_COMPLETION_INVALID'
    }
    $result = @{}
    foreach ($name in @('project', 'configuration', 'hostTfm', 'libraryTfm', 'runtimeIdentifier', 'publishAot',
        'sourceSha', 'runId', 'attempt', 'nonce', 'processId', 'exitCode', 'producedDigest', 'launchedDigest',
        'observedDigest', 'postRunDigest', 'startedAt', 'completedAt')) { $result[$name] = $native.$name }
    $result.image = @{}
    foreach ($name in @('format', 'machine', 'nativeAotHeader', 'nativeAotHeaderMajor', 'nativeAotHeaderMinor', 'digest', 'size')) {
        $result.image[$name] = $native.image.$name
    }
    $result.runtime = @{}
    foreach ($name in @('framework', 'architecture', 'isDynamicCodeSupported', 'isDynamicCodeCompiled', 'reportPhase')) {
        $result.runtime[$name] = $native.runtime.$name
    }
    $result.tools = @{}
    foreach ($name in @('sdkVersion', 'compilerVersion', 'compilerDigest')) { $result.tools[$name] = $native.tools.$name }
    return $result
}

function Get-AssuranceCodeqlProof($Proof, $Record, [scriptblock] $ReviewAuthenticator) {
    $analysis = $Proof.analysis
    $distinctAlerts = $analysis.distinctAlerts
    if ($Proof.kind -cne 'codeql' -or $analysis.status -cne 'completed' -or
        $analysis.sourceSha -cne $Record.sourceSha -or $analysis.runId -cne $Record.producer.runId -or
        $analysis.attempt -ne $Record.producer.attempt -or
        $analysis.sourceRef -cnotmatch '^refs/heads/(master|release/2\.[0-9A-Za-z._/-]+)$' -or
        $analysis.analysisId -cnotmatch '^[1-9][0-9]*$' -or $analysis.sarifId -cnotmatch '^[A-Za-z0-9-]+$' -or
        $analysis.category -cne 'assurance-csharp-net10' -or $analysis.tool.name -cne 'CodeQL' -or
        $analysis.tool.version -cnotmatch '^2\.[0-9]+\.[0-9]+$' -or
        $analysis.tool.digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
        $analysis.expectedQueries -le 0 -or $analysis.completedQueries -ne $analysis.expectedQueries -or
        $analysis.findings -lt 0 -or ($null -ne $distinctAlerts -and $distinctAlerts -lt 0) -or
        $analysis.findings -ne $Proof.counts.findings -or @($analysis.projects).Count -lt 1) {
        throw 'CODEQL_PROOF_INCOMPLETE'
    }
    if (@($Record.producer.tools | Where-Object {
        $_.id -ceq 'codeql' -and $_.version -ceq $analysis.tool.version -and $_.digest -ceq $analysis.tool.digest
    }).Count -ne 1) { throw 'CODEQL_PRODUCER_TOOL_MISMATCH' }
    foreach ($field in @('attempt', 'expectedQueries', 'completedQueries', 'findings')) {
        if ($analysis.$field -isnot [int] -and $analysis.$field -isnot [long]) { throw 'CODEQL_COUNTER_INVALID' }
    }
    if ($null -ne $distinctAlerts -and $distinctAlerts -isnot [int] -and $distinctAlerts -isnot [long]) {
        throw 'CODEQL_COUNTER_INVALID'
    }
    $digestFields = @('databaseDigest', 'extractionDigest', 'configurationDigest', 'suiteDigest', 'packDigest',
        'queryDigest', 'queryResultsDigest', 'buildDigest', 'populationDigest')
    foreach ($field in $digestFields) {
        if ($analysis.$field -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'CODEQL_IDENTITY_MISSING' }
    }
    $projectIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $projects = @(
        foreach ($project in $analysis.projects) {
            if ($project.projectDigest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
                -not $projectIds.Add($project.projectDigest) -or $project.expectedSources -le 0 -or
                $project.expectedSources -ne $project.extractedSources -or $project.extractionErrors -ne 0 -or
                $project.expectedSourceDigest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
                $project.expectedSourceDigest -cne $project.extractedSourceDigest) { throw 'CODEQL_SCOPE_INCOMPLETE' }
            foreach ($field in @('expectedSources', 'extractedSources', 'extractionErrors')) {
                if ($project.$field -isnot [int] -and $project.$field -isnot [long]) { throw 'CODEQL_COUNTER_INVALID' }
            }
            @{
                projectDigest = $project.projectDigest; expectedSources = $project.expectedSources
                extractedSources = $project.extractedSources; expectedSourceDigest = $project.expectedSourceDigest
                extractedSourceDigest = $project.extractedSourceDigest; extractionErrors = $project.extractionErrors
            }
        }
    )
    $review = $null
    if ($analysis.findings -eq 0 -and $null -ne $distinctAlerts -and $distinctAlerts -eq 0 -and
        $analysis.disposition -ceq 'clean-analysis') {
        $disposition = 'clean-analysis'
    }
    else {
        $recordReview = if ($null -ne $Proof.review) { $Proof.review } else { $analysis.review }
        if ($null -eq $ReviewAuthenticator -or $null -eq $recordReview) { throw 'CODEQL_AUTHENTICATED_REVIEW_MISSING' }
        if ($recordReview.id -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$' -or
            $recordReview.digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'CODEQL_REVIEW_IDENTITY_INVALID' }
        # The protected caller resolves and authenticates this opaque record through the core verifier.
        # A bool or the candidate's own "verified" property is deliberately not an authentication result.
        $authenticated = & $ReviewAuthenticator $recordReview.id $recordReview.digest
        if ($null -eq $authenticated -or $authenticated -is [bool] -or
            $authenticated.kind -cne 'codeql-disposition' -or $authenticated.id -cne $recordReview.id -or
            $authenticated.digest -cne $recordReview.digest -or $authenticated.sourceSha -cne $analysis.sourceSha -or
            $authenticated.runId -cne $analysis.runId -or $authenticated.attempt -ne $analysis.attempt -or
            $authenticated.analysisId -cne $analysis.analysisId -or $authenticated.queryDigest -cne $analysis.queryDigest -or
            $authenticated.populationDigest -cne $analysis.populationDigest -or
            $authenticated.revoked -isnot [bool] -or $authenticated.revoked -or
            $authenticated.reviewedOccurrences -ne $analysis.findings -or
            $authenticated.reviewedAlerts -lt 1 -or $authenticated.reviewedAlerts -gt $analysis.findings -or
            ($null -ne $distinctAlerts -and $authenticated.reviewedAlerts -ne $distinctAlerts) -or
            $authenticated.unresolvedFindings -ne 0 -or
            ([DateTimeOffset] $authenticated.expiresAt) -le [DateTimeOffset]::UtcNow -or
            ([DateTimeOffset] $authenticated.issuedAt) -gt [DateTimeOffset]::UtcNow) {
            throw 'CODEQL_REVIEW_UNVERIFIED'
        }
        foreach ($field in @('attempt', 'reviewedOccurrences', 'reviewedAlerts', 'unresolvedFindings')) {
            if ($authenticated.$field -isnot [int] -and $authenticated.$field -isnot [long]) {
                throw 'CODEQL_REVIEW_COUNTER_INVALID'
            }
        }
        $distinctAlerts = $authenticated.reviewedAlerts
        $review = @{ id = $recordReview.id; digest = $recordReview.digest }
        $disposition = 'authenticated-review'
    }
    $result = @{
        status = 'completed'; sourceSha = $analysis.sourceSha; sourceRef = $analysis.sourceRef
        runId = $analysis.runId; attempt = $analysis.attempt; analysisId = $analysis.analysisId
        sarifId = $analysis.sarifId; category = $analysis.category
        tool = @{ name = 'CodeQL'; version = $analysis.tool.version; digest = $analysis.tool.digest }
        projects = $projects; expectedQueries = $analysis.expectedQueries; completedQueries = $analysis.completedQueries
        findings = $analysis.findings; distinctAlerts = $distinctAlerts; disposition = $disposition
    }
    foreach ($field in $digestFields) { $result[$field] = $analysis.$field }
    if ($null -ne $review) { $result.review = $review }
    return $result
}
