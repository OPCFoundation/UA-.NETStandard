<#
Lists fuzz targets dynamically from a built fuzz host assembly.
Usage: powershell -File Fuzzing\scripts\fuzz-menu.ps1 -AssemblyPath Fuzzing\Encoders\Fuzz\bin\Debug\net10.0\Encoders.Fuzz.dll
Pass -Filter to narrow target names and -Index to emit a selected target name without prompting.
Per-area libfuzz.* and aflfuzz.* wrappers can call this script to avoid hardcoded target menus.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,

    [string]$Filter = ".*",

    [int]$Index = 0
)

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$bindingFlags = [System.Reflection.BindingFlags] "Public, Static"
$targetType = $assembly.GetTypes() | Where-Object { $_.Name -eq "FuzzableCode" } | Select-Object -First 1

if ($null -eq $targetType) {
    throw "No FuzzableCode type found in $AssemblyPath."
}

$targets = @($targetType.GetMethods($bindingFlags) |
    Where-Object { $_.GetParameters().Count -eq 1 -and $_.Name -match $Filter } |
    Sort-Object Name)

for ($i = 0; $i -lt $targets.Count; $i++) {
    "{0,3}: {1}" -f ($i + 1), $targets[$i].Name
}

if ($Index -gt 0) {
    $targets[$Index - 1].Name
} elseif ($Host.Name -ne "Default Host") {
    $selection = [int](Read-Host "Select fuzz target")
    $targets[$selection - 1].Name
}
