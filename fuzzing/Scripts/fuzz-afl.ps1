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
$ErrorActionPreference = 'Stop'
$aflFuzz = (Get-Command 'afl-fuzz' -ErrorAction Stop).Source
$command = (Get-Command $command -ErrorAction Stop).Source

$project = (Resolve-Path -LiteralPath $project).Path
$i = (Resolve-Path -LiteralPath $i).Path
if ($x) { $x = (Resolve-Path -LiteralPath $x).Path }
if (@(Get-ChildItem -LiteralPath $i -File).Count -eq 0) { throw "Required corpus is empty: $i" }
$work = Join-Path ([IO.Path]::GetTempPath()) ("opcua-afl-" + [guid]::NewGuid().ToString('N'))
$outputDir = Join-Path $work 'publish'
$findingsDir = Join-Path $work 'findings'
$null = New-Item -ItemType Directory -Path $work
$env:CustomTestTarget = 'net10.0'
dotnet publish $project --no-restore -m:1 -p:FuzzCoverage=true -c Release -f net10.0 -o $outputDir
if ($LASTEXITCODE -ne 0) { throw "Publication failed ($LASTEXITCODE)." }

$projectName = (Get-Item $project).BaseName
$projectDll = "$projectName.dll"
$project = Join-Path $outputDir $projectDll
$available = @(& dotnet $project --list)
if ($LASTEXITCODE -ne 0 -or $fuzztarget -notin $available -or $fuzztarget -notlike 'Aflfuzz*') {
    throw "Unknown AFL callback: $fuzztarget"
}

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

$env:AFL_SKIP_BIN_CHECK = 1
Write-Output "Retained campaign artifacts: $work"

if ($x) {
    Write-Output "afl-fuzz -i $i -o $findingsDir -t $t -m none -x $x dotnet $project $fuzztarget"
    & $aflFuzz -i $i -o $findingsDir -t $t -m none -x $x dotnet $project $fuzztarget
}
else {
    Write-Output "afl-fuzz -i $i -o $findingsDir -t $t -m none dotnet $project $fuzztarget"
    & $aflFuzz -i $i -o $findingsDir -t $t -m none dotnet $project $fuzztarget
}
if ($LASTEXITCODE -ne 0) { throw "Fuzz campaign failed ($LASTEXITCODE). Artifacts: $work" }
