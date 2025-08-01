<#
 .SYNOPSIS
    Sets CI version build variables and/or returns version information.

 .DESCRIPTION
    The script is a wrapper around any versioning tool we use and abstracts it from
    the rest of the build system.
#>

try {
    # Try install tool
    # Note: Keep Version 3.6.146, it is known working for 4 digit versioning 
    & dotnet @("tool", "install", "--tool-path", "./tools", "--version", "3.6.146", "--framework", "net80", "nbgv") 2>&1 

    $props = (& ./tools/nbgv  @("get-version", "-f", "json")) | ConvertFrom-Json
    if ($LastExitCode -ne 0) {
        throw "Error: 'nbgv get-version -f json' failed with $($LastExitCode)."
    }

    return [pscustomobject] @{ 
        Full = $props.CloudBuildAllVars.NBGV_Version
        Pre = $props.CloudBuildAllVars.NBGV_PrereleaseVersion
        Public = $props.CloudBuildAllVars.NBGV_PublicRelease
        Nuget = $props.CloudBuildAllVars.NBGV_NuGetPackageVersion
        Prefix = $props.CloudBuildAllVars.NBGV_SimpleVersion
        Revision = $props.CloudBuildAllVars.NBGV_VersionRevision
    }
}
catch {
    Write-Warning $_.Exception
    return $null
}
