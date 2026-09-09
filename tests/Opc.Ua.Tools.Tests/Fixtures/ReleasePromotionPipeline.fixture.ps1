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
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$fixture = Join-Path ([IO.Path]::GetTempPath()) "promotion-pipeline-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'candidate')
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'authority')
try {
    $trust = Join-Path $fixture 'authority\unconfigured.json'
    [IO.File]::WriteAllText($trust, '{"schemaVersion":1,"status":"pending"}')
    $candidate = Join-Path $fixture 'candidate'
    $env:OPCUA_RELEASE_TRUST_POLICY = ''
    $env:OPCUA_RELEASE_TRUST_POLICY_SHA256 = ''
    $env:GITHUB_REPOSITORY = 'OPCFoundation/UA-.NETStandard'
    $env:GITHUB_REF = 'refs/heads/master'
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    $env:GITHUB_WORKFLOW_REF = 'OPCFoundation/UA-.NETStandard/.github/workflows/release.yml@refs/heads/master'
    $parameters = @{
        Operation = 'Write'; ControllerRoot = $root; CandidateRoot = $candidate
        Evidence = (Join-Path $candidate 'evidence.json'); Expected = (Join-Path $candidate 'expected.json')
        VerificationBundle = (Join-Path $candidate 'verification-bundle.json')
        Request = (Join-Path $candidate 'request.json'); Work = (Join-Path $fixture 'work')
        Output = (Join-Path $fixture 'result.json')
    }
    $expected = 1
    switch ($Scenario) {
        'unconfigured' { }
        'foreign-caller' { $parameters.TrustPolicy = $trust; $env:GITHUB_REPOSITORY = 'foreign/repository' }
        'pr-caller' { $parameters.TrustPolicy = $trust; $env:GITHUB_EVENT_NAME = 'pull_request' }
        'candidate-controller' {
            $parameters.Operation = 'Verify'
            $parameters.ControllerRoot = $candidate
            $parameters.TrustPolicy = $trust
            $expected = 2
        }
        'candidate-trust' {
            $parameters.TrustPolicy = Join-Path $candidate 'trust.json'
            Copy-Item -LiteralPath $trust -Destination $parameters.TrustPolicy
            $expected = 2
        }
        'missing-assessment' {
            $parameters.Operation = 'Offline'
            $parameters.TrustPolicy = $trust
            $expected = 2
        }
        'dormant-policy' {
            . (Join-Path $root '.azurepipelines\nuget-evidence-functions.ps1')
            $policy = Get-NugetPolicy $root
            if ($policy.stage -cne 'required' -or $policy.publisherBoundaryVerified) {
                throw 'The active contract must require verified records without fabricating a publisher boundary.'
            }
            $workflow = Get-Content -LiteralPath (Join-Path $root '.github\workflows\release.yml') -Raw
            $lines = $workflow -split '\r?\n'
            foreach ($job in @('candidate-verification', 'candidate-writer')) {
                $offset = [Array]::IndexOf($lines, "  ${job}:")
                if ($offset -lt 0 -or $offset + 2 -ge $lines.Length -or
                    $lines[$offset + 2] -cne '    if: ${{ false }}') {
                    throw 'Dormant promotion jobs must remain literally disabled.'
                }
            }
        }
        default { throw 'Unknown promotion pipeline fixture scenario.' }
    }
    & (Join-Path $root '.azurepipelines\release-promotion.ps1') @parameters
    if ($LASTEXITCODE -ne $expected) { throw "Expected rejection $expected, got $LASTEXITCODE." }
    if (Test-Path -LiteralPath $parameters.Output) { throw 'Rejected promotion fabricated an assessment or delivery result.' }
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
