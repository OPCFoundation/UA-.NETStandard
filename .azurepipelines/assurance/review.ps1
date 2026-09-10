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
