# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoBuild,
    [switch] $ValidateOnly,
    [string] $ResultsDirectory = 'TestResults/lens-desktop'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expected = 24
$expectedMethods = [ordered]@{
    SecureEndpointAcceptReturnsAnonymousAndCancelReturnsNull = 1
    CancellationClosesUnselectableEndpointWithoutChoosingAnonymous = 1
    CatalogOpensActualToolAndTabCloseRestoresWelcome = 1
    RoutedKeyboardCyclesDocumentsWrapsAndRenamesWithoutStealingTextInput = 1
    WorkspaceMenusSaveAndLoadActualDocumentsAndSelectionOffline = 1
    ShutdownCancelsOwnedModalAndWaitsForDocumentCleanupBeforeClosingShell = 1
    InspectorWorkerCompletionsPublishRowsOnTheNativeDispatcher = 1
    CertificateCloseRoutesDismissModalAndRestoreOwnerFocus = 5
    WriteOutcomeRemainsVisibleUntilAcknowledgedAndPendingCloseDrains = 3
    AddItemValidationNamesAndFocusIdentifySamplingAndPercentBounds = 1
    KeyboardAndPointerSelectEveryNamedLaneStyleWithoutChangingConfigurationOrRecords = 1
    MonitorParametersExposeNamesAndUnitsIndependentOfValues = 1
    MinimumSizeKeepsShellActionsReachableAndExplorerCollapseRecoversMonitorLayout = 6
}

Push-Location (Join-Path $PSScriptRoot '../..')
try {
    if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
        throw 'ResultsDirectory must be relative to the repository root.'
    }
    $results = Join-Path $ResultsDirectory 'desktop.trx'
    if (-not $ValidateOnly) {
        if ($IsWindows) {
            if (-not [Environment]::UserInteractive) {
                throw 'Desktop tests require an interactive Windows desktop, not a service-only session.'
            }
        }
        elseif ($IsLinux) {
            if ([string]::IsNullOrWhiteSpace($env:DISPLAY)) {
                throw 'Desktop tests require a reachable X11 DISPLAY. No headless fallback is used.'
            }
        }
        else {
            throw 'The supported desktop regression runners are Windows and Linux X11.'
        }

        New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
        if (Test-Path -LiteralPath $results) {
            Remove-Item -LiteralPath $results
        }
        $filter = ($expectedMethods.Keys | ForEach-Object { "FullyQualifiedName~.$_" }) -join '|'
        $arguments = @(
            'test', 'tests/Opc.Ua.Lens.Desktop.Tests/Opc.Ua.Lens.Desktop.Tests.csproj',
            '--no-restore', '-p:CustomTestTarget=net10.0', '--framework', 'net10.0',
            '--configuration', $Configuration, '--filter', $filter,
            '--logger', 'trx;LogFileName=desktop.trx', '--results-directory', $ResultsDirectory,
            '-m:1'
        )
        if ($NoBuild) {
            $arguments += '--no-build'
        }
        $arguments += @('--', 'NUnit.ExplicitMode=Relaxed', 'NUnit.NumberOfTestWorkers=0')
        Write-Host "Running $expected expected desktop tests on the real platform backend."
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Desktop NUnit runner failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path -LiteralPath $results -PathType Leaf)) {
        throw "Desktop runner produced no TRX: $results"
    }
    [xml] $report = Get-Content -LiteralPath $results -Raw
    $summary = $report.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']")
    if ($null -eq $summary -or $summary.GetAttribute('outcome') -notin @('Completed', 'Passed')) {
        throw 'Desktop TRX reports an incomplete or failed test run.'
    }
    $counters = $report.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters) {
        throw 'Desktop TRX has no result counters.'
    }
    foreach ($counter in @('total', 'executed', 'passed')) {
        if ([int] $counters.GetAttribute($counter) -ne $expected) {
            throw "Desktop count mismatch: $counter=$($counters.GetAttribute($counter)); expected $expected."
        }
    }
    foreach ($counter in @('failed', 'error', 'timeout', 'aborted', 'inconclusive', 'notExecuted')) {
        if ([int] $counters.GetAttribute($counter) -ne 0) {
            throw "Desktop result is not a pass: $counter=$($counters.GetAttribute($counter))."
        }
    }
    $cases = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']"))
    if ($cases.Count -ne $expected -or @($cases | Where-Object outcome -NE 'Passed').Count -ne 0) {
        throw 'Every expected desktop case must have one Passed result; skips and missing cases fail the lane.'
    }
    if (@($cases | Select-Object -ExpandProperty testId -Unique).Count -ne $expected) {
        throw 'Desktop TRX contains duplicate test identities.'
    }
    foreach ($method in $expectedMethods.Keys) {
        $matched = @($cases | Where-Object {
            $_.testName -eq $method -or $_.testName.StartsWith("$method(", [StringComparison]::Ordinal)
        })
        if ($matched.Count -ne $expectedMethods[$method]) {
            throw "Expected $($expectedMethods[$method]) result(s) for $method; found $($matched.Count)."
        }
    }
    Write-Host "Desktop regression passed: $expected/$expected executed and passed; zero skips."
}
finally {
    Pop-Location
}
