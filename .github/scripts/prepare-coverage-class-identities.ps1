# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included in
# all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
# IN THE SOFTWARE.

<#
.SYNOPSIS
Protects partial-type file identities while merging copied Cobertura fragments.

.DESCRIPTION
ReportGenerator matches files in a partial class by basename. Distinct files
with the same basename therefore need distinct temporary class identities.
Only class names in the supplied copies change; paths, sources, methods, line
visits and branch evidence remain intact. Restore original class names in the
merged Cobertura report before publishing it or evaluating coverage gates.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InputDirectory,
    [string] $RestoreReport
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $InputDirectory).ProviderPath
$manifestPath = Join-Path $root 'coverage-class-identities.json'
$marker = '__UaCoverageFile_'

function Get-ClassKey([string] $package, [string] $name) {
    return "$package`n$name"
}

function Get-ClassRoot([string] $name) {
    $parent = ($name -split '/', 2)[0]
    return [regex]::Replace($parent, '.<.*>\w_?_?\w*\d*', '')
}

function Get-FileKey([string] $package, [string] $path, [string[]] $sources) {
    $normalized = '/' + $path.Replace('\', '/').TrimStart('/')
    $project = "/$package/"
    $index = $normalized.LastIndexOf($project, [StringComparison]::Ordinal)
    if ($index -ge 0) {
        return $normalized.Substring($index + 1)
    }
    $resolved = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $sources) {
        $candidate = $source.Replace('\', '/').TrimEnd('/') + $normalized
        $index = $candidate.LastIndexOf($project, [StringComparison]::Ordinal)
        if ($index -ge 0) {
            $null = $resolved.Add($candidate.Substring($index + 1))
        }
    }
    if ($resolved.Count -gt 1) {
        throw "Ambiguous coverage source roots for file: $path"
    }
    foreach ($file in $resolved) {
        return $file
    }
    return $normalized
}

function Read-CoverageDocument([string] $path) {
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.Load($path)
    if ($document.DocumentElement.Name -ne 'coverage') {
        throw "Not a Cobertura fragment: $path"
    }
    return ,$document
}

if ($RestoreReport) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'Coverage class-identity manifest is missing.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $aliases = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $manifest) {
        $aliases.Add((Get-ClassKey $entry.Package $entry.Alias), $entry.Name)
    }
    $path = (Resolve-Path -LiteralPath $RestoreReport).ProviderPath
    $document = Read-CoverageDocument $path
    foreach ($package in $document.SelectNodes('/coverage/packages/package')) {
        foreach ($class in $package.SelectNodes('classes/class')) {
            $name = $class.GetAttribute('name')
            $original = ''
            if ($aliases.TryGetValue((Get-ClassKey $package.GetAttribute('name') $name), [ref]$original)) {
                $class.SetAttribute('name', $original)
            }
            elseif ($name.Contains($marker, [StringComparison]::Ordinal)) {
                throw "Unclassified coverage class identity: $name"
            }
        }
    }
    $document.Save($path)
    foreach ($html in Get-ChildItem -LiteralPath (Split-Path -Parent $path) -File |
        Where-Object Extension -In '.html', '.htm') {
        $text = [IO.File]::ReadAllText($html.FullName)
        foreach ($entry in $manifest) {
            $text = $text.Replace(
                [Net.WebUtility]::HtmlEncode($entry.Alias),
                [Net.WebUtility]::HtmlEncode($entry.Name),
                [StringComparison]::Ordinal)
        }
        [IO.File]::WriteAllText($html.FullName, $text, [Text.UTF8Encoding]::new($false))
    }
    Write-Host 'Restored original class names in the merged Cobertura report.'
    return
}

if (Test-Path -LiteralPath $manifestPath) {
    throw 'Coverage class identities have already been prepared.'
}
$reports = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.cobertura.xml')
if ($reports.Count -eq 0) {
    throw 'No Cobertura fragments were found.'
}
$filesByClass = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[string]]]::new(
    [StringComparer]::Ordinal)
$settings = [Xml.XmlReaderSettings]::new()
$settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
$settings.XmlResolver = $null
foreach ($report in $reports) {
    $reader = [Xml.XmlReader]::Create($report.FullName, $settings)
    try {
        $package = ''
        $sources = [Collections.Generic.List[string]]::new()
        $positioned = $false
        while ($positioned -or $reader.Read()) {
            $positioned = $false
            if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) {
                continue
            }
            if ($reader.Name -eq 'source') {
                $sources.Add($reader.ReadElementContentAsString())
                $positioned = $true
            }
            elseif ($reader.Name -eq 'package') {
                $package = $reader.GetAttribute('name')
            }
            elseif ($reader.Name -eq 'class') {
                $name = $reader.GetAttribute('name')
                if ($name.Contains($marker, [StringComparison]::Ordinal)) {
                    throw "Raw coverage class uses a reserved identity: $name"
                }
                $key = Get-ClassKey $package (Get-ClassRoot $name)
                $files = $null
                if (-not $filesByClass.TryGetValue($key, [ref]$files)) {
                    $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                    $filesByClass.Add($key, $files)
                }
                $null = $files.Add((Get-FileKey $package $reader.GetAttribute('filename') $sources.ToArray()))
                $reader.Skip()
                $positioned = $true
            }
        }
    }
    finally {
        $reader.Dispose()
    }
}
$collisions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $filesByClass.GetEnumerator()) {
    $basenames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $entry.Value) {
        if (-not $basenames.Add(($file -split '/')[-1])) {
            $null = $collisions.Add($entry.Key)
        }
    }
}
if ($collisions.Count -eq 0) {
    '[]' | Set-Content -LiteralPath $manifestPath
    Write-Host 'No partial class file identities require protection.'
    return
}
$aliases = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    foreach ($report in $reports) {
        $document = Read-CoverageDocument $report.FullName
        $sources = @($document.SelectNodes('/coverage/sources/source') | ForEach-Object InnerText)
        $changed = $false
        foreach ($package in $document.SelectNodes('/coverage/packages/package')) {
            $packageName = $package.GetAttribute('name')
            foreach ($class in $package.SelectNodes('classes/class')) {
                $name = $class.GetAttribute('name')
                $classRoot = Get-ClassRoot $name
                if (-not $collisions.Contains((Get-ClassKey $packageName $classRoot))) {
                    continue
                }
                if ($classRoot.Contains('<') -or $classRoot.Contains('$')) {
                    throw "Unsupported generated partial coverage class identity: $name"
                }
                $file = Get-FileKey $packageName $class.GetAttribute('filename') $sources
                $hash = [Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($file)))
                $alias = $classRoot + $marker + $hash
                $key = Get-ClassKey $packageName $alias
                if (-not $aliases.ContainsKey($key)) {
                    $aliases.Add($key, [pscustomobject]@{
                        Package = $packageName; Name = $classRoot; Alias = $alias; File = $file
                    })
                }
                $class.SetAttribute('name', $alias + $name.Substring($classRoot.Length))
                $changed = $true
            }
        }
        if ($changed) {
            $document.Save($report.FullName)
        }
    }
}
finally {
    $sha.Dispose()
}
ConvertTo-Json -InputObject @($aliases.Values) -Depth 3 | Set-Content -LiteralPath $manifestPath
Write-Host "Protected $($collisions.Count) partial class identities with $($aliases.Count) file-qualified names."
