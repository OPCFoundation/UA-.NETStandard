# Copyright (c) OPC Foundation. Licensed under the MIT License.
<#
.SYNOPSIS
Reads actual TRX (including MTP's TRX report) or SARIF without exposing raw results.
.DESCRIPTION
Exit zero in collection mode; -Enforce requires completed execution. MTP console
output, successful process exit, and a SARIF file without successful invocations
are not result proof. SARIF completion is not a finding disposition or proof of
the evaluated solution scope. No raw names, findings, input data or logs are emitted.
#>
param(
    [Parameter(Mandatory)][string] $ResultsPath,
    [ValidateSet('trx', 'mtp-trx', 'sarif', 'fuzz-replay')][string] $Kind = 'trx',
    [Parameter(Mandatory)][string] $OutputPath,
    [switch] $RequireNoSkipped,
    [switch] $Enforce
)
$ErrorActionPreference = 'Stop'
$summary = [ordered]@{ schemaVersion = 1; kind = $Kind; status = 'missing'; documents = @() }
$extension = if ($Kind -eq 'sarif') { '*.sarif' } else { '*.trx' }
$files = @()
$skippedNames = @()
if (Test-Path -LiteralPath $ResultsPath -PathType Container) {
    $files = @(Get-ChildItem -LiteralPath $ResultsPath -File -Recurse -Filter $extension)
}
elseif (Test-Path -LiteralPath $ResultsPath -PathType Leaf) {
    $files = @(Get-Item -LiteralPath $ResultsPath)
}

function Read-Count($node, [string] $name, [switch] $Required) {
    $value = $node.GetAttribute($name)
    if (-not $value -and -not $Required) { return 0L }
    $count = 0L
    if (-not [long]::TryParse($value, [ref] $count) -or $count -lt 0) { throw 'Invalid result counter.' }
    return $count
}

if ($files.Count -gt 0) {
    $summary.status = 'completed'
    $counts = [ordered]@{ total = 0L; executed = 0L; passed = 0L; failed = 0L; skipped = 0L }
    if ($Kind -eq 'sarif') { $counts = [ordered]@{ findings = 0L } }
    try {
        foreach ($file in $files) {
            $summary.documents += [ordered]@{
                digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            if ($Kind -eq 'sarif') {
                $sarif = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                if ($sarif.version -ne '2.1.0' -or @($sarif.runs).Count -eq 0) { throw 'Invalid SARIF.' }
                foreach ($run in $sarif.runs) {
                    if ($run.tool.driver.name -cne 'CodeQL' -or @($run.invocations).Count -eq 0) {
                        throw 'Missing analysis invocation.'
                    }
                    foreach ($invocation in $run.invocations) {
                        if ($invocation.executionSuccessful -isnot [bool] -or -not $invocation.executionSuccessful) {
                            throw 'Analysis did not complete.'
                        }
                    }
                    $counts.findings += @($run.results | Where-Object { $null -ne $_ }).Count
                }
                continue
            }
            $settings = [System.Xml.XmlReaderSettings]::new()
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $reader = [System.Xml.XmlReader]::Create($file.FullName, $settings)
            try {
                $document = [System.Xml.XmlDocument]::new()
                $document.XmlResolver = $null
                $document.Load($reader)
            }
            finally { $reader.Dispose() }
            $ns = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
            $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
            $runSummary = $document.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
            $counter = $document.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $ns)
            if ($null -eq $counter -or $null -eq $runSummary) { throw 'Missing TRX counters.' }
            if ($runSummary.GetAttribute('outcome') -notin @('Completed', 'Passed')) {
                throw 'Unsuccessful run outcome.'
            }
            $total = Read-Count $counter 'total' -Required
            $executed = Read-Count $counter 'executed' -Required
            $passed = Read-Count $counter 'passed' -Required
            $failed = Read-Count $counter 'failed' -Required
            $skipped = Read-Count $counter 'notExecuted'
            $other = 0L
            foreach ($name in @('error', 'timeout', 'aborted', 'passedButRunAborted', 'disconnected', 'warning', 'notRunnable', 'inconclusive')) {
                $other += Read-Count $counter $name
            }
            $results = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
            $actualPassed = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Passed' }).Count
            $actualSkipped = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'NotExecuted' }).Count
            $skippedNames += @($results | Where-Object { $_.GetAttribute('outcome') -eq 'NotExecuted' } |
                ForEach-Object { $_.GetAttribute('testName') })
            $counts.total += $total
            $counts.executed += $executed
            $counts.passed += $passed
            $counts.failed += $failed + $other
            $counts.skipped += $skipped
            if ($total -le 0 -or $executed -le 0 -or $passed -le 0 -or
                $failed + $other -ne 0 -or $executed -ne $passed -or
                $total -ne $executed + $skipped -or $results.Count -ne $total -or
                $actualPassed -ne $passed -or $actualSkipped -ne $skipped -or
                ($RequireNoSkipped -and $skipped -gt 0)) {
                $summary.status = 'failed'
            }
        }
        $summary.counts = $counts
        if ($Kind -eq 'fuzz-replay') {
            $beforePath = Join-Path $ResultsPath 'public-inputs.json'
            $copyPath = Join-Path $ResultsPath 'copied-inputs.json'
            $replayPath = Join-Path $ResultsPath 'fuzz-replay.xml'
            $before = Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json
            $copy = Get-Content -LiteralPath $copyPath -Raw | ConvertFrom-Json
            if ($before.schemaVersion -ne 1 -or $copy.schemaVersion -ne 1 -or
                $before.inventoryDigest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
                $before.inventoryDigest -cne $copy.inventoryDigest -or
                $before.goodInputs -ne @($before.inputs | Where-Object category -eq good).Count -or
                $before.regressionInputs -ne @($before.inputs | Where-Object category -ne good).Count -or
                $copy.goodInputs -ne $before.goodInputs -or $copy.regressionInputs -ne $before.regressionInputs) {
                throw 'Changed replay inventory.'
            }
            $settings = [Xml.XmlReaderSettings]::new()
            $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $reader = [Xml.XmlReader]::Create($replayPath, $settings)
            try {
                $xml = [Xml.XmlDocument]::new()
                $xml.XmlResolver = $null
                $xml.Load($reader)
            }
            finally { $reader.Dispose() }
            if ($xml.replay.schemaVersion -ne '1') { throw 'Unknown replay format.' }
            $targets = @($xml.SelectNodes('/replay/targets/target') | ForEach-Object { $_.GetAttribute('id') })
            $inputs = @($xml.SelectNodes('/replay/inputs/input'))
            if ($targets.Count -eq 0 -or ($targets | Select-Object -Unique).Count -ne $targets.Count -or
                @($before.inputs | Where-Object category -eq good).Count -eq 0 -or
                $inputs.Count -ne @($before.inputs).Count -or
                @($copy.inputs).Count -ne $inputs.Count) { throw 'Incomplete frozen replay scope.' }
            $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $inputIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($input in $inputs) {
                $id = $input.GetAttribute('id')
                $digest = $input.GetAttribute('digest')
                $category = $input.GetAttribute('category')
                if ($id -notmatch '^sha256:[0-9a-f]{64}$' -or $digest -notmatch '^sha256:[0-9a-f]{64}$' -or
                    $category -notin @('good', 'crash', 'timeout', 'slow') -or -not $inputIds.Add($id) -or
                    @($before.inputs | Where-Object { $_.id -ceq $id -and $_.digest -ceq $digest -and $_.category -ceq $category }).Count -ne 1 -or
                    @($copy.inputs | Where-Object { $_.id -ceq $id -and $_.digest -ceq $digest -and $_.category -ceq $category }).Count -ne 1) {
                    throw 'Replay input identity mismatch.'
                }
                foreach ($target in $targets) {
                    if ($target -notmatch '^sha256:[0-9a-f]{64}$') { throw 'Invalid target identity.' }
                    $null = $expected.Add("$target|$id")
                }
            }
            $executions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($execution in $xml.SelectNodes('/replay/executions/execution')) {
                if (-not $executions.Add($execution.GetAttribute('target') + '|' + $execution.GetAttribute('input'))) {
                    throw 'Duplicate execution.'
                }
            }
            if (-not $expected.SetEquals($executions)) { throw 'Incomplete target/input execution.' }
            foreach ($name in $skippedNames) {
                $category = switch -Regex ($name) {
                    '^FuzzTimeoutAssets(\(.*\))?$' { 'timeout'; break }
                    '^FuzzSlowAssets(\(.*\))?$' { 'slow'; break }
                    default { throw 'Unexplained skipped test.' }
                }
                if (@($before.inputs | Where-Object category -eq $category).Count -ne 0) {
                    throw 'Skipped known regression input.'
                }
            }
            $summary.counts.expectedTargets = $targets.Count
            $summary.counts.executedTargets = $targets.Count
            $summary.counts.expectedInputs = $inputs.Count
            $summary.counts.executedInputs = $inputs.Count
            $summary.replay = @{
                inventoryDigest = $before.inventoryDigest
                targetDigest = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
                    [Text.Encoding]::UTF8.GetBytes(($targets | Sort-Object -CaseSensitive) -join "`n"))).ToLowerInvariant()
                executionDigest = 'sha256:' + (Get-FileHash -LiteralPath $replayPath -Algorithm SHA256).Hash.ToLowerInvariant()
                expectedPairs = $expected.Count; executedPairs = $executions.Count
                allowedEmptyRegressionSkips = $skippedNames.Count
            }
        }
    }
    catch {
        # Exceptions may embed source snippets or private testcase data.
        $summary.status = 'failed'
        Write-Host 'Result proof rejected: invalid, incomplete, or inconsistent execution evidence.'
    }
}
$parent = Split-Path -Parent $OutputPath
if ($parent) { $null = New-Item -ItemType Directory -Path $parent -Force }
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Result proof: kind=$Kind status=$($summary.status) documents=$($files.Count)"
if ($Enforce -and $summary.status -ne 'completed') { exit 1 }
exit 0
