<#
.SYNOPSIS
    Validates the source-generator NuGet payloads and builds clean consumers.

.PARAMETER PackageDirectory
    Directory containing the packed source-generator NuGet packages.
#>

param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory
)

$ErrorActionPreference = "Stop"

function Assert-Condition
{
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition)
    {
        throw $Message
    }
}

function Get-PackageInfo
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $PackageId
    )

    $packagePattern = "^$([Regex]::Escape($PackageId))\.(?<version>[0-9].+)\.nupkg$"
    $packages = @(Get-ChildItem -Path $PackageDirectory -Filter "$PackageId*.nupkg" -File -Recurse |
        Where-Object { $_.Name -notlike "*.snupkg" -and $_.Name -match $packagePattern })

    Assert-Condition ($packages.Count -gt 0) "Package '$PackageId' was not found in '$PackageDirectory'."
    Assert-Condition (
        $packages.Count -eq 1
    ) "Expected exactly one package '$PackageId' in '$PackageDirectory'; found $($packages.Count)."
    $package = $packages[0]

    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try
    {
        $entries = @($archive.Entries | ForEach-Object FullName)
        $nuspecEntries = @($archive.Entries |
            Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) })
        Assert-Condition (
            $nuspecEntries.Count -eq 1
        ) "Package '$($package.Name)' must contain exactly one nuspec."
        $nuspecEntry = $nuspecEntries[0]

        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try
        {
            [xml] $nuspec = $reader.ReadToEnd()
        }
        finally
        {
            $reader.Dispose()
        }
    }
    finally
    {
        $archive.Dispose()
    }

    [PSCustomObject] @{
        Id = $PackageId
        Path = $package.FullName
        Version = [string] $nuspec.package.metadata.version
        Entries = $entries
        Dependencies = @($nuspec.SelectNodes("//*[local-name()='dependency']"))
    }
}

function Test-PackageContents
{
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject] $Package,

        [Parameter(Mandatory = $true)]
        [string] $GeneratorAssembly
    )

    $analyzerPath = "analyzers/dotnet/cs/"
    $requiredAssemblies = @(
        $GeneratorAssembly,
        "Opc.Ua.SourceGeneration.Core.dll",
        "Opc.Ua.Types.dll",
        "SourceGenerator.Foundations.Contracts.dll",
        "SourceGenerator.Foundations.Windows.dll"
    )

    foreach ($assembly in $requiredAssemblies)
    {
        Assert-Condition (
            $Package.Entries -contains "$analyzerPath$assembly"
        ) "Package '$($Package.Id)' is missing '$analyzerPath$assembly'."
    }

    $dllEntries = @($Package.Entries |
        Where-Object { $_.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase) })
    Assert-Condition ($dllEntries.Count -gt 0) "Package '$($Package.Id)' contains no assemblies."
    Assert-Condition (
        @($dllEntries | Where-Object { -not $_.StartsWith(
            $analyzerPath,
            [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0
    ) "Package '$($Package.Id)' contains assemblies outside '$analyzerPath'."
    Assert-Condition (
        @($dllEntries | Where-Object {
            [IO.Path]::GetFileName($_).StartsWith(
                "Microsoft.CodeAnalysis",
                [StringComparison]::OrdinalIgnoreCase)
        }).Count -eq 0
    ) "Package '$($Package.Id)' must not ship Microsoft.CodeAnalysis host assemblies."
    Assert-Condition (
        $Package.Dependencies.Count -eq 0
    ) "Package '$($Package.Id)' must carry its analyzer runtime closure privately."
}

function Invoke-DotNet
{
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-MSBuildProperty
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath,

        [Parameter(Mandatory = $true)]
        [string] $Configuration,

        [Parameter(Mandatory = $true)]
        [string] $PropertyName
    )

    $output = & dotnet msbuild $ProjectPath `
        "-getProperty:$PropertyName" `
        "-p:Configuration=$Configuration" `
        -nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not read MSBuild property '$PropertyName' from '$ProjectPath'."
    }

    return [string]($output |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Last 1).Trim()
}

function Test-ConfigurationPackageIds
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath
    )

    $releaseId = Get-MSBuildProperty $ProjectPath "Release" "PackageId"
    $debugId = Get-MSBuildProperty $ProjectPath "Debug" "PackageId"
    Assert-Condition (
        $debugId -eq "$releaseId.Debug"
    ) "Debug package '$debugId' must use the release ID '$releaseId' with a '.Debug' suffix."
}

function Test-CleanConsumer
{
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject] $Package,

        [Parameter(Mandatory = $true)]
        [string] $ValidationRoot
    )

    $consumerName = $Package.Id.Split(".")[-1]
    $consumerDirectory = Join-Path $ValidationRoot $consumerName
    New-Item -ItemType Directory -Path $consumerDirectory | Out-Null
    $projectPath = Join-Path $consumerDirectory "$consumerName.csproj"
    $expectedDiagnosticSuppression = if ($Package.Id.EndsWith(
        ".Stack",
        [StringComparison]::Ordinal))
    {
        "    <NoWarn>STACKGEN001</NoWarn>"
    }
    else
    {
        ""
    }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
$expectedDiagnosticSuppression
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$($Package.Id)" Version="$($Package.Version)"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $projectPath -Encoding utf8
    @"
namespace SourceGeneratorConsumer;

public static class ConsumerMarker
{
    public static int Value => 42;
}
"@ | Set-Content -Path (Join-Path $consumerDirectory "ConsumerMarker.cs") -Encoding utf8

    $nugetConfig = Join-Path $ValidationRoot "NuGet.Config"
    $packagesPath = Join-Path $ValidationRoot "packages"
    Invoke-DotNet @(
        "restore",
        $projectPath,
        "--configfile",
        $nugetConfig,
        "--packages",
        $packagesPath,
        "--nologo"
    )
    Invoke-DotNet @(
        "build",
        $projectPath,
        "--configuration",
        "Release",
        "--no-restore",
        "--nologo"
    )
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedPackageDirectory = (Resolve-Path $PackageDirectory).Path
$PackageDirectory = $resolvedPackageDirectory
$repoRoot = Split-Path $PSScriptRoot -Parent
$validationRoot = Join-Path (Join-Path $repoRoot "artifacts") "source-generator-consumer"
Test-ConfigurationPackageIds (
    Join-Path $repoRoot "tools\Opc.Ua.SourceGeneration\Opc.Ua.SourceGeneration.csproj")
Test-ConfigurationPackageIds (
    Join-Path $repoRoot "tools\Opc.Ua.SourceGeneration.Stack\Opc.Ua.SourceGeneration.Stack.csproj")
$modelPackage = Get-PackageInfo "OPCFoundation.NetStandard.Opc.Ua.SourceGeneration"
$stackPackage = Get-PackageInfo "OPCFoundation.NetStandard.Opc.Ua.SourceGeneration.Stack"

Test-PackageContents $modelPackage "Opc.Ua.SourceGeneration.dll"
Test-PackageContents $stackPackage "Opc.Ua.SourceGeneration.Stack.dll"

Remove-Item -Path $validationRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $validationRoot | Out-Null
try
{
    "<Project />" | Set-Content -Path (Join-Path $validationRoot "Directory.Build.props") -Encoding utf8
    "<Project />" | Set-Content -Path (Join-Path $validationRoot "Directory.Build.targets") -Encoding utf8
    @"
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path (Join-Path $validationRoot "Directory.Packages.props") -Encoding utf8
    $escapedPackageDirectory = [System.Security.SecurityElement]::Escape($PackageDirectory)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="source-generator-packages" value="$escapedPackageDirectory" />
  </packageSources>
</configuration>
"@ | Set-Content -Path (Join-Path $validationRoot "NuGet.Config") -Encoding utf8

    Test-CleanConsumer $modelPackage $validationRoot
    Test-CleanConsumer $stackPackage $validationRoot
}
finally
{
    Remove-Item -Path $validationRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Validated source-generator package contents and clean consumers."
