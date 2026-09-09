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
Captures actual CodeQL compilation/extraction identities without publishing query or finding content.
.DESCRIPTION
Prepare emits an opt-in MSBuild compilation-input hook and the exact action configuration.
Build with -p:CustomAfterMicrosoftCommonTargets=<work>\capture.targets
and -p:AssuranceCodeqlDirectory=<work>\compilations, then Collect after analyze.
Collect uses the init codeql-path/codeql-version and analyze db-locations/sarif-id
outputs. Unsupported database/query layouts remain missing. No pack is installed.
#>
param(
    [Parameter(Mandatory)][ValidateSet('Prepare', 'Collect')][string] $Mode,
    [Parameter(Mandatory)][string] $WorkDirectory,
    [string] $OutputPath,
    [string] $CodeqlPath,
    [string] $CodeqlVersion,
    [string] $DatabaseLocations,
    [string] $SarifId,
    [string] $ResultsPath,
    [string] $ReviewReferencePath,
    [ValidateRange(10, 600)][int] $CommandTimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$work = [IO.Path]::GetFullPath($WorkDirectory)
$workspace = $work
. (Join-Path $PSScriptRoot 'assurance-github.ps1')

function Get-CodeqlDigest([string] $Path) {
    return 'sha256:' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-CodeqlSetDigest([string[]] $Values, [switch] $PreserveMultiplicity) {
    $ordered = if ($PreserveMultiplicity) { @($Values | Sort-Object -CaseSensitive) } else {
        @($Values | Sort-Object -CaseSensitive -Unique)
    }
    $text = $ordered -join "`n"
    return 'sha256:' + [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}

function Get-CodeqlDatabaseDigest([string] $Database) {
    $dataset = Join-Path $Database 'db-csharp'
    if (-not (Test-Path -LiteralPath $dataset -PathType Container)) { throw 'CODEQL_DATASET_LAYOUT_UNSUPPORTED' }
    $files = @(
        Get-Item -LiteralPath (Join-Path $Database 'codeql-database.yml'), (Join-Path $Database 'src.zip')
        Get-ChildItem -LiteralPath $dataset -File -Recurse
    )
    if ($files.Count -lt 3 -or $files.Count -gt 200000) { throw 'CODEQL_DATASET_LIMIT' }
    $total = 0L
    $identities = @()
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($CommandTimeoutSeconds)
    foreach ($file in $files) {
        $total += $file.Length
        if ($file.Length -gt 8589934592 -or $total -gt 68719476736 -or
            [DateTimeOffset]::UtcNow -ge $deadline -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'CODEQL_DATASET_LIMIT'
        }
        $relative = [IO.Path]::GetRelativePath($Database, $file.FullName).Replace('\', '/')
        $identities += "$relative|$(Get-CodeqlDigest $file.FullName)"
    }
    return Get-CodeqlSetDigest $identities
}

function Invoke-CodeqlCommand([string[]] $Arguments, [string] $Destination) {
    $info = [Diagnostics.ProcessStartInfo]::new($CodeqlPath)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ([IO.Path]::GetExtension($CodeqlPath) -eq '.cmd') {
        if ($CodeqlPath -match '["&|<>^%!\r\n]' -or @($Arguments | Where-Object { $_ -match '["&|<>^%!\r\n]' }).Count) {
            throw 'CODEQL_COMMAND_ARGUMENT_REJECTED'
        }
        $info.FileName = $env:ComSpec
        $info.Arguments = '/d /s /c ""' + $CodeqlPath + '" ' +
            (($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' ') + '"'
    }
    else { foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) } }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    $started = $false
    try {
        $null = $process.Start()
        $started = $true
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($stream)
        $stderr = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($CommandTimeoutSeconds)
        while (-not $process.HasExited -or -not $stdout.IsCompleted -or -not $stderr.IsCompleted) {
            if ([DateTimeOffset]::UtcNow -ge $deadline -or $stream.Position -gt 16777216) {
                throw 'CODEQL_COMMAND_LIMIT'
            }
            Start-Sleep -Milliseconds 25
        }
        if ($process.ExitCode -ne 0 -or -not $stdout.IsCompletedSuccessfully -or
            -not $stderr.IsCompletedSuccessfully) { throw 'CODEQL_COMMAND_UNSUPPORTED_OR_FAILED' }
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
        $stream.Dispose()
    }
}

if ($Mode -eq 'Prepare') {
    if (Test-Path -LiteralPath $work) { throw 'CODEQL_WORK_DIRECTORY_NOT_FRESH' }
    $null = New-Item -ItemType Directory -Path $work, (Join-Path $work 'compilations')
    @'
<Project>
  <Target Name="CaptureAssuranceCompilerInputs" BeforeTargets="CoreCompile"
          Condition="'$(MSBuildProjectExtension)' == '.csproj'">
    <WriteLinesToFile File="$(AssuranceCodeqlDirectory)/$(MSBuildProjectName).$(TargetFramework).$(RuntimeIdentifier).compile.txt"
                     Lines="project|$(MSBuildProjectFullPath);@(IntermediateAssembly->'assembly|%(FullPath)');@(Compile->'source|%(FullPath)')"
                     Overwrite="false" Encoding="UTF-8" />
  </Target>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $work 'capture.targets') -Encoding utf8
    "name: assurance-csharp-net10`ndisable-default-queries: false`n" |
        Set-Content -LiteralPath (Join-Path $work 'codeql-config.yml') -Encoding utf8
    [DateTimeOffset]::UtcNow.ToString('o') |
        Set-Content -LiteralPath (Join-Path $work 'started-at.txt') -Encoding utf8
    exit 0
}

if (-not $OutputPath) { throw 'CODEQL_OUTPUT_REQUIRED' }
$summary = [ordered]@{ schemaVersion = 1; kind = 'codeql'; status = 'missing'; documents = @(); reasons = @() }
$sourceArchive = $null
try {
    $sha = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$' -or
        $env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_RUN_ID -cnotmatch '^[1-9][0-9]*$' -or
        $env:GITHUB_RUN_ATTEMPT -cnotmatch '^[1-9][0-9]*$' -or
        $env:GITHUB_REF -cnotmatch '^refs/heads/(master|release/2\.[0-9A-Za-z._/-]+)$') {
        throw 'CODEQL_PRODUCER_IDENTITY_MISSING'
    }
    if (-not (Test-Path -LiteralPath $CodeqlPath -PathType Leaf) -or
        $CodeqlVersion -cnotmatch '^2\.[0-9]+\.[0-9]+$' -or $SarifId -cnotmatch '^[A-Za-z0-9-]+$') {
        throw 'CODEQL_ACTION_OUTPUT_MISSING'
    }
    $locations = $DatabaseLocations | ConvertFrom-Json
    $database = [string] $locations.csharp
    if (-not (Test-Path -LiteralPath $database -PathType Container)) { throw 'CODEQL_DATABASE_MISSING' }
    $versionFile = Join-Path $work 'version.json'
    Invoke-CodeqlCommand @('version', '--format=json') $versionFile
    $version = Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json
    if ($version.version -cne $CodeqlVersion) { throw 'CODEQL_VERSION_MISMATCH' }
    $databaseFile = Join-Path $work 'database.json'
    Invoke-CodeqlCommand @('resolve', 'database', '--format=json', '--', $database) $databaseFile
    $metadata = Get-Content -LiteralPath $databaseFile -Raw | ConvertFrom-Json
    if ($null -eq $metadata) { throw 'CODEQL_DATABASE_UNVERIFIED' }
    $queryFile = Join-Path $work 'queries.json'
    $suite = 'codeql/csharp-queries:codeql-suites/csharp-code-scanning.qls'
    Invoke-CodeqlCommand @('resolve', 'queries', '--format=json', '--', $suite) $queryFile
    $queries = @(Get-Content -LiteralPath $queryFile -Raw | ConvertFrom-Json)
    if ($queries.Count -lt 1 -or $queries.Count -gt 10000 -or
        @($queries | Select-Object -Unique).Count -ne $queries.Count) { throw 'CODEQL_QUERY_SCOPE_UNVERIFIED' }
    $queryIdentities = @()
    $resultIdentities = @()
    $packRoot = $null
    foreach ($query in $queries) {
        $file = Get-Item -LiteralPath $query
        $directory = $file.Directory
        while ($null -ne $directory -and -not (Test-Path -LiteralPath (Join-Path $directory.FullName 'qlpack.yml'))) {
            $directory = $directory.Parent
        }
        if ($null -eq $directory) { throw 'CODEQL_QUERY_PACK_UNVERIFIED' }
        $packRoot = $directory.FullName
        $relative = [IO.Path]::GetRelativePath($packRoot, $file.FullName).Replace('\', '/')
        $queryIdentities += "$relative|$(Get-CodeqlDigest $file.FullName)"
        $result = Join-Path $database ('results/codeql/csharp-queries/' + [IO.Path]::ChangeExtension($relative, '.bqrs'))
        if (-not (Test-Path -LiteralPath $result -PathType Leaf)) { throw 'CODEQL_QUERY_RESULT_LAYOUT_UNSUPPORTED' }
        $resultIdentities += "$relative|$(Get-CodeqlDigest $result)"
    }
    $suitePath = Join-Path $packRoot 'codeql-suites/csharp-code-scanning.qls'
    $packPath = Join-Path $packRoot 'qlpack.yml'
    $queryDirectory = Join-Path $work 'extraction-query'
    $null = New-Item -ItemType Directory -Path $queryDirectory
    Copy-Item -LiteralPath $packPath -Destination (Join-Path $queryDirectory 'qlpack.yml')
    $lock = Join-Path $packRoot 'codeql-pack.lock.yml'
    if (Test-Path -LiteralPath $lock) { Copy-Item -LiteralPath $lock -Destination $queryDirectory }
    $extractionQuery = Join-Path $queryDirectory 'AssuranceCompilations.ql'
    @'
import csharp
import semmle.code.csharp.commons.Compilation
import semmle.code.csharp.commons.Diagnostics

from Compilation compilation, File source, string output
where
  source = compilation.getAFileCompiled() and
  output = compilation.getExpandedArgument(_) and
  (output.toLowerCase().matches("/out:%") or output.toLowerCase().matches("-out:%"))
select compilation.getDirectoryString(), output, source.getAbsolutePath(),
  if exists(compilation.getElapsedSeconds()) then "completed" else "incomplete",
  count(CompilerError error | error.getCompilation() = compilation),
  count(ExtractorError error)
'@ | Set-Content -LiteralPath $extractionQuery -Encoding utf8
    $bqrs = Join-Path $work 'extraction.bqrs'
    Invoke-CodeqlCommand @('query', 'run', "--database=$database", "--output=$bqrs", '--threads=2',
        '--ram=2048', '--timeout=120', '--', $extractionQuery) (Join-Path $work 'query-command.json')
    $decoded = Join-Path $work 'extraction.json'
    Invoke-CodeqlCommand @('bqrs', 'decode', '--format=json', "--output=$decoded", '--', $bqrs) `
        (Join-Path $work 'decode-command.json')
    $tuples = @((Get-Content -LiteralPath $decoded -Raw | ConvertFrom-Json).'#select'.tuples)
    if ($tuples.Count -lt 1 -or $tuples.Count -gt 100000) { throw 'CODEQL_EXTRACTION_SCOPE_UNVERIFIED' }
    $projects = @()
    $buildIdentities = @()
    $sourceArchive = [IO.Compression.ZipFile]::OpenRead((Join-Path $database 'src.zip'))
    if ($sourceArchive.Entries.Count -gt 200000) { throw 'CODEQL_SOURCE_ARCHIVE_LIMIT' }
    $sourceEntries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $sourceArchive.Entries) {
        $key = $entry.FullName.Replace('\', '/').TrimStart('/')
        if (-not $sourceEntries.TryAdd($key, $entry)) { throw 'CODEQL_SOURCE_ARCHIVE_AMBIGUOUS' }
    }
    foreach ($capture in Get-ChildItem -LiteralPath (Join-Path $work 'compilations') -Filter '*.compile.txt' -File) {
        $lines = @(Get-Content -LiteralPath $capture.FullName | Where-Object { $_ })
        $project = @($lines | Where-Object { $_.StartsWith('project|') } | Select-Object -Unique)
        $assembly = @($lines | Where-Object { $_.StartsWith('assembly|') } | Select-Object -Unique)
        $sources = @($lines | Where-Object { $_.StartsWith('source|') } |
            ForEach-Object { [IO.Path]::GetFullPath($_.Substring(7)).ToLowerInvariant() } | Sort-Object -Unique)
        if ($project.Count -ne 1 -or $assembly.Count -ne 1 -or $sources.Count -lt 1) {
            throw 'CODEQL_BUILD_SCOPE_AMBIGUOUS'
        }
        $outputAssembly = [IO.Path]::GetFullPath($assembly[0].Substring(9))
        $extracted = @(
            foreach ($row in $tuples) {
                if ($row.Count -ne 6 -or $row[0] -isnot [string] -or $row[1] -isnot [string] -or
                    $row[2] -isnot [string] -or $row[1].Length -lt 6) { throw 'CODEQL_EXTRACTION_FORMAT_UNSUPPORTED' }
                $actualOutput = [IO.Path]::GetFullPath($row[1].Substring(5).Trim('"'), $row[0])
                if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualOutput, $outputAssembly)) { continue }
                if ($row.Count -ne 6 -or $row[3] -cne 'completed' -or $row[4] -ne 0 -or $row[5] -ne 0) {
                    throw 'CODEQL_EXTRACTION_INCOMPLETE'
                }
                [IO.Path]::GetFullPath($row[2]).ToLowerInvariant()
            }
        )
        $set = [Collections.Generic.HashSet[string]]::new([string[]] $extracted, [StringComparer]::OrdinalIgnoreCase)
        if (-not $set.IsSupersetOf([string[]] $sources)) { throw 'CODEQL_PROJECT_EXTRACTION_MISSING' }
        $expectedIdentities = @()
        $extractedIdentities = @()
        foreach ($source in $sources) {
            $sourceName = $source.Replace('\', '/').TrimStart('/')
            $entry = $null
            if (-not $sourceEntries.TryGetValue($sourceName, [ref] $entry) -and
                -not $sourceEntries.TryGetValue($sourceName.Replace(':', '_'), [ref] $entry)) {
                throw 'CODEQL_SOURCE_ARCHIVE_LAYOUT_UNSUPPORTED'
            }
            if ($entry.Length -lt 0 -or $entry.Length -gt 33554432) { throw 'CODEQL_SOURCE_ARCHIVE_LIMIT' }
            $input = $entry.Open()
            $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
            try {
                $buffer = [byte[]]::new(8192)
                $length = 0L
                while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $length += $read
                    if ($length -gt $entry.Length) { throw 'CODEQL_SOURCE_ARCHIVE_LIMIT' }
                    $hash.AppendData($buffer, 0, $read)
                }
                if ($length -ne $entry.Length) { throw 'CODEQL_SOURCE_ARCHIVE_INCOMPLETE' }
                $extractedDigest = 'sha256:' + [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            }
            finally { $input.Dispose(); $hash.Dispose() }
            $sourceDigest = Get-CodeqlDigest $source
            if ($sourceDigest -cne $extractedDigest) { throw 'CODEQL_EXTRACTED_SOURCE_CHANGED' }
            $expectedIdentities += "$sourceName|$sourceDigest"
            $extractedIdentities += "$sourceName|$extractedDigest"
        }
        $relativeProject = [IO.Path]::GetRelativePath($root, $project[0].Substring(8)).Replace('\', '/')
        $projects += @{
            projectDigest = Get-CodeqlSetDigest @($relativeProject)
            expectedSources = $sources.Count; extractedSources = $sources.Count
            expectedSourceDigest = Get-CodeqlSetDigest $expectedIdentities
            extractedSourceDigest = Get-CodeqlSetDigest $extractedIdentities
            extractionErrors = 0
        }
        $buildIdentities += Get-CodeqlDigest $capture.FullName
    }
    if ($projects.Count -eq 0) { throw 'CODEQL_BUILD_INPUTS_MISSING' }
    $trx = Join-Path $work 'sarif-summary.json'
    & (Join-Path $PSScriptRoot 'assurance-results.ps1') -ResultsPath $ResultsPath -Kind sarif -OutputPath $trx
    $sarif = Get-Content -LiteralPath $trx -Raw | ConvertFrom-Json
    $summary.documents = $sarif.documents
    if ($sarif.status -cne 'completed' -or @($sarif.documents).Count -ne 1) { throw 'CODEQL_ANALYSIS_INCOMPLETE' }
    $prefix = 'repos/OPCFoundation/UA-.NETStandard'
    $upload = Invoke-AssuranceApi "$prefix/code-scanning/sarifs/$SarifId"
    if ($upload.processing_status -cne 'complete' -or
        $upload.analyses_url -cnotmatch '^https://api\.github\.com/repos/OPCFoundation/UA-\.NETStandard/code-scanning/analyses\?sarif_id=[A-Za-z0-9-]+$') {
        throw 'CODEQL_UPLOAD_IDENTITY_UNVERIFIED'
    }
    $endpoint = $upload.analyses_url.Substring('https://api.github.com/'.Length)
    $analyses = @(Invoke-AssuranceApi ($endpoint + '&per_page=100'))
    if ($analyses.Count -ne 1) { throw 'CODEQL_ANALYSIS_IDENTITY_AMBIGUOUS' }
    $analysis = $analyses[0]
    if ($analysis.commit_sha -cne $sha -or $analysis.ref -cne $env:GITHUB_REF -or
        $analysis.tool.name -cne 'CodeQL' -or $analysis.tool.version -cne $CodeqlVersion -or
        $analysis.category -cne 'assurance-csharp-net10' -or
        $analysis.analysis_key -cne '.github/workflows/codeql-analysis.yml:analyze' -or
        $analysis.error -or [string] $analysis.id -cnotmatch '^[1-9][0-9]*$' -or
        ($analysis.results_count -isnot [int] -and $analysis.results_count -isnot [long]) -or
        $analysis.results_count -lt 0) {
        throw 'CODEQL_ANALYSIS_IDENTITY_MISMATCH'
    }
    $startedAt = [DateTimeOffset]::Parse((Get-Content -LiteralPath (Join-Path $work 'started-at.txt') -Raw).Trim())
    $analysisAt = [DateTimeOffset] $analysis.created_at
    if ($analysisAt -lt $startedAt -or $analysisAt -gt [DateTimeOffset]::UtcNow) {
        throw 'CODEQL_ANALYSIS_STALE'
    }
    $findingCount = [long] $sarif.counts.findings
    $population = @(
        foreach ($file in Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter '*.sarif' -File) {
            $raw = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            foreach ($run in $raw.runs) {
                $reportedVersion = if ($run.tool.driver.semanticVersion) {
                    $run.tool.driver.semanticVersion
                } else { $run.tool.driver.version }
                if ($reportedVersion -cne $CodeqlVersion) { throw 'CODEQL_SARIF_TOOL_MISMATCH' }
                foreach ($finding in $run.results) {
                    if ($null -ne $finding) { $finding | ConvertTo-Json -Depth 50 -Compress }
                }
            }
        }
    )
    $summary.counts = @{ findings = $findingCount; analyzedProjects = $projects.Count }
    $summary.analysis = @{
        status = 'completed'
        sourceSha = $sha; sourceRef = $env:GITHUB_REF; runId = $env:GITHUB_RUN_ID
        attempt = [int] $env:GITHUB_RUN_ATTEMPT; analysisId = [string] $analysis.id; sarifId = $SarifId
        category = $analysis.category; tool = @{ name = 'CodeQL'; version = $CodeqlVersion; digest = Get-CodeqlDigest $CodeqlPath }
        databaseDigest = Get-CodeqlDatabaseDigest $database
        databaseMetadataDigest = Get-CodeqlDigest $databaseFile; extractionDigest = Get-CodeqlDigest $decoded
        configurationDigest = Get-CodeqlDigest (Join-Path $work 'codeql-config.yml')
        suiteDigest = Get-CodeqlDigest $suitePath; packDigest = Get-CodeqlDigest $packPath
        queryDigest = Get-CodeqlSetDigest $queryIdentities; queryResultsDigest = Get-CodeqlSetDigest $resultIdentities
        expectedQueries = $queries.Count; completedQueries = $resultIdentities.Count
        buildDigest = Get-CodeqlSetDigest $buildIdentities; projects = $projects
        populationDigest = Get-CodeqlSetDigest $population -PreserveMultiplicity
        findings = $findingCount; reportedResults = [long] $analysis.results_count
        disposition = 'missing'
    }
    if ($findingCount -eq 0 -and $analysis.results_count -eq 0) {
        $summary.analysis.distinctAlerts = 0
        $summary.analysis.disposition = 'clean-analysis'
        $summary.counts.unresolvedFindings = 0
        $summary.status = 'completed'
    }
    else {
        # results_count is an analysis result count, not an authenticated deduplicated alert population.
        $summary.reasons += 'CODEQL_AUTHENTICATED_FINDING_REVIEW_MISSING'
        if ($ReviewReferencePath) {
            $reference = Get-Content -LiteralPath $ReviewReferencePath -Raw | ConvertFrom-Json
            if ($reference.id -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$' -or
                $reference.digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'CODEQL_REVIEW_REFERENCE_INVALID' }
            $summary.review = @{ id = $reference.id; digest = $reference.digest }
        }
    }
}
catch {
    $code = [string] $_.Exception.Message
    $summary.reasons += $(if ($code -cmatch '^CODEQL_[A-Z_]+$') { $code } else { 'CODEQL_PROOF_UNVERIFIED' })
    Write-Host "CodeQL assurance remains missing: $($summary.reasons[-1])."
}
finally { if ($null -ne $sourceArchive) { $sourceArchive.Dispose() } }
$null = New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))) -Force
$summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
