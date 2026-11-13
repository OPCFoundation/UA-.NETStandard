<#
 .SYNOPSIS
    Selects the exact repository .NET SDK and a job-private install directory.

 .DESCRIPTION
    global.json pins the minimum SDK version with rollForward 'latestFeature' so
    that contributors can build with any newer feature band or patch of that
    major version they have installed. The dotnet host applies that policy to
    every SDK in its own installation directory, and GitHub-hosted runner images
    preinstall newer SDKs into the default directory. A job that installs the
    repository SDK there therefore builds with whatever newer SDK the image
    happens to carry, and that SDK's new analyzers can break the build.

    This script points actions/setup-dotnet at a directory under RUNNER_TEMP
    that contains only the SDKs the job installs, so the host can resolve
    nothing newer than the global.json version. It writes:

      DOTNET_INSTALL_DIR  (GITHUB_ENV)    The job-private install directory.
      sdk                 (GITHUB_OUTPUT) The exact global.json SDK version.
      versions            (GITHUB_OUTPUT) The SDKs to install, the global.json
                                          version last.

 .PARAMETER AdditionalSdks
    Newline-separated SDK versions the job needs besides the global.json SDK,
    for example the 8.0.x and 9.0.x SDKs whose runtimes the older target
    frameworks' tests execute on.

 .PARAMETER GlobalJsonPath
    Path to the repository global.json.

 .PARAMETER InstallDirectory
    Directory that receives the SDKs. Defaults to RUNNER_TEMP/dotnet, which is
    empty at the start of every job.
#>
param(
    [string] $AdditionalSdks = '',
    [string] $GlobalJsonPath = 'global.json',
    [string] $InstallDirectory = ''
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GlobalJsonPath -PathType Leaf))
{
    Write-Host "::error::'$GlobalJsonPath' does not exist; check out the repository first."
    exit 1
}

$sdk = (Get-Content -LiteralPath $GlobalJsonPath -Raw | ConvertFrom-Json).sdk.version
if ([string]::IsNullOrWhiteSpace($sdk) -or $sdk -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')
{
    Write-Host "::error::'$GlobalJsonPath' must pin an exact sdk.version; found '$sdk'."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($InstallDirectory))
{
    if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP))
    {
        Write-Host '::error::RUNNER_TEMP is not set; pass -InstallDirectory explicitly.'
        exit 1
    }
    $InstallDirectory = Join-Path $env:RUNNER_TEMP 'dotnet'
}

$versions = [System.Collections.Generic.List[string]]::new()
foreach ($line in $AdditionalSdks -split '\r?\n')
{
    $version = $line.Trim()
    if ($version.Length -eq 0 -or $version -ceq $sdk -or $versions.Contains($version))
    {
        continue
    }
    if ($version -notmatch '^\d+(\.(\d+|x)){1,2}(-[0-9A-Za-z.-]+)?$')
    {
        Write-Host "::error::'$version' is not a .NET SDK version that actions/setup-dotnet accepts."
        exit 1
    }
    $versions.Add($version)
}
$versions.Add($sdk)

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_ENV))
{
    "DOTNET_INSTALL_DIR=$InstallDirectory" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
}
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT))
{
    $delimiter = "versions_$([guid]::NewGuid().ToString('N'))"
    @(
        "sdk=$sdk"
        "versions<<$delimiter"
        $versions
        $delimiter
    ) | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

Write-Host "Installing .NET SDK(s) $($versions -join ', ') into '$InstallDirectory'; the build uses $sdk."
