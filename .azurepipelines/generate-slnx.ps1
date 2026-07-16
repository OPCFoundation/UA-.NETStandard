<#
.SYNOPSIS
    Generates an on-demand pack solution (.slnx) by deriving it from
    UA.slnx with the test and fuzzing folders stripped out.

.DESCRIPTION
    Replaces the static `UA Core Library.slnx`.  The single source of
    truth for the project graph is `UA.slnx`; this script reads it,
    drops every folder whose name starts with `/tests/` or
    `/fuzzing/` (and every project inside those folders), and writes
    the result.

    Whether each remaining project actually produces a NuGet on
    `dotnet pack` is governed by its own `<IsPackable>` element
    (default true; sample EXEs and hidden build sub-components set
    it to false).  Adding a new csproj only requires adding it to
    `UA.slnx` and - if it's not a test/fuzz - it automatically flows
    into the preview build.

    Both `.azurepipelines/preview.yml` and
    `.github/workflows/preview-publish.yml` invoke this script before
    `dotnet restore` / `build` / `pack`.

.PARAMETER OutputPath
    The .slnx file to write.  Default: `<repo>/preview-pack.slnx`
    (gitignored - the file is treated as a build artifact).

.PARAMETER SourceSolution
    The full-graph .slnx to derive from.  Default: `<repo>/UA.slnx`.

.PARAMETER ExcludeFolderPrefixes
    Folder-name prefixes (as they appear inside the source .slnx) to
    drop entirely.  Default: `/tests/`, `/fuzzing/`, `/Solution Items/`.
    Folder matching is whole-name based on the `Name=` attribute, so
    `/Solution Items/` and any `/Solution Items/...` sub-folder
    matches the prefix.

.NOTES
    The script resolves the repo root from `$PSScriptRoot/..` so it
    can run from any working directory.
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$SourceSolution,
    [string[]]$ExcludeFolderPrefixes = @('/tests/', '/fuzzing/', '/Solution Items/')
)

$ErrorActionPreference = 'Stop'

# Resolve repo root from script location so callers don't need to be
# in any particular working directory.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $SourceSolution) {
    $SourceSolution = Join-Path $repoRoot 'UA.slnx'
}
if (-not (Test-Path $SourceSolution)) {
    throw "Source solution not found: $SourceSolution"
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'preview-pack.slnx'
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

# Parse the source .slnx as XML.  The .slnx format is intentionally
# small (folders, projects, files) and stable, so direct DOM walking
# is simpler than introducing a third-party parser.
[xml]$source = Get-Content -Raw -LiteralPath $SourceSolution

# Build a fresh DOM for the output so we keep only the bits we want
# and produce a clean diff against any committed reference copy.
$output = New-Object System.Xml.XmlDocument
$rootOut = $output.CreateElement('Solution')
[void]$output.AppendChild($rootOut)

function Should-ExcludeFolder([string]$name) {
    foreach ($prefix in $ExcludeFolderPrefixes) {
        if ($name -like ($prefix + '*') -or $name -eq $prefix.TrimEnd('/')) {
            return $true
        }
    }
    return $false
}

$keptFolderCount = 0
$keptProjectCount = 0
$droppedFolderCount = 0
$droppedProjectCount = 0

foreach ($folder in $source.Solution.Folder) {
    $folderName = $folder.Name
    if (Should-ExcludeFolder $folderName) {
        $droppedFolderCount++
        # Count projects we dropped just for the summary.
        if ($folder.Project) {
            $droppedProjectCount += @($folder.Project).Count
        }
        continue
    }

    # Build the folder element in the output document.
    $folderOut = $output.CreateElement('Folder')
    $folderOut.SetAttribute('Name', $folderName)

    # Carry over child <Project> entries.  Skip <File> entries
    # (solution-items style metadata is irrelevant to the pack
    # build); skip the folder entirely if it ends up empty.
    $hasProject = $false
    if ($folder.Project) {
        foreach ($project in $folder.Project) {
            $projOut = $output.CreateElement('Project')
            $projOut.SetAttribute('Path', $project.Path)
            [void]$folderOut.AppendChild($projOut)
            $hasProject = $true
            $keptProjectCount++
        }
    }
    if ($hasProject) {
        [void]$rootOut.AppendChild($folderOut)
        $keptFolderCount++
    }
}

# Pretty-print: indented, UTF-8 without BOM, no XML declaration
# (matches the surviving .slnx format conventions in this repo).
$xmlSettings = New-Object System.Xml.XmlWriterSettings
$xmlSettings.Indent = $true
$xmlSettings.IndentChars = '  '
$xmlSettings.OmitXmlDeclaration = $true
$xmlSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
$xmlSettings.NewLineChars = "`r`n"

$writer = [System.Xml.XmlWriter]::Create($OutputPath, $xmlSettings)
try {
    $output.Save($writer)
} finally {
    $writer.Dispose()
}

Write-Host "Generated $OutputPath from $SourceSolution"
Write-Host "  Kept:    $keptFolderCount folder(s), $keptProjectCount project(s)"
Write-Host "  Dropped: $droppedFolderCount folder(s), $droppedProjectCount project(s)"
Write-Host "  Excluded folder prefixes: $($ExcludeFolderPrefixes -join ', ')"

# Emit the absolute path on stdout so callers can capture it via
# command substitution if desired.  The console host gets the
# Write-Host messages above; only this single Write-Output ends up
# on the pipeline.
Write-Output $OutputPath
