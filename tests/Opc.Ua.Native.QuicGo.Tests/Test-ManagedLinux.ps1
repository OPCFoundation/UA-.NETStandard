# Copyright (c) OPC Foundation. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NativeLibrary,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$IncompatibleNativeLibrary = '',
    [string]$SdkImage = 'mcr.microsoft.com/dotnet/sdk@sha256:750c7bdfc4a67ed7d4f0affb908e046557f4f45e88a6a3c83f5d0d8f111957af'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stage = Join-Path $OutputRoot 'managed-linux-input'
$output = Join-Path $OutputRoot 'managed-linux'
New-Item -ItemType Directory -Force $stage,$output | Out-Null
podman image exists $SdkImage
if ($LASTEXITCODE -ne 0) { throw 'The pinned existing public SDK image is required; no image is pulled.' }
$tracked = @(
    'Directory.Build.props','Directory.Build.targets','Directory.Packages.props','common.props',
    'targets.props','version.props','version.targets','preview-version.props','roslyn.props',
    'version.json','global.json','NuGet.config','LICENSE.txt','.editorconfig','src\Opc.Ua.Types'
)
$files = @(git -C $repo ls-files -- @tracked)
if ($LASTEXITCODE -ne 0) { throw 'Read-only managed dependency inventory failed.' }
foreach ($folder in @('src\Opc.Ua.Native.QuicGo','tests\Opc.Ua.Native.QuicGo.Tests')) {
    $files += Get-ChildItem (Join-Path $repo $folder) -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|__pycache__)\\' } |
        ForEach-Object { [IO.Path]::GetRelativePath($repo, $_.FullName) }
}
foreach ($relative in $files) {
    $relative = $relative.Replace('/', '\')
    if ($relative -match '\.(key|pfx|p12|pem|snk|pdb)$') { throw "Forbidden test input: $relative" }
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
    Copy-Item (Join-Path $repo $relative) $destination -Force
}
Copy-Item $NativeLibrary (Join-Path $stage 'libopcua_quic_go.so') -Force
Copy-Item ($NativeLibrary + '.sha256') (Join-Path $stage 'libopcua_quic_go.so.sha256') -Force
if ($IncompatibleNativeLibrary) {
    Copy-Item $IncompatibleNativeLibrary (Join-Path $stage 'libopcua_quic_previous.so') -Force
}
# No SDK installation. Only the new projects' public package closure is restored.
# Both test hosts have an outer finite process timeout; the owned container is removed.
$command = 'cd /validation && mkdir -p results aot work packages dotnet-home cache && dotnet --version && timeout -k 5s 240s dotnet test tests/Opc.Ua.Native.QuicGo.Tests/Opc.Ua.Native.QuicGo.Tests.csproj -c Release -p:CustomTestTarget=net10.0 -p:Dockerbuild=true --logger "trx;LogFileName=managed-linux.trx" --results-directory /validation/results --nologo -v:minimal && timeout -k 5s 240s dotnet publish tests/Opc.Ua.Native.QuicGo.Tests/Aot/Opc.Ua.Native.QuicGo.Aot.csproj -c Release -r linux-x64 -p:CustomTestTarget=net10.0 -p:Dockerbuild=true -o /validation/aot --nologo -v:minimal && timeout -k 5s 90s /validation/aot/Opc.Ua.Native.QuicGo.Aot /validation/libopcua_quic_go.so'
if ($IncompatibleNativeLibrary) {
    $command += ' && timeout -k 5s 30s /validation/aot/Opc.Ua.Native.QuicGo.Aot --expect-incompatible /validation/libopcua_quic_previous.so'
}
$container = podman create --pull=never --cap-drop=all --security-opt=no-new-privileges `
    --ulimit core=0 --memory=2g --cpus=2 --env DOTNET_CLI_TELEMETRY_OPTOUT=1 `
    --env TMPDIR=/validation/work --env NUGET_PACKAGES=/validation/packages `
    --env DOTNET_CLI_HOME=/validation/dotnet-home --env XDG_CACHE_HOME=/validation/cache `
    --env OPCUA_QUIC_NATIVE_PATH=/validation/libopcua_quic_go.so `
    --entrypoint /bin/sh $SdkImage -c $command
if ($LASTEXITCODE -ne 0) { throw 'Could not create the bounded managed Linux container.' }
$container = $container.Trim()
try {
    podman cp $stage "${container}:/validation"
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy owned managed acceptance inputs.' }
    podman start --attach $container | Tee-Object -FilePath (Join-Path $output 'managed-linux.log')
    $code = $LASTEXITCODE
    podman cp "${container}:/validation/results/." $output
    if ($LASTEXITCODE -ne 0 -or $code -ne 0) { throw "Managed Linux acceptance failed: $code" }
    podman cp "${container}:/validation/aot/Opc.Ua.Native.QuicGo.Aot" $output
    if ($LASTEXITCODE -ne 0) { throw 'Could not retain the executed NativeAOT binary.' }
}
finally {
    podman rm --force $container | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Owned managed Linux container $container could not be removed." }
}
