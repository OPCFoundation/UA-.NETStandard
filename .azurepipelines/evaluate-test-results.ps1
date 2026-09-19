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

<#
.SYNOPSIS
    Requires completed, successful VSTest TRX runs, or a successful MTP job status.

.DESCRIPTION
    Every TRX must explicitly record a Completed or Passed run, with no run-level
    errors and complete, consistent counters and recorded results. Failed and
    Aborted runs are rejected even if all recorded tests passed or blame killed
    the host during process exit. A completed report remains authoritative when
    AGENT_JOBSTATUS is SucceededWithIssues. When no TRX exists, preserve the MTP
    fallback to AGENT_JOBSTATUS.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'

function Get-CounterValue([System.Xml.XmlElement] $element, [string] $name) {
    $raw = $element.GetAttribute($name)
    [long]$value = 0
    if (![long]::TryParse(
            $raw,
            [System.Globalization.NumberStyles]::None,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$value)) {
        throw "Missing or invalid counter '$name'."
    }
    return $value
}

$currentReport = $ResultsDirectory
try {
    $trx = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter *.trx -File | Sort-Object Name)
    if ($trx.Count -eq 0) {
        $jobStatus = $env:AGENT_JOBSTATUS
        Write-Host "No VSTest TRX found in $ResultsDirectory; deferring to Agent.JobStatus=$jobStatus."
        if ($jobStatus -eq 'Succeeded') {
            Write-Host "Test host exited cleanly; treating the run as successful."
            exit 0
        }
        throw "Test host did not exit cleanly and produced no TRX results."
    }

    $nonPassingCounters = @(
        'failed', 'error', 'timeout', 'aborted', 'passedButRunAborted', 'inconclusive',
        'notRunnable', 'disconnected', 'warning', 'completed', 'inProgress', 'pending'
    )
    $counterNames = @('total', 'executed', 'passed', 'notExecuted') + $nonPassingCounters
    [long]$passed = 0
    [long]$total = 0
    foreach ($file in $trx) {
        $currentReport = $file.Name
        $doc = New-Object System.Xml.XmlDocument
        $doc.XmlResolver = $null
        $doc.Load($file.FullName)

        $summaries = @($doc.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']"))
        if ($summaries.Count -ne 1) {
            throw "Expected exactly one ResultSummary."
        }
        $summary = $summaries[0]
        $outcome = $summary.GetAttribute('outcome')
        if (@('Completed', 'Passed') -cnotcontains $outcome) {
            throw "The run outcome '$outcome' is not completed and successful."
        }
        foreach ($info in $summary.SelectNodes("*[local-name()='RunInfos']/*[local-name()='RunInfo']")) {
            $infoOutcome = $info.GetAttribute('outcome')
            if (@('Completed', 'Passed', 'Informational', 'Warning') -cnotcontains $infoOutcome) {
                throw "RunInfo outcome '$infoOutcome' reports a run-level failure or incomplete run."
            }
        }

        $counters = @($summary.SelectNodes("*[local-name()='Counters']"))
        if ($counters.Count -ne 1) {
            throw "Expected exactly one Counters element."
        }
        $values = @{}
        foreach ($name in $counterNames) {
            $values[$name] = Get-CounterValue $counters[0] $name
        }
        if ($values.total -le 0) {
            throw "No tests were recorded (total=0); the run did not execute."
        }
        foreach ($name in $nonPassingCounters) {
            if ($values[$name] -ne 0) {
                throw "The '$name' counter records a non-passing or unfinished test."
            }
        }

        $resultSets = @($doc.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']"))
        if ($resultSets.Count -ne 1) {
            throw "Expected exactly one set of recorded results."
        }
        $results = @($resultSets[0].SelectNodes('*'))
        [long]$recordedPassed = 0
        [long]$recordedSkipped = 0
        foreach ($result in $results) {
            $testOutcome = $result.GetAttribute('outcome')
            if ($testOutcome -ceq 'Passed') {
                $recordedPassed++
            }
            elseif ($testOutcome -ceq 'NotExecuted') {
                $recordedSkipped++
            }
            else {
                throw "Recorded test outcome '$testOutcome' is not passed or skipped."
            }
        }
        # NUnit's TRX adapter can leave notExecuted=0 even when skipped results exist.
        if ($values.total -ne $results.Count -or
            $values.passed -ne $recordedPassed -or
            $values.executed -ne $recordedPassed -or
            ($values.notExecuted -ne 0 -and $values.notExecuted -ne $recordedSkipped)) {
            throw "Counters do not match the recorded results; the report is incomplete or inconsistent."
        }

        $passed += $values.passed
        $total += $values.total
        Write-Host "Validated TRX $($file.Name): outcome=$outcome, total=$($values.total), passed=$($values.passed)."
    }

    Write-Host "Aggregated test results: total=$total, passed=$passed (from $($trx.Count) trx file(s))."
    Write-Host "All TRX reports record completed, successful runs."
    exit 0
}
catch {
    Write-Host "##vso[task.logissue type=error]Could not evaluate '$currentReport': $($_.Exception.Message)"
    exit 1
}
