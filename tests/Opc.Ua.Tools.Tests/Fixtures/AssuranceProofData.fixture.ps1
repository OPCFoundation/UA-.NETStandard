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

# Isolated synthetic records test the proof validators, not live producer execution.
function Get-AssuranceFixtureDigest([string] $Text) {
    return 'sha256:' + [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}

function New-AssuranceFixtureNative($Directory, $SourceSha, $RunId, $Attempt) {
    $imagePath = Join-Path $Directory 'synthetic-native.exe'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'AssuranceNative.fixture.ps1') `
        -Scenario native-image -OutputImage $imagePath
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic PE generation failed.' }
    . (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot))) '.azurepipelines\assurance-native-image.ps1')
    $image = Get-AssuranceNativeImage $imagePath
    Remove-Item -LiteralPath $imagePath -Force
    $now = [DateTimeOffset]::UtcNow.AddMinutes(-1)
    return @{
        schemaVersion = 1; kind = 'native-aot'; status = 'completed'
        documents = @(@{ digest = Get-AssuranceFixtureDigest '<TestRun>synthetic MTP fixture</TestRun>' })
        counts = @{ total = 2; executed = 2; passed = 2; failed = 0; skipped = 0 }
        native = @{
            project = 'tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj'; configuration = 'Release'
            hostTfm = 'net10.0'; libraryTfm = 'net10.0'; runtimeIdentifier = 'win-x64'; publishAot = $true
            sourceSha = $SourceSha; runId = [string] $RunId; attempt = $Attempt
            nonce = [guid]::NewGuid().ToString('N'); processId = 1234; exitCode = 0
            producedDigest = $image.digest; launchedDigest = $image.digest
            observedDigest = $image.digest; postRunDigest = $image.digest
            startedAt = $now.ToString('o'); completedAt = $now.AddSeconds(1).ToString('o')
            image = $image
            runtime = @{
                framework = '.NET 10.0.11'; architecture = 'x64'; isDynamicCodeSupported = $false
                isDynamicCodeCompiled = $false; reportPhase = 'completed'
            }
            tools = @{
                sdkVersion = '10.0.303'; compilerVersion = '10.0.11'
                compilerDigest = Get-AssuranceFixtureDigest 'isolated synthetic compiler identity'
            }
        }
    }
}

function New-AssuranceFixtureCodeql($SourceSha, $RunId, $Attempt) {
    $projects = @(
        foreach ($project in @('alpha', 'beta')) {
            $digest = Get-AssuranceFixtureDigest "$project/One.cs`n$project/Two.cs"
            @{
                projectDigest = Get-AssuranceFixtureDigest "$project/$project.csproj"
                expectedSources = 2; extractedSources = 2; extractionErrors = 0
                expectedSourceDigest = $digest; extractedSourceDigest = $digest
            }
        }
    )
    return @{
        schemaVersion = 1; kind = 'codeql'; status = 'completed'
        documents = @(@{ digest = Get-AssuranceFixtureDigest '{"version":"2.1.0","synthetic":"full-clean-analysis"}' })
        counts = @{ findings = 0; analyzedProjects = 2; unresolvedFindings = 0 }
        analysis = @{
            status = 'completed'; sourceSha = $SourceSha; sourceRef = 'refs/heads/master'
            runId = [string] $RunId; attempt = $Attempt; analysisId = '800'; sarifId = [guid]::NewGuid().ToString()
            category = 'assurance-csharp-net10'; expectedQueries = 2; completedQueries = 2
            findings = 0; distinctAlerts = 0; projects = $projects; disposition = 'clean-analysis'
            tool = @{ name = 'CodeQL'; version = '2.23.0'; digest = Get-AssuranceFixtureDigest 'synthetic-codeql-cli' }
            databaseDigest = Get-AssuranceFixtureDigest 'synthetic database finalized identity'
            databaseMetadataDigest = Get-AssuranceFixtureDigest 'synthetic resolve database metadata'
            extractionDigest = Get-AssuranceFixtureDigest 'two completed compilations, four extracted source files'
            configurationDigest = Get-AssuranceFixtureDigest 'name: assurance-csharp-net10'
            suiteDigest = Get-AssuranceFixtureDigest 'synthetic effective suite'
            packDigest = Get-AssuranceFixtureDigest 'synthetic resolved pack'
            queryDigest = Get-AssuranceFixtureDigest 'two resolved query identities'
            queryResultsDigest = Get-AssuranceFixtureDigest 'two successful BQRS identities'
            buildDigest = Get-AssuranceFixtureDigest 'two evaluated compiler input records'
            populationDigest = Get-AssuranceFixtureDigest ''
        }
    }
}
