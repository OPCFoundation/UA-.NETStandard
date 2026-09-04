# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/

<#
.SYNOPSIS
Regenerates - or verifies - the pinned WoT specification statement-digest
inventory by ingesting the specification's own requirement ledgers at an exact
commit.

.DESCRIPTION
The stack-side evidence ledger
(tests/Opc.Ua.Types.Tests/Wot/Assets/wot-spec-requirements.json) maps each WoT
specification requirement onto the tests that prove it, and carries that
requirement's statementHash so a restatement upstream invalidates the mapping
rather than silently keeping evidence for something the specification no longer
says.

A hash nothing checks is a hash nobody can trust. This script is what checks
them, and it does so by reading the specification's published ledgers rather
than by re-deriving them:

    source/wot-specs/WoT-Binding/tools/requirements.json
    source/wot-specs/WoT-Connectivity/tools/requirements.json

Those files are generated upstream by `tools/check_requirements.py --update`,
which enumerates normative statements with `tools/normative.py`. A statement is
not a line: a paragraph is split into sentences, a normative table row into
cells, a fenced block contributes nothing, and code spans and link targets are
masked before splitting so a '.' inside one never ends a sentence. Re-deriving
that from Markdown is how a second, disagreeing implementation gets written, so
this script does not: it copies what the specification published and verifies
it.

The reads go through `git show <commit>:<path>`, so a dirty working tree, a
checked-out branch or a stale file cannot change the answer. The commit, its
tree, and each ledger's blob id and SHA-256 are recorded in the output, so a
re-vendor from another revision is a visible edit rather than a silent one.

What it refuses:

  * a commit git cannot resolve in the given checkout;
  * a commit other than the one the stack ledger pins;
  * a ledger whose schemaVersion is not the one this script reads;
  * a requirement identifier that appears twice, upstream or in the stack
    ledger;
  * a stack-ledger identifier the specification does not state;
  * a selected set that is not exactly the upstream set marked
    `pendingStackTests`, so an identifier added or dropped upstream fails;
  * a record whose `statement` and `statementHash` disagree - a restatement
    that was not re-hashed;
  * a statementHash, clause, specification or applicability the stack ledger
    records differently - a half-update;
  * in -Verify, an output file whose bytes differ from what the pinned sources
    produce.

.PARAMETER SpecRoot
The root of a spec-drafts checkout - the directory holding 'source/wot-specs'.
The commit only has to be present in the object database; it does not have to be
checked out.

.PARAMETER Commit
The commit to read. Defaults to the commit the stack ledger pins, and is
rejected when it resolves to anything else.

.PARAMETER LedgerPath
The stack-side evidence ledger the inventory is scoped to and checked against.

.PARAMETER OutputPath
Where to write, or - with -Verify - what to compare against.

.PARAMETER Verify
Produce the inventory and compare it with OutputPath instead of writing it. A
byte difference is a failure and names what moved.

.PARAMETER IncludeStatements
Also write each requirement's statement text. Off by default: the statements are
the normative prose of a members-only draft, and the digest is what this
repository needs. Useful locally when reading a diff.

.EXAMPLE
./Get-WotStatementDigests.ps1 -SpecRoot D:/git/spec-drafts

.EXAMPLE
./Get-WotStatementDigests.ps1 -SpecRoot D:/git/spec-drafts -Verify
Reproduces the vendored inventory from the pinned sources and fails if a single
byte differs. That is the provenance check.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SpecRoot,

    [string] $Commit = '',

    [string] $LedgerPath = '',

    [string] $OutputPath = '',

    [switch] $Verify,

    [switch] $IncludeStatements
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
if ([string]::IsNullOrWhiteSpace($LedgerPath)) {
    $LedgerPath = Join-Path $repoRoot 'tests/Opc.Ua.Types.Tests/Wot/Assets/wot-spec-requirements.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'tests/Opc.Ua.Types.Tests/Wot/Assets/wot-spec-statements.json'
}

$ledgerPaths = @(
    'source/wot-specs/WoT-Binding/tools/requirements.json'
    'source/wot-specs/WoT-Connectivity/tools/requirements.json'
)
$upstreamSchemaVersion = 1
$inventorySchemaVersion = 2

function Invoke-GitBytes {
    <#
    .SYNOPSIS
    Runs git and returns its standard output as bytes, so a blob is read exactly
    as it is stored rather than as the console encoding renders it.
    #>
    param(
        [string] $Root,
        [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('git')
    $startInfo.WorkingDirectory = $Root
    foreach ($argument in $Arguments) { $null = $startInfo.ArgumentList.Add($argument) }
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $buffer = [System.IO.MemoryStream]::new()
    $process.StandardOutput.BaseStream.CopyTo($buffer)
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw ("git {0} failed in '{1}': {2}" -f ($Arguments -join ' '), $Root, $standardError.Trim())
    }
    return $buffer.ToArray()
}

function Invoke-GitText {
    param(
        [string] $Root,
        [string[]] $Arguments
    )

    return [System.Text.Encoding]::UTF8.GetString((Invoke-GitBytes -Root $Root -Arguments $Arguments)).Trim()
}

function Get-Sha256Hex {
    param([byte[]] $Bytes)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.Convert]::ToHexString($algorithm.ComputeHash($Bytes)).ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function ConvertTo-JsonString {
    <#
    .SYNOPSIS
    Writes a JSON string literal with the minimal escaping, so the output bytes
    are reproducible and do not depend on a serializer's escaping policy.
    #>
    param([string] $Value)

    $builder = [System.Text.StringBuilder]::new($Value.Length + 2)
    $null = $builder.Append('"')
    foreach ($character in $Value.ToCharArray()) {
        switch ($character) {
            '"' { $null = $builder.Append('\"'); continue }
            '\' { $null = $builder.Append('\\'); continue }
            "`b" { $null = $builder.Append('\b'); continue }
            "`f" { $null = $builder.Append('\f'); continue }
            "`n" { $null = $builder.Append('\n'); continue }
            "`r" { $null = $builder.Append('\r'); continue }
            "`t" { $null = $builder.Append('\t'); continue }
            default {
                if ([int] $character -lt 0x20) {
                    $null = $builder.AppendFormat([cultureinfo]::InvariantCulture, '\u{0:x4}', [int] $character)
                }
                else {
                    $null = $builder.Append($character)
                }
            }
        }
    }
    $null = $builder.Append('"')
    return $builder.ToString()
}

function ConvertTo-DeterministicJson {
    <#
    .SYNOPSIS
    Serializes ordered dictionaries, arrays and scalars with two-space
    indentation and no re-ordering, so regenerating from the same sources
    produces the same bytes.
    #>
    param(
        $Value,
        [int] $Indent = 0
    )

    $pad = ' ' * $Indent
    $inner = ' ' * ($Indent + 2)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return ConvertTo-JsonString $Value }
    if ($Value -is [bool]) { return $(if ($Value) { 'true' } else { 'false' }) }
    if ($Value -is [int] -or $Value -is [long]) {
        return $Value.ToString([cultureinfo]::InvariantCulture)
    }
    if ($Value -is [System.Collections.IDictionary]) {
        if ($Value.Count -eq 0) { return '{}' }
        $parts = foreach ($key in $Value.Keys) {
            '{0}{1}: {2}' -f $inner, (ConvertTo-JsonString ([string] $key)),
            (ConvertTo-DeterministicJson -Value $Value[$key] -Indent ($Indent + 2))
        }
        return "{`n" + ($parts -join ",`n") + "`n$pad}"
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @($Value)
        if ($items.Count -eq 0) { return '[]' }
        $parts = foreach ($item in $items) {
            $inner + (ConvertTo-DeterministicJson -Value $item -Indent ($Indent + 2))
        }
        return "[`n" + ($parts -join ",`n") + "`n$pad]"
    }
    throw "Cannot serialize a value of type '$($Value.GetType().FullName)'."
}

if (-not (Test-Path -LiteralPath $LedgerPath)) {
    throw "The stack evidence ledger '$LedgerPath' does not exist."
}
$stackLedger = [System.Text.Encoding]::UTF8.GetString(
    [System.IO.File]::ReadAllBytes($LedgerPath)) | ConvertFrom-Json
$pinnedCommit = $stackLedger.pinnedTo.commit
if ([string]::IsNullOrWhiteSpace($Commit)) { $Commit = $pinnedCommit }

if (-not (Test-Path -LiteralPath (Join-Path $SpecRoot '.git'))) {
    throw "The specification checkout '$SpecRoot' is not a git repository."
}

# An unresolvable commit is a different failure from a wrong one, and the
# message has to say which, because the remedy differs: fetch, or re-pin.
try {
    $resolved = Invoke-GitText -Root $SpecRoot -Arguments @('rev-parse', '--verify', "$Commit^{commit}")
}
catch {
    throw ("The commit '{0}' is not present in '{1}': {2}" -f $Commit, $SpecRoot, $_.Exception.Message)
}
if ($resolved -ne $pinnedCommit) {
    throw (("The specification sources were read at '{0}', but the stack ledger pins '{1}'. " +
            'Re-pin the ledger, or read the pinned commit.') -f $resolved, $pinnedCommit)
}
$tree = Invoke-GitText -Root $SpecRoot -Arguments @('rev-parse', '--verify', "$resolved^{tree}")

$upstream = [ordered]@{}
$pending = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$sourceRecords = @()
foreach ($path in $ledgerPaths) {
    $blob = Invoke-GitText -Root $SpecRoot -Arguments @('rev-parse', '--verify', "${resolved}:$path")
    $bytes = Invoke-GitBytes -Root $SpecRoot -Arguments @('cat-file', 'blob', $blob)
    $ledger = [System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    if ($ledger.schemaVersion -ne $upstreamSchemaVersion) {
        throw ("The ledger '{0}' states schemaVersion {1}; this script reads {2}." -f `
                $path, $ledger.schemaVersion, $upstreamSchemaVersion)
    }
    $records = @($ledger.requirements)
    $sourceRecords += [ordered]@{
        path             = $path
        specification    = [string] $ledger.specification
        blob             = $blob
        sha256           = Get-Sha256Hex $bytes
        requirementCount = $records.Count
    }
    foreach ($record in $records) {
        if ($upstream.Contains($record.id)) {
            throw (("The requirement '{0}' is stated twice upstream; an identifier names one " +
                    'statement or it names nothing.') -f $record.id)
        }
        $upstream[$record.id] = [pscustomobject]@{
            Specification = [string] $ledger.specification
            Record        = $record
        }
        if ($record.PSObject.Properties.Name -contains 'pendingStackTests' -and $record.pendingStackTests) {
            $null = $pending.Add([string] $record.id)
        }
    }
}

$selected = @()
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($requirement in @($stackLedger.requirements)) {
    $specId = [string] $requirement.specId
    if (-not $seen.Add($specId)) {
        throw "The stack ledger records '$specId' twice; one of the two mappings is hidden."
    }
    if (-not $upstream.Contains($specId)) {
        throw ("The stack ledger records '{0}', which the specification does not state at {1}." -f `
                $specId, $resolved)
    }
    $entry = $upstream[$specId]
    $record = $entry.Record
    $statement = [string] $record.statement
    $recomputed = 'sha256:' + (Get-Sha256Hex ([System.Text.Encoding]::UTF8.GetBytes($statement)))
    if ($recomputed -ne $record.statementHash) {
        throw (("The upstream statement of '{0}' does not hash to the digest recorded beside " +
                'it; the specification was restated without re-hashing.') -f $specId)
    }
    if ($record.statementHash -ne $requirement.statementHash) {
        throw ("The stack ledger records {0} for '{1}'; the specification states {2}." -f `
                $requirement.statementHash, $specId, $record.statementHash)
    }
    if ($entry.Specification -ne $requirement.specification) {
        throw ("The stack ledger places '{0}' in '{1}'; the specification states '{2}'." -f `
                $specId, $requirement.specification, $entry.Specification)
    }
    if ($record.clause -ne $requirement.clause) {
        throw ("The stack ledger places '{0}' in clause '{1}'; the specification states '{2}'." -f `
                $specId, $requirement.clause, $record.clause)
    }
    if ($record.applicability -ne $requirement.applicability) {
        throw (("The stack ledger records applicability '{0}' for '{1}'; the specification " +
                "states '{2}'.") -f $requirement.applicability, $specId, $record.applicability)
    }
    $evidence = @($record.evidence)
    if ($evidence -notcontains 'stack') {
        throw (("The specification does not leave '{0}' to an implementation, so this " +
                'repository has no business answering for it.') -f $specId)
    }

    $separator = $specId.LastIndexOf('#')
    $ordinal = [int]::Parse($specId.Substring($separator + 1), [cultureinfo]::InvariantCulture)
    $entryOut = [ordered]@{
        specId          = $specId
        specification   = $entry.Specification
        clause          = [string] $record.clause
        ordinal         = $ordinal
        keywords        = @($record.keywords | ForEach-Object { [string] $_ })
        applicability   = [string] $record.applicability
        evidence        = @($evidence | ForEach-Object { [string] $_ })
        statementLength = [System.Text.Encoding]::UTF8.GetByteCount($statement)
        statementHash   = [string] $record.statementHash
    }
    if ($IncludeStatements.IsPresent) {
        $entryOut['statement'] = $statement
    }
    $selected += $entryOut
}

# The selection is not "whatever the stack ledger happens to list": it is exactly
# the set the specification marked as left to an implementation, so an identifier
# added or dropped upstream fails here rather than passing unnoticed.
$extra = @($pending | Where-Object { -not $seen.Contains($_) } | Sort-Object)
if ($extra.Count -gt 0) {
    throw (("The specification leaves {0} requirement(s) to this stack that the ledger does " +
            'not record: {1}.') -f $extra.Count, ($extra -join ', '))
}
$dropped = @($seen | Where-Object { -not $pending.Contains($_) } | Sort-Object)
if ($dropped.Count -gt 0) {
    throw (("The ledger records {0} requirement(s) the specification no longer leaves to this " +
            'stack: {1}.') -f $dropped.Count, ($dropped -join ', '))
}

$selected = @($selected | Sort-Object -Property specId -CaseSensitive)
$document = [ordered]@{
    '$comment'     = @(
        'Pinned statement-digest inventory for the WoT specification requirements this stack'
        'answers for. Every record is copied from the specification''s own requirement'
        'ledgers at the pinned commit - not re-derived from the prose - and verified against'
        'them: the statement is re-hashed and the digest, clause, specification and'
        'applicability are checked against wot-spec-requirements.json.'
        ''
        'This file exists so those statementHash values are checked rather than merely'
        'carried. The stack ledger pins this file by digest, so a statementHash edited in one'
        'file and not the other fails; this file pins the commit, its tree, and the blob id'
        'and SHA-256 of each upstream ledger, so a re-vendor from another revision is a'
        'visible edit.'
        ''
        'The statements themselves are the normative prose of a members-only draft and are'
        'not republished here; their digests and lengths are. Regenerate or check with'
        'tools/wot-spec/Get-WotStatementDigests.ps1 against a spec-drafts checkout holding'
        'the pinned commit; -Verify reproduces this file and fails on any byte that moved.'
    )
    schemaVersion  = $inventorySchemaVersion
    pinnedTo       = [ordered]@{
        repository    = [string] $stackLedger.pinnedTo.repository
        commit        = $resolved
        tree          = $tree
        selection     = 'every upstream requirement marked pendingStackTests'
        normalization = 'sha256 of the statement as the specification''s tools/normative.py ' +
        'normalizes it; the generator recomputes it from the upstream statement and refuses ' +
        'a record whose text and digest disagree'
        ledgers       = @($sourceRecords)
    }
    statementCount = $selected.Count
    statements     = @($selected)
}

$json = (ConvertTo-DeterministicJson -Value $document) + "`n"
$encoding = [System.Text.UTF8Encoding]::new($false)
$produced = $encoding.GetBytes($json)

if ($Verify.IsPresent) {
    if (-not (Test-Path -LiteralPath $OutputPath)) {
        throw "There is no inventory at '$OutputPath' to verify."
    }
    $existing = [System.IO.File]::ReadAllBytes($OutputPath)
    if ($existing.Length -ne $produced.Length -or
        (Get-Sha256Hex $existing) -ne (Get-Sha256Hex $produced)) {
        throw (("The inventory at '{0}' is not what commit {1} produces (vendored sha256 {2}, " +
                'produced sha256 {3}). Regenerate it.') -f `
                $OutputPath, $resolved, (Get-Sha256Hex $existing), (Get-Sha256Hex $produced))
    }
    Write-Host ("Verified {0} statement digest(s) against {1}." -f $selected.Count, $resolved)
    return
}

[System.IO.File]::WriteAllBytes($OutputPath, $produced)
Write-Host ("Wrote {0} statement digest(s) from {1} to {2}." -f $selected.Count, $resolved, $OutputPath)
Write-Host ("Pin this inventory in the stack ledger as sha256 {0}." -f (Get-Sha256Hex $produced))
