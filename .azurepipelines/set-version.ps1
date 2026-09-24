<#
 .SYNOPSIS
    Sets CI version build variables and/or returns version information.

 .DESCRIPTION
    The script is a wrapper around any versioning tool we use and abstracts it from
    the rest of the build system.
#>

# Keep the CLI and the MSBuild package on the same explicit version. A version
# range or omitted --version would make otherwise identical CI runs use
# different nbgv behavior as releases appear on nuget.org.
[xml]$centralPackages = Get-Content -LiteralPath './Directory.Packages.props' -Raw
$nbgvVersions = @($centralPackages.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -eq 'Nerdbank.GitVersioning' } |
    ForEach-Object { $_.Version })
if ($nbgvVersions.Count -ne 1 -or [string]::IsNullOrWhiteSpace($nbgvVersions[0])) {
    throw 'Directory.Packages.props must declare exactly one Nerdbank.GitVersioning version.'
}

$nbgvVersion = $nbgvVersions[0]
if ($nbgvVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Nerdbank.GitVersioning version '$nbgvVersion' must be an explicit stable version."
}

$toolPath = './tools'
$toolExecutable = Join-Path $toolPath 'nbgv'
if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    $toolExecutable += '.exe'
}

$toolCommand = if (Test-Path -LiteralPath $toolExecutable) { 'update' } else { 'install' }
& dotnet @(
    'tool',
    $toolCommand,
    '--tool-path',
    $toolPath,
    '--version',
    $nbgvVersion,
    '--framework',
    'net10.0',
    'nbgv') 2>&1
if ($LastExitCode -ne 0) {
    throw "Failed to $toolCommand nbgv $nbgvVersion (exit code $LastExitCode)."
}

$props = (& $toolExecutable @("get-version", "-f", "json")) | ConvertFrom-Json
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
   & $toolExecutable @("cloud", "-c", "-a", "-v", "$($version.Full)$($version.Pre)")
}
else
{
   & $toolExecutable @("cloud", "-c", "-a")
}

if ($LastExitCode -ne 0) {
   throw "Error: 'nbgv cloud -c -a' failed with $($LastExitCode)."
}

# Set build environment version numbers in pipeline context
Write-Host "Setting version build variables:"
Write-Host "##vso[task.setvariable variable=Version_Full;isOutput=true]$($version.Full)"
Write-Host "##vso[task.setvariable variable=Version_Prefix;isOutput=true]$($version.Prefix)"
