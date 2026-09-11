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
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
. (Join-Path $PSScriptRoot 'FixtureWorkspace.ps1')
$fixture = Join-Path (Get-FixturePhysicalTempDirectory) ('container-matrix-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $fixture '.azurepipelines/release') -Force

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "$Scenario : $Message" }
}

try {
    $catalog = Get-Content -LiteralPath (Join-Path $root '.azurepipelines/release/artifacts.json') -Raw |
        ConvertFrom-Json
    $ociGroups = @($catalog.groups | Where-Object kind -CEQ 'oci')
    foreach ($group in $ociGroups) {
        foreach ($image in $group.images) {
            $dockerfile = Join-Path $fixture $image.dockerfile
            $null = New-Item -ItemType Directory -Path (Split-Path $dockerfile) -Force
            Set-Content -LiteralPath $dockerfile '# Synthetic Dockerfile; no image is built.'
        }
    }
    $eventName = 'push'
    $ref = 'refs/heads/master'
    $repository = 'OPCFoundation/UA-.NETStandard'
    $expectedGroups = @('containers', 'pump')
    $invalid = $false
    switch ($Scenario) {
        'master' { }
        'release' { $ref = 'refs/heads/release/2.0'; $expectedGroups = @('containers') }
        'docker-branch' { $ref = 'refs/heads/docker-fix'; $expectedGroups = @('containers') }
        'pull-request' { $eventName = 'pull_request'; $ref = 'refs/pull/1/merge' }
        'manual-pump' { $eventName = 'workflow_dispatch'; $expectedGroups = @('pump') }
        'fork' { $repository = 'Example-Org/UA-.NETStandard' }
        'invalid-push-ref' { $ref = 'refs/heads/unrelated'; $invalid = $true }
        'invalid-event' { $eventName = 'schedule'; $invalid = $true }
        'missing-group' {
            $catalog.groups = @($catalog.groups | Where-Object id -CNE 'pump')
            $invalid = $true
        }
        'wrong-producer' {
            ($catalog.groups | Where-Object id -CEQ 'pump').producer = '.github/workflows/other.yml'
            $invalid = $true
        }
        'missing-dockerfile' {
            Remove-Item -LiteralPath (Join-Path $fixture 'samples/DI/PumpDeviceIntegrationServer/Dockerfile')
            $invalid = $true
        }
        'duplicate-image' {
            $group = $catalog.groups | Where-Object id -CEQ 'pump'
            $group.images += $group.images[0]
            $invalid = $true
        }
        'empty-platforms' {
            ($catalog.groups | Where-Object id -CEQ 'pump').platforms = @()
            $invalid = $true
        }
        'unsafe-dockerfile' {
            ($catalog.groups | Where-Object id -CEQ 'pump').images[0].dockerfile =
                '../samples/DI/PumpDeviceIntegrationServer/Dockerfile'
            $invalid = $true
        }
        'workflow-wiring' { }
        default { throw 'Unknown container workflow fixture scenario.' }
    }
    $catalog | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath (Join-Path $fixture '.azurepipelines/release/artifacts.json')
    $output = & pwsh -NoLogo -NoProfile -NonInteractive -File `
        (Join-Path $root '.azurepipelines/containers/matrix.ps1') `
        -EventName $eventName -Ref $ref -Repository $repository -RepositoryRoot $fixture 2>&1
    $code = $LASTEXITCODE
    if ($invalid) {
        Assert-True ($code -ne 0) 'Invalid selection input was accepted.'
        exit 0
    }
    Assert-True ($code -eq 0) ("Matrix selection failed: " + ($output -join "`n"))
    $selection = ($output -join "`n") | ConvertFrom-Json
    $images = @($selection.images.include)
    $groups = @($selection.groups.include)
    Assert-True (($groups.id -join ',') -ceq ($expectedGroups -join ',')) 'Wrong independent groups selected.'
    Assert-True (@($images.image | Sort-Object -Unique).Count -eq $images.Count) 'Image selection contains duplicates.'
    foreach ($group in $groups) {
        $members = @($images | Where-Object group -CEQ $group.id)
        if ($group.id -ceq 'pump') {
            Assert-True ($members.Count -eq 1 -and $members[0].image -ceq 'pumpdeviceintegrationserver') `
                'Pump selection must contain exactly its one image.'
            Assert-True ($members[0].platforms -ceq 'linux/amd64') 'Pump platform scope changed.'
            $owner = if ($Scenario -eq 'fork') { 'example-org' } else { 'opcfoundation' }
            Assert-True ($members[0].repository -ceq "ghcr.io/$owner/pumpdeviceintegrationserver") `
                'Pump owner-level registry path changed.'
            Assert-True ($group.manifestArtifact -ceq 'pump-artifact-group-manifest') 'Pump evidence is not independent.'
        }
        else {
            $expectedImages = @('boilerserver', 'calcserver', 'ldsserver', 'mcpserver', 'pubsubclient',
                'redundantclient', 'redundantpubsub', 'redundantserver', 'refserver')
            Assert-True ((($members.image | Sort-Object) -join ',') -ceq ($expectedImages -join ',')) `
                'Main container membership changed.'
            $owner = if ($Scenario -eq 'fork') { 'exampleorg' } else { 'opcfoundation' }
            foreach ($member in $members) {
                Assert-True ($member.platforms -ceq 'linux/amd64,linux/arm64/v8') 'Main platform scope changed.'
                Assert-True ($member.repository -ceq "ghcr.io/$owner/uanetstandard/$($member.image)") `
                    'Main repository normalization changed.'
            }
            Assert-True ($group.manifestArtifact -ceq 'container-artifact-group-manifest') `
                'Main container evidence is not independent.'
        }
    }
    if ($Scenario -eq 'workflow-wiring') {
        $workflow = Get-Content -LiteralPath (Join-Path $root '.github/workflows/docker-image.yml') -Raw
        Assert-True (-not (Test-Path -LiteralPath `
            (Join-Path $root '.github/workflows/pump-device-integration-server-docker.yml'))) `
            'The redundant standalone Pump workflow still exists.'
        foreach ($required in @(
            'matrix: ${{ fromJSON(needs.select-images.outputs.images) }}',
            'matrix: ${{ fromJSON(needs.select-images.outputs.groups) }}',
            'platforms: ${{ matrix.platforms }}',
            'IMAGE_REPOSITORY: ${{ matrix.repository }}',
            'IMAGE_GROUP: ${{ matrix.group }}',
            'push: ${{ github.event_name != ''pull_request'' }}',
            'pattern: container-status-${{ matrix.id }}-*',
            'name: ${{ matrix.manifestArtifact }}',
            'type=raw,value=latest',
            'type=raw,value=${{ env.IMAGE_VERSION }}',
            'type=sha',
            '-Operation Preflight -Group $env:IMAGE_GROUP',
            'workflow_dispatch:'
        )) {
            Assert-True ($workflow.Contains($required)) "Workflow lost required wiring: $required"
        }
    }
    exit 0
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
