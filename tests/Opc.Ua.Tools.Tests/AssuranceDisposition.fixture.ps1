# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
. (Join-Path $root '.azurepipelines\assurance-proofs.ps1')
. (Join-Path $PSScriptRoot 'AssuranceProofData.fixture.ps1')
$proof = New-AssuranceFixtureCodeql ('a' * 40) '502' 2
$proof.analysis.findings = 3
$proof.analysis.distinctAlerts = 2
$proof.analysis.disposition = 'missing'
$proof.counts.findings = 3
$proof.analysis.populationDigest = Get-AssuranceFixtureDigest 'three occurrences of two reviewed synthetic alerts'
if ($Scenario -eq 'signed-review-unreported-alert-count') { $proof.analysis.Remove('distinctAlerts') }
$now = [DateTimeOffset]::UtcNow
$review = @{
    kind = 'codeql-disposition'; id = 'isolated-review'
    sourceSha = 'a' * 40; runId = '502'; attempt = 2; analysisId = '800'
    queryDigest = $proof.analysis.queryDigest; populationDigest = $proof.analysis.populationDigest
    reviewedOccurrences = 3; reviewedAlerts = 2; unresolvedFindings = 0; revoked = $false
    issuedAt = $now.AddMinutes(-1).ToString('o'); expiresAt = $now.AddMinutes(10).ToString('o')
    restrictedReviewContent = 'RESTRICTED_REVIEW_SENTINEL'
}
switch ($Scenario) {
    'partial-review' { $review.reviewedOccurrences = 2 }
    'partial-alerts' { $review.reviewedAlerts = 1 }
    'revoked-review' { $review.revoked = $true }
    'expired-review' { $review.expiresAt = $now.AddSeconds(-1).ToString('o') }
    'source-mismatch' { $review.sourceSha = 'b' * 40 }
    'attempt-mismatch' { $review.attempt = 1 }
    'query-mismatch' { $review.queryDigest = Get-AssuranceFixtureDigest 'other-query' }
    'population-mismatch' { $review.populationDigest = Get-AssuranceFixtureDigest 'other-findings' }
    'unresolved-review' { $review.unresolvedFindings = 1 }
}
$bytes = [Text.Encoding]::UTF8.GetBytes(($review | ConvertTo-Json -Depth 20 -Compress))
$digest = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
$proof.review = @{ id = $review.id; digest = $digest }
$key = [Security.Cryptography.RSA]::Create(2048)
try {
    $signature = $key.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pss)
    if ($Scenario -eq 'forged-signature') { $signature[0] = $signature[0] -bxor 1 }
    $authenticator = {
        param($id, $expectedDigest)
        if ($id -cne $review.id -or $expectedDigest -cne $digest -or
            -not $key.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.RSASignaturePadding]::Pss)) { return $null }
        $verified = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json -AsHashtable
        $verified.digest = $digest
        return $verified
    }.GetNewClosure()
    if ($Scenario -eq 'boolean-authentication') { $authenticator = { $true } }
    if ($Scenario -eq 'missing-review') { $authenticator = $null }
    $record = @{
        sourceSha = 'a' * 40
        producer = @{
            runId = '502'; attempt = 2
            tools = @(@{ id = 'codeql'; version = $proof.analysis.tool.version; digest = $proof.analysis.tool.digest })
        }
    }
    $accepted = $false
    try {
        $result = Get-AssuranceCodeqlProof ($proof | ConvertTo-Json -Depth 20 | ConvertFrom-Json) $record $authenticator
        $accepted = $true
        if (($result | ConvertTo-Json -Depth 20).Contains('RESTRICTED_REVIEW_SENTINEL')) {
            throw 'Restricted review content leaked into sanitized proof.'
        }
    }
    catch {
        if ($Scenario -in @('signed-review', 'signed-review-unreported-alert-count')) { throw }
    }
    if ($accepted -ne ($Scenario -in @('signed-review', 'signed-review-unreported-alert-count'))) {
        throw "Wrong review result: $Scenario."
    }
}
finally { $key.Dispose() }
