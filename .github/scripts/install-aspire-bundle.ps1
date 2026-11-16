<#
 .SYNOPSIS
    Installs the Aspire CLI bundle before a solution build that contains an Aspire AppHost.

 .DESCRIPTION
    tools/Opc.Ua.Lens.AppHost builds with AspireUseCliBundle. When Aspire home
    holds no bundle, the AppHost build downloads it (about 350 MB) by running
    'aspire setup' itself under a two-minute timeout. On cold CI runners that
    in-build setup intermittently fails, and the build then fails with
    ASPIRE009. Installing the bundle up front, without that timeout and with
    retries, lets the AppHost build resolve the existing layout instead.

    The bundle version is the Aspire.Hosting.AppHost version that
    Directory.Packages.props pins.

 .PARAMETER PackagesPropsPath
    Path to Directory.Packages.props.

 .PARAMETER AspireHome
    Aspire home that receives the bundle. Defaults to ASPIRE_HOME or, when that
    is not set, to the .aspire directory in the user profile, which is where the
    AppHost build looks.

 .PARAMETER Attempts
    Number of install attempts before the script fails.
#>
param(
    [string] $PackagesPropsPath = 'Directory.Packages.props',
    [string] $AspireHome = '',
    [int] $Attempts = 3
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagesPropsPath -PathType Leaf))
{
    Write-Host "::error::'$PackagesPropsPath' does not exist; check out the repository first."
    exit 1
}

[xml] $props = Get-Content -LiteralPath $PackagesPropsPath -Raw
$version = @($props.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -ceq 'Aspire.Hosting.AppHost' } |
    ForEach-Object { $_.Version })
if ($version.Count -ne 1 -or $version[0] -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')
{
    Write-Host "::error::'$PackagesPropsPath' must pin exactly one Aspire.Hosting.AppHost version."
    exit 1
}
$version = $version[0]

if ([string]::IsNullOrWhiteSpace($AspireHome))
{
    $AspireHome = if ([string]::IsNullOrWhiteSpace($env:ASPIRE_HOME))
    {
        Join-Path ([Environment]::GetFolderPath('UserProfile')) '.aspire'
    }
    else
    {
        $env:ASPIRE_HOME
    }
}

$bundle = Join-Path $AspireHome 'bundle'
for ($attempt = 1; $attempt -le $Attempts; $attempt++)
{
    Write-Host "Installing the Aspire $version CLI bundle into '$AspireHome' (attempt $attempt of $Attempts)."
    dotnet dnx --yes "aspire.cli@$version" -- setup --install-path $AspireHome
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0 -and
        (Test-Path -LiteralPath (Join-Path $bundle 'dcp') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $bundle 'dashboard') -PathType Container))
    {
        Write-Host "Installed the Aspire $version CLI bundle into '$bundle'."
        exit 0
    }
    Write-Host "::warning::Aspire bundle setup attempt $attempt exited with $exitCode or left no complete bundle."
}

Write-Host "::error::Could not install the Aspire $version CLI bundle into '$AspireHome' after $Attempts attempts."
exit 1
