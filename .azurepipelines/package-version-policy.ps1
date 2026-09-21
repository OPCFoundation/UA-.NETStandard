function Test-PreviewPackageId {
    param([Parameter(Mandatory)][string]$PackageId)

    $baseId = $PackageId -replace '\.Debug$', ''
    return $baseId -match '^OPCFoundation\.NetStandard\.Opc\.Ua\.(XRegistry|WotCon|Vision|Robotics|Redundancy|Positioning|OpenUsd|ISA95|AI|Di)(\.|$)' -or
        $baseId -in @(
            'OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics',
            'OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision',
            'OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector',
            'OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector.Viewer')
}

function Get-PreviewPackageBuildNumber {
    <#
    .SYNOPSIS
        Reads the single source of truth for the numbered "-preview.N"
        suffix (see preview-version.props) that version.targets applies to
        the preview-only package families whenever the root package version
        is an exact stable release with no prerelease label of its own to
        reuse.
    #>
    param(
        [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    )

    $propsPath = Join-Path $RepositoryRoot 'preview-version.props'
    if (-not (Test-Path -LiteralPath $propsPath)) {
        throw "Cannot resolve the preview package build number: '$propsPath' was not found."
    }

    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $node = $props.SelectSingleNode('//*[local-name()="PreviewPackageBuildNumber"]')
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "'$propsPath' does not define a PreviewPackageBuildNumber value."
    }
    return $node.InnerText.Trim()
}

function ConvertTo-PreviewPackageVersion {
    param(
        [Parameter(Mandatory)][string]$Version,
        [string]$PreviewPackageBuildNumber = (Get-PreviewPackageBuildNumber)
    )

    if ($Version -match '-preview(?:[.+-]|$)') {
        return $Version
    }
    if ($Version.Contains('-')) {
        return $Version.Replace('-', '-preview.')
    }
    if ($Version.Contains('+')) {
        return $Version.Replace('+', "-preview.$PreviewPackageBuildNumber+")
    }
    return "$Version-preview.$PreviewPackageBuildNumber"
}

function Get-ExpectedPackageVersion {
    param(
        [Parameter(Mandatory)][string]$PackageId,
        [Parameter(Mandatory)][string]$BaseVersion
    )

    if (Test-PreviewPackageId $PackageId) {
        return ConvertTo-PreviewPackageVersion -Version $BaseVersion
    }
    return $BaseVersion
}

function Test-StablePackageVersion {
    <#
    .SYNOPSIS
        Returns $true when a package version is an exact stable release
        (no SemVer prerelease label and no build metadata), for example
        "2.0.0" - never "2.0.0-preview.6" or "2.0.0+gabcdef".
    #>
    param([Parameter(Mandatory)][string]$Version)

    return $Version -notmatch '[-+]'
}

function Test-CanonicalReleaseBranchRef {
    <#
    .SYNOPSIS
        Returns $true only for the exact maintained release-line branch
        naming this repository publishes stable packages from, e.g.
        "refs/heads/release/2.0" or "refs/heads/release/2.1". Rejects the
        historical three-component "release/2.0.0" naming, any other
        "release/*" branch, and every non-release ref (including master).
    #>
    param([Parameter(Mandatory)][string]$Ref)

    return $Ref -match '^refs/heads/release/\d+\.\d+$'
}

