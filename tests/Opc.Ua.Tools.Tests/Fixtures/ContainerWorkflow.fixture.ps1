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

param([Parameter(Mandatory)][string]$Scenario)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
. (Join-Path $PSScriptRoot 'FixtureWorkspace.ps1')
$fixture = Join-Path (Get-FixturePhysicalTempDirectory) ('container-matrix-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $fixture '.azurepipelines/release') -Force

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "$Scenario : $Message" }
}

function Get-WorkflowJob([string]$Name) {
    $pattern = '(?ms)^  ' + [regex]::Escape($Name) + ':\r?\n(?<body>.*?)(?=^  [\w-]+:\r?$|\z)'
    $matches = [regex]::Matches($script:workflow, $pattern)
    Assert-True ($matches.Count -eq 1) "Expected exactly one workflow job '$Name'."
    return $matches[0].Groups['body'].Value
}

function Get-WorkflowStep([string]$Job, [string]$Name) {
    $pattern = '(?ms)^    - name: ' + [regex]::Escape($Name) +
        '\r?\n(?<body>.*?)(?=^    - (?:name:|uses:)|\z)'
    $matches = [regex]::Matches((Get-WorkflowJob $Job), $pattern)
    Assert-True ($matches.Count -eq 1) "Expected exactly one step '$Name' in '$Job'."
    return $matches[0].Groups['body'].Value
}

function Get-WorkflowScript([string]$Job, [string]$Name) {
    $step = Get-WorkflowStep $Job $Name
    $match = [regex]::Match($step, '(?m)^      run: \|\r?\n(?<script>(?: {8}[^\r\n]*(?:\r?\n|$)|\r?\n)+)')
    Assert-True $match.Success "Expected an inline script in '$Name'."
    return ($match.Groups['script'].Value -replace '(?m)^ {8}', '').TrimEnd()
}

function Assert-DockerStages($Image) {
    $dockerfile = Get-Content -LiteralPath (Join-Path $root $Image.dockerfile) -Raw
    $declarations = [regex]::Matches($dockerfile,
        '(?m)^FROM (?:--platform=(?<platform>\S+) )?(?<base>\S+) AS (?<name>\w+)\r?$')
    $stages = @{}
    for ($index = 0; $index -lt $declarations.Count; $index++) {
        $declaration = $declarations[$index]
        $end = if ($index + 1 -lt $declarations.Count) { $declarations[$index + 1].Index } else { $dockerfile.Length }
        $body = $dockerfile.Substring($declaration.Index, $end - $declaration.Index)
        $name = $declaration.Groups['name'].Value
        Assert-True (-not $stages.ContainsKey($name)) "Duplicate stage '$name' in '$($Image.id)'."
        $stages[$name] = $body
        if ($body -match '(?m)^RUN ') {
            Assert-True ($declaration.Groups['platform'].Value -ceq '$BUILDPLATFORM') `
                "Image '$($Image.id)' executes '$name' on the target platform."
        }
    }
    Assert-True ($dockerfile.IndexOf('ARG PUBLISH_SOURCE=build') -lt $declarations[0].Index -and
        $dockerfile.Contains('ARG PUBLISH_SOURCE=build')) "Image '$($Image.id)' lost its local source-build default."
    $expectedStages = if ($Image.id -ceq 'pumpserver') { 'build,prebuilt,publish,layout,final' } else {
        'build,prebuilt,publish,final'
    }
    Assert-True (($declarations | ForEach-Object { $_.Groups['name'].Value }) -join ',' -ceq $expectedStages) `
        "Image '$($Image.id)' has an unexpected stage graph."
    Assert-True ($stages.prebuilt -match '(?m)^FROM scratch AS prebuilt\r?$' -and
        $stages.prebuilt.Contains("ARG PUBLISH_DIR=artifacts/docker/publish/$($Image.id)") -and
        $stages.prebuilt.Contains('COPY ${PUBLISH_DIR}/ /app/publish/')) `
        "Image '$($Image.id)' does not stage only its own native output."
    $publishInstructions = @($stages.publish -split '\r?\n' | Where-Object { $_ -notmatch '^\s*(#|$)' })
    Assert-True (($publishInstructions -join "`n") -ceq 'FROM ${PUBLISH_SOURCE} AS publish') `
        "Image '$($Image.id)' does not select the requested publish source."
    Assert-True ($stages.build.Contains('dotnet publish "' + $Image.project + '"')) `
        "Image '$($Image.id)' builds a project different from its catalog entry."
    foreach ($option in @('-p:PublishAot=false', '-p:UseAppHost=false', '-p:SelfContained=false',
        '-p:CustomTestTarget=net10.0', '-p:Dockerbuild=true', '-p:Version="$Version"',
        '-p:AssemblyVersion="$SimpleVersion"', '-p:FileVersion="$FileVersion"',
        '-p:InformationalVersion="$InformationalVersion"', '-p:SourceRevisionId="$SourceRevisionId"',
        '-p:RepositoryUrl="$SourceRepositoryUrl"')) {
        Assert-True ($stages.build.Contains($option)) "Image '$($Image.id)' lost source publish option '$option'."
    }
    Assert-True ($stages.build -notmatch '(?i)(?:\s-r\s|--runtime\b|RuntimeIdentifier=)') `
        "Image '$($Image.id)' must publish portable, not RID-specific output."
    Assert-True ($stages.final -notmatch '(?m)^RUN ') "Image '$($Image.id)' runs target-architecture commands."
    foreach ($label in @('org.opencontainers.image.version=$Version',
        'org.opencontainers.image.revision=$SourceRevisionId',
        'org.opencontainers.image.source=$SourceRepositoryUrl')) {
        Assert-True ($stages.final.Contains($label)) "Image '$($Image.id)' lost final-stage source/version metadata."
    }
    [xml]$project = Get-Content -LiteralPath (Join-Path $root $Image.project) -Raw
    $assemblyNames = @($project.SelectNodes('/Project/PropertyGroup/AssemblyName') |
        ForEach-Object InnerText)
    Assert-True ($assemblyNames.Count -eq 1 -and
        $stages.final.Contains('ENTRYPOINT ["dotnet", "' + $assemblyNames[0] + '.dll"]')) `
        "Image '$($Image.id)' does not launch the selected project's assembly."
    if ($Image.id -ceq 'pumpserver') {
        foreach ($instruction in @('COPY --from=publish /app/publish /layout/app/',
            'mkdir -p /openssl /layout/app/pki /layout/diag',
            'cp -p /etc/pki/tls/openssl.cnf /openssl/openssl.cnf',
            'sed -i ''s/^default_properties = .*/default_properties = ""/'' /openssl/openssl.cnf',
            'chown -R $APP_UID:0 /layout/app /layout/diag',
            'chmod -R g=u /layout/app /layout/diag')) {
            Assert-True ($stages.layout.Contains($instruction)) "Pump layout lost '$instruction'."
        }
        foreach ($instruction in @('COPY --from=layout /openssl/openssl.cnf /etc/pki/tls/openssl.cnf',
            'COPY --from=layout /layout/ /', 'USER $APP_UID', 'HOME=/app',
            'DOTNET_DiagnosticPorts=/diag/dotnet-diagnostic.sock,suspend=n,listen')) {
            Assert-True ($stages.final.Contains($instruction)) "Pump final stage lost '$instruction'."
        }
        Assert-True ($declarations[3].Groups['base'].Value -ceq $declarations[4].Groups['base'].Value) `
            'Pump layout and final stages must use the same pinned runtime for the OpenSSL configuration.'
    }
    else {
        Assert-True ($stages.final.Contains('COPY --from=publish /app/publish .') -and
            $stages.final -notmatch '(?m)^COPY --from=build ') `
            "Image '$($Image.id)' bypasses the selected prebuilt assembly stage."
    }
}

try {
    $script:workflow = Get-Content -LiteralPath (Join-Path $root '.github/workflows/docker-image.yml') -Raw
    $catalog = Get-Content -LiteralPath (Join-Path $root '.azurepipelines/release/artifacts.json') -Raw |
        ConvertFrom-Json
    $ociGroups = @($catalog.groups | Where-Object kind -CEQ 'oci')
    if ($Scenario -in @('sdk-pins', 'docker-stages', 'pump-layout')) {
        $sdk = (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk.version
        $sdkPin = 'mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:' +
            '35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29'
        $runtimePin = 'mcr.microsoft.com/dotnet/runtime:10.0.12@sha256:' +
            'ff17a18b639a0327e52c7c296fa2e1abe6e03eb61d8121a8ef67cc6aa430a27e'
        $aspnetPin = 'mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:' +
            '2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f'
        $pumpSdkPin = 'mcr.microsoft.com/dotnet/sdk:10.0.401-azurelinux3.0@sha256:' +
            '01bb0a1008dd2b753ff059c9c18ec7e728b3d98d0024fa6e8c84905cdb86ae4e'
        $pumpRuntimePin = 'mcr.microsoft.com/dotnet/runtime:10.0.12-azurelinux3.0@sha256:' +
            '78e83767f7bbb9ba0a334d482b196331d4dd5bfd879f4ec445b5226e289ca710'
        Assert-True (@($ociGroups.images).Count -eq 10) 'Expected all ten catalog Dockerfiles.'
        foreach ($group in $ociGroups) {
            foreach ($image in $group.images) {
                if ($Scenario -eq 'pump-layout' -and $image.id -cne 'pumpserver') { continue }
                if ($Scenario -ne 'sdk-pins') {
                    Assert-DockerStages $image
                    continue
                }
                $dockerfile = Get-Content -LiteralPath (Join-Path $root $image.dockerfile) -Raw
                $match = [regex]::Match($dockerfile,
                    '(?m)^FROM --platform=\$BUILDPLATFORM mcr\.microsoft\.com/dotnet/sdk:' +
                    '(?<version>\d+\.\d+\.\d+)(?:-[\w.]+)?@sha256:[0-9a-f]{64} AS build\r?$')
                Assert-True ($match.Success -and $match.Groups['version'].Value -ceq $sdk) `
                    "Image '$($image.id)' does not pin an immutable SDK matching global.json ($sdk)."
                $references = @([regex]::Matches($dockerfile, '(?m)^FROM (?:--platform=\S+ )?(?<image>mcr\.\S+) AS ') |
                    ForEach-Object { $_.Groups['image'].Value })
                $expectedPins = if ($image.id -ceq 'pumpserver') {
                    @($pumpSdkPin, $pumpRuntimePin, $pumpRuntimePin)
                } elseif ($image.id -in @('refserver', 'mcpserver')) {
                    @($sdkPin, $aspnetPin)
                } else {
                    @($sdkPin, $runtimePin)
                }
                Assert-True (($references -join ',') -ceq ($expectedPins -join ',')) `
                    "Image '$($image.id)' changed an approved immutable base pin."
            }
        }
        exit 0
    }
    foreach ($group in $ociGroups) {
        foreach ($image in $group.images) {
            $dockerfile = Join-Path $fixture $image.dockerfile
            $null = New-Item -ItemType Directory -Path (Split-Path $dockerfile) -Force
            Copy-Item -LiteralPath (Join-Path $root $image.dockerfile) -Destination $dockerfile
            Set-Content -LiteralPath (Join-Path $fixture $image.project) '<Project />'
        }
    }
    $eventName = 'push'
    $ref = 'refs/heads/master'
    $repository = 'OPCFoundation/UA-.NETStandard'
    $expectedGroups = @('containers', 'pump')
    $invalid = $false
    switch ($Scenario) {
        'master' { }
        'release' { $ref = 'refs/heads/release/2.0' }
        'docker-branch' { $ref = 'refs/heads/docker-fix' }
        'pull-request' { $eventName = 'pull_request'; $ref = 'refs/pull/1/merge' }
        'manual-pump' { $eventName = 'workflow_dispatch' }
        'fork' { $repository = 'Example-Org/UA-.NETStandard' }
        'invalid-push-ref' { $ref = 'refs/heads/unrelated'; $invalid = $true }
        'invalid-event' { $eventName = 'schedule'; $invalid = $true }
        'missing-group' {
            $catalog.groups = @($catalog.groups | Where-Object id -CNE 'pump')
            $invalid = $true
        }
        'wrong-producer' {
            ($catalog.groups | Where-Object id -CEQ 'pump').producer = '.github/workflows/other.yml'
            $invalid = $true
        }
        'missing-dockerfile' {
            Remove-Item -LiteralPath (Join-Path $fixture 'samples/DI/PumpDeviceIntegrationServer/Dockerfile')
            $invalid = $true
        }
        'duplicate-image' {
            $group = $catalog.groups | Where-Object id -CEQ 'pump'
            $group.images += $group.images[0]
            $invalid = $true
        }
        'duplicate-cross-group-image' {
            ($catalog.groups | Where-Object id -CEQ 'pump').images[0].id = 'refserver'
            $invalid = $true
        }
        'empty-platforms' {
            ($catalog.groups | Where-Object id -CEQ 'pump').platforms = @()
            $invalid = $true
        }
        'unsafe-dockerfile' {
            ($catalog.groups | Where-Object id -CEQ 'pump').images[0].dockerfile =
                '../samples/DI/PumpDeviceIntegrationServer/Dockerfile'
            $invalid = $true
        }
        'missing-project' {
            $image = ($catalog.groups | Where-Object id -CEQ 'pump').images[0]
            Remove-Item -LiteralPath (Join-Path $fixture $image.project)
            $invalid = $true
        }
        'missing-project-field' {
            ($catalog.groups | Where-Object id -CEQ 'pump').images[0].PSObject.Properties.Remove('project')
            $invalid = $true
        }
        'unsafe-project' {
            ($catalog.groups | Where-Object id -CEQ 'pump').images[0].project =
                '../samples/DI/PumpDeviceIntegrationServer/PumpDeviceIntegrationServer.csproj'
            $invalid = $true
        }
        'foreign-project' {
            ($catalog.groups | Where-Object id -CEQ 'pump').images[0].project =
                'samples/Lds/ConsoleLdsServer/ConsoleLdsServer.csproj'
            $invalid = $true
        }
        'workflow-wiring' { }
        'project-paths' { }
        { $_ -like 'native-*' -or $_ -like 'summary-*' -or $_ -like 'tag-*' } { }
        default { throw 'Unknown container workflow fixture scenario.' }
    }
    $catalog | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath (Join-Path $fixture '.azurepipelines/release/artifacts.json')
    $output = & pwsh -NoLogo -NoProfile -NonInteractive -File `
        (Join-Path $root '.azurepipelines/containers/matrix.ps1') `
        -EventName $eventName -Ref $ref -Repository $repository -RepositoryRoot $fixture 2>&1
    $code = $LASTEXITCODE
    if ($invalid) {
        Assert-True ($code -ne 0) 'Invalid selection input was accepted.'
        exit 0
    }
    Assert-True ($code -eq 0) ("Matrix selection failed: " + ($output -join "`n"))
    $selection = ($output -join "`n") | ConvertFrom-Json
    $images = @($selection.images.include)
    $groups = @($selection.groups.include)
    Assert-True (($groups.id -join ',') -ceq ($expectedGroups -join ',')) 'Wrong independent groups selected.'
    Assert-True (@($images.image | Sort-Object -Unique).Count -eq $images.Count) 'Image selection contains duplicates.'
    Assert-True ($images.Count -eq 10) 'All ten images must share the native publication matrix.'
    foreach ($image in $images) {
        $definition = @($ociGroups.images | Where-Object id -CEQ $image.image)
        Assert-True ($definition.Count -eq 1 -and $image.project -ceq $definition[0].project -and
            $image.dockerfile -ceq ('./' + $definition[0].dockerfile)) `
            "Image '$($image.image)' lost its catalog project/Dockerfile pairing."
    }
    foreach ($group in $groups) {
        $members = @($images | Where-Object group -CEQ $group.id)
        if ($group.id -ceq 'pump') {
            Assert-True ($members.Count -eq 1 -and $members[0].image -ceq 'pumpserver') `
                'Pump selection must contain exactly its one image.'
            Assert-True ($members[0].platforms -ceq 'linux/amd64,linux/arm64/v8') 'Pump platforms are incomplete.'
            $owner = if ($Scenario -eq 'fork') { 'exampleorg' } else { 'opcfoundation' }
            Assert-True ($members[0].repository -ceq "ghcr.io/$owner/uanetstandard/pumpserver") `
                'Pump does not use the shared registry layout.'
            Assert-True ($group.manifestArtifact -ceq 'pump-artifact-group-manifest') 'Pump evidence is not independent.'
        }
        else {
            $expectedImages = @('boilerserver', 'calcserver', 'ldsserver', 'mcpserver', 'pubsubclient',
                'redundantclient', 'redundantpubsub', 'redundantserver', 'refserver')
            Assert-True ((($members.image | Sort-Object) -join ',') -ceq ($expectedImages -join ',')) `
                'Main container membership changed.'
            $owner = if ($Scenario -eq 'fork') { 'exampleorg' } else { 'opcfoundation' }
            foreach ($member in $members) {
                Assert-True ($member.platforms -ceq 'linux/amd64,linux/arm64/v8') 'Main platform scope changed.'
                Assert-True ($member.repository -ceq "ghcr.io/$owner/uanetstandard/$($member.image)") `
                    'Main repository normalization changed.'
            }
            Assert-True ($group.manifestArtifact -ceq 'container-artifact-group-manifest') `
                'Main container evidence is not independent.'
        }
    }
    if ($Scenario -eq 'workflow-wiring') {
        Assert-True (-not (Test-Path -LiteralPath `
            (Join-Path $root '.github/workflows/pump-device-integration-server-docker.yml'))) `
            'The redundant standalone Pump workflow still exists.'
        foreach ($required in @(
            'matrix: ${{ fromJSON(needs.select-images.outputs.images) }}',
            'matrix: ${{ fromJSON(needs.select-images.outputs.groups) }}',
            'platforms: ${{ matrix.platforms }}',
            'IMAGE_REPOSITORY: ${{ matrix.repository }}',
            'IMAGE_GROUP: ${{ matrix.group }}',
            'push: ${{ github.event_name != ''pull_request'' }}',
            'pattern: container-status-${{ matrix.id }}-*',
            'name: ${{ matrix.manifestArtifact }}',
            'name: images summary',
            'tags: ${{ env.TAG_BRANCH }},${{ env.TAG_LATEST }}${{ env.TAG_RELEASE }}',
            'IS_NEWEST_RELEASE_LINE',
            '-Operation Preflight -Group $env:IMAGE_GROUP',
            'provenance: mode=min',
            'sbom: generator=${{ env.SBOM_SCANNER }}',
            'DOCKER_BUILD_RECORD_UPLOAD: ''false''',
            'cache-from: type=gha,scope=${{ matrix.image }}',
            'cache-to: type=gha,mode=max,scope=${{ matrix.image }},ignore-error=true',
            'PUBLISH_SOURCE=prebuilt',
            'PUBLISH_DIR=artifacts/docker/publish/${{ matrix.image }}',
            'artifact-ids: ${{ needs.publish-samples.outputs.artifact-id }}',
            'PUBLISH_RUN_ATTEMPT: ${{ needs.publish-samples.outputs.run-attempt }}',
            'SourceRevisionId=${{ github.sha }}',
            'SourceRepositoryUrl=${{ github.server_url }}/${{ github.repository }}',
            'container-image-${{ github.repository }}-${{ matrix.image }}-${{ github.ref }}',
            'cosign-release: ''v3.1.3''',
            '-Operation Sign -Group $env:IMAGE_GROUP',
            'path: ${{ runner.temp }}/container-evidence/public/status.json',
            'workflow_dispatch:'
        )) {
            Assert-True ($workflow.Contains($required)) "Workflow lost required wiring: $required"
        }
        Assert-True ($workflow -notmatch 'DOCKERSIGN|provenance:\s*false|setup-qemu-action|(?m)^  IMAGES:') `
            'Workflow reintroduced disabled proof, emulation or a second image catalog.'
        $publish = Get-WorkflowJob 'publish-samples'
        $build = Get-WorkflowJob 'build-and-push-image'
        Assert-True ($publish.Contains('needs: select-images') -and
            $publish.Contains('IMAGES: ${{ needs.select-images.outputs.images }}')) `
            'Native publication must consume the same catalog selection as the image matrix.'
        Assert-True ($build.Contains('needs: [select-images, publish-samples]') -and
            $build.Contains('needs.publish-samples.result == ''success''') -and
            $build.Contains('cancel-in-progress: false')) 'Image builds bypass native publication or serialization.'
        Assert-True ($build.IndexOf('Refuse unsupported required stable publication') -lt
            $build.IndexOf('- name: Build and push Docker image')) 'Required-stable preflight moved after publication.'
        Assert-True ($build.IndexOf('Validate published sample identity') -lt
            $build.IndexOf('- name: Build and push Docker image')) 'Image build runs before validating native payloads.'
        foreach ($job in @('discover', 'select-images', 'publish-samples', 'build-and-push-image')) {
            Assert-True ((Get-WorkflowJob 'images-summary').Contains("    - $job")) `
                "The summary does not wait for '$job'."
        }
    }
    if ($Scenario -like 'native-*') {
        $env:IMAGES = $selection.images | ConvertTo-Json -Depth 8 -Compress
        $env:GITHUB_REPOSITORY = 'OPCFoundation/UA-.NETStandard'
        $env:SOURCE_REPOSITORY_URL = 'https://github.com/OPCFoundation/UA-.NETStandard'
        $env:GITHUB_SHA = 'a' * 40
        $env:GITHUB_RUN_ID = '12345'
        $env:GITHUB_RUN_ATTEMPT = '2'
        $env:PUBLISH_RUN_ATTEMPT = '2'
        $env:NBGV_NuGetPackageVersion = '2.0.0-preview.7+build.abc'
        $env:NBGV_Version = '2.0.0.1729'
        $env:NBGV_SimpleVersion = '2.0.0'
        $env:NBGV_AssemblyInformationalVersion = '2.0.0-preview.7+source.aaaaaaaa'
        $env:IMAGE_VERSION = '2.0.0-preview.7'
        $env:GITHUB_OUTPUT = Join-Path $fixture 'publish-output.txt'
        $script:publishCalls = [Collections.Generic.List[object]]::new()
        function dotnet {
            $arguments = @($args)
            $script:publishCalls.Add($arguments)
            Assert-True ($arguments[0] -ceq 'publish') 'Fixture must never invoke a real dotnet command.'
            $entry = @($images | Where-Object project -CEQ $arguments[1])[0]
            if ($Scenario -eq 'native-publish-failure' -and $entry.image -ceq 'calcserver') {
                $global:LASTEXITCODE = 19
                return
            }
            $outputIndex = [Array]::IndexOf($arguments, '-o')
            Assert-True ($outputIndex -gt 1) 'Native publish has no explicit per-image output directory.'
            $directory = $arguments[$outputIndex + 1]
            $null = New-Item -ItemType Directory -Path (Join-Path $directory 'runtimes/linux-arm64/native') -Force
            $assembly = [IO.Path]::GetFileNameWithoutExtension($entry.project) + '.dll'
            if ($Scenario -ne 'native-missing-assembly' -or $entry.image -cne 'calcserver') {
                Set-Content -LiteralPath (Join-Path $directory $assembly) "assembly:$($entry.image)" -NoNewline
            }
            Set-Content -LiteralPath (Join-Path $directory 'Opc.Ua.Core.dll') "dependency:$($entry.image)" -NoNewline
            Set-Content -LiteralPath (Join-Path $directory 'runtimes/linux-arm64/native/probe.txt') `
                "native-resource:$($entry.image)" -NoNewline
            $global:LASTEXITCODE = 0
        }
        $publishError = $null
        if ($Scenario -eq 'native-invalid-version') { $env:NBGV_NuGetPackageVersion = 'invalid:tag' }
        Push-Location $fixture
        try {
            try {
                & ([scriptblock]::Create((Get-WorkflowScript 'publish-samples' 'Publish samples')))
            }
            catch { $publishError = $_.Exception.Message }
            $manifestPath = Join-Path $fixture 'artifacts/docker/publish/publish-manifest.json'
            $expectedPublishFailure = switch ($Scenario) {
                'native-publish-failure' {
                    'Publishing samples/MinimalApi/MinimalCalcServer/MinimalCalcServer.csproj failed (exit code 19).'
                }
                'native-missing-assembly' {
                    'Publishing samples/MinimalApi/MinimalCalcServer/MinimalCalcServer.csproj ' +
                        'did not produce its Docker entry assembly.'
                }
                'native-invalid-version' { 'Version does not yield a usable Docker tag.' }
                default { $null }
            }
            if ($null -ne $expectedPublishFailure) {
                Assert-True ($publishError -ceq $expectedPublishFailure) "Wrong publish failure: '$publishError'."
                $expectedCalls = if ($Scenario -eq 'native-invalid-version') { 0 } else { 4 }
                Assert-True ($publishCalls.Count -eq $expectedCalls) 'Native publication did not stop at the failed input.'
                Assert-True (-not (Test-Path -LiteralPath $manifestPath)) 'Failed publication produced a success manifest.'
                exit 0
            }
            Assert-True ($null -eq $publishError) "Native publication failed: '$publishError'."
            Assert-True ($publishCalls.Count -eq 10) 'Native publisher did not publish each selected project exactly once.'
            for ($index = 0; $index -lt $images.Count; $index++) {
                $entry = $images[$index]
                $expectedArguments = @('publish', $entry.project, '-c', 'Release', '-f', 'net10.0',
                    '-p:PublishAot=false', '-p:UseAppHost=false', '-p:SelfContained=false',
                    '-p:CustomTestTarget=net10.0', '-p:Dockerbuild=true',
                    '-p:Version=2.0.0-preview.7', '-p:AssemblyVersion=2.0.0', '-p:FileVersion=2.0.0',
                    '-p:InformationalVersion=2.0.0-preview.7+source.aaaaaaaa',
                    ('-p:SourceRevisionId=' + ('a' * 40)),
                    '-p:RepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard',
                    '-o', "artifacts/docker/publish/$($entry.image)")
                Assert-True (($publishCalls[$index] -join "`n") -ceq ($expectedArguments -join "`n")) `
                    "Native publish options or output ownership differ for '$($entry.image)'."
            }
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            Assert-True ($manifest.images.Count -eq 10 -and
                (($manifest.images.image | Sort-Object) -join ',') -ceq (($images.image | Sort-Object) -join ',')) `
                'Native manifest does not describe all ten published applications.'
            Assert-True ($manifest.sourceRevision -ceq ('a' * 40) -and $manifest.runId -ceq '12345' -and
                $manifest.runAttempt -ceq '2' -and $manifest.version -ceq '2.0.0-preview.7' -and
                $manifest.assemblyVersion -ceq '2.0.0' -and $manifest.fileVersion -ceq '2.0.0') `
                'Native manifest is not bound to the assembly metadata and producing run.'
            Assert-True ((Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw).Trim() -ceq 'run-attempt=2') `
                'Native publication did not expose its producing attempt for same-run job retries.'
            foreach ($entry in $images) {
                $record = @($manifest.images | Where-Object image -CEQ $entry.image)
                $assembly = [IO.Path]::GetFileNameWithoutExtension($entry.project) + '.dll'
                $expectedFiles = @($assembly, 'Opc.Ua.Core.dll', 'runtimes/linux-arm64/native/probe.txt') | Sort-Object
                Assert-True ($record.Count -eq 1 -and $record[0].project -ceq $entry.project -and
                    $record[0].dockerfile -ceq $entry.dockerfile -and
                    (($record[0].files.path | Sort-Object) -join ',') -ceq ($expectedFiles -join ',')) `
                    "Native manifest mixes application files for '$($entry.image)'."
                $assemblyRecord = @($record[0].files | Where-Object path -CEQ $assembly)[0]
                $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
                    [Text.Encoding]::UTF8.GetBytes("assembly:$($entry.image)"))).ToLowerInvariant()
                Assert-True ($assemblyRecord.sha256 -ceq $hash) 'Manifest did not hash the actual published assembly bytes.'
            }
            $target = $images[0]
            $payload = Join-Path $fixture "artifacts/docker/publish/$($target.image)"
            $expectedVerificationFailure = switch ($Scenario) {
                'native-publish' { $null }
                'native-consumer-rerun' { $env:GITHUB_RUN_ATTEMPT = '3'; $null }
                'native-source-mismatch' {
                    $env:GITHUB_SHA = 'b' * 40
                    'Native publish identity mismatch: sourceRevision.'
                }
                'native-repository-mismatch' {
                    $env:SOURCE_REPOSITORY_URL = 'https://github.com/other/repository'
                    'Native publish identity mismatch: sourceRepository.'
                }
                'native-run-mismatch' {
                    $env:GITHUB_RUN_ID = '67890'
                    'Native publish identity mismatch: runId.'
                }
                'native-attempt-mismatch' {
                    $env:PUBLISH_RUN_ATTEMPT = '1'
                    'Native publish identity mismatch: runAttempt.'
                }
                'native-version-mismatch' {
                    $env:IMAGE_VERSION = '2.0.0-preview.8'
                    'Native publish identity mismatch: version.'
                }
                'native-file-version-mismatch' {
                    $manifest.fileVersion = '2.0.0.1729'
                    'Native publish identity mismatch: fileVersion.'
                }
                'native-project-mismatch' {
                    $manifest.images[0].project = $images[1].project
                    'Native publish image does not match the selected catalog entry.'
                }
                'native-dockerfile-mismatch' {
                    $manifest.images[0].dockerfile = $images[1].dockerfile
                    'Native publish image does not match the selected catalog entry.'
                }
                'native-duplicate-entry' {
                    $manifest.images += $manifest.images[0]
                    'Native publish image does not match the selected catalog entry.'
                }
                'native-file-mismatch' {
                    Set-Content -LiteralPath (Join-Path $payload 'ConsoleReferenceServer.dll') 'foreign-assembly'
                    'Native publish payload mismatch: ConsoleReferenceServer.dll.'
                }
                'native-file-missing' {
                    Remove-Item -LiteralPath (Join-Path $payload 'runtimes/linux-arm64/native/probe.txt')
                    'Native publish payload membership changed.'
                }
                'native-file-extra' {
                    Set-Content -LiteralPath (Join-Path $payload 'apphost') 'nonportable-host'
                    'Native publish payload membership changed.'
                }
                default { throw 'Unknown native-publication scenario.' }
            }
            $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath
            $verifyScript = Get-WorkflowScript 'build-and-push-image' 'Validate published sample identity'
            $verified = 0
            foreach ($entry in $images) {
                $env:IMAGE_NAME = $entry.image
                $env:IMAGE_PROJECT = $entry.project
                $env:IMAGE_DOCKERFILE = $entry.dockerfile
                $verifyError = $null
                try { & ([scriptblock]::Create($verifyScript)) }
                catch { $verifyError = $_.Exception.Message }
                Assert-True ($verifyError -ceq $expectedVerificationFailure) `
                    "Wrong verification result for '$($entry.image)': '$verifyError'."
                $verified++
                if ($null -ne $expectedVerificationFailure) { break }
            }
            Assert-True ($verified -eq $(if ($null -eq $expectedVerificationFailure) { 10 } else { 1 })) `
                'Native payload verification did not cover the selected applications.'
        }
        finally { Pop-Location }
    }
    if ($Scenario -like 'summary-*') {
        $env:RELEVANT_CHANGES = 'true'
        $env:DISCOVER_RESULT = 'success'
        $env:SELECTION_RESULT = 'success'
        $env:PUBLISH_RESULT = 'success'
        $env:IMAGES_RESULT = 'success'
        $expectedExit = 1
        $expectedMessage = switch ($Scenario) {
            'summary-success' { $expectedExit = 0; 'All image builds succeeded' }
            'summary-docs-skipped' {
                $env:RELEVANT_CHANGES = 'false'
                $env:SELECTION_RESULT = 'skipped'
                $env:PUBLISH_RESULT = 'skipped'
                $env:IMAGES_RESULT = 'skipped'
                $expectedExit = 0
                'All image builds succeeded or were intentionally skipped.'
            }
            'summary-selected-publish-skipped' { $env:PUBLISH_RESULT = 'skipped'; 'publish-samples=skipped' }
            'summary-selected-images-skipped' { $env:IMAGES_RESULT = 'skipped'; 'build-and-push-image=skipped' }
            'summary-publish-failed' { $env:PUBLISH_RESULT = 'failure'; 'publish-samples=failure' }
            'summary-selection-failed' { $env:SELECTION_RESULT = 'failure'; 'select-images=failure' }
            'summary-discover-cancelled' { $env:DISCOVER_RESULT = 'cancelled'; 'discover=cancelled' }
            'summary-invalid-relevance' {
                $env:RELEVANT_CHANGES = ''
                'Image path relevance did not report a valid result.'
            }
            default { throw 'Unknown summary scenario.' }
        }
        $script = Join-Path $fixture 'summary.ps1'
        Get-WorkflowScript 'images-summary' 'Evaluate job results' | Set-Content -LiteralPath $script
        $output = & pwsh -NoLogo -NoProfile -NonInteractive -File $script 2>&1
        Assert-True ($LASTEXITCODE -eq $expectedExit -and ($output -join "`n").Contains($expectedMessage)) `
            "Unexpected summary decision: $($output -join "`n")"
    }
    if ($Scenario -like 'tag-*') {
        $bash = if ($IsWindows) {
            Join-Path (Split-Path (Split-Path (Get-Command git).Source)) 'bin/bash.exe'
        } else {
            (Get-Command bash).Source
        }
        Assert-True (Test-Path -LiteralPath $bash) 'Git Bash or bash is required to execute workflow tag guards.'
        $env:GITHUB_ENV = (Join-Path $fixture 'github-env.txt').Replace('\', '/')
        $env:IMAGE_REPOSITORY = 'ghcr.io/opcfoundation/uanetstandard/refserver'
        $env:NBGV_NuGetPackageVersion = '2.0.0-preview.7+build.abc'
        $env:NBGV_SimpleVersion = '2.0.0'
        $env:NBGV_MajorMinorVersion = '2.0'
        $env:NBGV_PublicRelease = 'False'
        $env:NBGV_PrereleaseVersion = '-preview.7'
        $env:IS_NOT_PR = 'true'
        $env:SOURCE_REF = 'refs/heads/release/2.0'
        $env:IMAGE_TAG = ''
        $env:TAG_RELEASE = ''
        $env:IS_NEWEST_RELEASE_LINE = ''
        $expectedSuffix = '-release-2.0'
        $stable = $Scenario -in @('tag-newest-release', 'tag-maintenance-release', 'tag-unknown-release-line')
        if ($stable) {
            $env:NBGV_NuGetPackageVersion = '2.0.1'
            $env:NBGV_SimpleVersion = '2.0.1'
            $env:NBGV_PublicRelease = 'True'
            $env:NBGV_PrereleaseVersion = ''
            $expectedSuffix = ''
        }
        switch ($Scenario) {
            'tag-preview' { $env:NBGV_PublicRelease = 'True' }
            'tag-injection' {
                $env:SOURCE_REF = 'refs/heads/Feat/Upper_Name;$(touch injected)'
                $expectedSuffix = '-feat-upper-name---touch-injected-'
            }
            'tag-invalid-version' { $env:NBGV_NuGetPackageVersion = 'invalid:tag' }
            'tag-newest-release' {
                $env:NBGV_MajorMinorVersion = '2.1'
                $env:NBGV_SimpleVersion = '2.1.0'
                $env:NBGV_NuGetPackageVersion = '2.1.0'
                $env:SOURCE_REF = 'refs/heads/release/2.1'
            }
            'tag-maintenance-release' { }
            'tag-unknown-release-line' { }
            default { throw 'Unknown tag scenario.' }
        }
        Assert-True ((Get-WorkflowStep 'build-and-push-image' 'Prepare Tag Branch info').Contains(
            'if: env.NBGV_PublicRelease == ''False'' || env.NBGV_PrereleaseVersion != ''''')) `
            'Prerelease commits on a public release line can overwrite unqualified aliases.'
        foreach ($step in @('Determine release line precedence', 'Prepare Release tags')) {
            Assert-True ((Get-WorkflowStep 'build-and-push-image' $step).Contains(
                'if: ${{ env.NBGV_PublicRelease == ''True'' && env.NBGV_PrereleaseVersion == '''' ' +
                '&& env.IS_NOT_PR == ''true'' }}')) "Stable alias step '$step' lost its exact stable/non-PR guard."
        }
        function Invoke-TagStep([string]$Name, [string]$Prefix = '') {
            $script = Join-Path $fixture 'tag.sh'
            $body = $Prefix + (Get-WorkflowScript 'build-and-push-image' $Name)
            [IO.File]::WriteAllText($script, $body.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
            $output = & $bash --noprofile --norc $script.Replace('\', '/') 2>&1
            $code = $LASTEXITCODE
            if (Test-Path -LiteralPath $env:GITHUB_ENV) {
                foreach ($line in Get-Content -LiteralPath $env:GITHUB_ENV) {
                    $pair = $line.Split('=', 2)
                    Assert-True ($pair.Count -eq 2) 'Tag step wrote an invalid environment-file record.'
                    [Environment]::SetEnvironmentVariable($pair[0], $pair[1])
                }
            }
            return @{ code = $code; output = $output -join "`n" }
        }
        Push-Location $fixture
        try {
            $result = Invoke-TagStep 'Prepare image version'
            if ($Scenario -eq 'tag-invalid-version') {
                Assert-True ($result.code -eq 1 -and $result.output.Contains('usable Docker tag') -and
                    -not (Test-Path -LiteralPath $env:GITHUB_ENV)) 'An invalid Docker version was not rejected before tagging.'
                exit 0
            }
            Assert-True ($result.code -eq 0) "Version tag preparation failed: $($result.output)"
            Assert-True ($env:IMAGE_VERSION -ceq $env:NBGV_NuGetPackageVersion.Split('+')[0]) `
                'SemVer build metadata reached the Docker version tag.'
            if (-not $stable) {
                $result = Invoke-TagStep 'Prepare Tag Branch info'
                Assert-True ($result.code -eq 0 -and $env:IMAGE_TAG -ceq $expectedSuffix) `
                    "Branch suffix is not sanitized correctly: '$env:IMAGE_TAG'."
            }
            else {
                $gitMock = if ($Scenario -eq 'tag-unknown-release-line') {
                    "git() { return 1; }`n"
                } else {
                    "git() { printf '%s\n' 'aaaaaaaa refs/heads/release/1.5' " +
                        "'bbbbbbbb refs/heads/release/2.0' 'cccccccc refs/heads/release/2.1'; }`n"
                }
                $result = Invoke-TagStep 'Determine release line precedence' $gitMock
                Assert-True ($result.code -eq 0) 'Release precedence step failed.'
                $newest = if ($Scenario -eq 'tag-newest-release') { 'true' } else { 'false' }
                Assert-True ($env:IS_NEWEST_RELEASE_LINE -ceq $newest) 'Release precedence did not fail closed.'
            }
            $result = Invoke-TagStep 'Prepare version and latest tag'
            Assert-True ($result.code -eq 0) 'Version/latest tag preparation failed.'
            $expectedLatest = if ($stable -and $Scenario -ne 'tag-newest-release') {
                '-2.0'
            } else {
                $expectedSuffix
            }
            Assert-True ($env:TAG_BRANCH -ceq "$($env:IMAGE_REPOSITORY):$($env:IMAGE_VERSION)$expectedSuffix" -and
                $env:TAG_LATEST -ceq "$($env:IMAGE_REPOSITORY):latest$expectedLatest") `
                'Version or latest aliases do not respect branch/release-line ownership.'
            if ($stable) {
                $result = Invoke-TagStep 'Prepare Release tags'
                Assert-True ($result.code -eq 0) 'Stable release tag preparation failed.'
                $expectedRelease = ",$($env:IMAGE_REPOSITORY):$env:NBGV_MajorMinorVersion," +
                    "$($env:IMAGE_REPOSITORY):$env:NBGV_SimpleVersion"
                if ($Scenario -eq 'tag-newest-release') { $expectedRelease += ",$($env:IMAGE_REPOSITORY):release" }
                Assert-True ($env:TAG_RELEASE -ceq $expectedRelease) 'A maintenance line moved the global release alias.'
            }
            else {
                Assert-True ([string]::IsNullOrEmpty($env:TAG_RELEASE)) 'A preview build acquired stable release aliases.'
            }
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture 'injected'))) `
                'The branch name executed as shell code instead of remaining data.'
        }
        finally { Pop-Location }
    }
    exit 0
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
