# Copyright (c) OPC Foundation. Licensed under the MIT License.
param([Parameter(Mandatory)][string] $Scenario, [string] $OutputImage)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
. (Join-Path $root '.azurepipelines\assurance-native-image.ps1')
. (Join-Path $root '.azurepipelines\assurance-proofs.ps1')
$directory = Join-Path $PSScriptRoot "obj\assurance-native-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $directory
try {
    $path = Join-Path $directory 'synthetic.exe'
    $stream = [IO.MemoryStream]::new([byte[]]::new(2560), $true)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $stream.Position = 0; $writer.Write([uint16] 0x5A4D)
        $stream.Position = 60; $writer.Write([int] 128)
        $stream.Position = 128; $writer.Write([uint32] 0x4550)
        $writer.Write([uint16] 0x8664); $writer.Write([uint16] 2)
        $stream.Position = 148; $writer.Write([uint16] 240); $writer.Write([uint16] 0x22)
        $stream.Position = 152; $writer.Write([uint16] 0x20B)
        $stream.Position = 168; $writer.Write([uint32] 0x1000); $writer.Write([uint32] 0x1000)
        $writer.Write([uint64] 0x140000000)
        $writer.Write([uint32] 0x1000); $writer.Write([uint32] 0x200)
        $stream.Position = 208; $writer.Write([uint32] 0x3000); $writer.Write([uint32] 0x400)
        $stream.Position = 220; $writer.Write([uint16] 3)
        $stream.Position = 260; $writer.Write([uint32] 16)
        $writer.Write([uint32] 0x2000); $writer.Write([uint32] 0x100)
        $stream.Position = 392; $writer.Write([Text.Encoding]::ASCII.GetBytes(".text`0`0`0"))
        $writer.Write([uint32] 0x200); $writer.Write([uint32] 0x1000)
        $writer.Write([uint32] 0x200); $writer.Write([uint32] 0x400)
        $stream.Position = 428; $writer.Write([uint32] 0x60000020)
        $writer.Write([Text.Encoding]::ASCII.GetBytes(".rdata`0`0"))
        $writer.Write([uint32] 0x400); $writer.Write([uint32] 0x2000)
        $writer.Write([uint32] 0x400); $writer.Write([uint32] 0x600)
        $stream.Position = 468; $writer.Write([uint32] 0x40000040)
        $stream.Position = 1556; $writer.Write([uint32] 1); $writer.Write([uint32] 1)
        $writer.Write([uint32] 0x2040); $writer.Write([uint32] 0x2044); $writer.Write([uint32] 0x2048)
        $stream.Position = 1600; $writer.Write([uint32] 0x2200); $writer.Write([uint32] 0x2080)
        $writer.Write([uint16] 0)
        $stream.Position = 1664; $writer.Write([Text.Encoding]::ASCII.GetBytes("DotNetRuntimeDebugHeader`0"))
        $stream.Position = 2048; $writer.Write([uint32] 0x48444E44)
        $writer.Write([uint16] 5); $writer.Write([uint16] 0); $writer.Write([uint32] 1)
        switch ($Scenario) {
            'wrong-architecture' { $stream.Position = 132; $writer.Write([uint16] 0xAA64) }
            'missing-export' { $stream.Position = 1664; $writer.Write([byte] 88) }
            'forged-header' { $stream.Position = 2048; $writer.Write([uint32] 0) }
            'forwarded-export' { $stream.Position = 1600; $writer.Write([uint32] 0x2080) }
            'malformed-image' { $stream.Position = 60; $writer.Write([int] 0x7fffffff) }
        }
        [IO.File]::WriteAllBytes($path, $stream.ToArray())
    }
    finally { $writer.Dispose(); $stream.Dispose() }
    if ($Scenario -eq 'apphost') { $path = (Get-Command dotnet -CommandType Application).Source }
    $accepted = $false
    try {
        $image = Get-AssuranceNativeImage $path
        $accepted = $true
    }
    catch {
        if ($Scenario -eq 'native-image') { throw }
    }
    if ($accepted -ne ($Scenario -eq 'native-image')) { throw "Wrong native image verdict: $Scenario." }
    if ($accepted -and ($image.digest -cne ('sha256:' + (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()) -or
        $image.nativeAotHeaderMajor -ne 5 -or $image.machine -cne 'amd64')) { throw 'Native image facts mismatch.' }
    if ($accepted -and $OutputImage) { Copy-Item -LiteralPath $path -Destination $OutputImage }
}
finally { Remove-Item -LiteralPath $directory -Recurse -Force }
