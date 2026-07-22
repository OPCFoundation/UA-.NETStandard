#Requires -Version 7.0
<#
.SYNOPSIS
    Synchronizes / verifies the pinned xRegistry and WoT Connectivity 1.1
    NodeSet2 model artifacts against the authoring draft repository.

.DESCRIPTION
    The Opc.Ua.WotCon model assembly source-generates two NodeSet2 models
    copied ("pinned") from the OPC UA drafts authoring repository:

        core-specs/xregistry/Opc.Ua.XRegistry.NodeSet2.xml   -> Design/Opc.Ua.XRegistry.NodeSet2.xml
        core-specs/xregistry/Opc.Ua.XRegistry.NodeIds.csv    -> Design/Opc.Ua.XRegistry.NodeSet2.csv
        wot-specs/WoT-Connectivity/Opc.Ua.WoTCon.NodeSet2.xml -> Design/Opc.Ua.WoTCon.NodeSet2.xml
        wot-specs/WoT-Connectivity/Opc.Ua.WoTCon.NodeIds.csv  -> Design/Opc.Ua.WoTCon.NodeSet2.csv

    The combined Opc.Ua.WoTCon NodeSet2 is WoT Connectivity revision 1.1: it
    incorporates the complete published OPC 10100-1 v1.02 model (NodeIds
    1..172, marked deprecated) plus the additive registry nodes (64000+) in
    one namespace, http://opcfoundation.org/UA/WoT-Con/. The legacy 1.02
    ModelDesign sources (WotConnection.xml / WotConnection.csv) are retained
    here only as human-readable documentation of the incorporated surface;
    they are no longer source-generated (the combined NodeSet is the single
    generation input, so the 1.02 model is never generated a second time).

    The generator matches each *.NodeSet2.xml to a side-by-side *.NodeSet2.csv
    stable NodeId table, so the draft *.NodeIds.csv files are pinned under the
    *.NodeSet2.csv name.

    Use -Check (default) in CI / pre-commit to fail if the pinned copies have
    drifted from the draft repository. Use -Update to refresh the pinned copies
    after the draft models change (the draft repository must not be modified).

.PARAMETER DraftRepo
    Path to the checked-out opcua-drafts repository. Defaults to a sibling
    'opcua-drafts2' directory next to this repository's root.

.PARAMETER Update
    Copy the draft artifacts over the pinned copies instead of only verifying.

.EXAMPLE
    pwsh Sync-WotConModels.ps1 -Check

.EXAMPLE
    pwsh Sync-WotConModels.ps1 -Update -DraftRepo D:\git\marcschier\opcua-drafts2
#>
[CmdletBinding(DefaultParameterSetName = 'Check')]
param(
    [Parameter()]
    [string]$DraftRepo,

    [Parameter(ParameterSetName = 'Update')]
    [switch]$Update,

    [Parameter(ParameterSetName = 'Check')]
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$designDir = $PSScriptRoot
# repo root is <root>/src/Opc.Ua.WotCon/Design -> up three levels.
$repoRoot = (Resolve-Path (Join-Path $designDir '..' '..' '..')).Path

if (-not $DraftRepo) {
    $DraftRepo = Join-Path (Split-Path $repoRoot -Parent) 'opcua-drafts2'
}

# Mapping of draft-repo source -> pinned Design destination file name.
$map = @(
    @{ Source = 'core-specs/xregistry/Opc.Ua.XRegistry.NodeSet2.xml';        Dest = 'Opc.Ua.XRegistry.NodeSet2.xml' }
    @{ Source = 'core-specs/xregistry/Opc.Ua.XRegistry.NodeIds.csv';         Dest = 'Opc.Ua.XRegistry.NodeSet2.csv' }
    @{ Source = 'wot-specs/WoT-Connectivity/Opc.Ua.WoTCon.NodeSet2.xml';     Dest = 'Opc.Ua.WoTCon.NodeSet2.xml' }
    @{ Source = 'wot-specs/WoT-Connectivity/Opc.Ua.WoTCon.NodeIds.csv';      Dest = 'Opc.Ua.WoTCon.NodeSet2.csv' }
)

function Get-NormalizedHash([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    # Normalize CRLF/LF and a leading UTF-8 BOM so hashing is line-ending and
    # BOM agnostic (the draft repo and this repo may check out with different
    # git autocrlf settings).
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $bytes = $bytes[3..($bytes.Length - 1)]
    }
    $text = [System.Text.Encoding]::UTF8.GetString($bytes) -replace "`r`n", "`n"
    $norm = [System.Text.Encoding]::UTF8.GetBytes($text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString($sha.ComputeHash($norm)).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

$doUpdate = $Update.IsPresent
$drift = @()
$missingSource = @()

foreach ($entry in $map) {
    $src = Join-Path $DraftRepo $entry.Source
    $dst = Join-Path $designDir $entry.Dest

    if (-not (Test-Path -LiteralPath $src)) {
        $missingSource += $src
        continue
    }

    if ($doUpdate) {
        Copy-Item -LiteralPath $src -Destination $dst -Force
        Write-Host "Updated $($entry.Dest)"
        continue
    }

    $srcHash = Get-NormalizedHash $src
    $dstHash = Get-NormalizedHash $dst
    if ($srcHash -ne $dstHash) {
        $drift += $entry.Dest
        Write-Warning "DRIFT: $($entry.Dest) differs from draft $($entry.Source)"
    }
    else {
        Write-Host "OK:    $($entry.Dest)"
    }
}

if ($missingSource.Count -gt 0) {
    Write-Warning "Draft repository not found or incomplete under '$DraftRepo':"
    $missingSource | ForEach-Object { Write-Warning "  missing: $_" }
    Write-Warning "Pass -DraftRepo <path> to point at a checked-out opcua-drafts repository."
    exit 2
}

if ($doUpdate) {
    Write-Host "Model artifacts synchronized. Rebuild Opc.Ua.WotCon and re-run source generation."
    exit 0
}

if ($drift.Count -gt 0) {
    Write-Error "Pinned WoT model artifacts have drifted from the draft repository: $($drift -join ', '). Run this script with -Update to refresh them."
    exit 1
}

Write-Host "All pinned WoT model artifacts match the draft repository."
exit 0
