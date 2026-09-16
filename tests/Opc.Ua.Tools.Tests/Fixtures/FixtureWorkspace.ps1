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

function Get-FixturePhysicalTempDirectory {
    <#
    .SYNOPSIS
    Resolves the host's temporary-directory aliases before creating fixture inputs.
    .DESCRIPTION
    macOS exposes temporary paths through /var, a symlink to /private/var.
    Fixture setup uses the physical path; production evidence validation continues
    rejecting all caller-provided symbolic links and reparse points.
    #>
    param([string]$Path = [IO.Path]::GetTempPath())
    $physical = [IO.Path]::GetFullPath($Path)
    for ($attempt = 0; $attempt -lt 32; $attempt++) {
        $current = [IO.DirectoryInfo]::new($physical)
        $resolved = $false
        while ($null -ne $current) {
            if ($null -ne $current.LinkTarget) {
                $target = $current.ResolveLinkTarget($true)
                if ($null -eq $target) { throw 'Fixture temporary-directory alias cannot be resolved.' }
                $relative = [IO.Path]::GetRelativePath($current.FullName, $physical)
                $physical = [IO.Path]::GetFullPath([IO.Path]::Combine($target.FullName, $relative))
                $resolved = $true
                break
            }
            $current = $current.Parent
        }
        if (-not $resolved) { return $physical }
    }
    throw 'Fixture temporary-directory aliases exceed the resolution bound.'
}
