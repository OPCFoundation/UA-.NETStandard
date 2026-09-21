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

function Get-BlockingPublishedPreviewVersions {
    <#
    .SYNOPSIS
        Returns the already-published versions that a synthesized
        "<BaseVersion>-preview.N" candidate does not sort strictly above.

    .DESCRIPTION
        Whenever the root version is an exact stable release it carries no
        git-height placeholder to reuse, so version.targets pins the
        preview-only families to the explicitly committed number in
        preview-version.props instead. That number is a manual release
        decision, so it goes stale on its own: ordinary development keeps
        publishing "<BaseVersion>-preview.<height>" packages with a growing
        height, and once the height passes the committed number the released
        package would sort *below* a CI package under SemVer 2 precedence.
        NuGet versions are immutable, so that mistake cannot be corrected
        after the fact - consumers tracking the latest prerelease would
        simply never see the release.

        This predicate is deliberately pure: callers supply the published
        version list they fetched from a feed, which keeps it deterministic
        and offline-testable. It fails closed - a published version sharing
        the same "<BaseVersion>-preview" base whose ordering cannot be
        decided numerically is reported as blocking and requires an explicit
        decision rather than being assumed lower.
    #>
    param(
        [Parameter(Mandatory)][string]$BaseVersion,
        [string[]]$PublishedVersions = @(),
        [string]$PreviewPackageBuildNumber = (Get-PreviewPackageBuildNumber)
    )

    if ($PreviewPackageBuildNumber -notmatch '^\d+$') {
        throw "The preview package build number '$PreviewPackageBuildNumber' is not a non-negative integer."
    }
    if (-not (Test-StablePackageVersion -Version $BaseVersion)) {
        throw (
            "Preview ordering is only defined for an exact stable base version such as '2.0.0'; " +
            "got '$BaseVersion'.")
    }

    $candidate = [int]$PreviewPackageBuildNumber
    $prefix = "$BaseVersion-preview"
    $numbered = [regex]::new(
        '^' + [regex]::Escape($prefix) + '\.(?<number>\d+)(?:[.+\-]|$)',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    $blocking = foreach ($published in $PublishedVersions) {
        if ([string]::IsNullOrWhiteSpace($published)) {
            continue
        }
        $value = $published.Trim()
        if (-not $value.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($value.Length -eq $prefix.Length) {
            # Exactly "<base>-preview": under SemVer 2 a prerelease with
            # fewer dot-separated identifiers always sorts below one that
            # shares its prefix and adds more, so this can never block.
            continue
        }

        $match = $numbered.Match($value)
        if (-not $match.Success) {
            # For example a legacy "<base>-preview20240131" label: the
            # ordering cannot be decided numerically, so surface it.
            $value
            continue
        }
        if ([int]$match.Groups['number'].Value -ge $candidate) {
            $value
        }
    }

    return , @($blocking | Sort-Object -Unique)
}

function Test-StablePackageVersion {
    <#
    .SYNOPSIS
        Returns $true when a package version is an exact stable
        major.minor.patch release, for example "2.0.0". The predicate
        deliberately rejects prerelease/build metadata and an old
        four-component NBGV build version such as "2.0.0.7": package
        patches are explicit release decisions, never git height.
    #>
    param([Parameter(Mandatory)][string]$Version)

    return $Version -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'
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

function Test-CanonicalReleaseBranchForPackageVersion {
    <#
    .SYNOPSIS
        Returns $true only when a canonical release/M.m branch matches the
        major/minor components of an exact stable M.m.p package version.
        This prevents a 2.1.0 candidate being built or promoted from
        release/2.0 (or the inverse).
    #>
    param(
        [Parameter(Mandatory)][string]$Ref,
        [Parameter(Mandatory)][string]$Version
    )

    if (-not (Test-StablePackageVersion -Version $Version)) {
        return $false
    }

    $branchMatch = [regex]::Match($Ref, '^refs/heads/release/(?<major>\d+)\.(?<minor>\d+)$')
    if (-not $branchMatch.Success) {
        return $false
    }

    $versionMatch = [regex]::Match($Version, '^(?<major>\d+)\.(?<minor>\d+)\.\d+$')
    return $versionMatch.Success -and
        $branchMatch.Groups['major'].Value -eq $versionMatch.Groups['major'].Value -and
        $branchMatch.Groups['minor'].Value -eq $versionMatch.Groups['minor'].Value
}
