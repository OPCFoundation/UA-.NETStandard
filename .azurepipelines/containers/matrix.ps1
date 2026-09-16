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
Selects Docker image and independent evidence-group matrices from the release catalog.
.DESCRIPTION
Master builds both groups; release/docker branch pushes build the main container group.
Manual dispatch retains the Pump-only operation. Pull requests validate both groups
without granting publication authority.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('push', 'pull_request', 'workflow_dispatch')]
    [string]$EventName,
    [Parameter(Mandatory)]
    [string]$Ref,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$selected = switch ($EventName) {
    'workflow_dispatch' { 'pump' }
    'pull_request' { 'containers'; 'pump' }
    'push' {
        if ($Ref -ceq 'refs/heads/master') { 'containers'; 'pump' }
        elseif ($Ref -cmatch '^refs/heads/(release/.+|docker.*)$') { 'containers' }
        else { throw 'Push reference is outside the Docker workflow trigger scope.' }
    }
}
$catalog = Get-Content -LiteralPath (Join-Path $RepositoryRoot '.azurepipelines/release/artifacts.json') -Raw |
    ConvertFrom-Json
$normalizedRepository = $Repository.ToLowerInvariant().Replace('-', '').Replace('.', '')
$owner = $Repository.Split('/')[0].ToLowerInvariant()
$images = @()
$groups = @()
foreach ($groupId in $selected) {
    $definitions = @($catalog.groups | Where-Object id -CEQ $groupId)
    if ($definitions.Count -ne 1) { throw "Expected one catalog definition for group '$groupId'." }
    $definition = $definitions[0]
    if ($definition.kind -cne 'oci' -or
        $definition.producer -cne '.github/workflows/docker-image.yml' -or
        @($definition.images).Count -eq 0 -or @($definition.platforms).Count -eq 0) {
        throw "Container group '$groupId' is not configured for the shared Docker producer."
    }
    $imageIds = @($definition.images | ForEach-Object id)
    if (@($imageIds | Sort-Object -Unique).Count -ne $imageIds.Count) {
        throw "Container group '$groupId' contains duplicate image identities."
    }
    foreach ($image in $definition.images) {
        if ($image.id -cnotmatch '^[a-z0-9][a-z0-9-]*$' -or
            $image.dockerfile -match '(^/|\\|:|(^|/)\.\.?(/|$))' -or
            -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $image.dockerfile) -PathType Leaf)) {
            throw "Container group '$groupId' contains an invalid image or missing Dockerfile."
        }
        $repositoryName = $definition.repositoryTemplate.
            Replace('{normalizedRepository}', $normalizedRepository).
            Replace('{owner}', $owner).
            Replace('{image}', $image.id)
        if ($repositoryName.Contains('{') -or $repositoryName.Contains('}')) {
            throw "Container group '$groupId' contains an unresolved repository template."
        }
        $images += @{
            group = $groupId
            image = $image.id
            dockerfile = './' + $image.dockerfile
            platforms = $definition.platforms -join ','
            repository = $repositoryName
            evidenceImage = "$($definition.upstreamRepositoryPrefix)/$($image.id)"
        }
    }
    $groups += @{
        id = $groupId
        manifestArtifact = if ($groupId -ceq 'pump') {
            'pump-artifact-group-manifest'
        } else {
            'container-artifact-group-manifest'
        }
    }
}
@{
    images = @{ include = @($images) }
    groups = @{ include = @($groups) }
} | ConvertTo-Json -Depth 8 -Compress
