<#
Lists fuzz targets dynamically from a built fuzz host assembly.
Usage: powershell -File fuzzing\Scripts\fuzz-menu.ps1 -AssemblyPath fuzzing\Opc.Ua.Encoders.Fuzz\bin\Debug\net10.0\Opc.Ua.Encoders.Fuzz.dll
Pass -Filter to narrow target names and -Index to emit a selected target name without prompting.
Per-area libfuzz.* and aflfuzz.* wrappers can call this script to avoid hardcoded target menus.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,

    [string]$Filter = ".*",

    [int]$Index = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$listed = @(& dotnet $AssemblyPath --list)
if ($LASTEXITCODE -ne 0) { throw "Target discovery failed ($LASTEXITCODE)." }
$targets = @($listed | Where-Object { $_ -match $Filter } | Sort-Object)
if ($targets.Count -eq 0) { throw 'No supported targets match the filter.' }
if ($Index -lt 0 -or $Index -gt $targets.Count) { throw 'Target index is outside the menu.' }

if ($Index -gt 0) {
    $targets[$Index - 1]
    return
}

for ($i = 0; $i -lt $targets.Count; $i++) {
    "{0,3}: {1}" -f ($i + 1), $targets[$i]
}

if ($Host.Name -ne "Default Host") {
    $selection = [int](Read-Host "Select fuzz target")
    if ($selection -lt 1 -or $selection -gt $targets.Count) { throw 'Invalid target selection.' }
    $targets[$selection - 1]
}
