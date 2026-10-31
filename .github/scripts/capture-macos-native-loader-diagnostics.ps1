<#
 Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 OPC Foundation MIT License 1.00
#>

Param(
    [string] $ResultsDirectory = 'TestResults',
    [string] $ProjectName = 'Opc.Ua.Security.Pkcs11.Tests',
    [Parameter(Mandatory = $true)]
    [ValidateSet('control', 'trace')]
    [string] $Mode
)

$ErrorActionPreference = 'Stop'

$resultsPath = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$diagnosticsPath = Join-Path $resultsPath 'mac-diagnostics'
$projectResultsPath = Join-Path $resultsPath $ProjectName
$null = New-Item -ItemType Directory -Path $diagnosticsPath -Force

$metadata = [System.Collections.Generic.List[string]]::new()
$metadata.Add("mode=$Mode")
$metadata.Add("source_sha=$env:GITHUB_SHA")
$metadata.Add("source_ref=$env:GITHUB_REF")
$metadata.Add("run_id=$env:GITHUB_RUN_ID")
$metadata.Add("run_attempt=$env:GITHUB_RUN_ATTEMPT")
$metadata.Add("runner_image=$env:ImageOS $env:ImageVersion")
if ($IsMacOS) {
    $metadata.Add("macos_version=$(& sw_vers -productVersion)")
    $metadata.Add("architecture=$(& uname -m)")
}
else {
    $metadata.Add('macos_version=not running on macOS')
    $metadata.Add("architecture=$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)")
}
$metadata.Add("pkcs11_module=$env:OPCUA_PKCS11_MODULE")

$trxResults = [System.Collections.Generic.List[object]]::new()
if (Test-Path -LiteralPath $projectResultsPath -PathType Container) {
    foreach ($trxFile in Get-ChildItem -LiteralPath $projectResultsPath -Recurse -File -Filter '*.trx') {
        [xml] $trx = Get-Content -LiteralPath $trxFile.FullName -Raw
        foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
            $endTime = $result.GetAttribute('endTime')
            if (-not [string]::IsNullOrWhiteSpace($endTime)) {
                $trxResults.Add([pscustomobject]@{
                    EndTime = [DateTimeOffset]::Parse(
                        $endTime,
                        [System.Globalization.CultureInfo]::InvariantCulture)
                    TestName = $result.GetAttribute('testName')
                    Outcome = $result.GetAttribute('outcome')
                    Source = $trxFile.Name
                })
            }
        }
    }
}

$lastResult = $trxResults | Sort-Object -Property EndTime | Select-Object -Last 1
if ($null -eq $lastResult) {
    $metadata.Add('last_completed_test=not available; no completed TRX result')
}
else {
    $metadata.Add(
        "last_completed_test=$($lastResult.TestName); outcome=$($lastResult.Outcome); " +
        "end_time=$($lastResult.EndTime.ToString('O')); trx=$($lastResult.Source)")
    $metadata.Add('last_test_note=last completed result only; it does not establish crash causation')
}

$traceFiles = @()
if (Test-Path -LiteralPath $projectResultsPath -PathType Container) {
    $traceFiles = @(Get-ChildItem -LiteralPath $projectResultsPath -Recurse -File -Filter 'build-and-test.log')
}
$modulePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
if (-not [string]::IsNullOrWhiteSpace($env:OPCUA_PKCS11_MODULE)) {
    $null = $modulePaths.Add($env:OPCUA_PKCS11_MODULE)
}

$tracePathPattern = '(?<path>/(?:[^\s"():\[\]]+/)*[^\s"():\[\]]+\.(?:dylib|so)(?:\.\d+)*)'
if ($Mode -eq 'trace') {
    foreach ($traceFile in $traceFiles) {
        $metadata.Add("process_output=$($traceFile.FullName)")
        foreach ($line in Get-Content -LiteralPath $traceFile.FullName) {
            foreach ($match in [regex]::Matches($line, $tracePathPattern)) {
                $null = $modulePaths.Add($match.Groups['path'].Value)
            }
        }
    }
}
else {
    $metadata.Add('native_loader_trace=disabled for control')
}

$crashSummaries = [System.Collections.Generic.List[string]]::new()
$ipsFiles = @(Get-ChildItem -LiteralPath $diagnosticsPath -File -Filter '*.ips')
foreach ($ipsFile in $ipsFiles) {
    $ipsContent = Get-Content -LiteralPath $ipsFile.FullName -Raw
    $bodyStart = $ipsContent.IndexOf("`n")
    if ($bodyStart -lt 0) {
        throw "The crash report '$($ipsFile.Name)' has no JSON body."
    }
    $crash = $ipsContent.Substring($bodyStart + 1) | ConvertFrom-Json -AsHashtable
    $crashSummaries.Add("crash_report=$($ipsFile.Name)")
    $crashSummaries.Add("exception_type=$($crash.exception.type)")
    $crashSummaries.Add("exception_signal=$($crash.exception.signal)")
    $crashSummaries.Add("exception_subtype=$($crash.exception.subtype)")

    $rawCodes = @($crash.exception.rawCodes)
    if ($rawCodes.Count -gt 1) {
        $faultAddress = ([UInt64]$rawCodes[1]).ToString('X')
        $crashSummaries.Add("fault_pc=0x$faultAddress")
    }

    $threadIndex = [int]$crash.faultingThread
    $threads = @($crash.threads)
    if ($threadIndex -lt 0 -or $threadIndex -ge $threads.Count) {
        $crashSummaries.Add("faulting_thread_index=$threadIndex; thread record unavailable")
        continue
    }

    $thread = $threads[$threadIndex]
    $crashSummaries.Add("faulting_thread_index=$threadIndex")
    $crashSummaries.Add("faulting_thread_id=$($thread.id)")
    $crashSummaries.Add("faulting_thread_queue=$($thread.queue)")
    $images = @($crash.usedImages)
    foreach ($image in $images) {
        if (-not [string]::IsNullOrWhiteSpace($image.path)) {
            $null = $modulePaths.Add($image.path)
        }
    }
    foreach ($frame in @($thread.frames)) {
        $imageDescription = 'image unavailable'
        if ($frame.ContainsKey('imageIndex')) {
            $imageIndex = [int]$frame.imageIndex
            if ($imageIndex -ge 0 -and $imageIndex -lt $images.Count) {
                $image = $images[$imageIndex]
                $imageDescription = "image_index=$imageIndex; image=$($image.name); path=$($image.path)"
                if (-not [string]::IsNullOrWhiteSpace($image.path)) {
                    $null = $modulePaths.Add($image.path)
                }
            }
        }
        $crashSummaries.Add(
            "native_frame image_offset=$($frame.imageOffset); symbol=$($frame.symbol); $imageDescription")
    }
}

$metadataPath = Join-Path $diagnosticsPath 'run-metadata.txt'
$metadata | Out-File -LiteralPath $metadataPath -Encoding utf8

$crashSummaryPath = Join-Path $diagnosticsPath 'native-crash-summary.txt'
if ($crashSummaries.Count -eq 0) {
    'No recent macOS crash report was staged.' |
        Out-File -LiteralPath $crashSummaryPath -Encoding utf8
}
else {
    $crashSummaries | Out-File -LiteralPath $crashSummaryPath -Encoding utf8
}

$identityLines = [System.Collections.Generic.List[string]]::new()
$identityLines.Add("mode=$Mode")
$identityLines.Add("module_candidate_count=$($modulePaths.Count)")
foreach ($modulePath in $modulePaths) {
    $identityLines.Add("path=$modulePath")
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        $identityLines.Add('file_status=not present on this runner; raw trace or IPS path retained')
        continue
    }

    if ($IsMacOS) {
        $hashOutput = & shasum -a 256 -- $modulePath 2>&1 | Out-String
        $hashExitCode = $LASTEXITCODE
        if ($hashExitCode -ne 0) {
            throw "Could not hash native module '$modulePath': $hashOutput"
        }
        $identityLines.Add("sha256=$($hashOutput.Trim())")

        $uuidOutput = & dwarfdump --uuid $modulePath 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            $identityLines.Add("uuid=$($uuidOutput.Trim())")
        }
        else {
            $identityLines.Add("uuid_unavailable=$($uuidOutput.Trim())")
        }

        $fileOutput = & file -b $modulePath 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            $identityLines.Add("file_identity=$($fileOutput.Trim())")
        }
        else {
            $identityLines.Add("file_identity_unavailable=$($fileOutput.Trim())")
        }
    }
    else {
        $identity = Get-FileHash -LiteralPath $modulePath -Algorithm SHA256
        $identityLines.Add("sha256=$($identity.Hash)")
        $identityLines.Add('uuid_and_architecture=available only on macOS')
    }
}

$identityPath = Join-Path $diagnosticsPath 'module-identities.txt'
$identityLines | Out-File -LiteralPath $identityPath -Encoding utf8

Write-Host "Wrote native diagnostic metadata to '$metadataPath'."
Write-Host "Wrote module identities to '$identityPath'."
Write-Host "Wrote native crash summary to '$crashSummaryPath'."
