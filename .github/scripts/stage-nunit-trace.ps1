<#
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

Permission is hereby granted, free of charge, to any person
obtaining a copy of this software and associated documentation
files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use,
copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following
conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/
#>

<#
 .SYNOPSIS
    Observability helpers that capture NUnit InternalTrace logs for one CI test
    project so a stalled run leaves evidence of which fixture was executing.

 .DESCRIPTION
    Dot-sourced by run-dotnet-tests.ps1. Kept separate so the decision, the
    generated settings and the staging can be exercised without building or
    running a project - see tests/Opc.Ua.Tools.Tests/CiNUnitTraceTests.cs.

    This is diagnostics only. It never changes which tests run, how many workers
    run them, the NUnit work directory or any time budget. The NUnit adapter
    writes its trace next to the test assembly, so the logs are copied from the
    assembly output directory into the results tree that is already uploaded.

    Limits worth knowing: after a forced termination the most recent entries may
    be missing from the trace file. At the Info level it records work item
    starts and fixture teardown starts, not per-test or end-of-fixture proof.
#>

$script:NUnitTraceProject = 'Opc.Ua.Server.Tests'
$script:NUnitTraceFramework = 'net48'
$script:NUnitTraceSettingsFileName = 'NUnitInternalTrace.runsettings'
$script:NUnitTraceSettingsXml =
    '<RunSettings><NUnit><InternalTraceLevel>Info</InternalTraceLevel></NUnit></RunSettings>'
$script:NUnitTraceNamePattern = '^InternalTrace\.(\d+)\.Opc\.Ua\.Server\.Tests(\.dll|\.exe)?\.log$'

<#
 .SYNOPSIS
    True only for the one project and target framework that is being diagnosed.
    Coverage runs use their own settings file and never run on net48.
#>
function Test-NUnitTraceApplies(
    [string] $ProjectStem,
    [string] $Framework,
    [bool] $Coverage)
{
    return ($ProjectStem -ceq $script:NUnitTraceProject) -and
        ($Framework -ceq $script:NUnitTraceFramework) -and
        (-not $Coverage)
}

<#
 .SYNOPSIS
    Writes the minimal settings file (UTF-8, no BOM) and returns its path.
#>
function New-NUnitTraceSettings([string] $Directory)
{
    $null = New-Item -ItemType Directory -Path $Directory -Force
    $path = Join-Path $Directory $script:NUnitTraceSettingsFileName
    [System.IO.File]::WriteAllText($path, $script:NUnitTraceSettingsXml, [System.Text.UTF8Encoding]::new($false))
    return $path
}

<#
 .SYNOPSIS
    Copies the fresh trace logs of this run from the assembly output directory
    into the results tree. Never deletes anything.

 .DESCRIPTION
    Only logs of the diagnosed assembly written at or after StartedUtc are
    considered, and the filter runs before anything is read or copied, so old or
    unrelated logs are never staged. A missing capture, an unresolved output
    directory and an oversize log are all reported as warnings and recorded in
    the returned object; none of them is treated as success of the capture.
#>
function Copy-NUnitTraceLogs(
    [string] $OutputDirectory,
    [datetime] $StartedUtc,
    [string] $Destination,
    [long] $MaxBytes = 50MB)
{
    $result = [ordered]@{
        captureStatus   = 'missing'
        outputDirectory = $OutputDirectory
        destination     = $Destination
        staged          = @()
        oversize        = @()
        warnings        = @()
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory) -or -not (Test-Path -LiteralPath $OutputDirectory)) {
        $result.warnings += "The test assembly output directory '$OutputDirectory' could not be resolved; no NUnit trace was captured."
        return [pscustomobject]$result
    }

    $candidates = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter 'InternalTrace.*.log' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -cmatch $script:NUnitTraceNamePattern -and $_.LastWriteTimeUtc -ge $StartedUtc })

    if ($candidates.Count -eq 0) {
        $result.warnings += "No fresh InternalTrace log for $script:NUnitTraceProject was found in '$OutputDirectory'; the run has no trace evidence."
        return [pscustomobject]$result
    }

    $null = New-Item -ItemType Directory -Path $Destination -Force
    foreach ($file in $candidates) {
        if ($file.Length -gt $MaxBytes) {
            $result.oversize += [ordered]@{ name = $file.Name; length = $file.Length }
            $result.warnings += "Trace log '$($file.Name)' is $($file.Length) bytes, over the $MaxBytes byte capture limit; it was not staged."
            continue
        }

        $target = Join-Path $Destination $file.Name
        $suffix = 0
        while (Test-Path -LiteralPath $target) {
            $suffix++
            $target = Join-Path $Destination ('{0}.{1}' -f $file.Name, $suffix)
        }
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $result.staged += [ordered]@{
            name   = $file.Name
            staged = Split-Path -Leaf $target
            length = $file.Length
        }
    }

    if ($result.staged.Count -eq 0) {
        $result.warnings += 'Trace logs were found but none could be staged.'
    }
    elseif ($result.oversize.Count -gt 0) {
        $result.captureStatus = 'partial'
    }
    else {
        $result.captureStatus = 'captured'
    }

    return [pscustomobject]$result
}

<#
 .SYNOPSIS
    Returns the directory of the TargetPath value reported by an MSBuild
    evaluation, or an empty string when it is missing or not an absolute path.
#>
function Get-TargetPathDirectory([string] $TargetPath)
{
    if ([string]::IsNullOrWhiteSpace($TargetPath) -or -not [System.IO.Path]::IsPathRooted($TargetPath)) {
        return ''
    }
    return [System.IO.Path]::GetDirectoryName($TargetPath)
}