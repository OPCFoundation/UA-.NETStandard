<# ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================#>

param([string]$CaseName)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, '..', '..'))
Set-Location -LiteralPath $root
$head = @(& git rev-parse --verify HEAD)
if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1) {
    throw 'The repository HEAD could not be resolved.'
}
$env:CI_TEST_MISSING_BASE = "refs/heads/ci-missing-$([Guid]::NewGuid().ToString('N'))"
$missingBasePrelude = @'
function git {
    $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    if ($args[0] -eq 'diff') {
        if ($args.Count -ne 4 -or $args[1] -ne '--name-only' -or $args[2] -ne '--no-renames' -or
            $args[3] -notin @("origin/master...$($env:GITHUB_SHA)", "$($env:GITHUB_SHA)..$($env:GITHUB_SHA)")) {
            throw 'Unexpected Git comparison.'
        }
        $range = if ($args[3] -eq "origin/master...$($env:GITHUB_SHA)") {
            "$($env:CI_TEST_MISSING_BASE)...$($env:GITHUB_SHA)"
        }
        else {
            $args[3]
        }
        & $executable diff --name-only --no-renames $range
        return
    }
    if ($args[0] -eq 'show-ref' -and $args.Count -eq 4 -and $args[1] -eq '--verify' -and
        $args[2] -eq '--quiet' -and $args[3] -eq 'refs/remotes/origin/master') {
        & $executable show-ref --verify --quiet $env:CI_TEST_MISSING_BASE
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
$unrelatedHistoryPrelude = @'
$executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
$env:GIT_OBJECT_DIRECTORY = $env:CI_TEST_OBJECT_DIRECTORY
$env:GIT_AUTHOR_NAME = 'CI test'
$env:GIT_AUTHOR_EMAIL = 'ci@example.invalid'
$env:GIT_COMMITTER_NAME = 'CI test'
$env:GIT_COMMITTER_EMAIL = 'ci@example.invalid'
$tree = @($null | & $executable mktree)
if ($LASTEXITCODE -ne 0 -or $tree.Count -ne 1) {
    throw 'Could not create the unrelated-history tree.'
}
$left = @('left' | & $executable -c commit.gpgsign=false commit-tree $tree[0])
if ($LASTEXITCODE -ne 0 -or $left.Count -ne 1) {
    throw 'Could not create the first unrelated commit.'
}
$right = @('right' | & $executable -c commit.gpgsign=false commit-tree $tree[0])
if ($LASTEXITCODE -ne 0 -or $right.Count -ne 1) {
    throw 'Could not create the second unrelated commit.'
}
function git {
    switch ($args[0]) {
        'diff' {
            if ($args.Count -ne 4 -or $args[1] -ne '--name-only' -or $args[2] -ne '--no-renames' -or
                $args[3] -notin @("origin/master...$($env:GITHUB_SHA)", "$($env:GITHUB_SHA)..$($env:GITHUB_SHA)")) {
                throw 'Unexpected Git comparison.'
            }
            $range = if ($args[3] -eq "origin/master...$($env:GITHUB_SHA)") {
                "$($left[0])...$($right[0])"
            }
            else {
                "$($right[0])..$($right[0])"
            }
            & $executable diff --name-only --no-renames $range
        }
        'show-ref' {
            if ($args.Count -ne 4 -or $args[1] -ne '--verify' -or $args[2] -ne '--quiet' -or
                $args[3] -ne 'refs/remotes/origin/master') {
                throw 'Unexpected Git reference lookup.'
            }
            & $executable cat-file -e "$($left[0])^{commit}"
        }
        'merge-base' {
            if ($args.Count -ne 3 -or $args[1] -ne 'origin/master' -or $args[2] -ne $env:GITHUB_SHA) {
                throw 'Unexpected Git merge-base lookup.'
            }
            & $executable merge-base $left[0] $right[0]
        }
        default { throw 'Unexpected Git invocation.' }
    }
}
'@
function Get-WorkflowBody {
    param([string]$Name, [string]$Indent)
    $workflow = [System.IO.Path]::Combine($root, '.github', 'workflows', $Name)
    $lines = @(Get-Content -LiteralPath $workflow)
    $step = [array]::IndexOf($lines, "$Indent- name: Resolve path relevance")
    if ($step -lt 0) {
        throw "The relevance step was not found in $Name."
    }
    $run = $step + 1
    while ($run -lt $lines.Count -and $lines[$run] -ne "$Indent  run: |") {
        if ($lines[$run].StartsWith("$Indent- ")) {
            throw "The relevance step in $Name has no run block."
        }
        $run++
    }
    if ($run -ge $lines.Count) {
        throw "The relevance step in $Name has no run block."
    }
    $body = @()
    $bodyIndent = $Indent + '    '
    for ($i = $run + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].StartsWith($bodyIndent)) {
            $body += $lines[$i].Substring($bodyIndent.Length)
        }
        elseif ($lines[$i].Trim().Length -eq 0) {
            $body += ''
        }
        else {
            break
        }
    }
    if ($body.Count -eq 0) {
        throw "The relevance run block in $Name is empty."
    }
    return $body
}

$bodies = @{
    Build = @(Get-WorkflowBody -Name 'buildandtest.yml' -Indent '    ')
    Package = @(Get-WorkflowBody -Name 'nuget-publish.yml' -Indent '      ')
    Image = @(Get-WorkflowBody -Name 'docker-image.yml' -Indent '    ')
}
$missingCommit = '0000000000000000000000000000000000000001'
& git cat-file -e "${missingCommit}^{commit}" 2>$null
if ($LASTEXITCODE -eq 0) {
    throw 'The missing previous-commit fixture unexpectedly exists.'
}
$nonCommit = @(& git rev-parse 'HEAD:README.md')
if ($LASTEXITCODE -ne 0 -or $nonCommit.Count -ne 1) {
    throw 'The previous non-commit fixture could not be resolved.'
}

$imageDocsPrelude = @'
function git {
    switch ($args[0]) {
        'cat-file' { $global:LASTEXITCODE = 0; "$($env:BEFORE_SHA) commit" }
        'diff' { $global:LASTEXITCODE = 0; 'docs/Overview.md'; 'tests/Example.cs' }
        default { throw 'Unexpected Git invocation.' }
    }
}
'@
$imageCodePrelude = @'
function git {
    switch ($args[0]) {
        'cat-file' { $global:LASTEXITCODE = 0; "$($env:BEFORE_SHA) commit" }
        'diff' { $global:LASTEXITCODE = 0; 'docs/Overview.md'; 'src/Example.cs' }
        default { throw 'Unexpected Git invocation.' }
    }
}
'@
$analyzerInputPrelude = @'
function git {
    $global:LASTEXITCODE = 0
    'tools/Opc.Ua.MigrationAnalyzer/AnalyzerReleases.Unshipped.md'
}
'@
$windowsAnalyzerInputPrelude = @'
function git {
    $global:LASTEXITCODE = 0
    'tools\Opc.Ua.SourceGeneration\AnalyzerReleases.Shipped.md'
}
'@
$whitespacePathPrelude = @'
$executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
$env:GIT_OBJECT_DIRECTORY = $env:CI_TEST_OBJECT_DIRECTORY
$emptyTree = @($null | & $executable mktree)
if ($LASTEXITCODE -ne 0 -or $emptyTree.Count -ne 1) {
    throw 'Could not create the empty fixture tree.'
}
$blob = @('fixture' | & $executable hash-object -w --stdin)
if ($LASTEXITCODE -ne 0 -or $blob.Count -ne 1) {
    throw 'Could not create the fixture blob.'
}
$process = [System.Diagnostics.Process]::new()
try {
    $process.StartInfo.FileName = $executable
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.ArgumentList.Add('mktree')
    if (-not $process.Start()) {
        throw 'Could not start Git to create the fixture tree.'
    }
    $inputBytes = [System.Text.Encoding]::UTF8.GetBytes("100644 blob $($blob[0])`t `n")
    $process.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length)
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(10000)) {
        Stop-Process -Id $process.Id
        throw 'Git timed out creating the fixture tree.'
    }
    $tree = $process.StandardOutput.ReadToEnd().Trim()
    $diagnostics = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0 -or $tree.Length -eq 0) {
        throw "Could not create the whitespace fixture tree: $diagnostics"
    }
}
finally {
    $process.Dispose()
}
function git {
    if ($args.Count -ne 4 -or $args[0] -ne 'diff' -or
        $args[1] -ne '--name-only' -or $args[2] -ne '--no-renames') {
        throw 'Unexpected Git comparison.'
    }
    $paths = @(& $executable diff --name-only --no-renames $emptyTree[0] $tree)
    if ($LASTEXITCODE -ne 0 -or $paths.Count -ne 1 -or $paths[0] -cne ' ') {
        throw 'Git did not return the whitespace fixture path.'
    }
    $paths
}
'@
$renamePrelude = @'
function git {
    if ($args[0] -eq 'cat-file') {
        $global:LASTEXITCODE = 0
        "$($env:BEFORE_SHA) commit"
        return
    }
    if ($args[0] -ne 'diff' -or $args[1] -ne '--name-only') {
        throw 'Unexpected Git invocation.'
    }

    $fixture = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(), "opcua-rename-$([Guid]::NewGuid().ToString('N'))")
    try {
        $source = [System.IO.Path]::Combine($fixture, 'before', $env:CI_TEST_RENAME_SOURCE)
        $destination = [System.IO.Path]::Combine($fixture, 'after', $env:CI_TEST_RENAME_DESTINATION)
        [void][System.IO.Directory]::CreateDirectory($source)
        [void][System.IO.Directory]::CreateDirectory($destination)
        $extension = if ($env:CI_TEST_RENAME_SOURCE -eq 'docs') { 'md' } else { 'cs' }
        [System.IO.File]::WriteAllText([System.IO.Path]::Combine($source, "Old.$extension"), 'rename fixture')
        [System.IO.File]::WriteAllText([System.IO.Path]::Combine($destination, "Example.$extension"), 'rename fixture')

        $gitArguments = @('-c', 'diff.renames=true', 'diff', '--no-index', '--name-status')
        if ($args -contains '--no-renames') {
            $gitArguments += '--no-renames'
        }
        $gitArguments += @('--', 'before', 'after')
        $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
        Push-Location -LiteralPath $fixture
        try {
            $entries = @(& $executable @gitArguments)
            if ($LASTEXITCODE -ne 1) {
                throw "The rename fixture did not produce the expected diff: $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }

        foreach ($entry in $entries) {
            $columns = $entry -split "`t"
            if ($columns.Count -eq 3 -and $columns[0] -match '^R[0-9]+$') {
                $path = $columns[2]
            }
            elseif ($columns.Count -eq 2 -and $columns[0] -in @('A', 'D')) {
                $path = $columns[1]
            }
            else {
                throw "Unexpected rename fixture output: $entry"
            }

            if ($path.StartsWith('before/')) {
                $path.Substring('before/'.Length)
            }
            elseif ($path.StartsWith('after/')) {
                $path.Substring('after/'.Length)
            }
            else {
                throw "Unexpected rename fixture path: $path"
            }
        }
        $global:LASTEXITCODE = 0
    }
    finally {
        if (Test-Path -LiteralPath $fixture) {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}
'@

$cases = @(
    @{
        Name = 'MissingMergeBase'
        EventName = 'push'
        Protected = 'false'
        Prelude = $missingBasePrelude
        Expected = 'relevant=true'
        Message = 'Could not resolve the merge base with master; packing.'
        Error = $false
    },
    @{
        Name = 'PackageUnrelatedHistory'
        EventName = 'push'
        Protected = 'false'
        Prelude = $unrelatedHistoryPrelude
        GitObjects = $true
        Expected = 'relevant=true'
        Message = 'Could not resolve the merge base with master; packing.'
        Error = $false
    },
    @{
        Name = 'PackageMissingMasterDoesNotHideMissingCommit'
        EventName = 'push'
        Protected = 'false'
        TargetSha = $missingCommit
        Prelude = $missingBasePrelude
        Expected = ''
        Message = 'Could not verify branch commit'
        Error = $true
    },
    @{
        Name = 'PackageMissingMasterDoesNotHideMissingCommitId'
        EventName = 'push'
        Protected = 'false'
        TargetSha = ''
        Prelude = $missingBasePrelude
        Expected = ''
        Message = 'Could not verify branch commit'
        Error = $true
    },
    @{
        Name = 'PackageMissingMasterDoesNotHideDiffFailure'
        EventName = 'push'
        Protected = 'false'
        Prelude = @'
function git {
    $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    if ($args[0] -eq 'diff') {
        & $executable diff --ci-test-invalid-option
        return
    }
    if ($args[0] -eq 'show-ref') {
        & $executable show-ref --verify --quiet $env:CI_TEST_MISSING_BASE
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = ''
        Message = 'Could not verify branch commit'
        Error = $true
    },
    @{
        Name = 'PackageMissingMasterRejectsUnexpectedVerificationOutput'
        EventName = 'push'
        Protected = 'false'
        Prelude = @'
function git {
    $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    if ($args[0] -eq 'diff' -and $args[3] -eq "origin/master...$($env:GITHUB_SHA)") {
        & $executable diff --ci-test-invalid-option
        return
    }
    if ($args[0] -eq 'show-ref') {
        & $executable show-ref --verify --quiet $env:CI_TEST_MISSING_BASE
        return
    }
    if ($args[0] -eq 'diff' -and $args[3] -eq "$($env:GITHUB_SHA)..$($env:GITHUB_SHA)") {
        $global:LASTEXITCODE = 0
        'src/unexpected.cs'
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = ''
        Message = 'unexpected Git diff output'
        Error = $true
    },
    @{
        Name = 'PackageMissingMasterRetainsVerificationWarning'
        EventName = 'push'
        Protected = 'false'
        Prelude = @'
function git {
    $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    if ($args[0] -eq 'diff' -and $args[3] -eq "origin/master...$($env:GITHUB_SHA)") {
        & $executable diff --name-only --no-renames "$($env:CI_TEST_MISSING_BASE)...$($env:GITHUB_SHA)"
        return
    }
    if ($args[0] -eq 'show-ref') {
        & $executable show-ref --verify --quiet $env:CI_TEST_MISSING_BASE
        return
    }
    if ($args[0] -eq 'diff' -and $args[3] -eq "$($env:GITHUB_SHA)..$($env:GITHUB_SHA)") {
        Write-Error -Message 'injected-verification-warning' -ErrorAction Continue
        $global:LASTEXITCODE = 0
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = 'relevant=true'
        Message = 'injected-verification-warning'
        Error = $false
    },
    @{
        Name = 'DocumentationOnly'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''README.md'' }'
        Expected = 'relevant=false'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageDiffWarningDoesNotBecomeAPath'
        EventName = 'push'
        Protected = 'false'
        Prelude = @'
function git {
    Write-Error -Message 'injected-diff-warning' -ErrorAction Continue
    $global:LASTEXITCODE = 0
    'README.md'
}
'@
        Expected = 'relevant=false'
        Message = 'injected-diff-warning'
        Error = $false
    },
    @{
        Name = 'NoChanges'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0 }'
        Expected = 'relevant=false'
        Message = ''
        Error = $false
    },
    @{
        Name = 'CodeChange'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''src/Opc.Ua.Types/BuiltIn/NodeId.cs'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'MixedChanges'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''README.md''; ''src/Opc.Ua.Types/BuiltIn/NodeId.cs'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageAnalyzerInput'
        EventName = 'push'
        Protected = 'false'
        Prelude = $analyzerInputPrelude
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageReadmeInput'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''src/Opc.Ua.Client/NugetREADME.md'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageMetaReadmeInput'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''nuget/MetaPackageREADME.md'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageNestedMarkdown'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''samples/Scales/README.md'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageDistinctRootDocumentCase'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''readme.md'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageWhitespaceOnlyFileName'
        EventName = 'push'
        Protected = 'false'
        Prelude = $whitespacePathPrelude
        GitObjects = $true
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageSourceMovedIntoDocs'
        EventName = 'push'
        Protected = 'false'
        Prelude = $renamePrelude
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'PackageDocumentationMovedWithinDocs'
        EventName = 'push'
        Protected = 'false'
        RenameSource = 'docs'
        Prelude = $renamePrelude
        Expected = 'relevant=false'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildDocumentationOnly'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''docs/Overview.md''; ''README.md'' }'
        Expected = 'relevant_changes=false'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildDistinctDocumentationDirectoryCase'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''DOCS/Generated.cs'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildWhitespaceOnlyFileName'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = $whitespacePathPrelude
        GitObjects = $true
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildAnalyzerInput'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = $analyzerInputPrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildPackageReadmeInput'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''src/Opc.Ua.Client/NugetREADME.md'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildMetaReadmeMixedChanges'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''README.md''; ''nuget/MetaPackageREADME.md'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildWindowsAnalyzerPath'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = $windowsAnalyzerInputPrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildSourceMovedIntoDocs'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = $renamePrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildDocumentationMovedWithinDocs'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        RenameSource = 'docs'
        Prelude = $renamePrelude
        Expected = 'relevant_changes=false'
        Message = ''
        Error = $false
    },
    @{
        Name = 'BuildGitDiffFailure'
        Workflow = 'Build'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 128 }'
        Expected = ''
        Message = 'Could not diff'
        Error = $true
    },
    @{
        Name = 'ProtectedRef'
        EventName = 'push'
        Protected = 'true'
        Prelude = 'function git { throw ''Git must not run.'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'ManualRun'
        EventName = 'workflow_dispatch'
        Protected = 'false'
        Prelude = 'function git { throw ''Git must not run.'' }'
        Expected = 'relevant=true'
        Message = ''
        Error = $false
    },
    @{
        Name = 'MissingOutputDirectory'
        EventName = 'push'
        Protected = 'false'
        Prelude = $missingBasePrelude
        Expected = ''
        Message = 'Out-File'
        Error = $true
        MissingOutputParent = $true
    },
    @{
        Name = 'UnexpectedGitError'
        EventName = 'push'
        Protected = 'false'
        Prelude = 'function git { throw ''injected-git-failure'' }'
        Expected = ''
        Message = 'injected-git-failure'
        Error = $true
    }
    @{
        Name = 'PackageUnexpectedNativeDiffFailure'
        EventName = 'push'
        Protected = 'false'
        Prelude = @'
function git {
    if ($args[0] -eq 'diff') {
        $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
        & $executable diff --ci-test-invalid-option
        return
    }
    if ($args[0] -eq 'merge-base') {
        $env:GITHUB_SHA
    }
    $global:LASTEXITCODE = 0
}
'@
        Expected = ''
        Message = 'Could not diff'
        Error = $true
    }
    @{
        Name = 'PackageUnexpectedNativeRefProbeFailure'
        EventName = 'push'
        Protected = 'false'
        Prelude = @'
function git {
    $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    if ($args[0] -eq 'diff') {
        & $executable diff --ci-test-invalid-option
        return
    }
    if ($args[0] -eq 'show-ref') {
        $missing = [System.IO.Path]::Combine(
            [System.IO.Path]::GetTempPath(), "opcua-missing-$([Guid]::NewGuid().ToString('N'))")
        & $executable -C $missing @args
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = ''
        Message = 'Could not inspect origin/master'
        Error = $true
    }
    @{
        Name = 'PackageUnexpectedNativeMergeBaseFailure'
        EventName = 'push'
        Protected = 'false'
        Prelude = @'
function git {
    $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    if ($args[0] -eq 'diff') {
        & $executable diff --ci-test-invalid-option
        return
    }
    if ($args[0] -eq 'show-ref') {
        $global:LASTEXITCODE = 0
        return
    }
    if ($args[0] -eq 'merge-base') {
        $missing = [System.IO.Path]::Combine(
            [System.IO.Path]::GetTempPath(), "opcua-missing-$([Guid]::NewGuid().ToString('N'))")
        & $executable -C $missing @args
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = ''
        Message = 'Could not determine the merge base'
        Error = $true
    }
    @{
        Name = 'ImageMissingPreviousCommit'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $missingCommit
        Prelude = ''
        Expected = 'relevant_changes=true'
        Message = 'Could not resolve previous commit'
        Error = $false
    }
    @{
        Name = 'ImagePreviousObjectIsNotACommit'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $nonCommit[0]
        Prelude = ''
        Expected = 'relevant_changes=true'
        Message = 'Could not resolve previous commit'
        Error = $false
    }
    @{
        Name = 'ImageUnexpectedNativeObjectProbeFailure'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = @'
function git {
    if ($args[0] -eq 'cat-file') {
        $executable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
        $missing = [System.IO.Path]::Combine(
            [System.IO.Path]::GetTempPath(), "opcua-missing-$([Guid]::NewGuid().ToString('N'))")
        & $executable -C $missing @args
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = ''
        Message = 'Could not inspect previous commit'
        Error = $true
    }
    @{
        Name = 'ImageUnexpectedObjectProbeResponse'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = @'
function git {
    if ($args[0] -eq 'cat-file') {
        $global:LASTEXITCODE = 0
        'unexpected-response'
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = ''
        Message = 'unexpected Git response'
        Error = $true
    }
    @{
        Name = 'ImageUnexpectedMultipleObjectProbeResponses'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = @'
function git {
    if ($args[0] -eq 'cat-file') {
        $global:LASTEXITCODE = 0
        "$($env:BEFORE_SHA) commit"
        'unexpected-extra-response'
        return
    }
    throw 'Unexpected Git invocation.'
}
'@
        Expected = ''
        Message = 'unexpected Git response'
        Error = $true
    }
    @{
        Name = 'ImageNewBranch'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = '0000000000000000000000000000000000000000'
        Prelude = 'function git { throw ''Git must not run.'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImageMalformedPreviousCommit'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = 'not-a-commit'
        Prelude = 'function git { throw ''Git must not run.'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImageManualRun'
        Workflow = 'Image'
        EventName = 'workflow_dispatch'
        Protected = 'false'
        Prelude = 'function git { throw ''Git must not run.'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePushNoChanges'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = ''
        Expected = 'relevant_changes=false'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePushDocumentationAndTests'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = $imageDocsPrelude
        Expected = 'relevant_changes=false'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePushMixedChanges'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = $imageCodePrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePullRequestDocumentationAndTests'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = $imageDocsPrelude
        Expected = 'relevant_changes=false'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePullRequestMissingBase'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        Prelude = 'function git { throw ''Git must not run.'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePullRequestNoChanges'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = ''
        Expected = 'relevant_changes=false'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePullRequestMixedChanges'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = $imageCodePrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImageDistinctDocumentationDirectoryCase'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''DOCS/Generated.cs'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImageDistinctTestDirectoryCase'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 0; ''TESTS/Generated.cs'' }'
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePullRequestSourceMovedIntoDocs'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = $renamePrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePushSourceMovedIntoDocs'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = $renamePrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImagePushSourceMovedIntoTests'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        RenameDestination = 'tests'
        Prelude = $renamePrelude
        Expected = 'relevant_changes=true'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImageDocumentationMovedWithinDocs'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        RenameSource = 'docs'
        Prelude = $renamePrelude
        Expected = 'relevant_changes=false'
        Message = ''
        Error = $false
    }
    @{
        Name = 'ImageGitDiffFailure'
        Workflow = 'Image'
        EventName = 'pull_request'
        Protected = 'false'
        BaseSha = $head[0]
        Prelude = 'function git { $global:LASTEXITCODE = 128 }'
        Expected = ''
        Message = 'Could not diff'
        Error = $true
    }
    @{
        Name = 'ImageUnexpectedGitError'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $head[0]
        Prelude = 'function git { throw ''injected-image-git-failure'' }'
        Expected = ''
        Message = 'injected-image-git-failure'
        Error = $true
    }
    @{
        Name = 'ImageMissingOutputDirectory'
        Workflow = 'Image'
        EventName = 'push'
        Protected = 'false'
        BeforeSha = $missingCommit
        Prelude = ''
        Expected = ''
        Message = 'Out-File'
        Error = $true
        MissingOutputParent = $true
    }
)

$executed = 0
foreach ($case in $cases) {
    if ($CaseName -and $case.Name -ne $CaseName) {
        continue
    }
    $executed++
    $env:EVENT_NAME = $case.EventName
    $env:PROTECTED_REF = $case.Protected
    $env:GITHUB_SHA = if ($case.ContainsKey('TargetSha')) { $case.TargetSha } else { $head[0] }
    $env:BEFORE_SHA = if ($case.ContainsKey('BeforeSha')) { $case.BeforeSha } else { '' }
    $env:BASE_SHA = if ($case.ContainsKey('BaseSha')) { $case.BaseSha } else { '' }
    $env:CI_TEST_RENAME_SOURCE = if ($case.ContainsKey('RenameSource')) {
        $case.RenameSource
    }
    else {
        'src'
    }
    $env:CI_TEST_RENAME_DESTINATION = if ($case.ContainsKey('RenameDestination')) {
        $case.RenameDestination
    }
    else {
        'docs'
    }
    $env:PKCS11_NATIVE_LOADER_DIAGNOSTICS = 'false'
    $env:PKCS11_FULL_SHARD_DIAGNOSTICS = 'false'
    $workflow = if ($case.ContainsKey('Workflow')) { $case.Workflow } else { 'Package' }
    $outputPath = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(), "opcua-relevance-$([Guid]::NewGuid().ToString('N'))")
    $objectDirectory = if ($case.ContainsKey('GitObjects')) { "$outputPath-objects" } else { '' }
    if ($objectDirectory) {
        [void][System.IO.Directory]::CreateDirectory($objectDirectory)
    }
    $env:CI_TEST_OBJECT_DIRECTORY = $objectDirectory
    $env:GITHUB_OUTPUT = if ($case.ContainsKey('MissingOutputParent')) {
        [System.IO.Path]::Combine($outputPath, 'output.txt')
    }
    else {
        "$outputPath.txt"
    }
    $env:NO_COLOR = '1'
    $env:TERM = 'dumb'
    $script = '$ErrorActionPreference = ''Stop''' + "`n" + $case.Prelude + "`n" +
        ($bodies[$workflow] -join "`n") + "`n" +
        'if (Test-Path -LiteralPath variable:\LASTEXITCODE) { exit $LASTEXITCODE }'
    try {
        $messages = @(& pwsh -NoLogo -NoProfile -NonInteractive -Command $script 2>&1)
        $exitCode = $LASTEXITCODE
        $result = if (Test-Path -LiteralPath $env:GITHUB_OUTPUT) {
            (Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw).Trim()
        }
        else {
            ''
        }
        if ($workflow -eq 'Build' -and $result.Length -ne 0) {
            $outputs = @{}
            foreach ($line in ($result -split "`r?`n")) {
                $separator = $line.IndexOf('=')
                if ($separator -le 0) {
                    throw "$($case.Name): unexpected build output '$line'."
                }
                $name = $line.Substring(0, $separator)
                if ($outputs.ContainsKey($name)) {
                    throw "$($case.Name): duplicate build output '$name'."
                }
                $outputs[$name] = $line.Substring($separator + 1)
            }
            if (-not $outputs.ContainsKey('relevant_changes') -or -not $outputs.ContainsKey('aot_matrix')) {
                throw "$($case.Name): build relevance or AOT matrix output is missing."
            }
            $matrix = @(ConvertFrom-Json -InputObject $outputs['aot_matrix'])
            if (($outputs['relevant_changes'] -eq 'true' -and $matrix.Count -ne 3) -or
                ($outputs['relevant_changes'] -eq 'false' -and $matrix.Count -ne 0)) {
                throw "$($case.Name): relevance and AOT matrix disagree."
            }
            $result = "relevant_changes=$($outputs['relevant_changes'])"
        }
        $text = ($messages | Out-String)
        $exitMatches = if ($case.Error) { $exitCode -ne 0 } else { $exitCode -eq 0 }
        if (-not $exitMatches -or $result -ne $case.Expected -or
            ($case.Message.Length -ne 0 -and -not $text.Contains($case.Message))) {
            throw "$($case.Name): exit=$exitCode, output='$result', messages=$text"
        }
        Write-Output "PASS $($case.Name)"
    }
    finally {
        if (Test-Path -LiteralPath $env:GITHUB_OUTPUT) {
            Remove-Item -LiteralPath $env:GITHUB_OUTPUT
        }
        if ($objectDirectory -and (Test-Path -LiteralPath $objectDirectory)) {
            Remove-Item -LiteralPath $objectDirectory -Recurse -Force
        }
    }
}
if ($executed -eq 0) {
    throw "No relevance case matched '$CaseName'."
}
exit 0
