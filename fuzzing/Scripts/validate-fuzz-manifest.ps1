#Requires -Version 7.2
<#
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/

.SYNOPSIS
Validates the fuzz manifest schema, source inventory, input paths and baseline.
.DESCRIPTION
This read-only gate does not build or load assemblies, evaluate conditional
compilation, prove successful seed decoding, or establish coverage parity.
Published callback and corpus replay remain separate execution requirements.
Generated-only corpora require a valid generator project and explicit output
declarations at this source stage. They MUST be generated or explicitly supplied
before publication; the separate drop validator requires actual nonempty inputs.
With no arguments, the repository and manifest are resolved relative to this
script, independently of the caller's current directory. ManifestPath overrides
only the JSON input; its project, corpus and asset paths remain repository-relative.
SelfTest checks negative controls using in-memory manifest copies only.
#>
[CmdletBinding()]
param(
    [string]$ManifestPath,
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$schema = @'
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": [
    "schemaVersion", "baseline", "execution", "oracles", "evidenceGaps",
    "forkRequirements", "excludedTargets", "corpusGenerators", "areas"
  ],
  "additionalProperties": false,
  "properties": {
    "schemaVersion": { "const": 1 },
    "baseline": {
      "type": "object",
      "required": ["stackCommit", "forkCommit", "forkInventory", "stackInventory", "replay", "regressionAssets"],
      "properties": {
        "stackCommit": { "const": "7411654c8d0f2bc58571c33dd0040696afd26595" },
        "forkCommit": { "const": "b5553f36b0f709a39efadaf49e5f2b687e4e35a7" },
        "forkInventory": {
          "type": "object",
          "required": ["localCallbacks", "oneFuzzRegistrations", "trackedSeedFiles"],
          "properties": {
            "localCallbacks": { "const": 28 },
            "oneFuzzRegistrations": { "const": 13 },
            "trackedSeedFiles": { "const": 18 }
          }
        },
        "stackInventory": { "type": "array", "minItems": 4, "maxItems": 4 },
        "replay": {
          "type": "object",
          "required": ["total", "passed", "failed", "skipped", "areas"],
          "properties": {
            "total": { "const": 5359 },
            "passed": { "const": 5359 },
            "failed": { "const": 0 },
            "skipped": { "const": 0 },
            "areas": { "type": "array", "minItems": 3, "maxItems": 3 }
          }
        },
        "regressionAssets": {
          "type": "array",
          "minItems": 3,
          "maxItems": 3,
          "items": {
            "type": "object",
            "required": ["path", "forkPath", "bytes", "sha256", "forkSha256"],
            "properties": {
              "path": { "$ref": "#/definitions/repoPath" },
              "forkPath": { "$ref": "#/definitions/repoPath" },
              "bytes": { "type": "integer", "minimum": 1 },
              "sha256": { "type": "string", "pattern": "^[0-9a-f]{64}$" },
              "forkSha256": { "type": "string", "pattern": "^[0-9a-f]{64}$" }
            }
          }
        }
      }
    },
    "execution": { "type": "object" },
    "corpusGenerators": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "required": ["id", "project", "arguments", "outputs"],
        "additionalProperties": false,
        "properties": {
          "id": { "type": "string", "pattern": "^[A-Za-z][A-Za-z0-9.-]*$" },
          "project": { "$ref": "#/definitions/repoPath" },
          "arguments": { "$ref": "#/definitions/nonEmptyStringList" },
          "outputs": {
            "type": "array",
            "minItems": 1,
            "items": {
              "type": "object",
              "required": ["path", "directory"],
              "additionalProperties": false,
              "properties": {
                "path": { "$ref": "#/definitions/repoPath" },
                "directory": { "$ref": "#/definitions/repoPath" }
              }
            }
          }
        }
      }
    },
    "oracles": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "required": ["id", "description"],
        "additionalProperties": false,
        "properties": {
          "id": { "$ref": "#/definitions/text" },
          "description": { "$ref": "#/definitions/text" }
        }
      }
    },
    "evidenceGaps": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "status", "description"],
        "additionalProperties": false,
        "properties": {
          "id": { "$ref": "#/definitions/text" },
          "status": { "enum": ["blocked", "unavailable", "unverified", "partial"] },
          "description": { "$ref": "#/definitions/text" }
        }
      }
    },
    "forkRequirements": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "required": [
          "id", "origin", "method", "signature", "adapter", "behavior",
          "oracle", "mappingStatus", "targets", "gaps"
        ],
        "additionalProperties": false,
        "properties": {
          "id": { "type": "string", "pattern": "^fork\\.(local|onefuzz)\\.[A-Za-z][A-Za-z0-9]*$" },
          "origin": { "enum": ["local", "onefuzz"] },
          "method": { "type": "string", "pattern": "^(Aflfuzz|Libfuzz|Fuzz)[A-Za-z0-9]+$" },
          "signature": { "enum": ["Stream", "string", "ReadOnlySpan<byte>"] },
          "adapter": { "$ref": "#/definitions/text" },
          "behavior": { "$ref": "#/definitions/text" },
          "oracle": { "$ref": "#/definitions/text" },
          "mappingStatus": { "enum": ["mapped", "partial"] },
          "targets": { "$ref": "#/definitions/nonEmptyStringList" },
          "gaps": { "$ref": "#/definitions/stringList" }
        }
      }
    },
    "excludedTargets": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "area", "method", "classification", "disposition", "reason", "replacement"],
        "additionalProperties": false,
        "properties": {
          "id": { "$ref": "#/definitions/targetId" },
          "area": { "enum": ["Encoders", "Certificates", "Network", "PubSub"] },
          "method": { "type": "string", "pattern": "^Libfuzz[A-Za-z0-9]+$" },
          "classification": { "const": "lifecycle-only" },
          "disposition": { "const": "excluded" },
          "reason": { "$ref": "#/definitions/text" },
          "replacement": { "$ref": "#/definitions/text" }
        }
      }
    },
    "areas": {
      "type": "array",
      "minItems": 4,
      "maxItems": 4,
      "items": {
        "type": "object",
        "required": ["id", "project", "assembly", "type", "targets"],
        "additionalProperties": false,
        "properties": {
          "id": { "enum": ["Encoders", "Certificates", "Network", "PubSub"] },
          "project": { "$ref": "#/definitions/repoPath" },
          "assembly": { "type": "string", "pattern": "^Opc\\.Ua\\.(Encoders|Certificates|Network|PubSub)\\.Fuzz$" },
          "type": { "const": "Opc.Ua.Fuzzing.FuzzableCode" },
          "targets": {
            "type": "array",
            "minItems": 1,
            "items": {
              "type": "object",
              "required": [
                "id", "method", "aflMethods", "corpus", "dictionaries",
                "adapter", "oracle", "forkRequirements", "classification"
              ],
              "additionalProperties": false,
              "properties": {
                "id": { "$ref": "#/definitions/targetId" },
                "method": { "type": "string", "pattern": "^Libfuzz[A-Za-z0-9]+$" },
                "aflMethods": {
                  "type": "array",
                  "uniqueItems": true,
                  "items": { "type": "string", "pattern": "^Aflfuzz[A-Za-z0-9]+$" }
                },
                "corpus": {
                  "type": "array", "minItems": 1, "uniqueItems": true,
                  "items": { "$ref": "#/definitions/repoPath" }
                },
                "dictionaries": {
                  "type": "array", "uniqueItems": true,
                  "items": { "$ref": "#/definitions/repoPath" }
                },
                "adapter": { "$ref": "#/definitions/text" },
                "oracle": { "$ref": "#/definitions/text" },
                "forkRequirements": { "$ref": "#/definitions/stringList" },
                "classification": { "const": "continuous" }
              }
            }
          }
        }
      }
    }
  },
  "definitions": {
    "text": { "type": "string", "minLength": 1, "pattern": "\\S" },
    "repoPath": {
      "type": "string",
      "minLength": 1,
      "pattern": "^(?!/)(?!.*(?:^|/)\\.{1,2}(?:/|$))(?!.*//)[^\\\\:*?\"<>|]+(?<!/)$"
    },
    "targetId": {
      "type": "string",
      "pattern": "^(Encoders|Certificates|Network|PubSub)\\.[A-Za-z][A-Za-z0-9.]*$"
    },
    "stringList": {
      "type": "array", "uniqueItems": true,
      "items": { "$ref": "#/definitions/text" }
    },
    "nonEmptyStringList": {
      "type": "array", "minItems": 1, "uniqueItems": true,
      "items": { "$ref": "#/definitions/text" }
    }
  }
}
'@

function Assert-ManifestRelativePath
{
    param([string]$Path)

    foreach ($segment in $Path.Split('/'))
    {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment.Trim() -cne $segment -or
            $segment -in @('.', '..') -or $segment -match '[\\:*?"<>|]')
        {
            throw "Nonportable repository path: $Path"
        }
    }
}

function Resolve-ManifestRepoPath
{
    param([string]$Root, [string]$Path, [ValidateSet('Leaf', 'Container', 'Any')][string]$PathType)

    Assert-ManifestRelativePath $Path
    $resolved = $Root
    foreach ($segment in $Path.Split('/'))
    {
        $resolved = Join-Path $resolved $segment
        if (-not (Test-Path -LiteralPath $resolved))
        {
            throw "Required $PathType path missing: $Path"
        }

        $item = Get-Item -LiteralPath $resolved -Force
        if ($item.Name -cne $segment)
        {
            throw "Repository path casing mismatch: $Path"
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "Manifest paths must not traverse symbolic links or reparse points: $Path"
        }
    }

    if (-not (Test-Path -LiteralPath $resolved -PathType $PathType))
    {
        throw "Required $PathType path has the wrong type: $Path"
    }
    return $resolved
}

function Get-ManifestCallbacks
{
    param([string]$ProjectDirectory, [string]$Type)

    $callbacks = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $namespace, $class = $Type -split '\.(?=[^.]+$)'
    $sources = @(Get-ChildItem -LiteralPath $ProjectDirectory -Filter 'FuzzableCode*.cs' -File -Recurse |
        Where-Object { [IO.Path]::GetRelativePath($ProjectDirectory, $_.FullName) -notmatch '(^|[\\/])(bin|obj)[\\/]' })
    if ($sources.Count -eq 0)
    {
        throw "No callback source files found: $ProjectDirectory"
    }

    foreach ($source in $sources)
    {
        $text = Get-Content -LiteralPath $source.FullName -Raw
        # Remove comments and literals before scanning declarations, not their examples in strings.
        $text = [regex]::Replace($text, '(?s)/\*.*?\*/|//[^\r\n]*|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"', ' ')
        if ($text -notmatch ('\bnamespace\s+' + [regex]::Escape($namespace) + '\s*[;{]') -or
            $text -notmatch ('\bpublic\s+static\s+partial\s+class\s+' + [regex]::Escape($class) + '\b'))
        {
            throw "Callback source does not declare $Type`: $($source.FullName)"
        }

        $pattern = '(?m)^\s*public\s+static\s+(?<return>\w+)\s+' +
            '(?<method>(?:Libfuzz|Aflfuzz)\w+)\s*\((?<arguments>[^)]*)\)'
        foreach ($match in [regex]::Matches($text, $pattern))
        {
            $method = $match.Groups['method'].Value
            $arguments = $match.Groups['arguments'].Value.Trim()
            if ($match.Groups['return'].Value -cne 'void' -or
                $arguments -cnotmatch '^(ReadOnlySpan\s*<\s*byte\s*>|Stream|string)\s+\w+$')
            {
                throw "Unsupported callback signature: $method in $($source.FullName)"
            }

            $signature = $Matches[1] -replace '\s', ''
            if (($method.StartsWith('Libfuzz', [StringComparison]::Ordinal) -and
                    $signature -cne 'ReadOnlySpan<byte>') -or
                ($method.StartsWith('Aflfuzz', [StringComparison]::Ordinal) -and
                    $signature -cnotin @('Stream', 'string')))
            {
                throw "Wrong engine callback signature: $method ($signature)"
            }
            if (-not $callbacks.TryAdd($method, $signature))
            {
                throw "Duplicate callback declaration: $method"
            }
        }
    }

    return ,$callbacks
}

function Assert-DictionaryTokens
{
    <#
    .SYNOPSIS
    Rejects entries libFuzzer's ParseDictionaryFile refuses to load.
    .DESCRIPTION
    libFuzzer aborts the whole campaign when any line is malformed, so an
    unloadable dictionary silently disables every target that declares it.
    Accepted forms are name="token" and a bare "token"; the token must be
    nonempty and may only contain an escaped double quote.
    #>
    param([string[]]$Lines, [string]$Path)

    $number = 0
    $tokens = 0
    foreach ($line in $Lines)
    {
        $number++
        $text = $line.Trim()
        if ($text.Length -eq 0 -or $text.StartsWith('#', [StringComparison]::Ordinal))
        {
            continue
        }
        if ($text -cnotmatch '^(?:[A-Za-z0-9_]+\s*=\s*)?"(?<token>.*)"$')
        {
            throw "Dictionary line $number is not a libFuzzer entry: $Path"
        }
        $token = $Matches['token']
        if ($token.Length -eq 0)
        {
            throw "Dictionary line $number defines an empty token: $Path"
        }
        if ([regex]::Replace($token, '\\.', '') -match '"')
        {
            throw "Dictionary line $number has an unescaped double quote: $Path"
        }
        $tokens++
    }
    if ($tokens -eq 0)
    {
        throw "Dictionary declares no tokens: $Path"
    }
}

function Assert-UniqueJsonProperties
{
    param([System.Text.Json.JsonElement]$Element)

    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object)
    {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject())
        {
            if (-not $names.Add($property.Name))
            {
                throw "Duplicate JSON property: $($property.Name)"
            }
            Assert-UniqueJsonProperties $property.Value
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array)
    {
        foreach ($item in $Element.EnumerateArray())
        {
            Assert-UniqueJsonProperties $item
        }
    }
}

function Assert-FuzzManifest
{
    param([string]$Json, [string]$Root)

    $document = [System.Text.Json.JsonDocument]::Parse($Json)
    try
    {
        Assert-UniqueJsonProperties $document.RootElement
    }
    finally
    {
        $document.Dispose()
    }
    if (-not (Test-Json -Json $Json -Schema $schema -ErrorAction Stop))
    {
        throw 'Fuzz manifest does not satisfy schemaVersion 1.'
    }
    $manifest = ConvertFrom-Json -InputObject $Json -AsHashtable -Depth 64
    $generatorIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $generatedCorpora = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $generatedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $generatedReferences = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($generator in $manifest.corpusGenerators)
    {
        if (-not $generatorIds.Add($generator.id))
        {
            throw "Duplicate corpus generator: $($generator.id)"
        }
        $project = Resolve-ManifestRepoPath $Root $generator.project Leaf
        if ([IO.Path]::GetExtension($project) -cne '.csproj')
        {
            throw "Corpus generator project must be a .csproj: $($generator.project)"
        }
        [xml]$projectXml = Get-Content -LiteralPath $project -Raw
        $outputTypes = @($projectXml.SelectNodes('/Project/PropertyGroup/OutputType') |
            ForEach-Object { $_.InnerText })
        if ('Exe' -cnotin $outputTypes)
        {
            throw "Corpus generator must declare an executable project: $($generator.id)"
        }
        $outputArguments = @($generator.arguments | Where-Object { $_.Contains('{outputRoot}') })
        if ($outputArguments.Count -ne 1 -or
            $outputArguments[0] -cnotmatch '^\{outputRoot\}(?:/([^{}]+))?$')
        {
            throw "Corpus generator requires one isolated outputRoot argument: $($generator.id)"
        }
        $suffix = $Matches[1]
        if (-not [string]::IsNullOrEmpty($suffix))
        {
            Assert-ManifestRelativePath $suffix
        }
        foreach ($argument in $generator.arguments)
        {
            if ($argument -match '[\x00-\x1f]' -or
                ($argument.Replace('{outputRoot}', '') -match '[{}]'))
            {
                throw "Unsupported corpus generator argument: $($generator.id)"
            }
        }
        $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($output in $generator.outputs)
        {
            Assert-ManifestRelativePath $output.path
            Assert-ManifestRelativePath $output.directory
            if (-not $generatedPaths.Add($output.path) -or -not $directories.Add($output.directory))
            {
                throw "Duplicate generated corpus path or directory: $($output.path)"
            }
            foreach ($existing in $generatedCorpora.Keys)
            {
                if ($existing.StartsWith($output.path + '/', [StringComparison]::OrdinalIgnoreCase) -or
                    $output.path.StartsWith($existing + '/', [StringComparison]::OrdinalIgnoreCase))
                {
                    throw "Overlapping generated corpus paths: $existing and $($output.path)"
                }
            }
            $generatedCorpora.Add($output.path, $generator.id)
        }
    }
    $oracles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($oracle in $manifest.oracles)
    {
        if (-not $oracles.Add($oracle.id))
        {
            throw "Duplicate oracle: $($oracle.id)"
        }
    }
    $gaps = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($gap in $manifest.evidenceGaps)
    {
        if (-not $gaps.Add($gap.id))
        {
            throw "Duplicate evidence gap: $($gap.id)"
        }
    }
    $requirements = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $references = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($requirement in $manifest.forkRequirements)
    {
        if (-not $requirements.TryAdd($requirement.id, $requirement))
        {
            throw "Duplicate fork requirement: $($requirement.id)"
        }
        $references.Add($requirement.id, [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal))
    }
    $excluded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($target in $manifest.excludedTargets)
    {
        if (-not $target.id.StartsWith("$($target.area).", [StringComparison]::Ordinal) -or
            -not $excluded.Add("$($target.area)/$($target.method)"))
        {
            throw "Invalid or duplicate lifecycle exclusion: $($target.id)"
        }
    }
    foreach ($method in @('LibfuzzMockServerReplay', 'LibfuzzMockClientReplay'))
    {
        if (-not $excluded.Contains("Network/$method"))
        {
            throw "Required lifecycle exclusion missing: Network/$method"
        }
    }

    $areas = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $aflCount = 0
    foreach ($area in $manifest.areas)
    {
        if (-not $areas.Add($area.id))
        {
            throw "Duplicate fuzz area: $($area.id)"
        }
        $project = Resolve-ManifestRepoPath $Root $area.project Leaf
        if ([IO.Path]::GetExtension($project) -cne '.csproj')
        {
            throw "Fuzz area project must be a .csproj: $($area.project)"
        }
        [xml]$projectXml = Get-Content -LiteralPath $project -Raw
        $assemblyNames = @($projectXml.SelectNodes('/Project/PropertyGroup/AssemblyName') |
            ForEach-Object { $_.InnerText })
        if ($area.assembly -cne "Opc.Ua.$($area.id).Fuzz" -or $area.assembly -cnotin $assemblyNames)
        {
            throw "Assembly name does not match the project: $($area.id)"
        }
        $callbacks = Get-ManifestCallbacks (Split-Path $project -Parent) $area.type
        $declared = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($target in $area.targets)
        {
            if (-not $target.id.StartsWith("$($area.id).", [StringComparison]::Ordinal))
            {
                throw "Target ID is not qualified by its area: $($target.id)"
            }
            if (-not $targets.Add($target.id))
            {
                throw "Duplicate target ID: $($target.id)"
            }
            if ($excluded.Contains("$($area.id)/$($target.method)"))
            {
                throw "Lifecycle-only callback cannot be a continuous target: $($target.method)"
            }
            if (-not $oracles.Contains($target.oracle))
            {
                throw "Unknown target oracle: $($target.oracle)"
            }
            foreach ($method in @($target.method) + @($target.aflMethods))
            {
                if (-not $callbacks.ContainsKey($method))
                {
                    throw "Missing declared callback: $($area.id)/$method"
                }
                if (-not $declared.Add($method))
                {
                    throw "Callback is assigned to multiple targets: $($area.id)/$method"
                }
            }
            $aflCount += $target.aflMethods.Count
            foreach ($path in $target.corpus)
            {
                if ($paths.Add("corpus/$path"))
                {
                    if ($generatedCorpora.ContainsKey($path))
                    {
                        # This is a source contract, never evidence that runtime inputs were materialized.
                        [void]$generatedReferences.Add($path)
                        continue
                    }
                    $inputPath = Resolve-ManifestRepoPath $Root $path Any
                    $seeds = @(Get-ChildItem -LiteralPath $inputPath -File -Recurse -Force |
                        Where-Object { $_.Length -gt 0 })
                    if ($seeds.Count -eq 0)
                    {
                        throw "Required corpus contains no nonempty seed files: $path"
                    }
                    if (@(Get-ChildItem -LiteralPath $inputPath -Recurse -Force |
                            Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -gt 0)
                    {
                        throw "Required corpus contains symbolic links or reparse points: $path"
                    }
                }
            }
            foreach ($path in $target.dictionaries)
            {
                if ($paths.Add("dictionary/$path"))
                {
                    $dictionary = Resolve-ManifestRepoPath $Root $path Leaf
                    if ([IO.Path]::GetExtension($dictionary) -cne '.dict' -or
                        (Get-Item -LiteralPath $dictionary).Length -eq 0)
                    {
                        throw "Required dictionary must be a nonempty .dict file: $path"
                    }
                    Assert-DictionaryTokens (Get-Content -LiteralPath $dictionary) $path
                }
            }
            foreach ($id in $target.forkRequirements)
            {
                if (-not $requirements.ContainsKey($id))
                {
                    throw "Unknown fork requirement: $id"
                }
                [void]$references[$id].Add($target.id)
            }
        }
        foreach ($method in $callbacks.Keys)
        {
            if ($excluded.Contains("$($area.id)/$method"))
            {
                throw "Lifecycle-only callback must be removed from the fuzz source: $method"
            }
            if (-not $declared.Contains($method))
            {
                throw "Unregistered callback: $($area.id)/$method"
            }
        }
    }
    foreach ($path in $generatedCorpora.Keys)
    {
        if (-not $generatedReferences.Contains($path))
        {
            throw "Unreferenced generated corpus: $path"
        }
    }

    $forkFamilies = @(
        'BinaryDecoder', 'BinaryEncoder', 'BinaryEncoderIndempotent',
        'JsonDecoder', 'JsonEncoder', 'BinaryJsonEncoder', 'XmlDecoder', 'XmlEncoder',
        'CertificateDecoder', 'CertificateChainDecoder', 'CertificateChainDecoderCustom',
        'CRLDecoder', 'CRLEncoder', 'CRLEncoderIndempotent'
    )
    $expectedRequirements = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($family in $forkFamilies)
    {
        [void]$expectedRequirements.Add("fork.local.Aflfuzz$family")
        [void]$expectedRequirements.Add("fork.local.Libfuzz$family")
        if ($family -cne 'CRLEncoderIndempotent')
        {
            [void]$expectedRequirements.Add("fork.onefuzz.Fuzz$family")
        }
    }
    foreach ($requirement in $manifest.forkRequirements)
    {
        $id = $requirement.id
        if ($references[$id].Count -eq 0)
        {
            throw "Unreferenced fork requirement: $id"
        }
        if ($id -cne "fork.$($requirement.origin).$($requirement.method)" -or
            -not $expectedRequirements.Remove($id))
        {
            throw "Unexpected pinned fork requirement: $id"
        }
        if (-not $references[$id].SetEquals([string[]]$requirement.targets))
        {
            throw "Fork requirement target references disagree: $id"
        }
        if (($requirement.mappingStatus -ceq 'partial') -ne ($requirement.gaps.Count -gt 0))
        {
            throw "Partial mappings must name their unresolved evidence gaps: $id"
        }
        foreach ($gap in $requirement.gaps)
        {
            if (-not $gaps.Contains($gap))
            {
                throw "Unknown requirement evidence gap: $gap"
            }
        }
        $expectedSignature = 'ReadOnlySpan<byte>'
        if ($requirement.method.StartsWith('Aflfuzz', [StringComparison]::Ordinal))
        {
            $expectedSignature = if ($requirement.method -cin @('AflfuzzJsonDecoder', 'AflfuzzJsonEncoder'))
            {
                'string'
            }
            else
            {
                'Stream'
            }
        }
        if ($requirement.signature -cne $expectedSignature)
        {
            throw "Pinned fork callback signature mismatch: $id"
        }
    }
    if ($expectedRequirements.Count -ne 0)
    {
        throw "Missing pinned fork requirements: $(@($expectedRequirements) -join ', ')"
    }
    $regressions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($asset in $manifest.baseline.regressionAssets)
    {
        if (-not $regressions.Add($asset.path))
        {
            throw "Duplicate baseline regression asset: $($asset.path)"
        }
        $path = Resolve-ManifestRepoPath $Root $asset.path Leaf
        if ((Get-Item -LiteralPath $path).Length -ne $asset.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $asset.sha256 -or
            $asset.sha256 -cne $asset.forkSha256)
        {
            throw "Regression hash or size mismatch: $($asset.path)"
        }
    }
    foreach ($metric in @('total', 'passed', 'failed', 'skipped'))
    {
        $sum = ($manifest.baseline.replay.areas | Measure-Object -Property $metric -Sum).Sum
        if ($sum -ne $manifest.baseline.replay[$metric])
        {
            throw "Baseline replay $metric count does not match its areas."
        }
    }

    return [pscustomobject]@{
        Targets = $targets.Count
        AflCallbacks = $aflCount
        ForkLocalCallbacks = 28
        ForkOneFuzzRegistrations = 13
        PartialMappings = @($manifest.forkRequirements | Where-Object { $_.mappingStatus -ceq 'partial' }).Count
        EvidenceGaps = $manifest.evidenceGaps.Count
        GeneratedCorpora = $generatedReferences.Count
    }
}

$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container))
{
    throw "Repository root missing: $RepositoryRoot"
}
if ([string]::IsNullOrWhiteSpace($ManifestPath))
{
    $ManifestPath = Join-Path (Join-Path $RepositoryRoot 'fuzzing') 'fuzz-targets.json'
}
$json = Get-Content -LiteralPath $ManifestPath -Raw
$summary = Assert-FuzzManifest $json $RepositoryRoot

if ($SelfTest)
{
    $cases = @(
        @{
            Name = 'missing target'
            Pattern = '*Unregistered callback:*'
            Mutate = {
                param($m)
                $area = @($m.areas | Where-Object { $_.id -ceq 'Network' })[0]
                $area.targets = @($area.targets | Select-Object -Skip 1)
            }
        },
        @{
            Name = 'missing declared method'
            Pattern = '*Missing declared callback:*'
            Mutate = { param($m) $m.areas[0].targets[0].method = 'LibfuzzManifestMissingCallback' }
        },
        @{
            Name = 'missing AFL method'
            Pattern = '*Unregistered callback:*'
            Mutate = {
                param($m)
                $area = @($m.areas | Where-Object { $_.id -ceq 'Network' })[0]
                $area.targets[0].aflMethods = @()
            }
        },
        @{
            Name = 'missing corpus'
            Pattern = '*Required Any path missing:*'
            Mutate = {
                param($m)
                $m.areas[0].targets[0].corpus = @('fuzzing/manifest-missing-' + [guid]::NewGuid().ToString('N'))
            }
        },
        @{
            Name = 'missing dictionary'
            Pattern = '*Required Leaf path missing:*'
            Mutate = {
                param($m)
                $m.areas[0].targets[0].dictionaries = @('fuzzing/manifest-missing-' + [guid]::NewGuid() + '.dict')
            }
        },
        @{
            Name = 'unreferenced requirement'
            Pattern = '*Unreferenced fork requirement:*'
            Mutate = {
                param($m)
                $id = $m.forkRequirements[0].id
                foreach ($area in $m.areas)
                {
                    foreach ($target in $area.targets)
                    {
                        $target.forkRequirements = @($target.forkRequirements | Where-Object { $_ -cne $id })
                    }
                }
            }
        },
        @{
            Name = 'unknown requirement'
            Pattern = '*Unknown fork requirement:*'
            Mutate = { param($m) $m.areas[0].targets[0].forkRequirements += 'fork.local.AflfuzzMissingRequirement' }
        },
        @{
            Name = 'duplicate target'
            Pattern = '*Duplicate target ID:*'
            Mutate = {
                param($m)
                $area = @($m.areas | Where-Object { $_.id -ceq 'Network' })[0]
                $area.targets[1].id = $area.targets[0].id
            }
        },
        @{
            Name = 'lifecycle callback resubmission'
            Pattern = '*Lifecycle-only callback cannot be a continuous target:*'
            Mutate = {
                param($m)
                $area = @($m.areas | Where-Object { $_.id -ceq 'Network' })[0]
                $area.targets[0].method = 'LibfuzzMockServerReplay'
            }
        },
        @{
            Name = 'missing lifecycle classification'
            Pattern = '*Required lifecycle exclusion missing:*'
            Mutate = {
                param($m)
                $m.excludedTargets = @($m.excludedTargets | Where-Object { $_.method -cne 'LibfuzzMockServerReplay' })
            }
        },
        @{
            Name = 'missing regression asset'
            Pattern = '*Required Leaf path missing:*'
            Mutate = {
                param($m)
                $m.baseline.regressionAssets[0].path =
                    'fuzzing/manifest-missing-regression-' + [guid]::NewGuid().ToString('N')
            }
        },
        @{
            Name = 'regression hash drift'
            Pattern = '*Regression hash or size mismatch:*'
            Mutate = { param($m) $m.baseline.regressionAssets[0].sha256 = '0' * 64 }
        },
        @{
            Name = 'unsupported schema version'
            Pattern = '*schema*'
            Mutate = { param($m) $m.schemaVersion = 2 }
        },
        @{
            Name = 'empty required corpus list'
            Pattern = '*schema*'
            Mutate = { param($m) $m.areas[0].targets[0].corpus = @() }
        },
        @{
            Name = 'nonportable corpus path'
            Pattern = '*schema*'
            Mutate = { param($m) $m.areas[0].targets[0].corpus[0] = 'fuzzing\invalid-corpus' }
        },
        @{
            Name = 'hidden parity gap'
            Pattern = '*Partial mappings must name their unresolved evidence gaps:*'
            Mutate = {
                param($m)
                $requirement = @($m.forkRequirements | Where-Object { $_.mappingStatus -ceq 'partial' })[0]
                $requirement.gaps = @()
            }
        },
        @{
            Name = 'missing generator project'
            Pattern = '*Required Leaf path missing:*'
            Mutate = {
                param($m)
                $m.corpusGenerators[0].project = 'fuzzing/missing-generator-' + [guid]::NewGuid() + '.csproj'
            }
        },
        @{
            Name = 'missing generator output argument'
            Pattern = '*Corpus generator requires one isolated outputRoot argument:*'
            Mutate = { param($m) $m.corpusGenerators[0].arguments = @('--testcases') }
        },
        @{
            Name = 'unreferenced generated corpus'
            Pattern = '*Unreferenced generated corpus:*'
            Mutate = {
                param($m)
                $m.corpusGenerators[0].outputs += @{
                    path = 'fuzzing/Unreferenced.Generated.Corpus'
                    directory = 'Testcases.Unreferenced'
                }
            }
        },
        @{
            Name = 'duplicate generated corpus'
            Pattern = '*Duplicate generated corpus path or directory:*'
            Mutate = { param($m) $m.corpusGenerators[0].outputs += $m.corpusGenerators[0].outputs[0] }
        },
        @{
            Name = 'escaping generated output'
            Pattern = '*schema*'
            Mutate = { param($m) $m.corpusGenerators[0].outputs[0].directory = '../outside' }
        },
        @{
            Name = 'missing generator declarations'
            Pattern = '*schema*'
            Mutate = { param($m) $m.corpusGenerators = @() }
        },
        @{
            Name = 'duplicate JSON property'
            Pattern = '*Duplicate JSON property:*'
            MutateJson = {
                param($text)
                [regex]::new('("schemaVersion"\s*:\s*1\s*,)').Replace($text, '$1 "schemaVersion": 1,', 1)
            }
        }
    )
    foreach ($case in $cases)
    {
        if ($case.ContainsKey('MutateJson'))
        {
            $negativeJson = & $case.MutateJson $json
        }
        else
        {
            $copy = ConvertFrom-Json -InputObject $json -AsHashtable -Depth 64
            & $case.Mutate $copy
            $negativeJson = ConvertTo-Json -InputObject $copy -Depth 64
        }
        $rejected = $false
        try
        {
            $null = Assert-FuzzManifest $negativeJson $RepositoryRoot
        }
        catch
        {
            # A negative control only passes for its expected validation failure.
            if ($_.Exception.Message -notlike $case.Pattern)
            {
                throw "Negative control '$($case.Name)' failed unexpectedly: $($_.Exception.Message)"
            }
            $rejected = $true
        }
        if (-not $rejected)
        {
            throw "Negative control was incorrectly accepted: $($case.Name)"
        }
    }
    $dictionaryCases = @(
        @{ Name = 'empty token'; Pattern = '*defines an empty token*'; Lines = @('good="a"', 'empty=""') },
        @{ Name = 'unescaped quote'; Pattern = '*unescaped double quote*'; Lines = @('quote="""') },
        @{ Name = 'malformed entry'; Pattern = '*is not a libFuzzer entry*'; Lines = @('name=a') },
        @{ Name = 'no tokens'; Pattern = '*declares no tokens*'; Lines = @('# comment only') }
    )
    foreach ($case in $dictionaryCases)
    {
        $rejected = $false
        try
        {
            Assert-DictionaryTokens $case.Lines 'self-test.dict'
        }
        catch
        {
            if ($_.Exception.Message -notlike $case.Pattern)
            {
                throw "Dictionary control '$($case.Name)' failed unexpectedly: $($_.Exception.Message)"
            }
            $rejected = $true
        }
        if (-not $rejected)
        {
            throw "Dictionary control was incorrectly accepted: $($case.Name)"
        }
    }
    Assert-DictionaryTokens @('# comment', '', 'name="a"', '"b"', 'escaped="\""') 'self-test.dict'
    Write-Output "Manifest negative controls passed: $($cases.Count + $dictionaryCases.Count)."
}

Write-Output (("Fuzz manifest source contract valid: {0} continuous targets, {1} AFL callbacks, " +
    "28 local + 13 OneFuzz requirements; {2} partial mappings and {3} evidence gaps remain. " +
    "{4} generated corpora require materialization and replay in the drop.") -f
    $summary.Targets, $summary.AflCallbacks, $summary.PartialMappings, $summary.EvidenceGaps, $summary.GeneratedCorpora)
