# Copyright (c) OPC Foundation. Licensed under the MIT License.
# Internal read-only transport. Fixtures replace gh on PATH, not validation.
function Invoke-AssuranceApiFile([string] $Endpoint, [string] $Destination, [long] $Limit = 4194304) {
    if ($Endpoint -notmatch '^repos/OPCFoundation/UA-\.NETStandard/[A-Za-z0-9._/?=&%-]+$') {
        throw 'API_ENDPOINT_REJECTED'
    }
    $command = Get-Command gh -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $start = [Diagnostics.ProcessStartInfo]::new($command.Source)
    if ([IO.Path]::GetExtension($command.Source) -eq '.cmd') {
        $start.FileName = $env:ComSpec
        $start.Arguments = '/d /s /c ""' + $command.Source +
            '" api --hostname github.com --method GET "' + $Endpoint + '""'
    }
    else {
        foreach ($argument in @('api', '--hostname', 'github.com', '--method', 'GET', $Endpoint)) {
            $start.ArgumentList.Add($argument)
        }
    }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $null = $process.Start()
        $started = $true
        $output = $process.StandardOutput.BaseStream.CopyToAsync($stream)
        $errors = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
        if ($AssuranceApiDeadline -is [DateTimeOffset] -and $AssuranceApiDeadline -lt $deadline) {
            $deadline = $AssuranceApiDeadline
        }
        while (-not $process.HasExited -or -not $output.IsCompleted -or -not $errors.IsCompleted) {
            if ($stream.Position -gt $Limit -or [DateTimeOffset]::UtcNow -gt $deadline) {
                if (-not $process.HasExited) { $process.Kill($true) }
                throw 'API_RESPONSE_LIMIT'
            }
            Start-Sleep -Milliseconds 25
        }
        if ($process.ExitCode -ne 0 -or -not $output.IsCompletedSuccessfully -or
            -not $errors.IsCompletedSuccessfully -or $stream.Position -gt $Limit) {
            throw 'API_UNAVAILABLE'
        }
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
        $stream.Dispose()
    }
}

function Invoke-AssuranceApi([string] $Endpoint) {
    $path = Join-Path $workspace ([guid]::NewGuid().ToString('N') + '.api.json')
    try {
        Invoke-AssuranceApiFile $Endpoint $path
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 50
    }
    finally {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

function Get-AssuranceApiCollection([string] $Endpoint, [string] $Property) {
    $items = @()
    for ($page = 1; $page -le 10; $page++) {
        $response = Invoke-AssuranceApi "${Endpoint}?per_page=100&page=$page"
        if ($response.total_count -gt 1000) { throw 'API_COLLECTION_LIMIT' }
        $items += @($response.$Property | Where-Object { $null -ne $_ })
        if ($items.Count -eq $response.total_count) { return ,$items }
        if (@($response.$Property).Count -ne 100) { throw 'API_COLLECTION_INCOMPLETE' }
    }
    throw 'API_COLLECTION_LIMIT'
}

function Expand-AssuranceArchive([string] $ArchivePath, [string] $Destination, [string[]] $AllowedNames) {
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($archive.Entries.Count -lt 1 -or $archive.Entries.Count -gt 8) { throw 'ARTIFACT_ENTRY_LIMIT' }
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $total = 0L
        foreach ($entry in $archive.Entries) {
            # Flat, explicitly named JSON only: no paths, links, devices or raw logs.
            if ($entry.FullName -cnotin $AllowedNames -or -not $names.Add($entry.FullName) -or
                $entry.FullName -notmatch '^[a-z0-9][a-z0-9.-]*\.json$' -or
                (($entry.ExternalAttributes -shr 16) -band 0xF000) -notin @(0, 0x8000) -or
                ($entry.ExternalAttributes -band 0x400) -ne 0 -or $entry.Length -gt 2097152) {
                throw 'ARTIFACT_ENTRY_REJECTED'
            }
            $total += $entry.Length
        }
        if ($total -gt 4194304) { throw 'ARTIFACT_EXPANSION_LIMIT' }
        $null = New-Item -ItemType Directory -Path $Destination
        $files = @()
        foreach ($entry in $archive.Entries) {
            $path = Join-Path $Destination $entry.FullName
            $input = $entry.Open()
            $output = [IO.File]::Open($path, [IO.FileMode]::CreateNew)
            try {
                $buffer = [byte[]]::new(8192)
                $written = 0L
                while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $written += $read
                    if ($written -gt $entry.Length) { throw 'ARTIFACT_EXPANSION_LIMIT' }
                    $output.Write($buffer, 0, $read)
                }
                if ($written -ne $entry.Length) { throw 'ARTIFACT_LENGTH_MISMATCH' }
            }
            finally { $input.Dispose(); $output.Dispose() }
            $files += @{
                name = $entry.FullName
                digest = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        return $files
    }
    finally { $archive.Dispose() }
}
