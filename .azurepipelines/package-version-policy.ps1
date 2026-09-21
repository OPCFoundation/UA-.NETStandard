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

function ConvertTo-PreviewPackageVersion {
    param([Parameter(Mandatory)][string]$Version)

    if ($Version -match '-preview(?:[.+-]|$)') {
        return $Version
    }
    if ($Version.Contains('-')) {
        return $Version.Replace('-', '-preview.')
    }
    if ($Version.Contains('+')) {
        return $Version.Replace('+', '-preview+')
    }
    return "$Version-preview"
}

function Get-ExpectedPackageVersion {
    param(
        [Parameter(Mandatory)][string]$PackageId,
        [Parameter(Mandatory)][string]$BaseVersion
    )

    if (Test-PreviewPackageId $PackageId) {
        return ConvertTo-PreviewPackageVersion $BaseVersion
    }
    return $BaseVersion
}
