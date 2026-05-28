<#
 .SYNOPSIS
    Creates a job-matrix variable for downstream pipeline jobs.

 .DESCRIPTION
    Discovers files matching a pattern in the repository (or uses an explicit
    caller-supplied list) and emits a pipeline output variable named
    "jobMatrix" containing one matrix entry per (file × agent × configuration)
    tuple. Either the agent or the configuration dimension may be left empty
    in which case the matrix is not fanned out across that dimension.

    Each matrix entry carries these variables:
       folder        - directory containing the file (relative to BuildRoot)
       fullFolder    - directory containing the file (absolute)
       file          - file path (relative to BuildRoot)
       agent         - agent key (only when -AgentTable is non-empty)
       poolImage     - vmImage for the agent (only when -AgentTable is non-empty)
       configuration - build configuration (only when -Configurations is non-empty)

 .PARAMETER BuildRoot
    The root folder to start traversing the repository from.

 .PARAMETER FileName
    File pattern to match defining the files in the matrix. Defaults to
    "Directory.Build.props". Ignored when -Files is supplied.

 .PARAMETER ExcludeFileName
    Optional file pattern to exclude from discovery.

 .PARAMETER JobPrefix
    Optional name prefix for each matrix key.

 .PARAMETER AgentTable
    Optional hashtable mapping agent name to vmImage. When non-empty, the
    matrix is fanned out across agents.

 .PARAMETER Configurations
    Optional comma-separated list of build configurations. When non-empty,
    the matrix is fanned out across configurations.

 .PARAMETER Files
    Optional comma-separated list of file paths (relative to BuildRoot).
    When supplied, recursive discovery via -FileName is skipped.
#>

Param(
    [string]    $BuildRoot       = $null,
    [string]    $FileName        = $null,
    [string]    $ExcludeFileName = $null,
    [string]    $JobPrefix       = '',
    [hashtable] $AgentTable      = $null,
    [string]    $Configurations  = '',
    [string]    $Files           = ''
)

if ([string]::IsNullOrEmpty($BuildRoot)) {
    $BuildRoot = & (Join-Path $PSScriptRoot 'get-root.ps1') -fileName '*.slnx'
}

if ([string]::IsNullOrEmpty($FileName)) {
    $FileName = 'Directory.Build.props'
}
if (![string]::IsNullOrEmpty($JobPrefix)) {
    $JobPrefix = "$($JobPrefix)-"
}

if ($null -eq $AgentTable) {
    # Caller did not supply an AgentTable - do not fan out across agents.
    $AgentTable = @{}
}
elseif ($AgentTable.Count -eq 0) {
    # Caller supplied an empty AgentTable - use the cross-platform defaults
    # (preserves the original get-matrix.ps1 behaviour for ci.yml callers).
    $AgentTable = @{
        windows = 'windows-2025-vs2026'
        linux   = 'ubuntu-22.04'
        mac     = 'macOS-15'
    }
}
$useAgents = $AgentTable.Count -gt 0

function Split-CsvList([string] $value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return @() }
    return $value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

$configList = Split-CsvList $Configurations
$useConfigs = $configList.Count -gt 0

$fileList = Split-CsvList $Files

$jobMatrix = [ordered]@{}

# Discover files when no explicit list was supplied.
if ($fileList.Count -gt 0) {
    Write-Host "Using caller-supplied file list: $($fileList -join ', ')"
    $buildRootFull = (Resolve-Path -Path $BuildRoot).Path
    $items = @()
    foreach ($rel in $fileList) {
        $full = Join-Path $buildRootFull $rel
        if (Test-Path $full -PathType Leaf) {
            $items += Get-Item -LiteralPath $full
        }
        else {
            Write-Warning "File not found: $rel"
        }
    }
}
else {
    Write-Host "Discovering files matching '$FileName' beneath '$BuildRoot'"
    $items = Get-ChildItem $BuildRoot -Recurse `
        | Where-Object Name -like $FileName `
        | Where-Object {
            if ([string]::IsNullOrEmpty($ExcludeFileName)) { return $true }
            $patterns = $ExcludeFileName -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
            foreach ($p in $patterns) {
                if ($_.Name -like $p) { return $false }
            }
            return $true
        }
}

foreach ($item in $items) {
    $fullFolder = $item.DirectoryName.Replace('\', '/')
    $folder     = $item.DirectoryName.Replace($BuildRoot, '').Replace('\', '/').TrimStart('/')
    $file       = $item.FullName.Replace($BuildRoot, '').Replace('\', '/').TrimStart('/')

    # Build the key prefix for this file.  Preserve the legacy shortcut that
    # treats a single yml file at the repository root as agent-only (so the
    # ci.yml caller continues to produce matrix keys like "windows"/"linux"
    # without a file-name prefix).  For every other file include the file
    # stem so that multiple files in the same folder don't collide.
    $postFix = ''
    if ([string]::IsNullOrEmpty($folder)) {
        if (-not $file.Contains('.yml')) {
            $postFix = [System.IO.Path]::GetFileNameWithoutExtension($file) + '_'
        }
    }
    else {
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($file)
        $postFix = $folder.Replace('/', '_') + '_' + $stem + '_'
    }

    if ($useAgents) {
        $agentKeys = @($AgentTable.Keys)
    }
    else {
        # Use a single sentinel iteration so the inner loop runs once.
        $agentKeys = @('')
    }
    if ($useConfigs) {
        $configKeys = $configList
    }
    else {
        $configKeys = @('')
    }

    foreach ($agentKey in $agentKeys) {
        foreach ($configuration in $configKeys) {
            $entry = @{
                folder     = $folder
                fullFolder = $fullFolder
                file       = $file
            }
            $jobName = "$($JobPrefix)$($postFix)"
            if ($useAgents) {
                $entry['agent']     = $agentKey
                $entry['poolImage'] = $AgentTable.Item($agentKey)
                $jobName += $agentKey
            }
            if ($useConfigs) {
                $entry['configuration'] = $configuration
                $jobName += "_$configuration"
            }
            # Matrix keys must be alphanumeric or underscore for Azure DevOps.
            $jobName = ($jobName -replace '[^A-Za-z0-9_]', '_')
            $jobName = ($jobName -replace '_+', '_').Trim('_')
            $jobMatrix.Add($jobName, $entry)
        }
    }
}

Write-Host ("Job matrix:`n" + ($jobMatrix | ConvertTo-Json -Depth 4))
Write-Host ("##vso[task.setVariable variable=jobMatrix;isOutput=true] {0}" `
    -f ($jobMatrix | ConvertTo-Json -Compress))
