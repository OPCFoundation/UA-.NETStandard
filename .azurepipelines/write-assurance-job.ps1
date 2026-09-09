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
Produces one sanitized profile-job record from a real result document.
.DESCRIPTION
Run after the test/analysis even on failure. Only profile projects are recorded;
other test projects still use assurance-results.ps1 for their baseline gate.
Source and CI run identity are observed, not supplied as success claims.
#>
param(
    [Parameter(Mandatory)][string] $Project,
    [Parameter(Mandatory)][string] $ResultsPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $Workflow,
    [string] $HostTfm = 'net10.0',
    [string] $LibraryTfm = 'net10.0',
    [string] $Configuration = 'Release',
    [AllowEmptyString()][string] $Filter = '',
    [ValidateSet('trx', 'mtp-trx', 'sarif', 'fuzz-replay', 'native-aot', 'codeql')][string] $Kind = 'trx'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$profilesPath = Join-Path $PSScriptRoot 'assurance-profiles.json'
$profiles = Get-Content $profilesPath -Raw | ConvertFrom-Json
$Project = $Project.Replace('\', '/').TrimStart('./')
$profile = @($profiles.profiles | Where-Object { $Project -in $_.jobs.project })
if ($profile.Count -eq 0) { Write-Host 'No release profile applies to this project.'; exit 0 }
if ($profile.Count -ne 1) { throw 'Ambiguous release profile.' }
$profile = $profile[0]
$definition = $profile.jobs | Where-Object project -eq $Project
if ($definition.kind -eq 'fuzz-replay') { $Kind = 'fuzz-replay' }
$sha = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40,64}$') { throw 'Cannot identify checked-out source.' }
$hostName = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
$arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if ($arch -eq 'x64') { $arch = 'amd64' }
$producer = [ordered]@{
    system = ''; workflow = $Workflow.Replace('\', '/')
    runId = ''; attempt = 0; job = ''
    completedAt = [DateTimeOffset]::UtcNow.ToString('o')
    tools = @(@{
        id = 'powershell'; version = $PSVersionTable.PSVersion.ToString()
        digest = 'sha256:' + (Get-FileHash -LiteralPath ([Environment]::ProcessPath) -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}
if ($env:GITHUB_ACTIONS -eq 'true') {
    $producer.system = 'github-actions'; $producer.runId = $env:GITHUB_RUN_ID
    $producer.attempt = [int] $env:GITHUB_RUN_ATTEMPT; $producer.job = $env:GITHUB_JOB
    $producer['definitionSha'] = $env:GITHUB_WORKFLOW_SHA
}
elseif ($env:TF_BUILD -eq 'True') {
    $producer.system = 'azure-pipelines'; $producer.runId = $env:BUILD_BUILDID
    $producer.attempt = [int] $env:SYSTEM_JOBATTEMPT; $producer.job = $env:SYSTEM_JOBID
}
else { throw 'CI producer identity is required; local replay is not release evidence.' }
if (-not $producer.runId -or $producer.attempt -lt 1 -or -not $producer.job -or
    $producer.job -notmatch '^[A-Za-z0-9_-]+$') { throw 'Incomplete CI identity.' }
# Azure's checkout does not establish the revision of an external definition.
# Omit it until a controlling authenticated definition-chain record can bind it.
if ($producer.system -eq 'github-actions' -and
    $producer.definitionSha -notmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') { throw 'Incomplete workflow identity.' }
$proofPath = [IO.Path]::ChangeExtension($OutputPath, 'results.json')
if ($Kind -in @('native-aot', 'codeql')) {
    $nativeProof = Get-Content -LiteralPath $ResultsPath -Raw | ConvertFrom-Json
    if ($nativeProof.schemaVersion -ne 1 -or $nativeProof.kind -cne $Kind -or
        $nativeProof.status -cnotin @('completed', 'failed', 'missing')) { throw 'Invalid profile producer proof.' }
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($proofPath))) -Force
    Copy-Item -LiteralPath $ResultsPath -Destination $proofPath
}
else {
    & (Join-Path $PSScriptRoot 'assurance-results.ps1') -ResultsPath $ResultsPath -Kind $Kind -OutputPath $proofPath
    if ($LASTEXITCODE -ne 0) { throw 'Result producer failed.' }
}
$proof = Get-Content $proofPath -Raw | ConvertFrom-Json
if ($Kind -eq 'native-aot' -and $proof.native.tools) {
    $producer.tools += @{
        id = 'dotnet'; version = $proof.native.tools.sdkVersion
        digest = 'sha256:' + (Get-FileHash -LiteralPath (
            Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
        ).Source -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $producer.tools += @{
        id = 'ilc'; version = $proof.native.tools.compilerVersion
        digest = $proof.native.tools.compilerDigest
    }
}
elseif ($Kind -eq 'codeql' -and $proof.analysis.tool) {
    $producer.tools += @{
        id = 'codeql'; version = $proof.analysis.tool.version; digest = $proof.analysis.tool.digest
    }
}
$job = [ordered]@{
    id = $definition.id; profile = $profile.id; project = $Project
    configuration = $Configuration; host = $hostName; hostTfm = $HostTfm; libraryTfm = $LibraryTfm
    platform = "$hostName/$arch"; shard = 'all'; filter = $Filter
    selected = $true; status = $proof.status; inputIds = @('assurance-profile')
    sourceSha = $sha; producer = $producer
    resultDocument = [IO.Path]::GetFileName($proofPath)
    resultDigest = 'sha256:' + (Get-FileHash -LiteralPath $proofPath -Algorithm SHA256).Hash.ToLowerInvariant()
    profileDigest = 'sha256:' + (Get-FileHash -LiteralPath $profilesPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
if ($proof.counts) { $job.counts = $proof.counts }
$job | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Assurance result recorded for $($definition.id): $($proof.status). Profile eligibility is assessed separately."
