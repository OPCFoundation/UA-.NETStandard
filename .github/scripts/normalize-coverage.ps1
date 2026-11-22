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
Copies Cobertura fragments with non-negative line hits represented as coverage presence.

.DESCRIPTION
ReportGenerator adds line hits using Int32 arithmetic. Even valid individual
fragments can overflow when merged. Each copied line therefore records zero or
one; source paths, branch evidence and the raw fragments remain unchanged.
The output directory must be empty and must not overlap the input directory.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InputDirectory,
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).ProviderPath
if (-not (Test-Path -LiteralPath $inputRoot -PathType Container)) {
    throw "Coverage input is not a directory: $InputDirectory"
}
$outputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$separator = [IO.Path]::DirectorySeparatorChar
$inputPrefix = $inputRoot.TrimEnd($separator) + $separator
$outputPrefix = $outputRoot.TrimEnd($separator) + $separator
if ($inputRoot.Equals($outputRoot, $comparison) -or
    $outputPrefix.StartsWith($inputPrefix, $comparison) -or
    $inputPrefix.StartsWith($outputPrefix, $comparison)) {
    throw 'Coverage input and output directories must not overlap.'
}
if ((Test-Path -LiteralPath $outputRoot) -and
    (-not (Test-Path -LiteralPath $outputRoot -PathType Container) -or
        @(Get-ChildItem -LiteralPath $outputRoot -Force).Count -ne 0)) {
    throw 'Coverage output directory must be empty.'
}
$reports = @(Get-ChildItem -LiteralPath $inputRoot -Recurse -File -Filter '*.cobertura.xml')
if ($reports.Count -eq 0) {
    throw 'No Cobertura fragments were found.'
}

$lineCount = 0
foreach ($report in $reports) {
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.Load($report.FullName)
    if ($document.DocumentElement.Name -ne 'coverage') {
        throw "Not a Cobertura fragment: $($report.FullName)"
    }
    foreach ($line in $document.GetElementsByTagName('line')) {
        $raw = $line.GetAttribute('hits')
        $hits = [System.Numerics.BigInteger]::Zero
        if (-not [System.Numerics.BigInteger]::TryParse(
                $raw,
                [Globalization.NumberStyles]::Integer,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$hits) -or $hits -lt 0) {
            throw "Invalid non-negative coverage hit count '$raw' in '$($report.FullName)'."
        }
        $line.SetAttribute('hits', $(if ($hits -gt 0) { '1' } else { '0' }))
        $lineCount++
    }
    $relative = [IO.Path]::GetRelativePath($inputRoot, $report.FullName)
    $destination = Join-Path $outputRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    $document.Save($destination)
}
Write-Host "Normalized $lineCount line records in $($reports.Count) copied Cobertura fragments."
