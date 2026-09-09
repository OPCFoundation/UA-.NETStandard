# Copyright (c) OPC Foundation, Inc. All rights reserved.
# Licensed under the MIT License. See LICENSE.txt in the project root for license information.
<#
.SYNOPSIS
Dormant controller-only promotion entrypoint, with a separately bounded offline fixture transport.
.DESCRIPTION
No candidate scripts or tools are executed. The core verifier reacquires current authorization
before every destination operation. Official transport and trust administration are unconfigured.
An assessment is an immutable handoff reference, never a grant by itself.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Verify', 'Write', 'Offline')][string]$Operation,
    [string]$ControllerRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory)][string]$CandidateRoot,
    [Parameter(Mandatory)][string]$Evidence,
    [Parameter(Mandatory)][string]$Expected,
    [Parameter(Mandatory)][string]$VerificationBundle,
    [string]$TrustPolicy = $env:OPCUA_RELEASE_TRUST_POLICY,
    [Parameter(Mandatory)][string]$Request,
    [Parameter(Mandatory)][string]$Work,
    [Parameter(Mandatory)][string]$Output,
    [string]$Assessment,
    [string]$AssessmentDigest,
    [string]$Journal,
    [string]$OfflineDestination
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'nuget-evidence-functions.ps1')
try {
    if ([string]::IsNullOrWhiteSpace($TrustPolicy)) {
        Stop-NugetEvidence 'Protected release trust is unconfigured; no promotion can be authorized.' 1
    }
    Assert-NugetIndependentTrust $TrustPolicy @($ControllerRoot, $CandidateRoot, $Work)
    if ($Operation -ceq 'Write' -and (
        $env:GITHUB_REPOSITORY -cne 'OPCFoundation/UA-.NETStandard' -or
        $env:GITHUB_REF -cne 'refs/heads/master' -or
        $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
        $env:GITHUB_WORKFLOW_REF -cne
            'OPCFoundation/UA-.NETStandard/.github/workflows/release.yml@refs/heads/master')) {
        Stop-NugetEvidence 'The official writer must execute as the protected existing release controller.' 1
    }
    $tool = Resolve-NugetPath $ControllerRoot `
        'tools/Opc.Ua.ReleaseEvidence/bin/Release/net10.0/Opc.Ua.ReleaseEvidence.dll'
    $candidatePrefix = [IO.Path]::GetFullPath($CandidateRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ([IO.Path]::GetFullPath($tool).StartsWith($candidatePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-NugetEvidence 'Candidate-provided controller binaries cannot execute.' 2
    }
    $null = New-Item -ItemType Directory -Force -Path $Work
    $arguments = @(
        "promotion-$($Operation.ToLowerInvariant())", '--repository-root', $ControllerRoot,
        '--candidate-root', $CandidateRoot, '--evidence', $Evidence, '--expected', $Expected,
        '--verification-bundle', $VerificationBundle, '--trust-policy', $TrustPolicy,
        '--request', $Request, '--work', $Work, '--output', $Output)
    if ($Operation -cne 'Verify') {
        if (-not $Assessment -or -not $Journal -or $AssessmentDigest -cnotmatch '^sha256:[0-9a-f]{64}$') {
            Stop-NugetEvidence 'The isolated writer requires the exact read-only assessment digest and external journal.' 2
        }
        $arguments += @('--assessment', $Assessment, '--assessment-digest', $AssessmentDigest, '--journal', $Journal)
    }
    if ($Operation -ceq 'Offline') {
        if (-not $OfflineDestination) { Stop-NugetEvidence 'An explicit offline fixture store is required.' 2 }
        $arguments += @('--offline-destination', $OfflineDestination)
    }
    $code = Invoke-NugetEvidenceTool $tool $arguments (Join-Path $Work 'promotion.log')
    if ($code -ne 0) {
        Write-Host '::error::Promotion rejected; protected runner-local diagnostics retained.'
        exit $code
    }
    Write-Host "Promotion $Operation finished; no official transport is configured by this repository."
}
catch {
    $code = if ($_.Exception.Data.Contains('NugetExitCode')) { [int]$_.Exception.Data['NugetExitCode'] } else { 2 }
    Write-Host '::error::Promotion unavailable or rejected; no authorization was inferred.'
    exit $code
}
exit 0
