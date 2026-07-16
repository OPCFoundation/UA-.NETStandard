<#
.SYNOPSIS
    Regenerates the UANodeSetSerializers.g.cs pre-compiled XmlSerializer code for UANodeSet types.

.DESCRIPTION
    This script uses the Microsoft.XmlSerializer.Generator (sgen) tool to generate pre-compiled
    XML serialization code for UANodeSet types. The generated code is needed for NativeAOT compatibility
    because XmlSerializer's reflection-based fallback has bugs with XmlElement properties under NativeAOT.

    The script:
    1. Builds the Opc.Ua.Types project for net10.0 Release
    2. Downloads the Microsoft.XmlSerializer.Generator NuGet package (if not cached)
    3. Runs the sgen tool against the built assembly
    4. Post-processes the generated code:
       - Wraps in #if NET5_0_OR_GREATER / #endif
       - Adds module-level [UnconditionalSuppressMessage] for IL2026/IL3050
       - Adds [RequiresUnreferencedCode]/[RequiresDynamicCode] to InitCallbacks() overrides
       - Adds #pragma warning disable/restore

.PARAMETER SgenVersion
    Version of the Microsoft.XmlSerializer.Generator package. Default: 10.0.5

.PARAMETER SkipBuild
    Skip the dotnet build step (use if the assembly is already built).

.EXAMPLE
    .\Regenerate-UANodeSetSerializers.ps1
    .\Regenerate-UANodeSetSerializers.ps1 -SgenVersion 10.0.5 -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$SgenVersion = "10.0.5",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path "$scriptDir\..\..\..\"
$projectPath = "$scriptDir\..\Opc.Ua.Types.csproj"
$outputFile = "$scriptDir\UANodeSetSerializers.g.cs"

$configuration = "Release"
$framework = "net10.0"
$assemblyDir = "$scriptDir\..\bin\$configuration\$framework"
$assemblyName = "Opc.Ua.Types.dll"

Write-Host "=== UANodeSet XmlSerializer Code Generator ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the project
if (-not $SkipBuild) {
    Write-Host "[1/4] Building Opc.Ua.Types ($framework $configuration)..." -ForegroundColor Yellow
    & dotnet build $projectPath -c $configuration -f $framework --no-restore -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed. Fix build errors and retry."
        exit 1
    }
    Write-Host "       Build succeeded." -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping build (--SkipBuild)." -ForegroundColor DarkGray
}

# Verify assembly exists
$assemblyPath = Join-Path $assemblyDir $assemblyName
if (-not (Test-Path $assemblyPath)) {
    Write-Error "Assembly not found at: $assemblyPath`nBuild the project first or remove -SkipBuild."
    exit 1
}

# Step 2: Download sgen tool if needed
Write-Host "[2/4] Locating Microsoft.XmlSerializer.Generator v$SgenVersion..." -ForegroundColor Yellow

$sgenCacheDir = "$env:TEMP\sgen-tool-$SgenVersion"
$sgenDll = "$sgenCacheDir\lib\netstandard2.0\dotnet-Microsoft.XmlSerializer.Generator.dll"

if (-not (Test-Path $sgenDll)) {
    Write-Host "       Downloading from NuGet..." -ForegroundColor DarkGray
    $nupkgUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.xmlserializer.generator/$SgenVersion/microsoft.xmlserializer.generator.$SgenVersion.nupkg"
    $nupkgPath = "$env:TEMP\sgen-$SgenVersion.nupkg"

    Invoke-WebRequest -Uri $nupkgUrl -OutFile $nupkgPath -ErrorAction Stop
    if (Test-Path $sgenCacheDir) { Remove-Item $sgenCacheDir -Recurse -Force }
    Expand-Archive $nupkgPath -DestinationPath $sgenCacheDir -Force
    Remove-Item $nupkgPath -Force

    if (-not (Test-Path $sgenDll)) {
        Write-Error "Failed to extract sgen tool from NuGet package."
        exit 1
    }
    Write-Host "       Downloaded and cached." -ForegroundColor Green
} else {
    Write-Host "       Using cached tool." -ForegroundColor Green
}

# Step 3: Generate serializer code
Write-Host "[3/4] Running sgen tool..." -ForegroundColor Yellow

# Collect reference assemblies from the build output
$references = Get-ChildItem $assemblyDir -Filter "*.dll" |
    Where-Object { $_.Name -ne $assemblyName -and $_.Name -ne "Opc.Ua.Types.XmlSerializers.dll" } |
    ForEach-Object { $_.FullName }

# Run the generator (sgen finds references from the assembly directory automatically)
$sgenOutput = "$env:TEMP\sgen-output"
if (Test-Path $sgenOutput) { Remove-Item $sgenOutput -Recurse -Force }
New-Item -ItemType Directory -Path $sgenOutput | Out-Null

& dotnet exec $sgenDll $assemblyPath --force --quiet --type Opc.Ua.Export.UANodeSet --out $sgenOutput

if ($LASTEXITCODE -ne 0) {
    Write-Error "sgen tool failed. Check the output above for errors."
    exit 1
}

$generatedFile = "$sgenOutput\Opc.Ua.Types.XmlSerializers.cs"
if (-not (Test-Path $generatedFile)) {
    Write-Error "Generated file not found at: $generatedFile"
    exit 1
}

Write-Host "       Code generated successfully." -ForegroundColor Green

# Step 4: Post-process the generated code
Write-Host "[4/4] Post-processing generated code..." -ForegroundColor Yellow

$rawCode = Get-Content $generatedFile -Raw

# The generated file has an auto-generated header - replace it with our own
$header = @"
// <auto-generated>
// This file was generated by Microsoft.XmlSerializer.Generator tool.
// It provides pre-compiled XML serialization code for UANodeSet types,
// avoiding the reflection-based XmlSerializer fallback which has issues
// under NativeAOT.
//
// To regenerate, run: .\Regenerate-UANodeSetSerializers.ps1
// </auto-generated>
"@

# Remove the original auto-generated header comment block
$rawCode = $rawCode -replace '(?s)^//------------------------------------------------------------------------------\r?\n// <auto-generated>.*?</auto-generated>\r?\n//------------------------------------------------------------------------------\r?\n', ''

# Remove assembly-level attributes injected by sgen (AllowPartiallyTrustedCallers, SecurityTransparent, etc.)
# These conflict with our own assembly setup and are not needed when embedding the code directly.
$rawCode = $rawCode -replace '(?m)^\[assembly:System\.Security\.AllowPartiallyTrustedCallers\(\)\]\r?\n', ''
$rawCode = $rawCode -replace '(?m)^\[assembly:System\.Security\.SecurityTransparent\(\)\]\r?\n', ''
$rawCode = $rawCode -replace '(?m)^\[assembly:System\.Security\.SecurityRules\([^\)]+\)\]\r?\n', ''
$rawCode = $rawCode -replace '(?m)^\[assembly:System\.Xml\.Serialization\.XmlSerializerVersionAttribute\([^\)]+\)\]\r?\n', ''

# Add AOT attributes to InitCallbacks() overrides.
# The sgen tool generates: protected override void InitCallbacks() { }
# Under NativeAOT, the base class methods have AOT attributes, so overrides must match (IL2046/IL3051).
# XmlSerializationWriter.InitCallbacks() has [RequiresUnreferencedCode] only.
# XmlSerializationReader.InitCallbacks() has [RequiresUnreferencedCode] AND [RequiresDynamicCode].
# We differentiate by matching the class context.

# Writer: only [RequiresUnreferencedCode]
$rawCode = $rawCode -replace `
    '(class XmlSerializationWriterUANodeSet[\s\S]*?)(\r?\n)([ \t]+)(protected override void InitCallbacks\(\))', `
    '$1$2$3[RequiresUnreferencedCode("Generated serializer")]$2$3protected override void InitCallbacks()'

# Reader: both attributes
$rawCode = $rawCode -replace `
    '(class XmlSerializationReaderUANodeSet[\s\S]*?)(\r?\n)([ \t]+)(protected override void InitCallbacks\(\))', `
    '$1$2$3[RequiresUnreferencedCode("Generated serializer")]$2$3[RequiresDynamicCode("Generated serializer")]$2$3protected override void InitCallbacks()'

# Build the final file
$finalContent = @"
$header
#if NET5_0_OR_GREATER
#pragma warning disable // Generated code - suppress all warnings

using System.Diagnostics.CodeAnalysis;

// Suppress AOT/trim warnings for generated serializer code.
// The pre-generated serializer uses known types only - these are false positives.
[module: UnconditionalSuppressMessage("AOT", "IL3050", Scope = "module",
    Justification = "Pre-generated XmlSerializer code uses known types only.")]
[module: UnconditionalSuppressMessage("Trimming", "IL2026", Scope = "module",
    Justification = "Pre-generated XmlSerializer code uses known types only.")]

$rawCode

#pragma warning restore
#endif

"@

# Write the output
Set-Content -Path $outputFile -Value $finalContent -Encoding UTF8
Write-Host "       Written to: $outputFile" -ForegroundColor Green

# Cleanup
Remove-Item $sgenOutput -Recurse -Force -ErrorAction SilentlyContinue

$lineCount = (Get-Content $outputFile).Count
Write-Host ""
Write-Host "=== Done! Generated $lineCount lines ===" -ForegroundColor Cyan
Write-Host "Next steps:" -ForegroundColor DarkGray
Write-Host "  1. Review the diff in UANodeSetSerializers.g.cs" -ForegroundColor DarkGray
Write-Host "  2. Build the full solution to verify no regressions" -ForegroundColor DarkGray
Write-Host "  3. Publish and run AOT tests to verify NativeAOT compatibility" -ForegroundColor DarkGray
