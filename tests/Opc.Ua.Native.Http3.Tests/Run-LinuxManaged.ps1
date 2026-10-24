# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuilderImage)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $root '..\..')).Path
$output = Join-Path $root 'artifacts'
$context = Join-Path $output 'managed-linux-context'
$snapshot = Join-Path $context 'repository'
$project = Join-Path $snapshot 'tests\Opc.Ua.Native.Http3.Tests'
$feed = Join-Path $context 'feed'
$assetDirectory = 'src\Opc.Ua.Native.Http3\artifacts\runtimes\linux-x64\native'
$builder = & podman image inspect $BuilderImage --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'Supply an existing audited SDK/AOT builder; this script never pulls or installs SDKs.' }
$assetsPath = Join-Path $root 'obj\project.assets.json'
if (!(Test-Path $assetsPath)) { throw 'Run the managed Windows/JIT check first to resolve the exact test dependency graph.' }
foreach ($path in $project,$feed,(Join-Path $snapshot $assetDirectory)) {
    New-Item -ItemType Directory -Force $path | Out-Null
}
foreach ($name in 'Directory.Build.props','Directory.Build.targets','Directory.Packages.props',
    'common.props','targets.props','version.props','version.targets','preview-version.props','version.json','roslyn.props',
    'NuGet.config','LICENSE.txt','.editorconfig') {
    Copy-Item (Join-Path $repo $name) (Join-Path $snapshot $name) -Force
}
foreach ($name in 'Directory.Build.props','Directory.Build.targets') {
    Copy-Item (Join-Path $repo "tests\$name") (Join-Path $snapshot "tests\$name") -Force
}
foreach ($name in 'NativeMethods.cs','NativeAbiTests.cs','Opc.Ua.Native.Http3.Tests.csproj','global.json') {
    Copy-Item (Join-Path $root $name) (Join-Path $project $name) -Force
}
Copy-Item (Join-Path $repo "$assetDirectory\libopcua_http3.so") (Join-Path $snapshot $assetDirectory) -Force
$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$cache = @($assets.packageFolders.Keys)[0]
$packages = [Collections.Generic.List[object]]::new()
foreach ($entry in $assets.libraries.GetEnumerator()) {
    if ($entry.Value.type -ne 'package' -or
        $entry.Key -match '^(runtime\.|Microsoft\.(NETCore\.App|DotNet\.ILCompiler|AspNetCore\.App))') { continue }
    $parts = $entry.Key.Split('/')
    $id = $parts[0].ToLowerInvariant()
    $version = $parts[1]
    $name = "$id.$version.nupkg"
    $source = Join-Path $cache "$id\$version\$name"
    if (!(Test-Path $source)) { throw "Resolved test package is not cached: $name" }
    Copy-Item $source (Join-Path $feed $name) -Force
    $packages.Add([ordered]@{name=$name;sha256=(Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()})
}
$packages | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'managed-linux-dependencies.json')
$imageFile = Join-Path $output 'managed-linux-image.txt'
$arguments = @('build','--pull=never','--network=none','--layers','--jobs=1','--iidfile',$imageFile,
    '--build-arg',"BUILDER_IMAGE=sha256:$builder",'--file',(Join-Path $root 'Containerfile.managed-linux'),$context)
& podman @arguments 2>&1 | Tee-Object (Join-Path $output 'managed-linux-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Linux managed NativeAOT build failed; no dependencies were downloaded.' }
$image = (Get-Content $imageFile -Raw).Trim()
$name = 'opcua-http3-aot-' + [Guid]::NewGuid().ToString('N')
$created = $false
try {
    & podman create --pull=never --network=none --timeout=120 --name $name $image | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the owned AOT validation container.' }
    $created = $true
    & podman start --attach $name 2>&1 | Tee-Object (Join-Path $output 'managed-linux-aot.log')
    $runExit = $LASTEXITCODE
    $destination = Join-Path $output 'managed-linux-aot'
    New-Item -ItemType Directory -Force $destination | Out-Null
    & podman cp "${name}:/work/results/." $destination
    if ($LASTEXITCODE -ne 0) { throw 'Could not retain Linux managed test reports.' }
    if ($runExit -ne 0) { throw "Linux AOT tests failed: $runExit" }
    foreach ($file in 'libopcua_http3.so','Opc.Ua.Native.Http3.Tests') {
        & podman cp "${name}:/app-http3/$file" $destination
        if ($LASTEXITCODE -ne 0) { throw "Could not retain the executed asset: $file" }
    }
    $nativeHash = (Get-FileHash (Join-Path $destination 'libopcua_http3.so') -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = (Get-FileHash (Join-Path $repo "$assetDirectory\libopcua_http3.so") -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($nativeHash -ne $expectedHash) { throw 'The executed native asset differs from the requested production asset.' }
    [ordered]@{builder_image=$builder;result_image=$image;test_exit_code=$runExit;network='none';
        native_sha256=$nativeHash;
        executable_sha256=(Get-FileHash (Join-Path $destination 'Opc.Ua.Native.Http3.Tests') -Algorithm SHA256).Hash.ToLowerInvariant()} |
        ConvertTo-Json | Set-Content (Join-Path $output 'managed-linux-acceptance.json')
}
finally {
    if ($created) { & podman rm -f $name | Out-Null }
}
