# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SpecificationRoot,
    [switch] $AllowUncommitted,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$source = (Resolve-Path -LiteralPath $SpecificationRoot).Path
$commit = & git -C $source rev-parse HEAD
if ($LASTEXITCODE -ne 0) {
    throw 'SpecificationRoot must be the companion specification Git checkout.'
}
$dirty = @(& git -C $source status --porcelain -- model extras source)
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot read the specification checkout status.'
}
if ($dirty.Count -gt 0 -and -not $AllowUncommitted) {
    throw 'Commit the specification changes first, or explicitly use -AllowUncommitted for local development.'
}

$models = @(
    @{ Name = 'XRegistry'; Domain = 'xregistry'; Directory = 'src\Opc.Ua.XRegistry' },
    @{ Name = 'SchemaRegistry'; Domain = 'schema-registry'; Directory = 'src\Opc.Ua.SchemaRegistry\Model' },
    @{ Name = 'EndpointRegistry'; Domain = 'endpoint-registry'; Directory = 'src\Opc.Ua.EndpointRegistry\Model' }
)
$records = @()
foreach ($model in $models) {
    $manifest = Get-Content -LiteralPath (Join-Path $source "source\$($model.Domain)\manifest.json") -Raw |
        ConvertFrom-Json
    foreach ($suffix in 'NodeSet2.xml', 'NodeIds.csv') {
        $relativeSource = "model\Opc.Ua.$($model.Name).$suffix"
        $relativeTarget = "$($model.Directory)\Opc.Ua.$($model.Name).$suffix"
        $inputFile = Join-Path $source $relativeSource
        $outputFile = Join-Path $root $relativeTarget
        $hash = (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($Check) {
            if (-not (Test-Path -LiteralPath $outputFile) -or
                (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) {
                throw "Imported model differs: $relativeTarget"
            }
        }
        else {
            [IO.Directory]::CreateDirectory((Split-Path $outputFile -Parent)) | Out-Null
            Copy-Item -LiteralPath $inputFile -Destination $outputFile
        }
        $records += [ordered]@{
            source = $relativeSource
            target = $relativeTarget
            sha256 = $hash
            namespaceUri = $manifest.identity.namespaceUri
            version = $manifest.identity.version
            publicationDate = $manifest.identity.publicationDate
        }
    }
}
$provenance = [ordered]@{
    repository = 'OPCF-Members/OPC30450-CloudInitiative'
    commit = $commit.Trim()
    uncommittedSource = ($dirty.Count -gt 0)
    files = $records
}
$json = $provenance | ConvertTo-Json -Depth 6
$provenancePath = Join-Path $PSScriptRoot 'registry-models.json'
if ($Check) {
    if (-not (Test-Path -LiteralPath $provenancePath -PathType Leaf)) {
        throw 'Imported model provenance is missing: tools\registry-models.json'
    }
    $recorded = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json | ConvertTo-Json -Depth 6
    if ($recorded -cne $json) {
        throw 'Imported model provenance differs: tools\registry-models.json'
    }
}
else {
    [IO.File]::WriteAllText(
        $provenancePath,
        $json + "`n",
        [Text.UTF8Encoding]::new($false))
}
Write-Output "Registry models: $($records.Count) files matched."
