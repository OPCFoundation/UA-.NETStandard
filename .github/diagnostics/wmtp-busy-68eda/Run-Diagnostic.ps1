param()

$ErrorActionPreference = 'Stop'
$sourceSha = '68eda7f6e84baf6399f37d5da228fb950115fc42'
$parentSha = 'c3701e5804e0c9b9691f8ec7b1fff4fc538d901c'
$ownerSha = 'ab3126f323ef949d816904b3daeb25fce7b8703b'
$source = Join-Path $env:GITHUB_WORKSPACE 'source'
$definition = Join-Path $env:GITHUB_WORKSPACE 'definition'
$root = Join-Path $env:RUNNER_TEMP 'wmtp-busy-68eda'
$raw = Join-Path $root 'raw'
$evidence = Join-Path $env:RUNNER_TEMP 'wmtp-busy-68eda-evidence'
$hook = Join-Path $root 'hook'
$observations = Join-Path $evidence 'observations'
$filter = 'FullyQualifiedName~Opc.Ua.Tools.Tests.WmtpMcpIntegrationTests&TestCategory!=LongRunning&TestCategory!=Stress'
$metadata = [ordered]@{
    originalRun = 38910474899
    originalAttempt = 1
    originalJob = 117010602393
    expectedSource = $sourceSha
    expectedParents = @($parentSha, $ownerSha)
    definitionSha = $env:DIAGNOSTIC_DEFINITION_SHA
    workflowSha = $env:GITHUB_SHA
    runId = $env:GITHUB_RUN_ID
    runAttempt = $env:GITHUB_RUN_ATTEMPT
    runnerOS = $env:RUNNER_OS
    runnerArchitecture = $env:RUNNER_ARCH
    osDescription = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    imageOS = $env:ImageOS
    imageVersion = $env:ImageVersion
    filter = $filter
    coverage = $true
    hangTimeout = '10m'
    projectTimeoutMinutes = 30
    fixtureExecutions = 0
    rawLogsExported = $false
    redaction = 'TRX stdout/stderr removed; credential/certificate lines omitted from runner logs. No dumps exported.'
}
$exitCode = 1

function Write-Json([string] $Path, $Value) {
    $Value | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-Git([string] $Directory, [string[]] $Arguments) {
    $result = @(& git -C $Directory @Arguments)
    if ($LASTEXITCODE -ne 0) { throw 'Pinned Git provenance could not be read.' }
    return $result
}

function Sanitize([string] $Text) {
    $Text = [regex]::Replace(
        $Text,
        '(?s)-----BEGIN [^-]*(?:PRIVATE KEY|CERTIFICATE)-----.*?-----END [^-]*(?:PRIVATE KEY|CERTIFICATE)-----',
        '<REDACTED>')
    return [regex]::Replace(
        $Text,
        '(?im)^.*(?:certificate|privatekey|private.key|pfx|thumbprint|sessionid|authenticationtoken|tokenid|authorization:|password|secret|api[-_]key).*(?:\r?\n|$)',
        "<REDACTED>`n")
}

try {
    foreach ($directory in @($raw, $evidence, $hook, $observations)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    if ($env:RUNNER_OS -ne 'Linux' -or $env:RUNNER_ARCH -ne 'X64' -or
        $env:GITHUB_RUN_ATTEMPT -ne '1' -or $env:GITHUB_EVENT_NAME -ne 'push' -or
        $env:GITHUB_REF -ne 'refs/heads/marcschier-extract-remaining-shared-changes' -or
        $env:DIAGNOSTIC_DEFINITION_SHA -notmatch '^[0-9a-f]{40}$' -or
        $env:GITHUB_SHA -ne $env:DIAGNOSTIC_DEFINITION_SHA) {
        throw 'The one-shot Linux definition guard was not satisfied.'
    }
    $metadata.definitionCheckout = @(Invoke-Git $definition @('rev-parse', 'HEAD'))[0]
    $metadata.sourceCheckout = @(Invoke-Git $source @('rev-parse', 'HEAD'))[0]
    $metadata.sourceTree = @(Invoke-Git $source @('rev-parse', 'HEAD^{tree}'))[0]
    $metadata.sourceParents = @(Invoke-Git $source @('show', '-s', '--format=%P', 'HEAD'))[0]
    if ($metadata.definitionCheckout -ne $env:DIAGNOSTIC_DEFINITION_SHA -or
        $metadata.sourceCheckout -ne $sourceSha -or
        $metadata.sourceParents -ne "$parentSha $ownerSha" -or
        @(Invoke-Git $source @('status', '--porcelain')).Count -ne 0) {
        throw 'The checked-out source or definition differs from the authorized immutable commits.'
    }
    $pr = & gh api 'repos/OPCFoundation/UA-.NETStandard/pulls/4806'
    if ($LASTEXITCODE -ne 0) { throw 'PR ownership could not be verified.' }
    $pr = $pr | ConvertFrom-Json
    if ($pr.state -ne 'open' -or $pr.merged -or $pr.head.sha -ne $env:DIAGNOSTIC_DEFINITION_SHA -or
        $pr.head.ref -ne 'marcschier-extract-remaining-shared-changes' -or $pr.base.ref -ne 'master') {
        throw 'The PR changed ownership or merged before diagnostic admission.'
    }
    $metadata.prHeadAtAdmission = $pr.head.sha
    $sdk = Get-Content -LiteralPath (Join-Path $source 'global.json') -Raw | ConvertFrom-Json
    if ($sdk.sdk.version -ne '10.0.401') { throw 'The tested source SDK pin changed.' }
    Set-Location -LiteralPath $source
    $metadata.sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $metadata.sdkVersion -ne $sdk.sdk.version) {
        throw 'The selected SDK does not match the tested source.'
    }
    & dotnet --info *> (Join-Path $evidence 'dotnet-info.log')
    if ($LASTEXITCODE -ne 0) { throw 'SDK provenance failed.' }
    $runner = Join-Path $source '.github/scripts/run-dotnet-tests.ps1'
    $coverageSettings = Join-Path $source 'tests/coverlet.runsettings.xml'
    $metadata.runnerScriptSha256 = (Get-FileHash -LiteralPath $runner).Hash
    $metadata.coverageSettingsSha256 = (Get-FileHash -LiteralPath $coverageSettings).Hash
    foreach ($name in @('BusyHook.csproj', 'StartupHook.cs')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $hook $name)
    }
    & dotnet build (Join-Path $hook 'BusyHook.csproj') -c Release --nologo -v:minimal `
        -p:Nullable=enable -p:RunAnalyzersDuringBuild=true -p:TreatWarningsAsErrors=true `
        *> (Join-Path $raw 'hook-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'The diagnostic-only startup hook did not build.' }
    $metadata.hookSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $hook 'StartupHook.cs')).Hash
    $hookAssembly = Join-Path $hook 'bin/Release/net10.0/BusyHook.dll'
    $metadata.hookAssemblySha256 = (Get-FileHash -LiteralPath $hookAssembly).Hash
    $env:DOTNET_STARTUP_HOOKS = $hookAssembly
    $env:WMTP_BUSY_DIAGNOSTICS = $observations
    $env:RunAnalyzersDuringBuild = 'true'
    $env:NBGV_SetCloudBuildVersionVars = 'false'
    $env:GH_TOKEN = $null
    $metadata.telemetry = @(
        'Busy first-chance stack and exception identity, never exception message/data',
        'Existing Experimental.ModelContextProtocol activities: WMTP tool names and span IDs only',
        'Existing opcua.server.isolation.rejections metric: bounded stage/reason tags',
        'NUnit case, hashed MCP request/session/peripheral IDs and non-atomic active-owner/age snapshots')
    $results = Join-Path $raw 'results'
    $arguments = @(
        '-NoProfile', '-File', $runner,
        '-Projects', 'tests/Opc.Ua.Tools.Tests/Opc.Ua.Tools.Tests.csproj',
        '-CustomTestTarget', 'net10.0', '-Framework', 'net10.0', '-Configuration', 'Release',
        '-Filter', $filter, '-HangTimeout', '10m', '-PerProjectTimeoutMinutes', '30',
        '-ResultsDirectory', $results, '-Coverage', '-QuietOutput')
    $metadata.command = @('pwsh') + $arguments
    $metadata.started = [DateTimeOffset]::UtcNow
    $metadata.fixtureExecutions = 1
    Write-Json (Join-Path $evidence 'provenance.json') $metadata
    & pwsh @arguments *> (Join-Path $raw 'runner.log')
    $exitCode = $LASTEXITCODE
    $metadata.runnerExitCode = $exitCode
    $metadata.finished = [DateTimeOffset]::UtcNow
    $metadata.sourceCheckoutAfter = @(Invoke-Git $source @('rev-parse', 'HEAD'))[0]
    $metadata.sourceTreeAfter = @(Invoke-Git $source @('rev-parse', 'HEAD^{tree}'))[0]
    $metadata.sourceChangesAfter = @(Invoke-Git $source @('status', '--porcelain'))
    $trxFiles = @(Get-ChildItem -LiteralPath $results -Recurse -File -Filter '*.trx')
    if ($trxFiles.Count -ne 1) { throw 'The fixture did not produce exactly one TRX.' }
    [xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
    $metadata.rawTrxSha256 = (Get-FileHash -LiteralPath $trxFiles[0].FullName).Hash
    $metadata.counters = $trx.TestRun.ResultSummary.Counters |
        Select-Object total, executed, passed, failed, notExecuted, error, timeout, aborted
    $metadata.tests = @($trx.TestRun.Results.UnitTestResult |
        Select-Object testName, outcome, startTime, endTime, duration)
    $target = 'ExplicitDeletionKeepsOtherStoresAndInstancesIntact("service","time",2)'
    $metadata.target = @($metadata.tests | Where-Object testName -eq $target)
    if ($metadata.tests.Count -ne 29 -or $metadata.target.Count -ne 1) {
        throw 'The authorized fixture or exact target case was not discovered completely.'
    }
    foreach ($node in @($trx.SelectNodes("//*[local-name()='StdOut' or local-name()='StdErr']"))) {
        [void]$node.ParentNode.RemoveChild($node)
    }
    foreach ($node in @($trx.SelectNodes("//*[local-name()='ErrorInfo']/*[local-name()='Message']"))) {
        $node.InnerText = Sanitize $node.InnerText
    }
    $trx.Save((Join-Path $evidence 'wmtp-fixture-redacted.trx'))
    foreach ($coverage in @(Get-ChildItem -LiteralPath $results -Recurse -File -Filter 'coverage.cobertura.xml')) {
        Copy-Item -LiteralPath $coverage.FullName -Destination (Join-Path $evidence 'coverage.cobertura.xml')
    }
    $events = @(Get-ChildItem -LiteralPath $observations -File -Filter '*.jsonl' |
        ForEach-Object { Get-Content -LiteralPath $_.FullName | ForEach-Object { $_ | ConvertFrom-Json } })
    $metadata.diagnosticCounts = @($events | Group-Object kind | Select-Object Name, Count)
    $metadata.hookCompleted = @($events | Where-Object kind -eq 'completed' | Select-Object -ExpandProperty data)
    foreach ($processId in @($events | Where-Object kind -eq 'mcp-start' |
        Select-Object -ExpandProperty process -Unique)) {
        if (@($events | Where-Object { $_.kind -eq 'completed' -and $_.process -eq $processId }).Count -ne 1) {
            throw 'The instrumented test process did not retain its terminal diagnostic summary.'
        }
    }
    if (@($events | Where-Object kind -eq 'mcp-start').Count -eq 0 -or
        @($events | Where-Object kind -eq 'hook-error').Count -ne 0 -or
        @($metadata.hookCompleted | Where-Object { $_.truncated -or $_.hookErrors -ne 0 }).Count -ne 0) {
        throw 'Diagnostic activation failed or its bounded output was incomplete.'
    }
    if ($metadata.sourceCheckoutAfter -ne $sourceSha -or
        $metadata.sourceTreeAfter -ne $metadata.sourceTree -or $metadata.sourceChangesAfter.Count -ne 0) {
        throw 'The actual tested source was not preserved.'
    }
}
catch {
    $metadata.diagnosticFailureType = $_.Exception.GetType().FullName
    $metadata.diagnosticFailure = 'A provenance, activation, runner or artifact guard failed; inspect retained evidence.'
    $exitCode = 1
}
finally {
    $env:DOTNET_STARTUP_HOOKS = $null
    $env:WMTP_BUSY_DIAGNOSTICS = $null
    if (Test-Path -LiteralPath $evidence) {
        foreach ($log in @(Get-ChildItem -LiteralPath $raw -File -Filter '*.log')) {
            Sanitize (Get-Content -LiteralPath $log.FullName -Raw) |
                Set-Content -LiteralPath (Join-Path $evidence $log.Name) -Encoding utf8
        }
        $metadata.finalExitCode = $exitCode
        Write-Json (Join-Path $evidence 'provenance.json') $metadata
        $hashes = @(Get-ChildItem -LiteralPath $evidence -Recurse -File | ForEach-Object {
            [pscustomobject]@{
                path = [IO.Path]::GetRelativePath($evidence, $_.FullName)
                sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash
                bytes = $_.Length
            }
        })
        Write-Json (Join-Path $evidence 'hashes.json') $hashes
    }
}
Write-Host "One-shot WMTP diagnostic finished with exit $exitCode; evidence excludes raw output and dumps."
exit $exitCode
