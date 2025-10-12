<#
 .SYNOPSIS
    Sets CI version build variables and/or returns version information.

 .DESCRIPTION
    The script is a wrapper around any versioning tool we use and abstracts it from
    the rest of the build system.
#>

# Try install tool
# Note: Keep Version 3.7.115, it is known working for 4 digit versioning
& dotnet @("tool", "install", "--tool-path", "./tools", "--version", "3.7.115", "--framework", "net80", "nbgv") 2>&1 

$props = (& ./tools/nbgv  @("get-version", "-f", "json")) | ConvertFrom-Json
if ($LastExitCode -ne 0) {
   throw "Error: 'nbgv get-version -f json' failed with $($LastExitCode)."
}

$version = [pscustomobject] @{ 
   Full = $props.CloudBuildAllVars.NBGV_Version
   Pre = $props.CloudBuildAllVars.NBGV_PrereleaseVersion
   Public = $props.CloudBuildAllVars.NBGV_PublicRelease
   Nuget = $props.CloudBuildAllVars.NBGV_NuGetPackageVersion
   Prefix = $props.CloudBuildAllVars.NBGV_SimpleVersion
   Revision = $props.CloudBuildAllVars.NBGV_VersionRevision
}

# Call versioning for build
if ($version.Public -eq 'True')
{
   & ./tools/nbgv  @("cloud", "-c", "-a", "-v", "$($version.Full)$($version.Pre)")
}
else
{
   & ./tools/nbgv  @("cloud", "-c", "-a")
}

if ($LastExitCode -ne 0) {
   throw "Error: 'nbgv cloud -c -a' failed with $($LastExitCode)."
}

# Set build environment version numbers in pipeline context
Write-Host "Setting version build variables:"
Write-Host "##vso[task.setvariable variable=Version_Full;isOutput=true]$($version.Full)"
Write-Host "##vso[task.setvariable variable=Version_Prefix;isOutput=true]$($version.Prefix)"
