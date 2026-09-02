<#
 .SYNOPSIS
    Enforces the repository's code-coverage gates against a Cobertura report.

 .DESCRIPTION
    The enforced coverage gate, run inside the pipeline so it can react to how
    much a patch actually changed. codecov.io is still uploaded to, but purely
    for reporting - its own status checks are `informational: true` (see
    codecov.yml) precisely so there is only ever one gate. Three checks are made
    against the merged Cobertura report produced by ReportGenerator:

      1. Project floor (BLOCKING)  - total line and branch rates must meet the
                                     absolute floors in coverage-thresholds.json.
      2. Patch coverage (GRADUATED)- lines added or modified relative to the pull
                                     request's base must reach a floor that
                                     scales with the size of the patch: small
                                     patches warn, large ones fail. See
                                     'patch.bands' in coverage-thresholds.json.
      3. Baseline delta (ADVISORY) - reports how the current total line rate
                                     compares to the recorded master baseline.
                                     Never fails the build.

    Files matching the 'ignore' globs in coverage-thresholds.json are excluded
    from the patch calculation. Keep that list in step with the 'ignore' list in
    codecov.yml so both report on the same code.

    When no base ref is supplied - a scheduled or master build rather than a pull
    request - the patch gate is skipped and only the project floor is enforced.

 .PARAMETER CoberturaPath
    Path to the merged Cobertura XML report.

 .PARAMETER ThresholdsPath
    Path to coverage-thresholds.json. Defaults to the file at the repository root.

 .PARAMETER BaseRef
    Git ref of the pull request's base branch, for example 'refs/heads/master' or
    'master'. When empty the patch gate is skipped.

 .PARAMETER RepoRoot
    Repository root used to make coverage file paths relative. Defaults to the
    parent of this script's directory.

 .PARAMETER SkipFetch
    Do not run 'git fetch' for the base ref. Used by the unit tests, which
    operate on a purpose-built local repository.

 .PARAMETER SummaryPath
    Optional path to write a markdown summary of the gate result to. Azure
    Pipelines attaches it with '##vso[task.uploadsummary]' and GitHub Actions
    appends it to $GITHUB_STEP_SUMMARY and to the pull request comment, so the
    numbers are visible without opening the log.
#>

[CmdletBinding()]
Param(
    [Parameter(Mandatory = $true)]
    [string] $CoberturaPath,
    [string] $ThresholdsPath = '',
    [string] $BaseRef = '',
    [string] $RepoRoot = '',
    [switch] $SkipFetch,
    [string] $SummaryPath = ''
)

$ErrorActionPreference = 'Stop'

# Pin the culture so rates parse and format identically on every agent. Cobertura
# writes rates as invariant decimals ("0.75"), and a log that reads "75,00%" on one
# agent and "75.00%" on another makes the gate output needlessly hard to compare.
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
[System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::InvariantCulture

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).ProviderPath
if ([string]::IsNullOrWhiteSpace($ThresholdsPath)) {
    $ThresholdsPath = Join-Path $RepoRoot 'coverage-thresholds.json'
}

$script:IsAzurePipeline = -not [string]::IsNullOrEmpty($env:TF_BUILD)
$script:IsGitHubActions = $env:GITHUB_ACTIONS -eq 'true'

function Write-GateError([string] $message) {
    if ($script:IsAzurePipeline) {
        Write-Host "##vso[task.logissue type=error]$message"
    }
    if ($script:IsGitHubActions) {
        # Workflow command: surfaces the message as an annotation on the run.
        Write-Host "::error title=Coverage gate::$message"
    }
    Write-Host "ERROR: $message"
}

function Write-GateWarning([string] $message) {
    if ($script:IsAzurePipeline) {
        Write-Host "##vso[task.logissue type=warning]$message"
    }
    if ($script:IsGitHubActions) {
        Write-Host "::warning title=Coverage gate::$message"
    }
    Write-Host "WARNING: $message"
}

<#
.SYNOPSIS
Selects the patch-coverage requirement that applies to a change of a given size.

.DESCRIPTION
A coverage percentage over a handful of lines carries almost no information: a
single uncovered line in a two-line fix reads as 50%, which a flat floor would
fail even though nothing is wrong. Small changes therefore get a lower bar and
report a warning rather than a failure, while changes large enough for the
percentage to mean something are enforced. Bands are consulted in order and the
first one whose maxChangedLines covers the patch wins; anything larger falls
through to the enforced target.

.PARAMETER changedLines
Number of coverable changed lines in the patch.

.PARAMETER patch
The 'patch' object from coverage-thresholds.json.
#>
function Get-PatchBand([int] $changedLines, $patch) {
    $bands = @($patch.bands)
    foreach ($band in $bands) {
        if ($null -eq $band) { continue }
        if ($changedLines -le [int]$band.maxChangedLines) {
            return [pscustomobject]@{
                Floor    = [double]$band.target
                Enforced = [bool]$band.enforced
                Scope    = ('<= {0} changed lines' -f [int]$band.maxChangedLines)
            }
        }
    }

    $largest = if ($bands.Count -gt 0) { [int]$bands[-1].maxChangedLines } else { 0 }
    return [pscustomobject]@{
        Floor    = [double]$patch.target - [double]$patch.threshold
        Enforced = $true
        Scope    = ('> {0} changed lines' -f $largest)
    }
}

# Markdown summary accumulated as the gate runs and written to -SummaryPath at
# the end. Kept separate from the console output because the console log is a
# flat transcript while this is rendered as a table in both CI UIs.
$script:SummaryRows = [System.Collections.Generic.List[string]]::new()
$script:SummaryNotes = [System.Collections.Generic.List[string]]::new()

<#
 .SYNOPSIS
    Adds a row to the markdown summary table.

 .PARAMETER check
    Name of the check, for example 'Project line rate'.

 .PARAMETER value
    The measured value, already formatted.

 .PARAMETER target
    The threshold the value is compared against, or '-' when there is none.

 .PARAMETER state
    One of 'pass', 'fail', 'warn' or 'info'.
#>
function Add-SummaryRow([string] $check, [string] $value, [string] $target, [string] $state) {
    $icon = switch ($state) {
        'pass' { ':white_check_mark:' }
        'fail' { ':x:' }
        'warn' { ':warning:' }
        default { ':information_source:' }
    }
    $script:SummaryRows.Add(('| {0} {1} | {2} | {3} |' -f $icon, $check, $value, $target))
}

<#
 .SYNOPSIS
    Adds a free-form markdown note below the summary table.
#>
function Add-SummaryNote([string] $note) {
    $script:SummaryNotes.Add($note)
}

<#
 .SYNOPSIS
    Converts a repository-relative glob into an anchored regular expression.
#>
function ConvertTo-GlobRegex([string] $glob) {
    $normalized = $glob.Replace('\', '/')
    $pattern = [System.Text.StringBuilder]::new()
    $null = $pattern.Append('^')
    $i = 0
    while ($i -lt $normalized.Length) {
        $c = $normalized[$i]
        if ($c -eq '*') {
            if ($i + 1 -lt $normalized.Length -and $normalized[$i + 1] -eq '*') {
                # '**/' matches any number of leading directories, including none.
                if ($i + 2 -lt $normalized.Length -and $normalized[$i + 2] -eq '/') {
                    $null = $pattern.Append('(?:.*/)?')
                    $i += 3
                    continue
                }
                $null = $pattern.Append('.*')
                $i += 2
                continue
            }
            # A single '*' does not cross directory boundaries.
            $null = $pattern.Append('[^/]*')
            $i++
            continue
        }
        if ($c -eq '?') {
            $null = $pattern.Append('[^/]')
            $i++
            continue
        }
        $null = $pattern.Append([regex]::Escape([string]$c))
        $i++
    }
    $null = $pattern.Append('$')
    return $pattern.ToString()
}

<#
 .SYNOPSIS
    Tests whether a repository-relative path matches any of the ignore globs.
#>
function Test-PathIgnored([string] $relativePath, [string[]] $ignoreRegexes) {
    foreach ($regex in $ignoreRegexes) {
        if ([regex]::IsMatch($relativePath, $regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            return $true
        }
    }
    return $false
}

<#
 .SYNOPSIS
    Normalizes a Cobertura filename into a repository-relative forward-slash path.
#>
function ConvertTo-RelativePath([string] $path, [string[]] $sourceRoots, [string] $repoRoot) {
    $normalized = $path.Replace('\', '/')
    $normalizedRepo = $repoRoot.Replace('\', '/').TrimEnd('/')

    # Common case: ReportGenerator emits absolute paths inside the working tree,
    # so stripping the repository root is all that is needed.
    if ($normalized.StartsWith("$normalizedRepo/", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $normalized.Substring($normalizedRepo.Length + 1)
    }

    # Reports merged from several operating systems can retain an absolute
    # filename from a different agent. Re-anchor the longest suffix that exists
    # in this checkout so /Users/.../src/Foo.cs matches src/Foo.cs on Linux.
    if ($normalized.StartsWith('/') -or [System.IO.Path]::IsPathRooted($normalized)) {
        $segments = @($normalized.Trim('/') -split '/')
        for ($index = 1; $index -lt $segments.Count; $index++) {
            $candidate = $segments[$index..($segments.Count - 1)] -join '/'
            if (Test-Path -LiteralPath (Join-Path $repoRoot $candidate) -PathType Leaf) {
                return $candidate
            }
        }
    }

    foreach ($root in $sourceRoots) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $normalizedRoot = $root.Replace('\', '/').TrimEnd('/')
        if ([string]::IsNullOrWhiteSpace($normalizedRoot)) { continue }

        # <source> is the common prefix of every file in the report, which is
        # usually a sub-directory of the repository such as '<repo>/src'. The
        # part of it that lives inside the repository has to be put back, or
        # 'src/Foo/Bar.cs' would collapse to 'Foo/Bar.cs' and stop matching the
        # paths git reports in the diff.
        $prefix = ''
        if ($normalizedRoot.StartsWith("$normalizedRepo/", [System.StringComparison]::OrdinalIgnoreCase)) {
            $prefix = $normalizedRoot.Substring($normalizedRepo.Length + 1)
        }

        if ($normalized.StartsWith("$normalizedRoot/", [System.StringComparison]::OrdinalIgnoreCase)) {
            $tail = $normalized.Substring($normalizedRoot.Length + 1)
            if ($prefix) { return "$prefix/$tail" }
            return $tail
        }

        # Some collectors emit paths relative to <source> rather than to the
        # repository root; re-anchor them when that yields a real file.
        if ($prefix -and -not [System.IO.Path]::IsPathRooted($normalized)) {
            $candidate = "$prefix/$($normalized.TrimStart('/'))"
            if (Test-Path -LiteralPath (Join-Path $repoRoot $candidate)) {
                return $candidate
            }
        }
    }

    return $normalized.TrimStart('/')
}

<#
 .SYNOPSIS
    Reads a Cobertura report and returns overall rates plus per-file line hits.
#>
function Get-CoverageReport([string] $path, [string] $repoRoot) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Cobertura report not found: $path"
    }

    $document = New-Object System.Xml.XmlDocument
    $document.Load((Resolve-Path -LiteralPath $path).Path)

    $root = $document.DocumentElement
    if ($null -eq $root -or $root.Name -ne 'coverage') {
        throw "Not a Cobertura report (expected a <coverage> root element): $path"
    }

    $sourceRoots = @()
    foreach ($source in $document.GetElementsByTagName('source')) {
        if (-not [string]::IsNullOrWhiteSpace($source.InnerText)) {
            $sourceRoots += $source.InnerText.Trim()
        }
    }

    # Per-file map of line number -> hits, and line number -> (branches covered,
    # branches total). The same file can appear under several <class> elements
    # (one per type), so hits are merged with Max: a line counted as covered by
    # any class is covered.
    $fileLines = @{}
    $fileBranches = @{}
    foreach ($class in $document.GetElementsByTagName('class')) {
        $fileName = $class.GetAttribute('filename')
        if ([string]::IsNullOrWhiteSpace($fileName)) { continue }
        $relative = ConvertTo-RelativePath -path $fileName -sourceRoots $sourceRoots -repoRoot $repoRoot
        if (-not $fileLines.ContainsKey($relative)) {
            $fileLines[$relative] = @{}
            $fileBranches[$relative] = @{}
        }
        $map = $fileLines[$relative]
        $branchMap = $fileBranches[$relative]
        foreach ($line in $class.GetElementsByTagName('line')) {
            $numberRaw = $line.GetAttribute('number')
            $hitsRaw = $line.GetAttribute('hits')
            if ([string]::IsNullOrWhiteSpace($numberRaw)) { continue }
            $number = [int]$numberRaw
            $hits = 0
            if (-not [string]::IsNullOrWhiteSpace($hitsRaw)) { $hits = [int]$hitsRaw }
            if ($map.ContainsKey($number)) {
                $map[$number] = [Math]::Max([int]$map[$number], $hits)
            }
            else {
                $map[$number] = $hits
            }

            # condition-coverage looks like '50% (1/2)'.
            if ($line.GetAttribute('branch') -eq 'true') {
                $condition = $line.GetAttribute('condition-coverage')
                $match = [regex]::Match($condition, '\((\d+)/(\d+)\)')
                if ($match.Success) {
                    $covered = [int]$match.Groups[1].Value
                    $total = [int]$match.Groups[2].Value
                    if ($branchMap.ContainsKey($number)) {
                        $existing = $branchMap[$number]
                        $branchMap[$number] = @([Math]::Max($existing[0], $covered), [Math]::Max($existing[1], $total))
                    }
                    else {
                        $branchMap[$number] = @($covered, $total)
                    }
                }
            }
        }
    }

    function Get-Rate([System.Xml.XmlElement] $element, [string] $name) {
        $raw = $element.GetAttribute($name)
        if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
        return [double]$raw * 100.0
    }

    return [pscustomobject]@{
        ReportLineRate   = Get-Rate $root 'line-rate'
        ReportBranchRate = Get-Rate $root 'branch-rate'
        FileLines        = $fileLines
        FileBranches     = $fileBranches
    }
}

<#
 .SYNOPSIS
    Aggregates line and branch totals over the files that are not ignored.

 .DESCRIPTION
    The rates on the Cobertura root element cover everything in the report,
    including samples and any other assembly the report generator happened to
    include. Recomputing the totals here means the project floor and the
    changed-lines gate are governed by exactly the same 'ignore' list.
#>
function Get-FilteredTotals($report, [string[]] $ignoreRegexes) {
    $totalLines = 0
    $coveredLines = 0
    $totalBranches = 0
    $coveredBranches = 0
    $ignoredFiles = 0

    foreach ($file in $report.FileLines.Keys) {
        if (Test-PathIgnored -relativePath $file -ignoreRegexes $ignoreRegexes) {
            $ignoredFiles++
            continue
        }
        foreach ($entry in $report.FileLines[$file].GetEnumerator()) {
            $totalLines++
            if ([int]$entry.Value -gt 0) { $coveredLines++ }
        }
        if ($report.FileBranches.ContainsKey($file)) {
            foreach ($entry in $report.FileBranches[$file].GetEnumerator()) {
                $coveredBranches += [int]$entry.Value[0]
                $totalBranches += [int]$entry.Value[1]
            }
        }
    }

    return [pscustomobject]@{
        LineRate      = if ($totalLines -gt 0) { 100.0 * $coveredLines / $totalLines } else { $null }
        BranchRate    = if ($totalBranches -gt 0) { 100.0 * $coveredBranches / $totalBranches } else { $null }
        CoveredLines  = $coveredLines
        TotalLines    = $totalLines
        IgnoredFiles  = $ignoredFiles
    }
}

<#
 .SYNOPSIS
    Returns a map of repository-relative path -> set of added/modified line numbers.
#>
function Get-ChangedLines([string] $baseRef, [string] $repoRoot, [bool] $skipFetch) {
    $branch = $baseRef -replace '^refs/heads/', ''
    $baseCommit = $null

    Push-Location $repoRoot
    try {
        if (-not $skipFetch) {
            # The pipeline checkout may not have the base branch locally, so fetch
            # it explicitly and resolve through FETCH_HEAD.
            & git fetch --no-tags --quiet origin $branch 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                $baseCommit = (& git rev-parse FETCH_HEAD 2>$null)
            }
        }

        if ([string]::IsNullOrWhiteSpace($baseCommit)) {
            foreach ($candidate in @("origin/$branch", $branch)) {
                $resolved = (& git rev-parse --verify --quiet "$candidate^{commit}" 2>$null)
                if (-not [string]::IsNullOrWhiteSpace($resolved)) {
                    $baseCommit = $resolved
                    break
                }
            }
        }

        if ([string]::IsNullOrWhiteSpace($baseCommit)) {
            throw "Could not resolve the base branch '$branch' to compute changed lines."
        }

        $mergeBase = (& git merge-base $baseCommit HEAD 2>$null)
        if (-not [string]::IsNullOrWhiteSpace($mergeBase)) {
            $baseCommit = $mergeBase.Trim()
        }

        Write-Host "Comparing against base commit $baseCommit (branch '$branch')."
        $diff = & git diff --unified=0 --no-color --diff-filter=d $baseCommit HEAD -- '*.cs'
    }
    finally {
        Pop-Location
    }

    $changed = @{}
    $currentFile = $null
    $inHeader = $false
    foreach ($line in $diff) {
        # Track header state explicitly: with --unified=0 an *added* line whose
        # content happens to start with '++ ' would otherwise look like a '+++ '
        # file header. A header is only valid between 'diff --git' and the first
        # hunk of that file.
        if ($line.StartsWith('diff --git ')) {
            $currentFile = $null
            $inHeader = $true
            continue
        }
        if ($inHeader -and $line.StartsWith('+++ ')) {
            $candidate = $line.Substring(4).Trim()
            if ($candidate -eq '/dev/null') {
                $currentFile = $null
                continue
            }
            # Strip the 'b/' prefix git puts on the post-image path.
            $currentFile = ($candidate -replace '^b/', '').Replace('\', '/')
            continue
        }
        if (-not $line.StartsWith('@@')) { continue }
        $inHeader = $false
        if ($null -eq $currentFile) { continue }

        # @@ -oldStart,oldCount +newStart,newCount @@
        $match = [regex]::Match($line, '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@')
        if (-not $match.Success) { continue }
        $start = [int]$match.Groups[1].Value
        $count = 1
        if ($match.Groups[2].Success) { $count = [int]$match.Groups[2].Value }
        if ($count -le 0) { continue }

        if (-not $changed.ContainsKey($currentFile)) {
            $changed[$currentFile] = [System.Collections.Generic.HashSet[int]]::new()
        }
        for ($n = $start; $n -lt $start + $count; $n++) {
            $null = $changed[$currentFile].Add($n)
        }
    }
    return $changed
}

$thresholds = Get-Content -LiteralPath $ThresholdsPath -Raw | ConvertFrom-Json
$ignoreRegexes = @()
foreach ($glob in @($thresholds.ignore)) {
    $ignoreRegexes += ConvertTo-GlobRegex $glob
}

$report = Get-CoverageReport -path $CoberturaPath -repoRoot $RepoRoot
$totals = Get-FilteredTotals -report $report -ignoreRegexes $ignoreRegexes

Write-Host '--- Coverage gate ---'
Write-Host ("Report:      {0}" -f $CoberturaPath)
Write-Host ("Thresholds:  {0}" -f $ThresholdsPath)
Write-Host ("Line rate:   {0:N2}% ({1}/{2} lines, {3} ignored file(s) excluded)" -f `
    $totals.LineRate, $totals.CoveredLines, $totals.TotalLines, $totals.IgnoredFiles)
Write-Host ("Branch rate: {0:N2}%" -f $totals.BranchRate)
Write-Host ("Whole report (before exclusions): line {0:N2}%, branch {1:N2}%" -f `
    $report.ReportLineRate, $report.ReportBranchRate)

$failures = @()

# 1. Project floor (blocking).
$minLine = [double]$thresholds.project.minimumLineRate
if ($null -eq $totals.LineRate) {
    $failures += 'No coverable lines were found in the report; the run did not produce usable coverage.'
    Add-SummaryRow 'Project line rate' 'no data' ('>= {0:N2}%' -f $minLine) 'fail'
}
elseif ($totals.LineRate -lt $minLine) {
    $failures += ('Total line coverage {0:N2}% is below the required floor of {1:N2}%.' -f $totals.LineRate, $minLine)
    Add-SummaryRow 'Project line rate' ('**{0:N2}%** ({1}/{2} lines)' -f $totals.LineRate, $totals.CoveredLines, $totals.TotalLines) ('>= {0:N2}%' -f $minLine) 'fail'
}
else {
    Add-SummaryRow 'Project line rate' ('**{0:N2}%** ({1}/{2} lines)' -f $totals.LineRate, $totals.CoveredLines, $totals.TotalLines) ('>= {0:N2}%' -f $minLine) 'pass'
}

$minBranch = [double]$thresholds.project.minimumBranchRate
if ($null -ne $totals.BranchRate -and $totals.BranchRate -lt $minBranch) {
    $failures += ('Total branch coverage {0:N2}% is below the required floor of {1:N2}%.' -f $totals.BranchRate, $minBranch)
    Add-SummaryRow 'Project branch rate' ('**{0:N2}%**' -f $totals.BranchRate) ('>= {0:N2}%' -f $minBranch) 'fail'
}
elseif ($null -ne $totals.BranchRate) {
    Add-SummaryRow 'Project branch rate' ('**{0:N2}%**' -f $totals.BranchRate) ('>= {0:N2}%' -f $minBranch) 'pass'
}

# 2. Patch coverage (blocking, pull requests only).
if ([string]::IsNullOrWhiteSpace($BaseRef)) {
    Write-Host 'No base ref supplied; skipping the changed-lines (patch) gate.'
    Add-SummaryRow 'Patch coverage' 'not a pull request' '-' 'info'
}
else {
    $changed = Get-ChangedLines -baseRef $BaseRef -repoRoot $RepoRoot -skipFetch:$SkipFetch.IsPresent

    $coverableChanged = 0
    $coveredChanged = 0
    $uncoveredByFile = [ordered]@{}
    $changedFiles = [System.Collections.Generic.List[string]]::new()
    $matchedFiles = [System.Collections.Generic.List[string]]::new()
    $unmatchedExistingFiles = [System.Collections.Generic.List[string]]::new()

    foreach ($entry in $changed.GetEnumerator()) {
        $file = $entry.Key
        if (Test-PathIgnored -relativePath $file -ignoreRegexes $ignoreRegexes) { continue }
        $changedFiles.Add($file)
        if (-not $report.FileLines.ContainsKey($file)) {
            if (Test-Path -LiteralPath (Join-Path $RepoRoot $file)) {
                $unmatchedExistingFiles.Add($file)
            }
            continue
        }
        $matchedFiles.Add($file)

        $lineHits = $report.FileLines[$file]
        $uncovered = @()
        foreach ($number in ($entry.Value | Sort-Object)) {
            if (-not $lineHits.ContainsKey($number)) { continue }
            $coverableChanged++
            if ([int]$lineHits[$number] -gt 0) { $coveredChanged++ } else { $uncovered += $number }
        }
        if ($uncovered.Count -gt 0) { $uncoveredByFile[$file] = $uncovered }
    }

    $patchBand = Get-PatchBand -changedLines $coverableChanged -patch $thresholds.patch
    $patchFloor = $patchBand.Floor
    if ($changedFiles.Count -eq 0) {
        Write-Host 'No changed C# files were found; the patch gate passes.'
        Add-SummaryRow 'Patch coverage' 'no changed C# files' '-' 'info'
    }
    elseif ($matchedFiles.Count -eq 0) {
        $exampleChangedFile = @($changedFiles | Sort-Object)[0]
        $exampleReportFile = @(
            $report.FileLines.Keys |
                Where-Object { -not (Test-PathIgnored -relativePath $_ -ignoreRegexes $ignoreRegexes) } |
                Sort-Object
        )[0]
        if ([string]::IsNullOrWhiteSpace($exampleReportFile)) {
            $exampleReportFile = '<no non-ignored files in the coverage report>'
        }
        $message = ('Changed C# files were found, but none matched any file in the coverage report. ' +
            'The diff and report disagree on path shape. Example changed file: {0}. Example report file: {1}.' -f `
            $exampleChangedFile, $exampleReportFile)
        $failures += $message
        Write-Host $message
        Add-SummaryRow 'Patch coverage' 'changed files did not match the coverage report' '-' 'fail'
    }
    else {
        if ($unmatchedExistingFiles.Count -gt 0) {
            $exampleUnmatched = @($unmatchedExistingFiles | Sort-Object)[0]
            Write-GateWarning ('Some changed C# files exist on disk but were not present in the coverage report. ' +
                'Example unmatched file: {0}.' -f $exampleUnmatched)
        }

        if ($coverableChanged -eq 0) {
            Write-Host 'No coverable changed lines were found; the patch gate passes vacuously.'
            Add-SummaryRow 'Patch coverage' 'no coverable changed lines' '-' 'info'
        }
        else {
            $patchRate = 100.0 * $coveredChanged / $coverableChanged
            Write-Host ("Patch:       {0:N2}% ({1}/{2} changed lines covered, floor {3:N2}% for {4}, {5})" -f `
                $patchRate, $coveredChanged, $coverableChanged, $patchFloor, $patchBand.Scope,
                $(if ($patchBand.Enforced) { 'enforced' } else { 'advisory' }))

            if ($uncoveredByFile.Count -gt 0) {
                Write-Host 'Uncovered changed lines:'
                foreach ($file in $uncoveredByFile.Keys) {
                    Write-Host ("  {0}: {1}" -f $file, ($uncoveredByFile[$file] -join ', '))
                }
            }

            $patchBelowFloor = $patchRate -lt $patchFloor
            $patchState = if (-not $patchBelowFloor) {
                'pass'
            }
            elseif ($patchBand.Enforced) {
                'fail'
            }
            else {
                'warn'
            }

            Add-SummaryRow 'Patch coverage' `
                ('**{0:N2}%** ({1}/{2} changed lines)' -f $patchRate, $coveredChanged, $coverableChanged) `
                ('>= {0:N2}% ({1}{2})' -f $patchFloor, $patchBand.Scope,
                    $(if ($patchBand.Enforced) { '' } else { ', advisory' })) `
                $patchState

            # List the uncovered changed lines in the summary too - that is the
            # actionable part for the author, and it saves opening the raw log.
            if ($uncoveredByFile.Count -gt 0) {
                $detail = [System.Text.StringBuilder]::new()
                $null = $detail.AppendLine('<details><summary>Uncovered changed lines</summary>')
                $null = $detail.AppendLine('')
                foreach ($file in $uncoveredByFile.Keys) {
                    $null = $detail.AppendLine(('- `{0}`: {1}' -f $file, ($uncoveredByFile[$file] -join ', ')))
                }
                $null = $detail.AppendLine('')
                $null = $detail.Append('</details>')
                Add-SummaryNote $detail.ToString()
            }

            if ($patchBelowFloor) {
                $message = ((
                    'Patch coverage {0:N2}% is below {1:N2}% for {2} ' +
                    '({3} of {4} changed lines are uncovered).') -f `
                    $patchRate,
                    $patchFloor,
                    $patchBand.Scope,
                    ($coverableChanged - $coveredChanged),
                    $coverableChanged)
                if ($patchBand.Enforced) {
                    $failures += $message
                }
                else {
                    Write-GateWarning ($message +
                        ' Advisory at this patch size - add a test if the change deserves one.')
                }
            }
        }
    }
}

# 3. Baseline delta (advisory only).
$baseline = [double]$thresholds.project.baselineLineRate
$tolerance = [double]$thresholds.project.advisoryDeltaTolerance
if ($null -ne $totals.LineRate -and $baseline -gt 0) {
    $delta = $totals.LineRate - $baseline
    Write-Host ("Baseline:    {0:N2}% recorded, delta {1:+0.00;-0.00;0.00} percentage points" -f $baseline, $delta)
    $deltaText = '{0:+0.00;-0.00;0.00} pp' -f $delta
    if ($delta -lt (-1 * $tolerance)) {
        Write-GateWarning ('Total line coverage dropped {0:N2} percentage points below the recorded master baseline of {1:N2}%. This is advisory and does not fail the build.' -f `
            [Math]::Abs($delta), $baseline)
        Add-SummaryRow 'Baseline delta (advisory)' $deltaText ('{0:N2}% recorded' -f $baseline) 'warn'
    }
    else {
        if ($delta -gt $tolerance) {
            Write-Host 'Coverage is above the recorded baseline; consider ratcheting coverage-thresholds.json.'
            Add-SummaryNote 'Coverage is above the recorded baseline - consider ratcheting `coverage-thresholds.json`.'
        }
        Add-SummaryRow 'Baseline delta (advisory)' $deltaText ('{0:N2}% recorded' -f $baseline) 'info'
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-GateError $failure }
}

if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
    $verdict = if ($failures.Count -gt 0) {
        ':x: **Coverage gate failed.** This check is advisory and does not block the merge.'
    }
    else {
        ':white_check_mark: **Coverage gate passed.**'
    }

    $summary = [System.Text.StringBuilder]::new()
    $null = $summary.AppendLine('## Code coverage')
    $null = $summary.AppendLine('')
    $null = $summary.AppendLine($verdict)
    $null = $summary.AppendLine('')
    $null = $summary.AppendLine('| Check | Result | Threshold |')
    $null = $summary.AppendLine('| --- | --- | --- |')
    foreach ($row in $script:SummaryRows) {
        $null = $summary.AppendLine($row)
    }
    if ($failures.Count -gt 0) {
        $null = $summary.AppendLine('')
        foreach ($failure in $failures) {
            $null = $summary.AppendLine(('- :x: {0}' -f $failure))
        }
    }
    foreach ($note in $script:SummaryNotes) {
        $null = $summary.AppendLine('')
        $null = $summary.AppendLine($note)
    }
    $null = $summary.AppendLine('')
    $null = $summary.AppendLine(('<sub>Thresholds live in `coverage-thresholds.json`. Whole report before exclusions: line {0:N2}%, branch {1:N2}%.</sub>' -f `
        $report.ReportLineRate, $report.ReportBranchRate))

    $summaryDir = Split-Path -Parent $SummaryPath
    if (-not [string]::IsNullOrWhiteSpace($summaryDir) -and -not (Test-Path $summaryDir)) {
        $null = New-Item -ItemType Directory -Force -Path $summaryDir
    }
    # UTF8 without BOM: GitHub renders a leading BOM as a literal character at
    # the top of the step summary.
    [System.IO.File]::WriteAllText($SummaryPath, $summary.ToString(), [System.Text.UTF8Encoding]::new($false))
    Write-Host "Wrote the markdown summary to $SummaryPath."
}

if ($failures.Count -gt 0) {
    Write-Host 'Coverage gate FAILED.'
    exit 1
}

Write-Host 'Coverage gate passed.'
exit 0
