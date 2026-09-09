param (
    [Parameter(Mandatory = $true)]
    [string]$libFuzzer,
    [Parameter(Mandatory = $true)]
    [string]$project,
    [Parameter(Mandatory = $true)]
    [string]$corpus,
    [Parameter(Mandatory = $true)]
    [string]$fuzztarget,
    [string]$temp,
    [string]$dict = $null,
    [ValidateRange(1, 3600)]
    [int]$timeout = 120,
    [ValidateRange(1, 2147483647)]
    [int]$runs = 10000,
    [ValidateRange(1, 86400)]
    [int]$maxTotalTime = 600,
    [ValidateRange(0, 2147483647)]
    [int]$seed = 1,
    [string]$command = "sharpfuzz"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$libFuzzer = (Get-Command $libFuzzer -ErrorAction Stop).Source
$command = (Get-Command $command -ErrorAction Stop).Source

$project = (Resolve-Path -LiteralPath $project).Path
$corpus = (Resolve-Path -LiteralPath $corpus).Path
if ($dict) { $dict = (Resolve-Path -LiteralPath $dict).Path }
$seedFiles = @(Get-ChildItem -LiteralPath $corpus -File -Recurse)
if ($seedFiles.Count -eq 0) { throw "Required corpus is empty: $corpus" }
$work = Join-Path ([IO.Path]::GetTempPath()) ("opcua-libfuzz-" + [guid]::NewGuid().ToString('N'))
$outputDir = Join-Path $work 'publish'
$null = New-Item -ItemType Directory -Path $work
if (-not $temp) { $temp = Join-Path $work 'corpus' }
if (Test-Path -LiteralPath $temp) { throw "Refusing to overwrite existing corpus: $temp" }
$null = New-Item -ItemType Directory -Path $temp
$temp = (Resolve-Path -LiteralPath $temp).Path
$env:CustomTestTarget = 'net10.0'

dotnet publish $project --no-restore -m:1 -p:FuzzCoverage=true -p:LibFuzzer=true -p:UseAppHost=true `
    -c Release -f net10.0 -o $outputDir
if ($LASTEXITCODE -ne 0) { throw "Publication failed ($LASTEXITCODE)." }

$projectName = (Get-Item $project).BaseName
$projectDll = "$projectName.dll"
$project = Join-Path $outputDir $projectDll
$available = @(& dotnet $project --list)
if ($LASTEXITCODE -ne 0 -or $fuzztarget -notin $available -or $fuzztarget -notlike 'Libfuzz*') {
    throw "Unknown libFuzzer callback: $fuzztarget"
}
$appHost = Join-Path $outputDir $projectName
if ([IO.Path]::DirectorySeparatorChar -eq '\') { $appHost += '.exe' }
if (-not (Test-Path -LiteralPath $appHost -PathType Leaf)) { throw "Missing native apphost: $appHost" }
# The driver accepts exactly one target_arg. Use the apphost, not "dotnet <dll> <target>".
$driverTarget = $appHost
if ([IO.Path]::DirectorySeparatorChar -eq '\') { $driverTarget = '"' + $appHost + '"' }

$fuzzingTargets = Get-ChildItem $outputDir -Filter *.dll `
| Where-Object { $_.Name -like "Opc.Ua.*.dll" -and $_.Name -ne $projectDll }

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

foreach ($seedFile in $seedFiles) {
    $hash = (Get-FileHash -LiteralPath $seedFile.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $seedFile.FullName -Destination (Join-Path $temp $hash) -Force
}

Write-Output "Retained campaign artifacts: $work"
Write-Output "Start $libFuzzer"
if ($dict) {
    & $libFuzzer -runs="$runs" -max_total_time="$maxTotalTime" -seed="$seed" -timeout="$timeout" -dict="$dict" `
        "-artifact_prefix=$work$([IO.Path]::DirectorySeparatorChar)" `
        "--target_path=$driverTarget" "--target_arg=$fuzztarget" $temp
}
else {
    & $libFuzzer -runs="$runs" -max_total_time="$maxTotalTime" -seed="$seed" -timeout="$timeout" `
        "-artifact_prefix=$work$([IO.Path]::DirectorySeparatorChar)" `
        "--target_path=$driverTarget" "--target_arg=$fuzztarget" $temp
}
if ($LASTEXITCODE -ne 0) { throw "Fuzz campaign failed ($LASTEXITCODE). Artifacts: $work" }
