param (
    [Parameter(Mandatory = $true)]
    [string]$project,
    [Parameter(Mandatory = $true)]
    [string]$i,
    [Parameter(Mandatory = $true)]
    [string]$fuzztarget,
    [string]$x = $null,
    [int]$t = 10000,
    [string]$command = "sharpfuzz"
)

Set-StrictMode -Version Latest

$outputDir = "bin"
$findingsDir = "findings"

if (Test-Path $outputDir) { 
    Remove-Item -Recurse -Force $outputDir 
}

if (Test-Path $findingsDir) {
    Remove-Item -Recurse -Force $findingsDir 
}

dotnet publish $project --force -c release -o $outputDir

$projectName = (Get-Item $project).BaseName
$projectDll = "$projectName.dll"
$project = Join-Path $outputDir $projectDll

$exclusions = @(
    "dnlib.dll",
    "SharpFuzz.dll",
    "SharpFuzz.Common.dll",
    $projectDll
)

$fuzzingTargets = Get-ChildItem $outputDir -Filter *.dll `
| Where-Object { $_.Name -notin $exclusions } `
| Where-Object { $_.Name -notlike "System.*.dll" }

if (($fuzzingTargets | Measure-Object).Count -eq 0) {
    Write-Error "No fuzzing targets found"
    exit 1
}

foreach ($fuzzingTarget in $fuzzingTargets) {
    Write-Output "Instrumenting $fuzzingTarget"
    & $command $fuzzingTarget
    
    if ($LastExitCode -ne 0) {
        Write-Error "An error occurred while instrumenting $fuzzingTarget"
        exit 1
    }
}

$env:AFL_SKIP_BIN_CHECK = 1

if ($x) {
    Write-Output "afl-fuzz -i $i -o $findingsDir -t $t -m none -x $x dotnet $project $fuzztarget"
    afl-fuzz -i $i -o $findingsDir -t $t -m none -x $x dotnet $project $fuzztarget
}
else {
    Write-Output "afl-fuzz -i $i -o $findingsDir -t $t -m none dotnet $project $fuzztarget"
    afl-fuzz -i $i -o $findingsDir -t $t -m none dotnet $project $fuzztarget
}
