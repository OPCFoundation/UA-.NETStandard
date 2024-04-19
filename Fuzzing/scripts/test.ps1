New-Item -Path "corpus/test" -ItemType File -Force -Value "W"

dotnet publish src/SharpFuzz.CommandLine/SharpFuzz.CommandLine.csproj `
    --output out `
    --configuration release `
    --framework net8.0

& scripts/fuzz.ps1 `
    -project tests/Library.Fuzz/Library.Fuzz.csproj `
    -i corpus `
    -command out/SharpFuzz.CommandLine

$output = Get-Content -Path "./findings/.cur_input" -Raw
$crasher = "Whoopsie"

if (-not $output.Contains($crasher)) {
    Write-Error "Crasher is missing from the AFL output"
    exit 1
}

Write-Host $crasher
exit 0
