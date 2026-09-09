# Copyright (c) OPC Foundation, Inc. All rights reserved.
# Licensed under the MIT License. See LICENSE.txt in the project root for license information.

function New-AssuranceReviewAuthenticator {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $VerificationBundle,
        [Parameter(Mandatory)][string] $TrustPolicy,
        [Parameter(Mandatory)][string] $WorkDirectory
    )
    $controller = [IO.Path]::GetFullPath($RepositoryRoot)
    $bundle = [IO.Path]::GetFullPath($VerificationBundle)
    $trust = [IO.Path]::GetFullPath($TrustPolicy)
    $work = [IO.Path]::GetFullPath($WorkDirectory)
    $tool = Join-Path $controller 'tools\Opc.Ua.ReleaseEvidence\bin\Release\net10.0\Opc.Ua.ReleaseEvidence.dll'
    $bundleRoot = [IO.Path]::GetDirectoryName($bundle).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($tool.StartsWith($bundleRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $work.StartsWith($bundleRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        throw 'CODEQL_PROTECTED_REVIEW_VERIFIER_UNAVAILABLE'
    }
    $null = New-Item -ItemType Directory -Path $work -Force
    return {
        param([string] $Id, [string] $Digest)
        if ($Id -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$' -or
            $Digest -cnotmatch '^sha256:[a-f0-9]{64}$') {
            throw 'CODEQL_REVIEW_IDENTITY_INVALID'
        }
        $result = Join-Path $work ([Guid]::NewGuid().ToString('N') + '.json')
        # Execute the protected reader every time; a saved projection is never an authentication input.
        & dotnet $tool verify-codeql-review --repository-root $controller --verification-bundle $bundle `
            --trust-policy $trust --review-id $Id --review-digest $Digest --output $result *> "$result.log"
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $result -PathType Leaf)) {
            throw 'CODEQL_REVIEW_UNVERIFIED'
        }
        $projection = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
        if ($projection.id -cne $Id -or $projection.digest -cne $Digest) {
            throw 'CODEQL_REVIEW_IDENTITY_INVALID'
        }
        return $projection
    }.GetNewClosure()
}
