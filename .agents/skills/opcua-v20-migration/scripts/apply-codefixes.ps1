#!/usr/bin/env pwsh
# ========================================================================
# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person
# obtaining a copy of this software and associated documentation
# files (the "Software"), to deal in the Software without
# restriction, including without limitation the rights to use,
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the
# Software is furnished to do so, subject to the following
# conditions:
#
# The above copyright notice and this permission notice shall be
# included in all copies or substantial portions of the Software.
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
# EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
# OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
# NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
# HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/
# ========================================================================

<#
.SYNOPSIS
    Apply all auto-fixable OPC UA migration analyzer rules in one pass.

.DESCRIPTION
    Wraps `dotnet format analyzers ... --diagnostics UA0002 ... --severity warn`
    against the auto-fixable subset of the UA00xx rule set shipped by the
    OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer NuGet. Auto-discovers the
    solution file when not given, and reports the before/after analyzer warning
    counts so you can see how much progress the pass made.

    The 14 auto-fixable rules covered:
        UA0002 - <Type>Collection -> List<T>
        UA0003 - == null on now-struct types -> .IsNull
        UA0004 - ?. on now-struct types -> direct access
        UA0005 - byte[] -> ByteString
        UA0006 - new Variant(...) -> Variant.From(...)
        UA0007 - new NodeId(string) -> NodeId.Parse
        UA0008 - Session.Call(..., params object[]) -> wrap with Variant.From
        UA0009 - [DataContract]/[DataMember] -> [DataType]/[DataTypeField]
        UA0010 - using/Dispose on cert/identity types -> drop disposable
        UA0012 - CertificateFactory.* static -> DefaultCertificateFactory.Instance
        UA0014 - DataValue.IsGood(dv) -> dv.IsGood
        UA0019 - new DataValue(StatusCode, ts) -> DataValue.FromStatusCode
        UA0020 - EncodeableFactory.Create() -> Fork()
        UA0022 - .CertificateValidator -> .CertificateManager

    The 11 manual residuals are left for human follow-up:
        UA0001 (telemetry plumbing), UA0011 / UA0015 (sync->async promotion),
        UA0018 (cert load async refactor), UA0021 (CertificateValidator
        structural rewrite), and UA0023-UA0028 (PubSub and removed exposed-lock
        APIs).

    SecurityPolicies compatibility calls marked UA0029 surface as compiler
    CS0618 warnings rather than an analyzer diagnostic and also require manual
    migration.

.PARAMETER Solution
    Path to a .sln or .slnx file. If omitted, the script searches the current
    directory and walks up to 3 parents for the first match.

.PARAMETER Severity
    Severity threshold passed to `dotnet format analyzers --severity`. Defaults
    to 'warn' (most UA rules emit Warning by default). Use 'info' to also pick
    up UA0001 / UA0011 / UA0015 / UA0018 / UA0021 markers (but those rules have
    no code-fix, so the pass is just informational for them).

.PARAMETER ExtraDiagnostics
    Additional diagnostic IDs to include in the auto-fix pass beyond the
    default UA00xx set. Comma- or whitespace-separated. Useful for combining
    with other repo-specific analyzers.

.PARAMETER DryRun
    Pass `--verify-no-changes` to `dotnet format analyzers`. The command then
    fails with a non-zero exit code if any fix would have been applied — useful
    in CI to assert the source tree is fully migrated. The script propagates
    that exit code immediately; it does not print a post-fix summary because a
    dry run intentionally leaves the source unchanged.

.EXAMPLE
    pwsh ./scripts/apply-codefixes.ps1
    # Auto-discovers the solution, applies all UA00xx auto-fixes.

.EXAMPLE
    pwsh ./scripts/apply-codefixes.ps1 -Solution MyApp.sln -Severity info
    # Includes Info-level diagnostics; useful for an audit pass.

.EXAMPLE
    pwsh ./scripts/apply-codefixes.ps1 -DryRun
    # CI-friendly: exits non-zero if any fix would have been applied.

.NOTES
    Requires .NET SDK 10.0.300+ (for `dotnet format analyzers`).
    Part of the .agents/skills/opcua-v20-migration skill.
#>

[CmdletBinding()]
param(
    [string]$Solution,

    [ValidateSet('error', 'warn', 'info')]
    [string]$Severity = 'warn',

    [string]$ExtraDiagnostics = '',

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# The 14 auto-fixable UA00xx rules. Keep this list in sync with the analyzer
# package's NugetREADME.md "Auto-fix" column and references/analyzer-rules.md.
$AutoFixableRules = @(
    'UA0002', 'UA0003', 'UA0004', 'UA0005', 'UA0006', 'UA0007', 'UA0008',
    'UA0009', 'UA0010', 'UA0012', 'UA0014', 'UA0019', 'UA0020', 'UA0022'
)

function Find-Solution {
    [CmdletBinding()]
    param([string]$StartDir)

    $dir = Get-Item $StartDir
    for ($depth = 0; $depth -lt 4 -and $dir; $depth++) {
        $candidate = Get-ChildItem -Path $dir.FullName -Filter "*.slnx" -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }

        $candidate = Get-ChildItem -Path $dir.FullName -Filter "*.sln" -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }

        $dir = $dir.Parent
    }
    return $null
}

function Get-AnalyzerWarningCounts {
    [CmdletBinding()]
    param([string]$SolutionPath)

    $buildOutput = & dotnet build $SolutionPath --nologo --no-incremental /p:WarningLevel=4 2>&1
    $uniqueBuildLines = $buildOutput | ForEach-Object { "$_" } | Sort-Object -Unique
    $ua = ($uniqueBuildLines | Select-String -Pattern ': warning UA\d{4}' | Measure-Object).Count
    $mig = ($uniqueBuildLines | Select-String -Pattern ': warning MIG\d{2}' | Measure-Object).Count
    $obsolete = ($uniqueBuildLines | Select-String -Pattern ': warning CS061[28]' | Measure-Object).Count
    $errors = ($uniqueBuildLines | Select-String -Pattern ': error ' | Measure-Object).Count
    return [pscustomobject]@{
        UaWarnings       = $ua
        MigWarnings      = $mig
        ObsoleteWarnings = $obsolete
        Errors           = $errors
    }
}

# Resolve solution path
if (-not $Solution) {
    Write-Host "Auto-discovering solution file..." -ForegroundColor Cyan
    $Solution = Find-Solution -StartDir (Get-Location)
    if (-not $Solution) {
        throw (
            "Could not find a .sln or .slnx file in the current directory or up to 3 parents above. " +
            "Pass -Solution explicitly."
        )
    }
    Write-Host "Found: $Solution" -ForegroundColor Cyan
}

if (-not (Test-Path $Solution)) {
    throw "Solution file not found: $Solution"
}

# Build the diagnostic list
$diagnostics = $AutoFixableRules
if ($ExtraDiagnostics) {
    $extras = $ExtraDiagnostics -split '[,\s]+' | Where-Object { $_ }
    $diagnostics = @($diagnostics) + $extras | Sort-Object -Unique
}

Write-Host ""
Write-Host "=== Baseline (pre-fix) warning counts ===" -ForegroundColor Cyan
$before = Get-AnalyzerWarningCounts -SolutionPath $Solution
$before | Format-List

# Run the format pass
Write-Host "=== Applying auto-fixes ===" -ForegroundColor Cyan
$args = @(
    'format', 'analyzers', $Solution,
    '--severity', $Severity,
    '--diagnostics'
) + $diagnostics

if ($DryRun) {
    $args += '--verify-no-changes'
    Write-Host "(dry-run: --verify-no-changes; will exit non-zero if any fix would apply)" -ForegroundColor Yellow
}

Write-Host "dotnet $($args -join ' ')" -ForegroundColor DarkGray
& dotnet @args
$formatExit = $LASTEXITCODE

if ($formatExit -ne 0) {
    if ($DryRun) {
        Write-Host (
            "Dry run found pending fixes or dotnet format failed (exit code $formatExit)."
        ) -ForegroundColor Yellow
    }
    else {
        Write-Host "dotnet format failed with exit code $formatExit" -ForegroundColor Red
    }
    exit $formatExit
}

# Re-measure
Write-Host ""
Write-Host "=== Post-fix warning counts ===" -ForegroundColor Cyan
$after = Get-AnalyzerWarningCounts -SolutionPath $Solution
$after | Format-List

# Delta summary
Write-Host "=== Delta ===" -ForegroundColor Cyan
$summary = [pscustomobject]@{
    Rule              = 'UA00xx warnings'
    Before            = $before.UaWarnings
    After             = $after.UaWarnings
    Fixed             = $before.UaWarnings - $after.UaWarnings
}
$summary | Format-Table
Write-Host ("CS0612/CS0618 (obsolete): {0} -> {1}" -f $before.ObsoleteWarnings, $after.ObsoleteWarnings)
Write-Host ("MIG01:                    {0} -> {1}" -f $before.MigWarnings, $after.MigWarnings)
Write-Host ("Errors:                   {0} -> {1}" -f $before.Errors, $after.Errors)

if ($after.UaWarnings -gt 0) {
    Write-Host ""
    Write-Host "Remaining UA00xx warnings - inspect manually:" -ForegroundColor Yellow
    Write-Host "  UA0001 (Utils.Trace -> ILogger)" -ForegroundColor DarkYellow
    Write-Host "  UA0011 (sync token-handler -> Async)" -ForegroundColor DarkYellow
    Write-Host "  UA0015 (sync GDS/LDS -> Async)" -ForegroundColor DarkYellow
    Write-Host (
        "  UA0018 (CertificateIdentifier.Certificate -> CertificateIdentifierResolver.ResolveAsync)"
    ) -ForegroundColor DarkYellow
    Write-Host "  UA0021 (CertificateValidator -> CertificateManager structural rewrite)" -ForegroundColor DarkYellow
    Write-Host "  UA0023 (legacy PubSub API -> builder / DI)" -ForegroundColor DarkYellow
    Write-Host "  UA0024 (diagnostics locks -> update/read methods)" -ForegroundColor DarkYellow
    Write-Host "  UA0025 (node DataLock -> node-owned synchronization)" -ForegroundColor DarkYellow
    Write-Host "  UA0026 (BaseVariableValue.Lock -> caller-owned Lock)" -ForegroundColor DarkYellow
    Write-Host "  UA0027 (NodeBrowser.DataLock -> single-consumer access)" -ForegroundColor DarkYellow
    Write-Host "  UA0028 (PropertiesLock -> concurrent properties APIs)" -ForegroundColor DarkYellow
    Write-Host "  See references/migration-patterns.md for the categorical playbook." -ForegroundColor DarkYellow
}

if ($after.ObsoleteWarnings -gt 0) {
    Write-Host ""
    Write-Host (
        "Remaining CS0612/CS0618 warnings - migrate shim calls before removing the package."
    ) -ForegroundColor Yellow
    Write-Host (
        "SecurityPolicies shim messages tagged UA0029 require manual ISecurityPolicyRegistry migration."
    ) -ForegroundColor DarkYellow
}

if ($after.MigWarnings -gt 0) {
    Write-Host ""
    Write-Host "MIG01 warnings - the source generator couldn't resolve an element type." -ForegroundColor Yellow
    Write-Host (
        "Migrate the site manually or define the wrapper explicitly. " +
        "See references/source-generator.md."
    ) -ForegroundColor DarkYellow
}

if ($after.Errors -gt 0) {
    Write-Host ""
    Write-Host "$($after.Errors) build error(s) remain. See references/migration-patterns.md." -ForegroundColor Red
    exit 1
}

exit 0
