# Copyright (c) OPC Foundation. Licensed under the MIT License.
# The .NET 10 NativeAOT diagnostic export is a format discriminator, not producer authentication.
# Its DNDH v5 layout is defined by dotnet/runtime v10.0.0 Runtime/DebugHeader.cpp.
function Get-AssuranceNativeImage([string] $Path) {
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($file.Length -lt 512 -or $file.Length -gt 1073741824 -or
        ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'NATIVE_IMAGE_REJECTED'
    }
    $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $pe = $null
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        $headers = $pe.PEHeaders
        if ($headers.CoffHeader.Machine -ne [Reflection.PortableExecutable.Machine]::Amd64 -or
            $null -eq $headers.PEHeader -or $null -ne $headers.CorHeader -or $pe.HasMetadata -or
            $headers.PEHeader.Magic -ne [Reflection.PortableExecutable.PEMagic]::PE32Plus -or
            ([int] $headers.CoffHeader.Characteristics -band 0x2002) -ne 2) {
            throw 'NATIVE_IMAGE_FORMAT_REJECTED'
        }
        $entry = $headers.PEHeader.AddressOfEntryPoint
        $code = @($headers.SectionHeaders | Where-Object {
            $entry -ge $_.VirtualAddress -and $entry -lt ([long] $_.VirtualAddress + $_.SizeOfRawData) -and
            ([long] $_.SectionCharacteristics -band 0x20000000) -ne 0
        })
        $directory = $headers.PEHeader.ExportTableDirectory
        if ($code.Count -ne 1 -or $directory.RelativeVirtualAddress -le 0 -or $directory.Size -lt 40) {
            throw 'NATIVE_AOT_HEADER_MISSING'
        }
        $reader = $pe.GetSectionData($directory.RelativeVirtualAddress).GetReader(0, 40)
        $reader.Offset = 20
        $functions = $reader.ReadUInt32()
        $names = $reader.ReadUInt32()
        $functionRva = $reader.ReadInt32()
        $nameRva = $reader.ReadInt32()
        $ordinalRva = $reader.ReadInt32()
        if ($functions -lt 1 -or $functions -gt 65536 -or $names -lt 1 -or $names -gt 65536) {
            throw 'NATIVE_EXPORT_LIMIT'
        }
        $nameReader = $pe.GetSectionData($nameRva).GetReader(0, [int] $names * 4)
        $ordinalReader = $pe.GetSectionData($ordinalRva).GetReader(0, [int] $names * 2)
        $functionReader = $pe.GetSectionData($functionRva).GetReader(0, [int] $functions * 4)
        $found = 0
        $major = 0
        foreach ($i in 0..($names - 1)) {
            $rva = $nameReader.ReadInt32()
            $ordinal = $ordinalReader.ReadUInt16()
            if ($ordinal -ge $functions) { throw 'NATIVE_EXPORT_REJECTED' }
            $block = $pe.GetSectionData($rva)
            $text = $block.GetReader(0, [Math]::Min(256, $block.Length))
            $name = [Text.StringBuilder]::new()
            $terminated = $false
            while ($text.RemainingBytes -gt 0) {
                $value = $text.ReadByte()
                if ($value -eq 0) { $terminated = $true; break }
                $null = $name.Append([char] $value)
            }
            if (-not $terminated) { throw 'NATIVE_EXPORT_NAME_LIMIT' }
            if ($name.ToString() -cne 'DotNetRuntimeDebugHeader') { continue }
            $found++
            $functionReader.Offset = $ordinal * 4
            $headerRva = $functionReader.ReadInt32()
            if ($headerRva -ge $directory.RelativeVirtualAddress -and
                $headerRva -lt ([long] $directory.RelativeVirtualAddress + $directory.Size)) {
                throw 'NATIVE_FORWARDED_HEADER_REJECTED'
            }
            $header = $pe.GetSectionData($headerRva).GetReader(0, 32)
            $cookie = $header.ReadUInt32()
            $major = $header.ReadUInt16()
            $minor = $header.ReadUInt16()
            $flags = $header.ReadUInt32()
            if ($cookie -ne 0x48444E44 -or $major -ne 5 -or $minor -ne 0 -or $flags -ne 1) {
                throw 'NATIVE_AOT_HEADER_UNSUPPORTED'
            }
        }
        if ($found -ne 1) { throw 'NATIVE_AOT_HEADER_MISSING' }
        return @{
            format = 'pe32plus'; machine = 'amd64'; nativeAotHeader = 'DNDH'
            nativeAotHeaderMajor = $major; nativeAotHeaderMinor = 0
            digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            size = $file.Length
        }
    }
    finally {
        if ($null -ne $pe) { $pe.Dispose() }
        $stream.Dispose()
    }
}
