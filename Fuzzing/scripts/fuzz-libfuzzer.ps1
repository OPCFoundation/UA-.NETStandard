param (
    [Parameter(Mandatory = $true)]
    [string]$libFuzzer,
    [Parameter(Mandatory = $true)]
    [string]$project,
    [Parameter(Mandatory = $true)]
    [string]$corpus,
    [Parameter(Mandatory = $true)]
    [string]$fuzztarget,
    [string]$temp = "./libfuzz/Testcases/",
    [string]$dict = $null,
    [int]$timeout = 10,
    [string]$command = "sharpfuzz"
)

Set-StrictMode -Version Latest

$outputDir = "bin"

if (Test-Path $outputDir) {
    Remove-Item -Recurse -Force $outputDir
}

dotnet.exe publish $project -p:LibFuzzer=true -c release --force -o $outputDir

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
    & $command $fuzzingTarget.FullName
    
    if ($LastExitCode -ne 0) {
        Write-Error "An error occurred while instrumenting $fuzzingTarget"
        exit 1
    }
}

if (Test-Path $temp) {
    Remove-Item -Recurse -Force $temp
}

New-Item -ItemType Directory -Force -Path "$temp"
Copy-Item -Path "$corpus/*.*" -Destination "$temp"

Write-Output "Start $libFuzzer"
if ($dict) {
    Write-Output "$libFuzzer -timeout=$timeout -dict=$dict --target_path=dotnet --target_arg=$project $fuzztarget $temp"
    & $libFuzzer -timeout="$timeout" -dict="$dict" --target_path=dotnet --target_arg="$project $fuzztarget" $temp
}
else {
    Write-Output "$libFuzzer -timeout=$timeout --target_path=dotnet --target_arg=$project $fuzztarget $temp"
    & $libFuzzer -timeout="$timeout" --target_path=dotnet --target_arg="$project $fuzztarget" $temp
}
