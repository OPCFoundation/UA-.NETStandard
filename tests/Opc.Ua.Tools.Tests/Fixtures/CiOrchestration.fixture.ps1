<#
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to
deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/
#>

param([Parameter(Mandatory = $true)][string] $Scenario)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$fixture = Join-Path ([System.IO.Path]::GetTempPath()) "opcua-ci-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
$scripts = Join-Path $root '.github/scripts'
$assurance = Join-Path $root '.azurepipelines/assurance'
. (Join-Path $scripts 'get-test-verdict.ps1')

function Assert-Condition([bool] $condition, [string] $message)
{
    if (-not $condition) { throw $message }
}

function Write-Json([string] $path, $value)
{
    $null = New-Item -ItemType Directory -Path (Split-Path $path) -Force
    ConvertTo-Json -InputObject $value -Depth 15 | Set-Content -LiteralPath $path
}

function Write-Trx([string] $path)
{
    $values = [ordered]@{
        total = 2; executed = 2; passed = 2; failed = 0; error = 0; timeout = 0
        aborted = 0; inconclusive = 0; passedButRunAborted = 0; notRunnable = 0
        notExecuted = 0; disconnected = 0; warning = 0; completed = 0; inProgress = 0; pending = 0
    }
    $outcome = 'Completed'
    $info = ''
    $results = '<UnitTestResult testName="PRIVATE-CORPUS-DO-NOT-PUBLISH" outcome="Passed"/>' +
        '<UnitTestResult testName="D:\private\input-secret" outcome="Passed"/>'
    switch ($Scenario) {
        'proof-aborted' { $outcome = 'Aborted' }
        'proof-run-error' {
            $info = '<RunInfos><RunInfo outcome="Error"><Text>PRIVATE-CORPUS-DO-NOT-PUBLISH</Text></RunInfo></RunInfos>'
        }
        'proof-pending-counter' { $values.pending = 1 }
        'proof-missing-counter' { $values.Remove('pending') }
        'proof-zero-execution' {
            $values.total = 0
            $values.executed = 0
            $values.passed = 0
            $results = ''
        }
    }
    if ($Scenario.StartsWith('proof-replay-')) {
        $values.total = 4
        $results += '<UnitTestResult testName="FuzzTimeoutAssetsAsync" outcome="NotExecuted"/>' +
            '<UnitTestResult testName="FuzzSlowAssetsAsync" outcome="NotExecuted"/>'
    }
    $attributes = ($values.GetEnumerator() | ForEach-Object { "$($_.Key)=`"$($_.Value)`"" }) -join ' '
    "<TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results>$results</Results>" +
        "<ResultSummary outcome=`"$outcome`"><Counters $attributes/>$info</ResultSummary></TestRun>" |
        Set-Content -LiteralPath $path
}

$savedEnvironment = @{}
foreach ($name in @('GITHUB_OUTPUT', 'GITHUB_STEP_SUMMARY', 'AGENT_JOBSTATUS', 'GITHUB_ACTIONS',
    'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_JOB', 'GITHUB_WORKFLOW_SHA')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:GITHUB_STEP_SUMMARY = Join-Path $fixture 'step-summary.md'
    if ($Scenario -in @('selected-success', 'selected-skipped', 'selected-failure', 'selected-cancelled',
        'unselected-skipped', 'unselected-failure', 'missing-job', 'unknown-job')) {
        $expected = [ordered]@{ discover = $true; tests = $true }
        $results = [ordered]@{ discover = 'success'; tests = 'success' }
        $shouldPass = $Scenario -in @('selected-success', 'unselected-skipped')
        switch ($Scenario) {
            'selected-skipped' { $results.tests = 'skipped' }
            'selected-failure' { $results.tests = 'failure' }
            'selected-cancelled' { $results.tests = 'cancelled' }
            'unselected-skipped' { $expected.tests = $false; $results.tests = 'skipped' }
            'unselected-failure' { $expected.tests = $false; $results.tests = 'failure' }
            'missing-job' { $results.Remove('tests') }
            'unknown-job' { $results.extra = 'success' }
        }
        $verdict = Get-CiJobVerdict -Expected $expected -Results $results
        Assert-Condition ($verdict.Passed -eq $shouldPass) 'Incorrect selected-job verdict.'
        Assert-Condition (($verdict.Failures.Count -eq 0) -eq $shouldPass) 'Missing failing-job explanation.'
    }
    elseif ($Scenario -in @('empty-matrix', 'missing-matrix')) {
        if ($Scenario -eq 'empty-matrix') {
            Assert-Condition (-not (Test-CiMatrixSelected '[]')) 'An intentionally empty matrix was selected.'
            Assert-Condition (Test-CiMatrixSelected '[{"id":"one"}]') 'A nonempty matrix was not selected.'
        }
        else {
            foreach ($invalid in @('', 'null', '{}', 'not-json')) {
                $rejected = $false
                try { $null = Test-CiMatrixSelected $invalid }
                catch { $rejected = $true }
                Assert-Condition $rejected 'Missing or malformed discovery was treated as an empty matrix.'
            }
        }
    }
    elseif ($Scenario.StartsWith('coverage-')) {
        $entries = @(
            [ordered]@{
                id = 'windows-net10-01'; projects = 'tests/First.Tests.csproj;fuzzing/Second.Fuzz.Tests.csproj'
                customTestTarget = 'net10.0'; framework = 'net10.0'; configuration = 'Release'
                filter = 'TestCategory!=LongRunning'; coverage = $true
            },
            [ordered]@{
                id = 'windows-net48-01'; projects = 'tests/Legacy.Tests.csproj'
                customTestTarget = 'net48'; framework = 'net48'; configuration = 'Release'
                filter = 'TestCategory!=LongRunning'; coverage = $false
            })
        if ($Scenario -eq 'coverage-legacy') { $entries = @($entries[1]) }
        $artifacts = Join-Path $fixture 'artifacts'
        foreach ($entry in $entries) {
            $directory = Join-Path $artifacts "dotnet-results-$($entry.id)"
            $records = @()
            foreach ($project in $entry.projects.Split(';')) {
                $record = [ordered]@{
                    project = $project; outcome = 'passed'; total = 2; passed = 2; failed = 0
                    notApplicableRule = ''
                }
                if ($Scenario -in @('coverage-restricted', 'coverage-unverified-skip',
                    'coverage-explicit-unsupported', 'coverage-explicit-supported-disabled')) {
                    $record.outcome = 'not-applicable'
                    $record.total = 0
                    $record.passed = 0
                    if ($Scenario -eq 'coverage-restricted') { $record.notApplicableRule = 'RestrictForLegacyTfm' }
                    if ($Scenario.StartsWith('coverage-explicit-')) {
                        $record.notApplicableRule = 'SupportedTestTargets'
                        $record.supportedTestTargets = if ($Scenario -eq 'coverage-explicit-supported-disabled') {
                            @($entry.customTestTarget)
                        } else { @('unsupported-in-this-profile') }
                    }
                }
                elseif ($entry.coverage) {
                    $projectDirectory = Join-Path $directory ([IO.Path]::GetFileNameWithoutExtension($project))
                    $null = New-Item -ItemType Directory -Path $projectDirectory -Force
                    '<coverage />' | Set-Content -LiteralPath (Join-Path $projectDirectory 'coverage.cobertura.xml')
                }
                if ($Scenario -eq 'coverage-failed-project') { $record.outcome = 'failed' }
                $records += $record
            }
            $summary = @{
                customTestTarget = $entry.customTestTarget; framework = $entry.framework
                configuration = $entry.configuration; filter = $entry.filter; coverage = $entry.coverage
                inputScope = 'public'; projects = $records
            }
            if ($Scenario -eq 'coverage-private-scope') { $summary.inputScope = 'private' }
            Write-Json (Join-Path $directory 'batch-summary.json') $summary
        }
        switch ($Scenario) {
            'coverage-missing-batch' {
                Remove-Item -LiteralPath (Join-Path $artifacts 'dotnet-results-windows-net48-01') -Recurse -Force
            }
            'coverage-missing-project' {
                Remove-Item -LiteralPath (
                    Join-Path $artifacts 'dotnet-results-windows-net10-01/Second.Fuzz.Tests/coverage.cobertura.xml')
            }
            'coverage-missing-summary' {
                Remove-Item -LiteralPath (Join-Path $artifacts 'dotnet-results-windows-net10-01/batch-summary.json')
            }
        }
        $shouldPass = $Scenario -in @('coverage-complete', 'coverage-legacy', 'coverage-restricted',
            'coverage-explicit-unsupported')
        $actual = $null
        $rejected = $false
        try {
            $actual = Assert-CiCoverage -Matrix (ConvertTo-Json -InputObject $entries -Depth 5 -Compress) `
                -ArtifactsDirectory $artifacts
        }
        catch { $rejected = $true }
        Assert-Condition ((-not $rejected) -eq $shouldPass) 'Incorrect per-project coverage completeness verdict.'
        if ($shouldPass) {
            $count = if ($Scenario -eq 'coverage-complete') { 2 } else { 0 }
            Assert-Condition ($actual.Reports -eq $count) 'Coverage counted the wrong project fragments.'
            Assert-Condition ($actual.HasReports -eq ($count -gt 0)) 'Incorrect coverage report availability.'
        }
    }
    elseif ($Scenario.StartsWith('proof-')) {
        $raw = Join-Path $fixture 'raw'
        $null = New-Item -ItemType Directory -Path $raw
        $trx = Join-Path $raw 'PRIVATE-CORPUS-DO-NOT-PUBLISH.trx'
        $kind = 'trx'
        $project = 'tests/Opc.Ua.Core.Security.Tests/Opc.Ua.Core.Security.Tests.csproj'
        if ($Scenario.StartsWith('proof-replay-')) {
            $kind = 'fuzz-replay'
            $project = 'fuzzing/Opc.Ua.Encoders.Fuzz.Tests/Opc.Ua.Encoders.Fuzz.Tests.csproj'
            $target = 'sha256:' + ('a' * 64)
            $inputId = 'sha256:' + ('b' * 64)
            $inputDigest = 'sha256:' + ('c' * 64)
            $inputs = @(@{ id = $inputId; digest = $inputDigest; category = 'good' })
            if ($Scenario -eq 'proof-replay-skipped-known-input') {
                $inputs += @{ id = 'sha256:' + ('d' * 64); digest = $inputDigest; category = 'timeout' }
            }
            $inventory = @{
                schemaVersion = 1; inventoryDigest = 'sha256:' + ('e' * 64)
                goodInputs = 1; regressionInputs = $inputs.Count - 1; inputs = $inputs
            }
            Write-Json (Join-Path $raw 'public-inputs.json') $inventory
            Write-Json (Join-Path $raw 'copied-inputs.json') $inventory
            $inputXml = ($inputs | ForEach-Object {
                "<input id='$($_.id)' digest='$($_.digest)' category='$($_.category)'/>"
            }) -join ''
            $executions = ($inputs | ForEach-Object { "<execution target='$target' input='$($_.id)'/>" }) -join ''
            "<replay schemaVersion='1'><targets><target id='$target'/></targets><inputs>$inputXml</inputs>" +
                "<executions>$executions</executions></replay>" | Set-Content -LiteralPath (Join-Path $raw 'fuzz-replay.xml')
        }
        if ($Scenario -ne 'proof-mtp-fallback') { Write-Trx $trx }
        else {
            $env:AGENT_JOBSTATUS = 'Succeeded'
            $output = & (Join-Path $assurance 'evaluate-test-results.ps1') `
                -ResultsDirectory $raw -Sanitize *>&1 | Out-String
            Assert-Condition ($LASTEXITCODE -eq 0) 'The supported exit-only MTP fallback regressed.'
            Assert-Condition ($output.Contains('No VSTest TRX')) 'The fallback did not disclose its limited evidence.'
        }
        $proofPath = Join-Path $fixture 'public-results.json'
        $output = & (Join-Path $assurance 'results.ps1') -ResultsPath $raw -Kind $kind `
            -OutputPath $proofPath -StrictTrx -Enforce *>&1 | Out-String
        $code = $LASTEXITCODE
        $proof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
        $shouldPass = $Scenario -in @('proof-completed', 'proof-process-failure', 'proof-replay-empty-regressions')
        Assert-Condition (($code -eq 0) -eq $shouldPass) 'The strict result gate returned an incorrect exit code.'
        Assert-Condition (($proof.status -eq 'completed') -eq $shouldPass) 'Incomplete execution received credit.'
        if ($Scenario -eq 'proof-mtp-fallback') {
            Assert-Condition ($proof.status -eq 'missing') 'Exit-only success was credited as execution.'
            Assert-Condition ($null -eq $proof.counts) 'Unknown execution was invented as zero counts.'
        }
        else {
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $trx -Algorithm SHA256).Hash.ToLowerInvariant()
            Assert-Condition ($proof.documents.Count -eq 1 -and $proof.documents[0].digest -ceq $digest) `
                'The exact raw document was not hash-bound.'
        }
        if ($Scenario -eq 'proof-replay-empty-regressions') {
            Assert-Condition ($proof.counts.expectedTargets -eq 1 -and $proof.counts.executedTargets -eq 1 -and
                $proof.counts.expectedInputs -eq 1 -and $proof.counts.executedInputs -eq 1 -and
                $proof.replay.allowedEmptyRegressionSkips -eq 2 -and $proof.replay.executedPairs -eq 1) `
                'Async replay names lost the precisely bounded empty-regression exception.'
        }
        $env:GITHUB_ACTIONS = 'true'
        $env:GITHUB_RUN_ID = '987654'
        $env:GITHUB_RUN_ATTEMPT = '2'
        $env:GITHUB_JOB = 'assurance-windows'
        $env:GITHUB_WORKFLOW_SHA = 'a' * 40
        $recordPath = Join-Path $fixture 'record/evidence.job.json'
        $output += & (Join-Path $assurance 'write-job.ps1') `
            -Project $project -ResultsPath $raw `
            -OutputPath $recordPath -Workflow '.github/workflows/buildandtest.yml' -StrictTrx `
            -ExecutionFailed:($Scenario -eq 'proof-process-failure') *>&1 | Out-String
        Assert-Condition ($LASTEXITCODE -eq 0) 'The sanitized job record could not be produced.'
        $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
        $recordProofPath = Join-Path $fixture 'record/evidence.job.results.json'
        $recordDigest = 'sha256:' + (Get-FileHash -LiteralPath $recordProofPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Condition ($record.resultDigest -ceq $recordDigest -and
            $record.resultDocument -ceq 'evidence.job.results.json') 'The record does not bind the exact proof.'
        $expectedStatus = if ($Scenario -eq 'proof-process-failure') { 'failed' } else { $proof.status }
        Assert-Condition ($record.status -ceq $expectedStatus) 'The job lost the observed execution outcome.'
        if ($Scenario -eq 'proof-process-failure') {
            $recordProof = Get-Content -LiteralPath $recordProofPath -Raw | ConvertFrom-Json
            Assert-Condition ($recordProof.status -ceq 'failed' -and $recordProof.counts.passed -eq 2) `
                'A killed host received completion credit, or the actual test counts were invented.'
        }
        Assert-Condition ($record.producer.job -ceq 'assurance-windows' -and
            $record.producer.runId -ceq '987654' -and $record.producer.attempt -eq 2) 'Wrong producer identity.'
        $publicText = $output + (Get-Content -LiteralPath $proofPath -Raw) +
            (Get-Content -LiteralPath $recordPath -Raw) + (Get-Content -LiteralPath $recordProofPath -Raw)
        Assert-Condition (-not $publicText.Contains('PRIVATE-CORPUS-DO-NOT-PUBLISH') -and
            -not $publicText.Contains('D:\private')) 'Raw identifiers or exception details became public.'
    }
    elseif ($Scenario -eq 'runner-process-output-capture') {
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $scripts 'run-dotnet-tests.ps1'), [ref] $tokens, [ref] $errors)
        Assert-Condition ($errors.Count -eq 0) 'The shared executor no longer parses.'
        $definition = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq 'Invoke-Dotnet'
        }, $true)
        . ([scriptblock]::Create($definition.Extent.Text))
        $captureOutput = $true
        $log = Join-Path $fixture 'private-process.log'
        $records = @(Invoke-Dotnet @('--list-sdks') ([Diagnostics.Stopwatch]::StartNew()) 1 $log 6>&1)
        $result = @($records | Where-Object { $_ -is [pscustomobject] -and $null -ne $_.ExitCode })
        Assert-Condition ($result.Count -eq 1 -and $result[0].ExitCode -eq 0 -and -not $result[0].TimedOut) `
            'The bounded process runner failed.'
        $privateText = Get-Content -LiteralPath $log -Raw
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($privateText)) 'Redirected diagnostics were not retained.'
        $publicText = ($records | ForEach-Object { [string] $_ }) -join "`n"
        Assert-Condition (-not $publicText.Contains($privateText.Trim())) 'Private subprocess output was streamed publicly.'
    }
    elseif ($Scenario.StartsWith('runner-')) {
        $arguments = @{
            Projects = 'fuzzing/Opc.Ua.PubSub.Fuzz.Tests/Opc.Ua.PubSub.Fuzz.Tests.csproj'
            CustomTestTarget = 'net10.0'; Framework = 'net10.0'; Configuration = 'Release'
            ResultsDirectory = (Join-Path $fixture 'raw')
            PublicResultsDirectory = (Join-Path $fixture 'public')
        }
        switch ($Scenario) {
            'runner-private-publication' { $arguments.InputScope = 'private' }
            'runner-stale-results' {
                $null = New-Item -ItemType Directory -Path $arguments.ResultsDirectory
                'stale' | Set-Content -LiteralPath (Join-Path $arguments.ResultsDirectory 'old.trx')
            }
            'runner-legacy-coverage' {
                $arguments.CustomTestTarget = 'net48'
                $arguments.Framework = 'net48'
                $arguments.Coverage = $true
            }
            'runner-pinned-framework' {
                $arguments.CustomTestTarget = 'net9.0'
                $arguments.Framework = 'net9.0'
            }
            { $_ -in @('runner-explicit-unsupported', 'runner-explicit-unproven-scope', 'runner-required-unsupported') } {
                $arguments.CustomTestTarget = 'net48'
                $arguments.Framework = 'net48'
                if ($Scenario -eq 'runner-required-unsupported') {
                    $arguments.RequireAssurance = $true
                    $arguments.AssuranceDirectory = Join-Path $fixture 'assurance'
                    $arguments.AssuranceWorkflow = '.github/workflows/buildandtest.yml'
                }
            }
        }
        $global:CiFixtureScenario = $Scenario
        $global:CiFixtureRaw = Join-Path $arguments.ResultsDirectory 'Opc.Ua.PubSub.Fuzz.Tests'
        function global:dotnet {
            if ($global:CiFixtureScenario -eq 'runner-private-failure') {
                'PRIVATE-CORPUS-DO-NOT-PUBLISH' | Set-Content (Join-Path $global:CiFixtureRaw 'private.dmp')
                'PRIVATE-CORPUS-DO-NOT-PUBLISH' | Set-Content (Join-Path $global:CiFixtureRaw 'private.trx')
                'PRIVATE-CORPUS-DO-NOT-PUBLISH' | Set-Content (Join-Path $global:CiFixtureRaw 'public-invented.json')
                '<coverage />' | Set-Content (Join-Path $global:CiFixtureRaw 'private-name.cobertura.xml')
                $global:LASTEXITCODE = 1
                Write-Output 'PRIVATE-CORPUS-DO-NOT-PUBLISH D:\private\input-secret'
                return
            }
            if ($global:CiFixtureScenario -eq 'runner-pinned-framework' -and
                @($args | Where-Object { $_ -like '-p:TargetFramework=*' }).Count -gt 0) {
                throw 'Applicability must not override a pinned project framework.'
            }
            $restricted = if ($global:CiFixtureScenario -in @('runner-restricted', 'runner-pinned-framework')) {
                'true'
            } else {
                'false'
            }
            $global:LASTEXITCODE = 0
            @{ Properties = @{
                IsTestProject = 'false'; _RestrictedToLegacyTfm = $restricted
                SupportedTestTargets = if ($global:CiFixtureScenario -match '^runner-(explicit|required)-') {
                    'net10.0'
                } else { '' }
                TargetFrameworks = ''
                TargetFramework = if ($global:CiFixtureScenario -eq 'runner-explicit-unproven-scope') {
                    'net48'
                } else { 'net10.0' }
                TargetDir = ''
            } } | ConvertTo-Json -Compress
        }
        $captured = [System.Collections.Generic.List[string]]::new()
        $rejected = $false
        try {
            & (Join-Path $scripts 'run-dotnet-tests.ps1') @arguments *>&1 |
                ForEach-Object { $captured.Add([string] $_) }
        }
        catch {
            $rejected = $true
            $captured.Add([string] $_)
        }
        finally {
            Remove-Item Function:\dotnet
            Remove-Variable CiFixtureScenario,CiFixtureRaw -Scope Global
        }
        Assert-Condition ($rejected -eq ($Scenario -notin @(
            'runner-restricted', 'runner-pinned-framework', 'runner-explicit-unsupported'))) `
            'Incorrect shared-runner verdict.'
        if ($Scenario -in @('runner-restricted', 'runner-pinned-framework', 'runner-unverified-skip',
            'runner-private-failure', 'runner-explicit-unsupported')) {
            $summary = Get-Content -LiteralPath (Join-Path $arguments.PublicResultsDirectory 'batch-summary.json') -Raw |
                ConvertFrom-Json
            Assert-Condition ($summary.projects.Count -eq 1 -and $summary.inputScope -eq 'public') `
                'The runner lost its actual selection.'
            if ($Scenario -in @('runner-restricted', 'runner-pinned-framework')) {
                Assert-Condition ($summary.projects[0].outcome -eq 'not-applicable' -and
                    $summary.projects[0].notApplicableRule -eq 'RestrictForLegacyTfm' -and
                    $summary.projects[0].passed -eq 0) 'An evaluated empty shell was counted as execution.'
            }
            elseif ($Scenario -eq 'runner-explicit-unsupported') {
                $result = $summary.projects[0]
                Assert-Condition ($summary.projects.Count -eq 1 -and $result.outcome -eq 'not-applicable' -and
                    $result.notApplicableRule -eq 'SupportedTestTargets' -and $result.passed -eq 0 -and
                    $result.supportedTestTargets.Count -eq 1 -and $result.supportedTestTargets[0] -ceq 'net10.0') `
                    'Explicit framework support did not retain its verified scope without claiming execution.'
            }
            else {
                Assert-Condition ($summary.projects[0].outcome -eq 'failed') 'An invalid project skip passed.'
            }
        }
        if ($Scenario -eq 'runner-private-failure') {
            $files = @(Get-ChildItem -LiteralPath $arguments.PublicResultsDirectory -File -Recurse)
            Assert-Condition (@($files | Where-Object Extension -in @('.trx', '.dmp', '.log')).Count -eq 0) `
                'A raw result, dump or log escaped public staging.'
            Assert-Condition (@($files | Where-Object Name -eq 'public-invented.json').Count -eq 0) `
                'Public staging accepted an arbitrary prefixed file.'
            Assert-Condition (@($files | Where-Object Name -eq 'coverage-1.cobertura.xml').Count -eq 1) `
                'Safe coverage was not staged with a non-private name.'
            $publicText = ($captured -join "`n") + (($files | Get-Content -Raw) -join "`n") +
                (Get-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Raw)
            Assert-Condition (-not $publicText.Contains('PRIVATE-CORPUS-DO-NOT-PUBLISH') -and
                -not $publicText.Contains('D:\private')) 'Private diagnostics reached a public output.'
            $rawText = Get-Content -LiteralPath (
                Join-Path $arguments.ResultsDirectory 'Opc.Ua.PubSub.Fuzz.Tests/build-and-test.log') -Raw
            Assert-Condition ($rawText.Contains('PRIVATE-CORPUS-DO-NOT-PUBLISH')) 'Private diagnostics were lost.'
        }
    }
    elseif ($Scenario.StartsWith('public-inputs-')) {
        $catalog = Get-Content -LiteralPath (Join-Path $assurance 'profiles.json') -Raw | ConvertFrom-Json
        $definition = $catalog.additionalReplayProjects[0]
        $global:CiFixtureTrackedInputs = @()
        foreach ($bucket in $definition.corpusBuckets) {
            $relative = "$($definition.corpusRoot)/$bucket/seed"
            $source = Join-Path $fixture $relative
            $destination = Join-Path $fixture ('output/Testcases/' + $bucket.Substring('Testcases.'.Length) + '/seed')
            $null = New-Item -ItemType Directory -Path (Split-Path $source),(Split-Path $destination) -Force
            $bucket | Set-Content -LiteralPath $source
            Copy-Item -LiteralPath $source -Destination $destination
            $global:CiFixtureTrackedInputs += $relative
        }
        if ($Scenario -eq 'public-inputs-untracked-overlay') {
            $global:CiFixtureTrackedInputs = @($global:CiFixtureTrackedInputs | Select-Object -Skip 1)
        }
        $global:CiFixtureModifiedInputs = $Scenario -eq 'public-inputs-modified-overlay'
        function global:git {
            $global:LASTEXITCODE = 0
            if ('ls-files' -in $args) {
                return ($global:CiFixtureTrackedInputs -join [char] 0) + [char] 0
            }
            if ('diff' -in $args) {
                if ($global:CiFixtureModifiedInputs) { $global:LASTEXITCODE = 1 }
                return
            }
            throw 'Unexpected Git operation in public-input verification.'
        }
        $manifestPath = Join-Path $fixture 'manifest.json'
        try {
            $output = & (Join-Path $assurance 'fuzz-inputs.ps1') -RepoRoot $fixture `
                -Project $definition.project -BuildOutput (Join-Path $fixture 'output') `
                -OutputPath $manifestPath -RequireCommitted *>&1 | Out-String
            $code = $LASTEXITCODE
        }
        finally {
            Remove-Item Function:\git
            Remove-Variable CiFixtureTrackedInputs,CiFixtureModifiedInputs -Scope Global
        }
        $shouldPass = $Scenario -eq 'public-inputs-committed'
        Assert-Condition (($code -eq 0) -eq $shouldPass) 'A private overlay was credited as committed-public input.'
        Assert-Condition ((Test-Path -LiteralPath $manifestPath) -eq $shouldPass) 'An invalid public manifest escaped.'
        Assert-Condition (-not $output.Contains($fixture)) 'Private input locations were logged.'
        if ($shouldPass) {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            Assert-Condition ($manifest.goodInputs -eq 3 -and $manifest.regressionInputs -eq 0) `
                'Committed additional PubSub inputs were not preserved.'
        }
    }
    elseif ($Scenario.StartsWith('selection-')) {
        $repository = Join-Path $fixture 'repository'
        $catalog = Get-Content -LiteralPath (Join-Path $assurance 'profiles.json') -Raw | ConvertFrom-Json
        $projects = @($catalog.profiles.jobs.project | Where-Object { $_ -like '*.csproj' }) +
            @($catalog.additionalReplayProjects.project) +
            @('tests/Opc.Ua.Subscriptions.Durable.Tests/Opc.Ua.Subscriptions.Durable.Tests.csproj')
        foreach ($project in $projects) {
            $path = Join-Path $repository $project
            $null = New-Item -ItemType Directory -Path (Split-Path $path) -Force
            '<Project />' | Set-Content -LiteralPath $path
        }
        '<Solution />' | Set-Content -LiteralPath (Join-Path $repository 'UA.slnx')
        Write-Json (Join-Path $repository '.azurepipelines/assurance/profiles.json') $catalog
        if ($Scenario -eq 'selection-missing-replay') {
            Remove-Item -LiteralPath (Join-Path $repository $catalog.additionalReplayProjects[0].project)
        }
        $env:GITHUB_OUTPUT = Join-Path $fixture 'matrix-output.txt'
        $rejected = $false
        try {
            & (Join-Path $scripts 'get-ci-matrix.ps1') -Scope pr -RepositoryRoot $repository `
                -ManifestPath (Join-Path $fixture 'manifest.json') *> (Join-Path $fixture 'matrix.log')
        }
        catch { $rejected = $true }
        Assert-Condition ($rejected -eq ($Scenario -eq 'selection-missing-replay')) 'Required replay selection vanished.'
        if (-not $rejected) {
            $outputs = @{}
            foreach ($line in Get-Content -LiteralPath $env:GITHUB_OUTPUT) {
                $parts = $line.Split('=', 2)
                $outputs[$parts[0]] = $parts[1]
            }
            $selected = @($outputs.assurance_projects | ConvertFrom-Json)
            $tests = @($outputs.tests | ConvertFrom-Json)
            $builds = @($outputs.builds | ConvertFrom-Json)
            Assert-Condition ($selected.Count -eq 5 -and @($catalog.profiles.jobs).Count -eq 7) `
                'The fixed release profile expanded or lost a job.'
            Assert-Condition (@($selected | Where-Object project -like '*PubSub*').Count -eq 0) `
                'Additional PubSub replay was promoted into the release profile.'
            foreach ($profile in @('windows-net48', 'windows-net10.0', 'linux-net10.0', 'macos-net10.0')) {
                Assert-Condition (@($tests | Where-Object {
                    $_.profile -ceq $profile -and $_.projects.Contains($catalog.additionalReplayProjects[0].project)
                }).Count -eq 1) 'A baseline PubSub replay profile disappeared.'
            }
            foreach ($tfm in @('net48', 'net10.0')) {
                foreach ($configuration in @('Debug', 'Release')) {
                    Assert-Condition (@($builds | Where-Object {
                        $_.os -eq 'windows' -and $_.customTestTarget -eq $tfm -and $_.configuration -eq $configuration
                    }).Count -gt 0) 'The Windows PR solution-build matrix was narrowed.'
                }
            }
            Assert-Condition (@($builds | Where-Object os -eq 'linux').Count -eq 4) 'The Linux all-TFM build was narrowed.'
        }
    }
    elseif ($Scenario -eq 'workflow-contracts') {
        $ci = Get-Content -LiteralPath (Join-Path $root '.github/workflows/buildandtest.yml') -Raw
        $nightly = Get-Content -LiteralPath (Join-Path $root '.github/workflows/nightly.yml') -Raw
        $action = Get-Content -LiteralPath (Join-Path $root '.github/actions/run-dotnet-tests/action.yml') -Raw
        $runner = Get-Content -LiteralPath (Join-Path $scripts 'run-dotnet-tests.ps1') -Raw
        $azure = Get-Content -LiteralPath (Join-Path $root '.azurepipelines/test.yml') -Raw
        $pipeline = Get-Content -LiteralPath (Join-Path $root 'azure-pipelines.yml') -Raw
        $changedPaths = Join-Path $fixture 'changed-paths.txt'
        foreach ($change in @(
            @{ path = '.azurepipelines/assurance/write-job.ps1'; relevant = $true },
            @{ path = '.azurepipelines/assurance/profiles.json'; relevant = $true },
            @{ path = 'release/evidence.schema.json'; relevant = $true },
            @{ path = '.github/actions/run-dotnet-tests/action.yml'; relevant = $true },
            @{ path = 'docs/DeveloperGuide.md'; relevant = $false }
        )) {
            $change.path | Set-Content -LiteralPath $changedPaths
            $primary = & (Join-Path $scripts 'get-path-relevance.ps1') -ChangedFile @($change.path)
            $legacy = & (Join-Path $assurance 'discovery.ps1') -ChangedFilesPath $changedPaths | ConvertFrom-Json
            Assert-Condition ($primary -eq $change.relevant -and $legacy.relevantChanges -eq $primary) `
                'Compatibility discovery diverged from the primary CI relevance rule.'
        }
        Assert-Condition ($ci -notmatch 'csprojs|test_os|gh_owns_build|<<<<<<<|>>>>>>>' -and
            $ci -notmatch '(?m)^\s+schedule:') 'Obsolete PR execution or schedule survived.'
        Assert-Condition ([regex]::Matches($ci, 'uses: \./\.github/actions/run-dotnet-tests').Count -eq 2) `
            'Baseline and standalone assurance do not share the runner.'
        Assert-Condition ($ci.Contains('TARGET_REF: ${{ github.base_ref || github.ref_name }}') -and
            $ci.Contains("@{ os = 'windows-2025'; rid = 'win-x64' }") -and $ci.Contains("rid = 'osx-arm64'")) `
            'The selected Windows/ARM NativeAOT target is not defined.'
        Assert-Condition ($ci.Contains(
            'assurance-${{ matrix.project.id }}-${{ github.run_id }}-${{ github.run_attempt }}-assurance-windows') -and
            $ci.Contains('assurance-native-aot-${{ github.run_id }}-${{ github.run_attempt }}-aot-test-${{ matrix.os }}')) `
            'Authenticated release artifact identities changed.'
        foreach ($workflow in @($ci, $nightly)) {
            Assert-Condition ($workflow.Contains("NBGV_SetCloudBuildVersionVars: 'false'") -and
                $workflow.Contains('Assert-CiCoverage -Matrix $env:TEST_MATRIX') -and
                $workflow.Contains('Get-CiJobVerdict -Results $results -Expected $expected')) `
                'A workflow bypasses version, coverage or selected-job guards.'
        }
        Assert-Condition ([regex]::Matches($action, "!contains\(inputs.projects, 'fuzzing/'\)").Count -eq 2 -and
            $action.Contains("-PublicResultsDirectory 'TestResults'") -and
            $action.Contains("(Join-Path `$env:RUNNER_TEMP 'ci-test-results')") -and
            $action.Contains('kernel.core_pattern') -and $action.Contains('prlimit --pid')) `
            'Native diagnostics or the private/public result boundary regressed.'
        Assert-Condition ([regex]::Matches($runner, '-RequireCommitted').Count -eq 2 -and
            $runner.Contains("-ExecutionFailed:(`$record.outcome -ne 'passed')") -and
            $runner.Contains("-ReportsCompleted `$results.Completed")) `
            'The shared runner bypasses committed-input or successful-execution evidence checks.'
        Assert-Condition ($nightly.Contains("cron: '0 2 * * 0'") -and $nightly.Contains('-InputScope private') -and
            $nightly.Contains('| sha256:$identity | sha256:$hash |') -and
            -not $nightly.Contains('$lines += "| $relative')) 'Private nightly scope leaks or claims public replay.'
        Assert-Condition ($pipeline -match '(?m)^pr: none\r?$' -and $pipeline -notmatch 'FullBuild|<<<<<<<|>>>>>>>' -and
            $pipeline.Contains("eq(variables.ScheduledBuild, 'False')")) 'Azure cadence or fail-closed gating regressed.'
        Assert-Condition ($azure.Contains('.azurepipelines/assurance/evaluate-test-results.ps1') -and
            $azure.Contains('IsTestingPlatformApplication') -and $azure.Contains('-StrictTrx') -and
            -not (Test-Path (Join-Path $root '.azurepipelines/evaluate-test-results.ps1'))) `
            'The Azure strict/MTP gate was not integrated into the reviewed helper layout.'
    }
    else {
        throw 'Unknown CI fixture scenario.'
    }
    Write-Host "PASS: $Scenario"
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
    }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
