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
    Replaces everything a C# file says that cannot become an executable
    statement - comments, literals and preprocessor directives - with blanks.

 .DESCRIPTION
    The residue is what the classification below reads. Blanking rather than
    deleting keeps the surrounding tokens apart, so 'a/*x*/b' does not become
    the single identifier 'ab'.
#>
function Remove-CSharpNonCode([string] $text) {
    $text = [regex]::Replace($text, '(?m)^[ \t]*#.*$', '')
    $builder = New-Object System.Text.StringBuilder
    $index = 0
    while ($index -lt $text.Length) {
        $ch = $text[$index]
        $next = if ($index + 1 -lt $text.Length) { $text[$index + 1] } else { [char]0 }
        if ($ch -eq '/' -and $next -eq '/') {
            while ($index -lt $text.Length -and $text[$index] -ne "`n") { $index++ }
            continue
        }
        if ($ch -eq '/' -and $next -eq '*') {
            $index += 2
            while ($index + 1 -lt $text.Length -and
                -not ($text[$index] -eq '*' -and $text[$index + 1] -eq '/')) { $index++ }
            $index += 2
            $null = $builder.Append(' ')
            continue
        }
        if ($ch -eq '"') {
            # A raw string literal opens and closes with the same run of quotes.
            $quotes = 0
            while ($index + $quotes -lt $text.Length -and $text[$index + $quotes] -eq '"') { $quotes++ }
            if ($quotes -ge 3) {
                $index += $quotes
                $run = 0
                while ($index -lt $text.Length -and $run -lt $quotes) {
                    $run = if ($text[$index] -eq '"') { $run + 1 } else { 0 }
                    $index++
                }
                $null = $builder.Append(' ')
                continue
            }
            $index++
            while ($index -lt $text.Length -and $text[$index] -ne '"') {
                if ($text[$index] -eq '\') { $index++ }
                $index++
            }
            $index++
            $null = $builder.Append(' ')
            continue
        }
        if ($ch -eq '@' -and $next -eq '"') {
            $index += 2
            while ($index -lt $text.Length) {
                if ($text[$index] -eq '"') {
                    if ($index + 1 -lt $text.Length -and $text[$index + 1] -eq '"') {
                        $index += 2
                        continue
                    }
                    $index++
                    break
                }
                $index++
            }
            $null = $builder.Append(' ')
            continue
        }
        if ($ch -eq "'") {
            $index++
            while ($index -lt $text.Length -and $text[$index] -ne "'") {
                if ($text[$index] -eq '\') { $index++ }
                $index++
            }
            $index++
            $null = $builder.Append(' ')
            continue
        }
        $null = $builder.Append($ch)
        $index++
    }

    # Attribute sections and array-rank specifiers cannot become statements, and
    # a named argument inside one would otherwise read as an initializer.
    $residue = $builder.ToString()
    for ($pass = 0; $pass -lt 8; $pass++) {
        $reduced = [regex]::Replace($residue, '\[[^\[\]\{\}]*\]', ' ')
        if ($reduced -eq $residue) { break }
        $residue = $reduced
    }
    return $residue
}

<#
 .SYNOPSIS
    Names the kind of declaration a block header opens.
#>
function Get-CSharpBlockKind([string] $header) {
    if ($header -cmatch '\bnamespace\b') { return 'Namespace' }
    if ($header -cmatch '\benum\b') { return 'Enum' }
    if ($header -cmatch '\binterface\b') { return 'Interface' }
    if ($header -cmatch '\b(class|struct|record)\b') { return 'Type' }
    return 'Member'
}

<#
 .SYNOPSIS
    Gets whether a declaration assigns a value that runs, as opposed to one the
    compiler folds.
#>
function Test-CSharpInitializes([string] $statement) {
    if ($statement -cmatch '\bconst\b') { return $false }
    $depth = 0
    for ($ii = 0; $ii -lt $statement.Length; $ii++) {
        $ch = $statement[$ii]
        if ($ch -eq '(') { $depth++; continue }
        if ($ch -eq ')') { $depth--; continue }
        if ($ch -ne '=' -or $depth -ne 0) { continue }
        $before = if ($ii -gt 0) { $statement[$ii - 1] } else { [char]0 }
        $after = if ($ii + 1 -lt $statement.Length) { $statement[$ii + 1] } else { [char]0 }
        if ($after -eq '=' -or $before -eq '=' -or $after -eq '>') { continue }
        if ('!<>+-*/%&|^'.Contains($before)) { continue }
        return $true
    }
    return $false
}

<#
 .SYNOPSIS
    Finds the index of the brace that closes the one at the given index.
#>
function Find-CSharpMatchingBrace([string] $text, [int] $open) {
    $depth = 0
    for ($ii = $open; $ii -lt $text.Length; $ii++) {
        if ($text[$ii] -eq '{') { $depth++ }
        elseif ($text[$ii] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $ii }
        }
    }
    return -1
}

<#
 .SYNOPSIS
    Names the first construct in a C# source that produces an executable
    statement, or returns $null when the source provably produces none.

 .DESCRIPTION
    A sequence point comes from a member body, an expression-bodied member, a
    primary constructor or a field initializer, and from nothing else. A file
    that declares only interfaces, enumerations, delegates and constants
    therefore has no line a coverage collector can measure, and no report will
    ever mention it.

    The classification is deliberately biased towards reporting evidence: every
    construct it does not recognize is read as executable, so an unrecognized
    file makes the gate fail rather than pass.
#>
function Get-CSharpExecutableEvidence([string] $source) {
    $text = Remove-CSharpNonCode $source
    $kinds = New-Object System.Collections.Generic.Stack[string]
    $header = New-Object System.Text.StringBuilder
    $index = 0
    while ($index -lt $text.Length) {
        $ch = $text[$index]
        if ($ch -eq '{') {
            $head = $header.ToString()
            $null = $header.Clear()
            $kind = Get-CSharpBlockKind $head
            if ($kind -eq 'Member') {
                $enclosing = if ($kinds.Count -gt 0) { $kinds.Peek() } else { 'Namespace' }
                $close = Find-CSharpMatchingBrace $text $index
                $body = if ($close -gt $index) {
                    $text.Substring($index + 1, $close - $index - 1)
                } else { 'x' }

                # An accessor list that states no bodies is a declaration, not
                # code - but only where the member itself has none, which is
                # what an interface member is. The same shape on a class is an
                # auto-property, whose accessors a collector does measure.
                if ($enclosing -eq 'Interface' -and
                    $body -cmatch '^[\s;]*((public|protected|internal|private|get|set|init|add|remove)\b[\s;]*)*$') {
                    $index = $close + 1
                    continue
                }
                return 'a member body'
            }
            if ($kind -ne 'Namespace' -and $kind -ne 'Enum' -and $head.Contains('(')) {
                return 'a primary constructor'
            }
            $kinds.Push($kind)
            $index++
            continue
        }
        if ($ch -eq '}') {
            $null = $header.Clear()
            if ($kinds.Count -gt 0) { $null = $kinds.Pop() }
            $index++
            continue
        }
        if ($ch -eq ';') {
            $statement = $header.ToString()
            $null = $header.Clear()
            $enclosing = if ($kinds.Count -gt 0) { $kinds.Peek() } else { 'Namespace' }
            if ($enclosing -ne 'Enum') {
                if ($statement.Contains('=>')) { return 'an expression-bodied member' }
                if ($statement -cmatch '\b(class|struct|record)\b' -and $statement.Contains('(')) {
                    return 'a primary constructor'
                }
                if (Test-CSharpInitializes $statement) { return 'a field initializer' }
            }
            $index++
            continue
        }
        $null = $header.Append($ch)
        $index++
    }
    return $null
}

<#
 .SYNOPSIS
    Names the project directory that builds a repository-relative source file.
#>
function Get-OwningProjectDirectory([string] $relativePath, [string] $repoRoot) {
    $directory = Split-Path -Parent $relativePath
    while (-not [string]::IsNullOrEmpty($directory)) {
        $full = Join-Path $repoRoot $directory
        if ((Test-Path -LiteralPath $full -PathType Container) -and
            @(Get-ChildItem -LiteralPath $full -Filter '*.csproj' -File `
                -ErrorAction SilentlyContinue).Count -gt 0) {
            return $directory.Replace('\', '/')
        }
        $parent = Split-Path -Parent $directory
        if ($parent -eq $directory) { break }
        $directory = $parent
    }
    return $null
}

<#
 .SYNOPSIS
    Explains why a governed changed file is legitimately absent from the
    coverage report, or returns $null when its absence is unexplained.

 .DESCRIPTION
    A file the report never mentions is normally the vacuous pass this gate
    exists to stop: the assembly was not collected, or the diff and the report
    disagree about path shape. A file that declares only interfaces,
    enumerations, delegates or constants is the one honest exception - it
    produces no sequence point, so no report can ever mention it, and failing
    the rule for it would make the rule impossible to satisfy.

    Two independent proofs are required before the absence is excused, so an
    arbitrary absent file is never skipped:

      1. the assembly evidence - some other file of the same project IS in the
         report, so the assembly was collected; and
      2. the source evidence - the file provably contains no construct that
         produces an executable statement.
#>
function Get-NonCoverableReason([string] $file, $report, [string] $repoRoot) {
    $project = Get-OwningProjectDirectory -relativePath $file -repoRoot $repoRoot
    if ($null -eq $project) {
        return $null
    }
    $prefix = "$project/"
    $collected = $false
    foreach ($measured in $report.FileLines.Keys) {
        if ($measured.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $collected = $true
            break
        }
    }
    if (-not $collected) {
        return $null
    }
    $source = Get-Content -LiteralPath (Join-Path $repoRoot $file) -Raw -ErrorAction SilentlyContinue
    if ($null -eq $source) {
        return $null
    }
    if ($null -ne (Get-CSharpExecutableEvidence $source)) {
        return $null
    }
    return "declares no executable statement, and '$project' was collected"
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

# 2b. Path-scoped changed-code rules (blocking).
#
# The graduated patch band is right for the repository as a whole: over a
# handful of lines a percentage is noise, and a gate that fails a two-line fix
# teaches authors to ignore it. It is wrong for a small number of areas where
# every changed line is meant to be exercised - a protocol mapping whose
# untested branch is a wire-format bug nobody sees until interop. Those areas
# state their own floor here, applied to the same changed lines, and the floor
# is not graduated: it is the rule for the path, whatever the patch size.
#
# A rule matches a changed file when one of its 'include' globs matches and none
# of its 'exclude' globs does. Exclusions are explicit rather than inherited so
# that reading the rule tells you what it covers.
if (-not [string]::IsNullOrWhiteSpace($BaseRef) -and $null -ne $thresholds.pathRules) {
    $ruleChanged = Get-ChangedLines -baseRef $BaseRef -repoRoot $RepoRoot -skipFetch:$SkipFetch.IsPresent

    foreach ($rule in @($thresholds.pathRules)) {
        $ruleName = $rule.name
        $includeGlobs = @($rule.include | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($includeGlobs.Count -eq 0) {
            # An empty or mistyped include list matches nothing, so every rule
            # built on it passes without ever looking at a file. That is a
            # configuration fault, not a clean patch.
            $message = (("Path rule '{0}' declares no usable 'include' globs, so it can never " +
                'match a changed file. Fix the rule rather than letting it pass vacuously.') -f $ruleName)
            $failures += $message
            Write-Host $message
            Add-SummaryRow ('Changed code: {0}' -f $ruleName) 'rule declares no include globs' '-' 'fail'
            continue
        }

        $includeRegexes = @()
        foreach ($glob in $includeGlobs) { $includeRegexes += ConvertTo-GlobRegex $glob }
        $excludeRegexes = @()
        foreach ($glob in @($rule.exclude)) { $excludeRegexes += ConvertTo-GlobRegex $glob }

        $ruleLines = 0
        $ruleCovered = 0
        $ruleBranches = 0
        $ruleCoveredBranches = 0
        $ruleUncovered = [ordered]@{}
        $rulePartial = [ordered]@{}
        $ruleScopedFiles = [System.Collections.Generic.List[string]]::new()
        $ruleMatchedFiles = [System.Collections.Generic.List[string]]::new()
        $ruleUnmatchedExisting = [System.Collections.Generic.List[string]]::new()
        $ruleNonCoverableFiles = [System.Collections.Generic.List[string]]::new()
        $ruleNoCoverableLines = [System.Collections.Generic.List[string]]::new()

        foreach ($entry in $ruleChanged.GetEnumerator()) {
            $file = $entry.Key
            if (Test-PathIgnored -relativePath $file -ignoreRegexes $ignoreRegexes) { continue }
            if (-not (Test-PathIgnored -relativePath $file -ignoreRegexes $includeRegexes)) { continue }
            if ($excludeRegexes.Count -gt 0 -and
                (Test-PathIgnored -relativePath $file -ignoreRegexes $excludeRegexes)) { continue }
            $ruleScopedFiles.Add($file)
            if (-not $report.FileLines.ContainsKey($file)) {
                # A file the rule governs that the report never mentions is the
                # vacuous pass this gate exists to stop: the assembly was not
                # collected, or the diff and the report disagree on path shape.
                # A file that no longer exists on disk was deleted by the patch
                # and is correctly absent. A file that declares no executable
                # statement at all produces no sequence point, so no report can
                # mention it - that one is excused, but only on the evidence
                # Get-NonCoverableReason requires.
                if (Test-Path -LiteralPath (Join-Path $RepoRoot $file)) {
                    $reason = Get-NonCoverableReason -file $file -report $report -repoRoot $RepoRoot
                    if ($null -eq $reason) {
                        $ruleUnmatchedExisting.Add($file)
                    }
                    else {
                        $ruleNonCoverableFiles.Add("$file ($reason)")
                    }
                }
                continue
            }
            $ruleMatchedFiles.Add($file)

            $lineHits = $report.FileLines[$file]
            $branchHits = $report.FileBranches[$file]
            $uncovered = @()
            $partial = @()
            $coverableInFile = 0
            foreach ($number in ($entry.Value | Sort-Object)) {
                if (-not $lineHits.ContainsKey($number)) { continue }
                $coverableInFile++
                $ruleLines++
                if ([int]$lineHits[$number] -gt 0) { $ruleCovered++ } else { $uncovered += $number }
                if ($null -ne $branchHits -and $branchHits.ContainsKey($number)) {
                    $pair = $branchHits[$number]
                    $ruleCoveredBranches += [int]$pair[0]
                    $ruleBranches += [int]$pair[1]
                    if ([int]$pair[0] -lt [int]$pair[1]) { $partial += $number }
                }
            }
            if ($coverableInFile -eq 0) { $ruleNoCoverableLines.Add($file) }
            if ($uncovered.Count -gt 0) { $ruleUncovered[$file] = $uncovered }
            if ($partial.Count -gt 0) { $rulePartial[$file] = $partial }
        }

        if ($ruleUnmatchedExisting.Count -gt 0) {
            $message = (("Path rule '{0}' governs {1} changed file(s) that exist on disk but are absent " +
                'from the coverage report, so the rule would pass without measuring them: {2}. ' +
                'Collect coverage for the assemblies that build these files, or fix the path shape.') -f `
                $ruleName,
                $ruleUnmatchedExisting.Count,
                (($ruleUnmatchedExisting | Sort-Object) -join ', '))
            $failures += $message
            Write-Host $message
            Add-SummaryRow ('Changed code: {0}' -f $ruleName) `
                ('{0} changed file(s) missing from the report' -f $ruleUnmatchedExisting.Count) '-' 'fail'
            continue
        }

        if ($ruleNonCoverableFiles.Count -gt 0) {
            # Never silent: the reader has to be able to see which files were
            # excused and why, or the exception becomes the hiding place the
            # unmatched-file check exists to close.
            Write-Host (("Rule '{0}': {1} changed file(s) produce no sequence point and are " +
                'correctly absent from the report: {2}.') -f `
                $ruleName,
                $ruleNonCoverableFiles.Count,
                (($ruleNonCoverableFiles | Sort-Object) -join ', '))
        }

        if ($ruleScopedFiles.Count -gt 0 -and $ruleNoCoverableLines.Count -gt 0) {
            # A file the report knows about but whose changed lines are all
            # non-coverable (comments, braces, declarations) is legitimate; say
            # so rather than leaving the reader to guess why it is not counted.
            Write-Host ("Rule '{0}': {1} changed file(s) contributed no coverable line: {2}." -f `
                $ruleName,
                $ruleNoCoverableLines.Count,
                (($ruleNoCoverableLines | Sort-Object) -join ', '))
        }

        if ($ruleLines -eq 0) {
            if ($ruleScopedFiles.Count -eq 0) {
                Add-SummaryRow ('Changed code: {0}' -f $ruleName) 'no changed lines in scope' '-' 'info'
            }
            else {
                Add-SummaryRow ('Changed code: {0}' -f $ruleName) `
                    ('{0} changed file(s), no coverable lines' -f $ruleScopedFiles.Count) '-' 'info'
            }
            continue
        }

        $lineFloor = [double]$rule.minimumChangedLineRate
        $lineRate = 100.0 * $ruleCovered / $ruleLines
        Write-Host ("Rule '{0}': line {1:N2}% ({2}/{3} changed lines)" -f `
            $ruleName, $lineRate, $ruleCovered, $ruleLines)
        if ($ruleUncovered.Count -gt 0) {
            Write-Host '  Uncovered changed lines:'
            foreach ($file in $ruleUncovered.Keys) {
                Write-Host ("    {0}: {1}" -f $file, ($ruleUncovered[$file] -join ', '))
            }
        }
        if ($lineRate -lt $lineFloor) {
            $failures += ("Changed-line coverage {0:N2}% for '{1}' is below the required {2:N2}% ({3} of {4} changed lines are uncovered)." -f `
                $lineRate, $ruleName, $lineFloor, ($ruleLines - $ruleCovered), $ruleLines)
            Add-SummaryRow ('Changed lines: {0}' -f $ruleName) `
                ('**{0:N2}%** ({1}/{2})' -f $lineRate, $ruleCovered, $ruleLines) `
                ('>= {0:N2}%' -f $lineFloor) 'fail'
        }
        else {
            Add-SummaryRow ('Changed lines: {0}' -f $ruleName) `
                ('**{0:N2}%** ({1}/{2})' -f $lineRate, $ruleCovered, $ruleLines) `
                ('>= {0:N2}%' -f $lineFloor) 'pass'
        }

        if ($null -eq $rule.minimumChangedBranchRate) { continue }
        $branchFloor = [double]$rule.minimumChangedBranchRate
        if ($ruleBranches -eq 0) {
            Add-SummaryRow ('Changed branches: {0}' -f $ruleName) 'no branches in scope' '-' 'info'
            continue
        }
        $branchRate = 100.0 * $ruleCoveredBranches / $ruleBranches
        Write-Host ("Rule '{0}': branch {1:N2}% ({2}/{3} changed branches)" -f `
            $ruleName, $branchRate, $ruleCoveredBranches, $ruleBranches)
        if ($rulePartial.Count -gt 0) {
            Write-Host '  Partially covered changed branches:'
            foreach ($file in $rulePartial.Keys) {
                Write-Host ("    {0}: {1}" -f $file, ($rulePartial[$file] -join ', '))
            }
        }
        if ($branchRate -lt $branchFloor) {
            $failures += ("Changed-branch coverage {0:N2}% for '{1}' is below the required {2:N2}% ({3} of {4} changed branches are unexercised)." -f `
                $branchRate, $ruleName, $branchFloor, ($ruleBranches - $ruleCoveredBranches), $ruleBranches)
            Add-SummaryRow ('Changed branches: {0}' -f $ruleName) `
                ('**{0:N2}%** ({1}/{2})' -f $branchRate, $ruleCoveredBranches, $ruleBranches) `
                ('>= {0:N2}%' -f $branchFloor) 'fail'
        }
        else {
            Add-SummaryRow ('Changed branches: {0}' -f $ruleName) `
                ('**{0:N2}%** ({1}/{2})' -f $branchRate, $ruleCoveredBranches, $ruleBranches) `
                ('>= {0:N2}%' -f $branchFloor) 'pass'
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
