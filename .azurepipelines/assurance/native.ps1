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
Publishes and launches a fresh native test image; no local run is a release approval.
.DESCRIPTION
PublishAot must be enabled by the application project, not a command-global
property that propagates to .NET Standard source-generator references.
The initial image verifier supports .NET 10 Windows x64 only. Other RIDs still
run their baseline tests, but retain missing native-format assurance. Raw runtime
reports and process paths stay in the private working directory.
#>
param(
    [Parameter(Mandatory)][string] $Project,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')][string] $RuntimeIdentifier,
    [Parameter(Mandatory)][string] $WorkDirectory,
    [Parameter(Mandatory)][string] $OutputPath,
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [ValidateRange(1, 3600)][int] $TimeoutSeconds = 1800,
    [switch] $NoRestore
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'native-image.ps1')
$root = Split-Path (Split-Path $PSScriptRoot)
$projectFile = [IO.Path]::GetFullPath($Project, $root)
$Project = [IO.Path]::GetRelativePath($root, $projectFile).Replace('\', '/')
if ($Project.StartsWith('../', [StringComparison]::Ordinal)) { throw 'NATIVE_PROJECT_OUTSIDE_REPOSITORY' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw 'NATIVE_WORK_DIRECTORY_NOT_FRESH' }
$null = New-Item -ItemType Directory -Path $work
$publish = Join-Path $work 'publish'
$results = Join-Path $work 'results'
$handshake = Join-Path $work 'handshake'
$null = New-Item -ItemType Directory -Path $results, $handshake
$output = [IO.Path]::GetFullPath($OutputPath)
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force
$summary = [ordered]@{ schemaVersion = 1; kind = 'native-aot'; status = 'missing'; documents = @(); reasons = @() }
$process = $null
$started = $false
$baselineFailed = $false
try {
    $sha = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') {
        throw 'NATIVE_SOURCE_UNAVAILABLE'
    }
    $runId = if ($env:GITHUB_ACTIONS -eq 'true') { $env:GITHUB_RUN_ID } else { $env:BUILD_BUILDID }
    $attempt = if ($env:GITHUB_ACTIONS -eq 'true') { $env:GITHUB_RUN_ATTEMPT } else { $env:SYSTEM_JOBATTEMPT }
    $arguments = @('publish', $projectFile, '-c', $Configuration, '-f', 'net10.0', '-r', $RuntimeIdentifier,
        '-p:CustomTestTarget=net10.0', '-o', $publish)
    if ($NoRestore) { $arguments += '--no-restore' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'NATIVE_PUBLISH_FAILED' }
    $evaluation = & dotnet msbuild $projectFile -nologo "-p:Configuration=$Configuration" `
        -p:CustomTestTarget=net10.0 "-p:RuntimeIdentifier=$RuntimeIdentifier" `
        "-p:PublishDir=$publish" `
        -getProperty:PublishAot,Configuration,TargetFramework,RuntimeIdentifier,AssemblyName,NETCoreSdkVersion,ProjectAssetsFile
    if ($LASTEXITCODE -ne 0) { throw 'NATIVE_EVALUATION_FAILED' }
    $properties = ($evaluation -join "`n" | ConvertFrom-Json).Properties
    if ($properties.PublishAot -cne 'true' -or $properties.TargetFramework -cne 'net10.0' -or
        $properties.RuntimeIdentifier -cne $RuntimeIdentifier -or $properties.Configuration -cne $Configuration -or
        $properties.NETCoreSdkVersion -cnotmatch '^10\.[0-9]+\.[0-9]+$' -or
        $properties.AssemblyName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]+$') { throw 'NATIVE_EVALUATION_MISMATCH' }
    $assets = Get-Content -LiteralPath $properties.ProjectAssetsFile -Raw | ConvertFrom-Json -AsHashtable
    $compiler = @($assets.libraries.Keys | Where-Object { $_ -match "^runtime\.$RuntimeIdentifier\.microsoft\.dotnet\.ilcompiler/" })
    if ($compiler.Count -ne 1) { throw 'NATIVE_COMPILER_IDENTITY_MISSING' }
    $compilerVersion = $compiler[0].Split('/')[1]
    $compilerFiles = @(
        foreach ($folder in $assets.packageFolders.Keys) {
            $name = if ($IsWindows) { 'ilc.exe' } else { 'ilc' }
            $candidate = Join-Path $folder ($assets.libraries[$compiler[0]].path + "/tools/$name")
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $candidate }
        }
    )
    if ($compilerFiles.Count -ne 1) { throw 'NATIVE_COMPILER_IDENTITY_MISSING' }
    $exeName = if ($IsWindows) { "$($properties.AssemblyName).exe" } else { $properties.AssemblyName }
    $exe = Join-Path $publish $exeName
    $image = if ($RuntimeIdentifier -eq 'win-x64' -and $IsWindows) { Get-AssuranceNativeImage $exe } else { $null }
    $producedDigest = 'sha256:' + (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $nonce = [guid]::NewGuid().ToString('N')
    $startInfo = [Diagnostics.ProcessStartInfo]::new($exe)
    $startInfo.WorkingDirectory = $publish
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('--report-trx', '--results-directory', $results, '--report-trx-filename', 'aot.trx')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.Environment['OPCUA_ASSURANCE_NATIVE_DIRECTORY'] = $handshake
    $startInfo.Environment['OPCUA_ASSURANCE_NATIVE_NONCE'] = $nonce
    $startInfo.Environment['OPCUA_ASSURANCE_SOURCE_SHA'] = $sha
    $startInfo.Environment['OPCUA_ASSURANCE_RUN_ID'] = [string] $runId
    $startInfo.Environment['OPCUA_ASSURANCE_ATTEMPT'] = [string] $attempt
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $launchedAt = [DateTimeOffset]::UtcNow
    $null = $process.Start()
    $started = $true
    $stdout = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
    $stderr = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
    $deadline = $launchedAt.AddSeconds($TimeoutSeconds)
    $reportPath = Join-Path $handshake 'runtime.started.json'
    while (-not (Test-Path -LiteralPath $reportPath)) {
        if ($process.HasExited) { throw 'NATIVE_CRASHED_BEFORE_REPORT' }
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'NATIVE_TIMEOUT' }
        Start-Sleep -Milliseconds 25
    }
    # The assembly hook cannot start tests until this independent observation is acknowledged.
    $observedPath = [IO.Path]::GetFullPath($process.MainModule.FileName)
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($observedPath, $exe)) {
        throw 'NATIVE_OBSERVED_IMAGE_MISMATCH'
    }
    $observedDigest = 'sha256:' + (Get-FileHash -LiteralPath $observedPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($observedDigest -cne $producedDigest) { throw 'NATIVE_OBSERVED_IMAGE_MISMATCH' }
    [IO.File]::WriteAllText((Join-Path $handshake 'launcher.ack.tmp'), $nonce)
    Move-Item -LiteralPath (Join-Path $handshake 'launcher.ack.tmp') -Destination (Join-Path $handshake 'launcher.ack')
    while (-not $process.HasExited -or -not $stdout.IsCompleted -or -not $stderr.IsCompleted) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'NATIVE_TIMEOUT' }
        Start-Sleep -Milliseconds 25
    }
    $completedAt = [DateTimeOffset]::UtcNow
    if ($process.ExitCode -ne 0) { throw 'NATIVE_PROCESS_FAILED' }
    $postDigest = 'sha256:' + (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($postDigest -cne $producedDigest) { throw 'NATIVE_IMAGE_CHANGED' }
    $runtime = $null
    foreach ($phase in @('started', 'completed')) {
        $runtime = Get-Content -LiteralPath (Join-Path $handshake "runtime.$phase.json") -Raw | ConvertFrom-Json
        if ($runtime.schemaVersion -ne 1 -or $runtime.phase -cne $phase -or $runtime.nonce -cne $nonce -or
            $runtime.processId -ne $process.Id -or $runtime.imageDigest -cne $producedDigest -or
            -not [StringComparer]::OrdinalIgnoreCase.Equals($runtime.imagePath, $exe) -or
            $runtime.sourceSha -cne $sha -or $runtime.runId -cne [string] $runId -or
            $runtime.attempt -cne [string] $attempt -or
            $runtime.isDynamicCodeSupported -isnot [bool] -or $runtime.isDynamicCodeSupported -or
            $runtime.isDynamicCodeCompiled -isnot [bool] -or $runtime.isDynamicCodeCompiled) {
            throw 'NATIVE_RUNTIME_REPORT_MISMATCH'
        }
        $observedAt = [DateTimeOffset] $runtime.observedAt
        if ($observedAt -lt $launchedAt -or $observedAt -gt $completedAt) { throw 'NATIVE_RUNTIME_REPORT_STALE' }
    }
    $trxProof = Join-Path $work 'mtp-results.json'
    & (Join-Path $PSScriptRoot 'results.ps1') -ResultsPath $results -Kind mtp-trx `
        -OutputPath $trxProof -RequireNoSkipped
    $mtp = Get-Content -LiteralPath $trxProof -Raw | ConvertFrom-Json
    $summary.documents = $mtp.documents
    $summary.counts = $mtp.counts
    if ($mtp.status -cne 'completed') { throw 'NATIVE_TESTS_INCOMPLETE' }
    $summary.native = @{
        project = $Project.Replace('\', '/'); configuration = $Configuration
        hostTfm = 'net10.0'; libraryTfm = 'net10.0'; runtimeIdentifier = $RuntimeIdentifier; publishAot = $true
        sourceSha = $sha; runId = [string] $runId; attempt = [int] $attempt
        nonce = $nonce; processId = $process.Id; exitCode = $process.ExitCode
        producedDigest = $producedDigest; launchedDigest = $producedDigest
        observedDigest = $observedDigest; postRunDigest = $postDigest
        startedAt = $launchedAt.ToString('o'); completedAt = $completedAt.ToString('o')
        image = $image
        runtime = @{
            framework = $runtime.framework; architecture = $runtime.architecture
            isDynamicCodeSupported = $runtime.isDynamicCodeSupported
            isDynamicCodeCompiled = $runtime.isDynamicCodeCompiled; reportPhase = $runtime.phase
        }
        tools = @{
            sdkVersion = $properties.NETCoreSdkVersion; compilerVersion = $compilerVersion
            compilerDigest = 'sha256:' + (Get-FileHash -LiteralPath $compilerFiles[0] -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    if ($null -eq $image) { $summary.reasons += 'NATIVE_FORMAT_ADAPTER_UNAVAILABLE' }
    elseif (-not $runId -or -not $attempt) { $summary.reasons += 'CI_PRODUCER_IDENTITY_MISSING' }
    else { $summary.status = 'completed' }
}
catch {
    $baselineFailed = $true
    $summary.status = 'failed'
    $code = [string] $_.Exception.Message
    $summary.reasons += $(if ($code -cmatch '^[A-Z][A-Z_]+$') { $code } else { 'NATIVE_PROOF_REJECTED' })
    Write-Host "Native assurance failed: $($summary.reasons[-1])."
}
finally {
    if ($null -ne $process) {
        if ($started -and -not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
    $summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $output -Encoding utf8
}
if ($baselineFailed) { exit 1 }
exit 0
