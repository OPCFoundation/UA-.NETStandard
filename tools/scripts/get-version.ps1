<#
 .SYNOPSIS
    Sets CI version build variables and/or returns version information.

 .DESCRIPTION
    The script is a wrapper around any versioning tool we use and abstracts it from
    the rest of the build system.
#>

try {
    $buildRoot = & (Join-Path $PSScriptRoot "get-root.ps1") -startDir $path `
        -fileName "version.props"
    # set version number from first encountered version.props
    [xml] $props=Get-Content -Path (Join-Path $buildRoot "version.props")
    $VersionPrefix="$($props.Project.PropertyGroup.VersionPrefix)".Trim()
    $VersionFull = $VersionPrefix

    return [pscustomobject] @{ 
        Full = $VersionFull
        Prefix = $VersionPrefix
    }
}
catch {
    Write-Warning $_.Exception
    return $null
}
