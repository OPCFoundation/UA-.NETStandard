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

    $analyzerRoot = "analyzers/dotnet/"
    $requiredAssemblies = @(
        $GeneratorAssembly,
        "Opc.Ua.SourceGeneration.Core.dll",
        "Opc.Ua.Types.dll"
    )

    $dllEntries = @($Package.Entries |
        Where-Object { $_.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase) })
    Assert-Condition ($dllEntries.Count -gt 0) "Package '$($Package.Id)' contains no assemblies."
    Assert-Condition (
        @($dllEntries | Where-Object { -not $_.StartsWith(
            $analyzerRoot,
            [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0
    ) "Package '$($Package.Id)' contains assemblies outside '$analyzerRoot'."

    # One folder per supported Roslyn API version; the .NET SDK loads the
    # highest one its compiler supports and ignores folders above it.
    $roslynFolders = @($dllEntries |
        ForEach-Object { $_.Substring($analyzerRoot.Length).Split("/")[0] } |
        Sort-Object -Unique)
    Assert-Condition (
        $roslynFolders.Count -ge 2
    ) ("Package '$($Package.Id)' must ship one analyzer folder per supported Roslyn " +
        "band; found: $($roslynFolders -join ', ').")
    Assert-Condition (
        @($roslynFolders | Where-Object { $_ -notmatch "^roslyn[0-9]+\.[0-9]+$" }).Count -eq 0
    ) ("Package '$($Package.Id)' analyzer folders must be named 'roslyn<major>.<minor>'; " +
        "found: $($roslynFolders -join ', ').")

    foreach ($roslynFolder in $roslynFolders)
    {
        $analyzerPath = "$analyzerRoot$roslynFolder/cs/"
        foreach ($assembly in $requiredAssemblies)
        {
            Assert-Condition (
                $Package.Entries -contains "$analyzerPath$assembly"
            ) "Package '$($Package.Id)' is missing '$analyzerPath$assembly'."
        }
    }

    # Assemblies the Roslyn host supplies itself must never be shipped alongside an
    # analyzer. A second copy makes the analyzer load context bind a different
    # identity for types that cross Roslyn's own API surface - most visibly
    # ImmutableArray<T> - so the generator dies on its first API call with
    # MissingMethodException, surfaced only as warning CS8784.
    $hostProvided = @(
        "Microsoft.CodeAnalysis",
        "System.Collections.Immutable",
        "System.Reflection.Metadata"
    )
    foreach ($forbidden in $hostProvided)
    {
        Assert-Condition (
            @($dllEntries | Where-Object {
                [IO.Path]::GetFileName($_).StartsWith(
                    $forbidden,
                    [StringComparison]::OrdinalIgnoreCase)
            }).Count -eq 0
        ) ("Package '$($Package.Id)' must not ship '$forbidden*' - the Roslyn host " +
            "provides it, and a second copy breaks generator initialization.")
    }
    Assert-Condition (
        @($dllEntries | Where-Object {
            [IO.Path]::GetFileName($_).StartsWith(
                "SourceGenerator.Foundations",
                [StringComparison]::OrdinalIgnoreCase)
        }).Count -eq 0
    ) "Package '$($Package.Id)' must not ship SourceGenerator.Foundations assemblies."
    Assert-Condition (
        $Package.Dependencies.Count -eq 0
    ) "Package '$($Package.Id)' must carry its analyzer runtime closure privately."
}

function Test-AnalyzerBands
{
    <#
    .SYNOPSIS
        Asserts a package ships the expected analyzer assemblies in every Roslyn band.

    .DESCRIPTION
        For packages that Test-PackageContents cannot check because they also ship lib/
        assemblies and carry dependencies - the migration analyzer, whose payload comes
        from a hand-written nuspec rather than SourceGeneratorPack.targets. A band added
        to one half of that nuspec and not the other is invisible at pack time.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject] $Package,

        [Parameter(Mandatory = $true)]
        [string[]] $ExpectedFolders,

        [Parameter(Mandatory = $true)]
        [string[]] $ExpectedAssemblies
    )

    $analyzerRoot = "analyzers/dotnet/"
    foreach ($folder in $ExpectedFolders)
    {
        foreach ($assembly in $ExpectedAssemblies)
        {
            $path = "$analyzerRoot$folder/cs/$assembly"
            Assert-Condition (
                $Package.Entries -contains $path
            ) "Package '$($Package.Id)' is missing '$path'."
        }
    }

    $actualFolders = @($Package.Entries |
        Where-Object {
            $_.StartsWith($analyzerRoot, [StringComparison]::OrdinalIgnoreCase) -and
            $_.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase)
        } |
        ForEach-Object { $_.Substring($analyzerRoot.Length).Split("/")[0] } |
        Sort-Object -Unique)
    Assert-Condition (
        @(Compare-Object $actualFolders @($ExpectedFolders | Sort-Object -Unique)).Count -eq 0
    ) ("Package '$($Package.Id)' analyzer bands are '$($actualFolders -join ', ')' but " +
        "'$(($ExpectedFolders | Sort-Object -Unique) -join ', ')' was expected.")
}

function Test-PackageBuildProps
{
    <#
    .SYNOPSIS
        Asserts the auto-imported MSBuild props file is named after the package id.

    .DESCRIPTION
        NuGet only auto-imports `build/<PackageId>.props`. The model generator's props
        declares every CompilerVisibleProperty / CompilerVisibleItemMetadata the generator
        needs, so a name that does not track the package id (including the `.Debug`
        configuration suffix) silently strips every `ModelSourceGenerator*` setting from
        package consumers without any build error.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject] $Package,

        [switch] $RequiresProps
    )

    $expected = "build/$($Package.Id).props"
    if ($RequiresProps)
    {
        Assert-Condition (
            $Package.Entries -contains $expected
        ) ("Package '$($Package.Id)' is missing '$expected'. NuGet only auto-imports " +
            "build/<PackageId>.props, so any other name is never imported by consumers.")
    }

    $strayBuildFiles = @($Package.Entries |
        Where-Object { $_.StartsWith("build/", [StringComparison]::OrdinalIgnoreCase) } |
        Where-Object { $_ -ne $expected })
    Assert-Condition (
        $strayBuildFiles.Count -eq 0
    ) ("Package '$($Package.Id)' ships unexpected build/ entries that NuGet will never " +
        "auto-import: $($strayBuildFiles -join ', ').")
}

function Test-ExpectedPackageSet
{
    <#
    .SYNOPSIS
        Asserts the packed output matches the checked-in expected package list.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string] $ManifestPath
    )

    Assert-Condition (Test-Path $ManifestPath) "Expected package manifest '$ManifestPath' not found."

    $expected = @(Get-Content $ManifestPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith("#") } |
        Sort-Object -Unique)

    $actual = @(Get-ChildItem -Path $PackageDirectory -Filter "*.nupkg" -File -Recurse |
        Where-Object { $_.Name -notlike "*.snupkg" } |
        ForEach-Object { $_.Name -replace "\.[0-9].*\.nupkg$", "" } |
        Sort-Object -Unique)

    $missing = @($expected | Where-Object { $actual -notcontains $_ })
    $unexpected = @($actual | Where-Object { $expected -notcontains $_ })

    Assert-Condition ($missing.Count -eq 0) (
        "Packages listed in '$ManifestPath' were not produced: $($missing -join ', '). " +
        "If a package was intentionally removed or renamed, update the manifest in the " +
        "same pull request.")
    Assert-Condition ($unexpected.Count -eq 0) (
        "Unexpected packages were produced: $($unexpected -join ', '). If a package was " +
        "intentionally added, add it to '$ManifestPath'; otherwise a build-time-only " +
        "project has become packable by accident.")

    Write-Host "Package set matches the expected manifest ($($expected.Count) packages)."
}

function Test-SourceGeneratingConsumer
{
    <#
    .SYNOPSIS
        Builds a standalone project that actually drives the packaged model generator.

    .DESCRIPTION
        Test-CleanConsumer only proves the analyzer loads. This test proves the packaged
        generator *generates*: it feeds a real NodeSet2 in as an AdditionalFile and then
        references the emitted types from hand-written code, so a generator that fails to
        run, or runs with the wrong options, becomes a compile error.

        The consumer deliberately does NOT import the generator's props file by path the
        way the in-repo projects do. It relies on NuGet auto-importing
        `build/<PackageId>.props`, and it pins a custom `ModelSourceGeneratorPrefix` that
        cannot be derived from the NodeSet itself. If that props file is ever misnamed
        again the metadata becomes invisible to the compiler, the emitted namespace falls
        back to the model-derived default, and the references below stop compiling.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject] $Package,

        [Parameter(Mandatory = $true)]
        [string] $ValidationRoot,

        [Parameter(Mandatory = $true)]
        [string] $RepoRoot
    )

    $nodeSet = Join-Path $RepoRoot "samples\MinimalApi\MinimalBoilerServer\Model\Boiler.NodeSet2.xml"
    Assert-Condition (Test-Path $nodeSet) "NodeSet '$nodeSet' used by the generator consumer test not found."

    $consumerDirectory = Join-Path $ValidationRoot "SourceGenerating"
    New-Item -ItemType Directory -Path $consumerDirectory | Out-Null
    $projectPath = Join-Path $consumerDirectory "SourceGenerating.csproj"
    $generatedRoot = "generated"
    # Namespace prefix that the generator can only learn from the AdditionalFiles
    # metadata, i.e. only when build/<PackageId>.props was auto-imported.
    $prefix = "PackagedGeneratorProbe"
    $escapedNodeSet = [System.Security.SecurityElement]::Escape($nodeSet)

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$generatedRoot</CompilerGeneratedFilesOutputPath>
    <ModelSourceGeneratorVersion>v105</ModelSourceGeneratorVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$($Package.Id)" Version="$($Package.Version)"
                      PrivateAssets="all" />
    <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Server"
                      Version="$($Package.Version)" />
  </ItemGroup>
  <ItemGroup>
    <AdditionalFiles Include="$escapedNodeSet">
      <ModelSourceGeneratorModelUri>http://opcfoundation.org/UA/Boiler/</ModelSourceGeneratorModelUri>
      <ModelSourceGeneratorName>Boiler</ModelSourceGeneratorName>
      <ModelSourceGeneratorPrefix>$prefix</ModelSourceGeneratorPrefix>
    </AdditionalFiles>
  </ItemGroup>
</Project>
"@ | Set-Content -Path $projectPath -Encoding utf8

    # Referencing the generated identifier tables and a generated NodeState type is the
    # assertion: without a successful generation run under the requested prefix none of
    # these resolve and the consumer does not compile.
    @"
using $prefix;

namespace SourceGeneratingConsumer;

public static class GeneratedModelProbe
{
    public static uint BoilerTypeIdentifier => ObjectTypes.BoilerType;

    public static string BoilerTypeBrowseName => BrowseNames.BoilerType;

    public static System.Type BoilerStateType => typeof(BoilerState);
}
"@ | Set-Content -Path (Join-Path $consumerDirectory "GeneratedModelProbe.cs") -Encoding utf8

    $nugetConfig = Join-Path $ValidationRoot "NuGet.WithUpstream.Config"
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

    $generatedFiles = @(Get-ChildItem -Path (Join-Path $consumerDirectory $generatedRoot) `
        -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue)
    Assert-Condition (
        $generatedFiles.Count -gt 0
    ) ("The packaged generator produced no source for '$($Package.Id)'. The consumer " +
        "compiled, but nothing was emitted under '$generatedRoot'.")

    Write-Host (
        "Packaged model generator emitted $($generatedFiles.Count) file(s) for a " +
        "standalone NodeSet consumer.")
}

function Test-DownlevelAnalyzerHost
{
    <#
    .SYNOPSIS
        Loads the down-level analyzer payload in a real compiler of that Roslyn band.

    .DESCRIPTION
        The repository builds against the newest band, so nothing else here ever executes
        the down-level payload. Every way it can be wrong is reported by the compiler as a
        *warning*, which means a broken band ships silently and the consumer simply gets no
        generated code:

          CS9057 - built against a newer compiler than the host, so it is skipped entirely.
          CS8784 - loaded but failed to initialize, e.g. MissingMethodException because a
                   shipped System.Collections.Immutable bound a second ImmutableArray<T>.
          CS8032 - the analyzer instance could not be created at all.

        So run the matching csc over the packed payload and fail on any of them. Absence of
        diagnostics is necessary but not sufficient - a generator that is never handed to
        the compiler also produces none - so /reportanalyzer is used to additionally assert
        that the generator positively executed.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject] $Package,

        [Parameter(Mandatory = $true)]
        [string] $ValidationRoot,

        [Parameter(Mandatory = $true)]
        [string] $RoslynFolder,

        [Parameter(Mandatory = $true)]
        [string] $CompilerToolsetVersion,

        [Parameter(Mandatory = $true)]
        [string] $GeneratorAssembly
    )

    $root = Join-Path $ValidationRoot "downlevel-$RoslynFolder-$($Package.Id)"
    $analyzerDirectory = Join-Path $root "analyzers"
    # Per package, and emptied first: a shared directory would leak the previous
    # package's assemblies into this compilation and the assertion below would pass on
    # someone else's generator.
    Remove-Item -Path $analyzerDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $analyzerDirectory -Force | Out-Null

    $archive = [IO.Compression.ZipFile]::OpenRead($Package.Path)
    try
    {
        $prefix = "analyzers/dotnet/$RoslynFolder/cs/"
        $entries = @($archive.Entries |
            Where-Object { $_.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } |
            Where-Object { $_.FullName.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase) })
        Assert-Condition (
            $entries.Count -gt 0
        ) "Package '$($Package.Id)' has no assemblies under '$prefix'."
        foreach ($entry in $entries)
        {
            [IO.Compression.ZipFileExtensions]::ExtractToFile(
                $entry, (Join-Path $analyzerDirectory ([IO.Path]::GetFileName($entry.FullName))), $true)
        }
    }
    finally
    {
        $archive.Dispose()
    }

    # `dotnet tool`-free way to get a specific csc: restore the toolset package.
    $toolsetProject = Join-Path $root "toolset.csproj"
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Net.Compilers.Toolset"
                      Version="$CompilerToolsetVersion" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $toolsetProject -Encoding utf8

    $packagesPath = Join-Path $ValidationRoot "packages"
    Invoke-DotNet @(
        "restore",
        $toolsetProject,
        "--configfile",
        (Join-Path $ValidationRoot "NuGet.WithUpstream.Config"),
        "--packages",
        $packagesPath,
        "--nologo"
    )

    $csc = Join-Path $packagesPath `
        "microsoft.net.compilers.toolset/$CompilerToolsetVersion/tasks/netcore/bincore/csc.dll"
    Assert-Condition (Test-Path $csc) "csc from Microsoft.Net.Compilers.Toolset $CompilerToolsetVersion not found at '$csc'."

    $sourceFile = Join-Path $root "Probe.cs"
    "namespace DownlevelProbe { public class Marker { } }" | Set-Content -Path $sourceFile -Encoding utf8

    $referenceDirectory = @(Get-ChildItem -Path (
        Join-Path $env:ProgramFiles "dotnet\packs\Microsoft.NETCore.App.Ref") -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName "ref") -Directory -ErrorAction SilentlyContinue } |
        Select-Object -First 1)
    Assert-Condition (
        $referenceDirectory.Count -eq 1
    ) "Could not locate a Microsoft.NETCore.App reference assembly directory."

    $arguments = @(
        $csc, "/noconfig", "/nostdlib+", "/target:library",
        "/out:$(Join-Path $root 'probe.dll')", "/reportanalyzer"
    )
    $arguments += @(Get-ChildItem (Join-Path $referenceDirectory[0].FullName "*.dll") |
        ForEach-Object { "/r:$($_.FullName)" })
    $arguments += @(Get-ChildItem (Join-Path $analyzerDirectory "*.dll") |
        ForEach-Object { "/analyzer:$($_.FullName)" })
    $arguments += $sourceFile

    $output = & dotnet @arguments 2>&1
    $loadDiagnostics = @($output | Where-Object { $_ -match "CS9057|CS8784|CS8032|CS8034" })
    Assert-Condition (
        $loadDiagnostics.Count -eq 0
    ) ("Package '$($Package.Id)' analyzer folder '$RoslynFolder' does not load in " +
        "Microsoft.Net.Compilers.Toolset $CompilerToolsetVersion. A consumer on that " +
        "compiler silently gets no generated code:`n" + ($loadDiagnostics -join "`n"))

    # /reportanalyzer lists every generator the compiler actually ran, so this turns
    # "nothing complained" into "the generator executed".
    $ran = @($output | Where-Object { $_ -match "(^|\s)$([Regex]::Escape($GeneratorAssembly)), Version=" })
    Assert-Condition (
        $ran.Count -gt 0
    ) ("Package '$($Package.Id)' analyzer folder '$RoslynFolder' reported no execution of " +
        "'$GeneratorAssembly' under Microsoft.Net.Compilers.Toolset $CompilerToolsetVersion. " +
        "The payload loaded without complaint but the generator never ran:`n" +
        ($output -join "`n"))

    Write-Host "Analyzer folder '$RoslynFolder' of '$($Package.Id)' runs in csc $CompilerToolsetVersion."
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
    Join-Path $repoRoot "tools\Opc.Ua.SourceGeneration.Pack\Opc.Ua.SourceGeneration.Pack.csproj")
Test-ConfigurationPackageIds (
    Join-Path $repoRoot "tools\Opc.Ua.SourceGeneration.Stack.Pack\Opc.Ua.SourceGeneration.Stack.Pack.csproj")
$modelPackage = Get-PackageInfo "OPCFoundation.NetStandard.Opc.Ua.SourceGeneration"
$stackPackage = Get-PackageInfo "OPCFoundation.NetStandard.Opc.Ua.SourceGeneration.Stack"
$migrationPackage = Get-PackageInfo "OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer"

Test-PackageContents $modelPackage "Opc.Ua.SourceGeneration.dll"
Test-PackageContents $stackPackage "Opc.Ua.SourceGeneration.Stack.dll"
Test-AnalyzerBands $migrationPackage @("roslyn4.14", "roslyn5.0") @(
    "Opc.Ua.MigrationAnalyzer.dll",
    "Opc.Ua.MigrationAnalyzer.CodeFixer.dll",
    "Opc.Ua.MigrationAnalyzer.Generator.dll")

# Only the model generator exposes MSBuild settings to consumers, so only it ships a
# build/<PackageId>.props; the stack generator must not smuggle in stray build/ content.
Test-PackageBuildProps $modelPackage -RequiresProps
Test-PackageBuildProps $stackPackage

Test-ExpectedPackageSet (Join-Path $PSScriptRoot "expected-packages.txt")

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
    # The generator packages carry their whole closure privately, so the clean consumers
    # restore from the artifact directory alone - that isolation is part of what they
    # assert. The source-generating consumer additionally pulls Opc.Ua.Server, whose
    # Microsoft.Extensions.* graph has to come from upstream.
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="source-generator-packages" value="$escapedPackageDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path (Join-Path $validationRoot "NuGet.WithUpstream.Config") -Encoding utf8

    Test-CleanConsumer $modelPackage $validationRoot
    Test-CleanConsumer $stackPackage $validationRoot
    Test-SourceGeneratingConsumer $modelPackage $validationRoot $repoRoot
    # The repository builds against the newest band, so the down-level payload is only
    # ever exercised here. Every failure mode is a compiler *warning*, so without this
    # a broken band ships silently.
    Test-DownlevelAnalyzerHost $modelPackage $validationRoot "roslyn4.14" "4.14.0" "Opc.Ua.SourceGeneration"
    Test-DownlevelAnalyzerHost $stackPackage $validationRoot "roslyn4.14" "4.14.0" "Opc.Ua.SourceGeneration.Stack"
    # The migration analyzer ships the same two bands from a hand-written nuspec rather
    # than SourceGeneratorPack.targets, so its down-level payload has its own way to rot.
    Test-DownlevelAnalyzerHost $migrationPackage $validationRoot "roslyn4.14" "4.14.0" `
        "Opc.Ua.MigrationAnalyzer.Generator"
}
finally
{
    Remove-Item -Path $validationRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Validated source-generator package contents and clean consumers."
