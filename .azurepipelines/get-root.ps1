<#
 .SYNOPSIS
    find the top most folder with file in it and return the path

 .DESCRIPTION
    Generic functionality needed to find a root.
#>
param(
    [string] $startDir,
    [string] $fileName
)

if ([string]::IsNullOrEmpty($startDir)) {
    $startDir = $PSScriptRoot
}

$cur = $startDir
while (![string]::IsNullOrEmpty($cur)) {
    $test = Join-Path $cur $fileName
    if (Test-Path -Path $test -PathType Any) {
        return $cur
    }
    $cur = Split-Path $cur
}
return $startDir
