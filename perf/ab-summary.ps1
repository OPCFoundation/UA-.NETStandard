param(
    [Parameter(Position = 0)]
    [string] $File = 'ab-results.txt',
    [ValidateRange(0, 100000)]
    [int] $ExpectedRounds = 0,
    [switch] $RequireExitStatus,
    [switch] $RequireRawCpu
)

$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$rows = [System.Collections.Generic.List[object]]::new()
$cur = $null
$done = $false

function Close-Result
{
    if ($null -eq $script:cur)
    {
        return
    }

    if ($script:cur.exitSeen -and $script:cur.exitCode -ne 0)
    {
        throw "Run exited with code $($script:cur.exitCode) for side=$($script:cur.side) " +
            "round=$($script:cur.round) scenario=$($script:cur.scenario)."
    }
    if ($null -ne $script:cur.serverCpuTicks -and $null -ne $script:cur.received -and
        $script:cur.received -gt 0 -and $null -ne $script:cur.cpu -and
        (($script:cur.resultScenario -ceq 'sub' -and $script:cur.rateUnit -ceq 'notif' -and
                $script:cur.cpuUnit -ceq 'notification') -or
            ($script:cur.resultScenario -ceq 'events' -and $script:cur.rateUnit -ceq 'events' -and
                $script:cur.cpuUnit -ceq 'event notification')))
    {
        $script:cur.cpu = [decimal]$script:cur.serverCpuTicks / (10 * [decimal]$script:cur.received)
        $script:cur.cpuSource = 'ticks/counts'
    }
    $missing = @(
        if (-not $script:cur.resultSeen) { 'RESULT' }
        if (-not $script:cur.clientSeen) { 'client' }
        if (-not $script:cur.serverSeen) { 'server' }
        if ($null -eq $script:cur.tput) { 'rate' }
        if ($null -eq $script:cur.cpu) { 'server per-op' }
        if ($null -eq $script:cur.ccpu) { 'client per-op' }
        if ($RequireExitStatus -and -not $script:cur.exitSeen) { 'exit status' }
        if ($RequireRawCpu -and $script:cur.cpuSource -cne 'ticks/counts') { 'raw per-notification CPU' }
    )
    if ($missing.Count -ne 0)
    {
        throw "Incomplete result for side=$($script:cur.side) round=$($script:cur.round) " +
            "scenario=$($script:cur.scenario): missing $($missing -join ', ')."
    }
    if ($script:cur.failed)
    {
        throw "Client reported FAIL for side=$($script:cur.side) round=$($script:cur.round) " +
            "scenario=$($script:cur.scenario)."
    }

    $script:rows.Add([pscustomobject]$script:cur)
    $script:cur = $null
}

foreach ($line in Get-Content -LiteralPath $File)
{
    if ([string]::IsNullOrWhiteSpace($line))
    {
        continue
    }
    if ($done)
    {
        throw 'Unexpected content after the done marker.'
    }
    if ($line -ceq 'done')
    {
        Close-Result
        $done = $true
        continue
    }
    if ($line -match '^### side=(A|B) round=([1-9][0-9]*) (.+)$')
    {
        $side = $Matches[1]
        $round = [int]::Parse($Matches[2], $invariant)
        $scenario = $Matches[3].Trim()
        if ($scenario.Length -eq 0)
        {
            throw 'A result header has an empty scenario.'
        }
        Close-Result
        $cur = @{
            side = $side; round = $round; scenario = $scenario
            resultSeen = $false; resultLine = $null; resultScenario = $null
            clientSeen = $false; serverSeen = $false; failed = $false
            exitSeen = $false; exitCode = $null
            tput = $null; rateUnit = $null; p50 = $null; p90 = $null; p99 = $null
            received = $null; expected = $null; deliv = $null; deliverySource = 'unavailable'
            errors = $null; writeErrors = $null
            alloc = $null; cpu = $null; reportedCpu = $null; cpuUnit = $null
            serverCpuMs = $null; serverCpuTicks = $null; cpuSource = 'reported-per-op'
            pause = $null; cont = $null; calloc = $null; ccpu = $null; clientCpuUnit = $null
            cpause = $null; ccont = $null
        }
        continue
    }
    if ($line -match '^###')
    {
        throw "Invalid result header: $line"
    }
    if ($null -eq $cur)
    {
        throw "Unexpected content outside a result block: $line"
    }
    if ($line -match '^FAILED\b')
    {
        throw "Run failed for side=$($cur.side) round=$($cur.round) scenario=$($cur.scenario): $line"
    }
    if ($line -cmatch '^EXIT code=([0-9]{1,3})$')
    {
        if ($cur.exitSeen) { throw 'A result block contains multiple exit statuses.' }
        $cur.exitCode = [int]::Parse($Matches[1], $invariant)
        if ($cur.exitCode -gt 255) { throw 'A result block has a malformed exit status.' }
        $cur.exitSeen = $true
    }
    elseif ($line -match '^EXIT(?:\s|$)')
    {
        throw 'A result block has a malformed exit status.'
    }
    elseif ($line -match '^RESULT(?:\s|$)')
    {
        if ($cur.resultSeen) { throw 'A result block contains multiple RESULT lines.' }
        if ($line -match '^RESULT scenario=(\S+)(?:\s|$)')
        {
            $cur.resultScenario = $Matches[1]
        }
        else
        {
            throw 'A RESULT line is missing its scenario.'
        }
        $cur.resultSeen = $true
        $cur.resultLine = $line
    }
    elseif ($line -match '^\s+client: ')
    {
        if ($cur.clientSeen) { throw 'A result block contains multiple client lines.' }
        $cur.clientSeen = $true
        $cur.failed = [regex]::IsMatch($line, '(?:^|\s)FAIL(?:\s|$)')
        if ($line -match '(?:^|\s)(notif|events|req|samples)/s=([0-9]+)(?:\s|$)')
        {
            $cur.rateUnit = $Matches[1]
            $cur.tput = [decimal]::Parse($Matches[2], $invariant)
        }
        if ($line -match 'p50=([0-9.]+)ms p90=([0-9.]+)ms p99=([0-9.]+)ms')
        {
            $cur.p50 = [decimal]::Parse($Matches[1], $invariant)
            $cur.p90 = [decimal]::Parse($Matches[2], $invariant)
            $cur.p99 = [decimal]::Parse($Matches[3], $invariant)
        }
        $reported = $null
        if ($line -match '(?:^|\s)received/expected=([0-9][0-9,.]*)\s*%')
        {
            $styles = [Globalization.NumberStyles]::AllowDecimalPoint -bor
                [Globalization.NumberStyles]::AllowThousands
            $reported = [decimal]::Parse($Matches[1], $styles, $invariant)
        }
        elseif ($line -match '(?:^|\s)received/expected=')
        {
            throw 'The reported delivery percentage is malformed.'
        }
        $received = [regex]::Match($line, '(?:^|\s)received=([0-9]+)(?=\s|$)')
        $expected = [regex]::Match($line, '(?:^|\s)expected=([0-9]+)(?=\s|$)')
        $hasReceived = [regex]::IsMatch($line, '(?:^|\s)received=')
        $hasExpected = [regex]::IsMatch($line, '(?:^|\s)expected=')
        if ($hasReceived -ne $hasExpected -or
            ($hasReceived -and (-not $received.Success -or -not $expected.Success)))
        {
            throw 'Delivery counts must contain both nonnegative received and expected values.'
        }
        if ($hasReceived)
        {
            $cur.received = [long]::Parse($received.Groups[1].Value, $invariant)
            $cur.expected = [long]::Parse($expected.Groups[1].Value, $invariant)
            $cur.deliverySource = 'counts'
            if ($cur.expected -gt 0)
            {
                $cur.deliv = [decimal]$cur.received * 100 / [decimal]$cur.expected
            }
        }
        elseif ($null -ne $reported)
        {
            $cur.deliv = $reported
            $cur.deliverySource = 'reported-rounded'
        }
        if ($line -match '(?:^|\s)errors=([0-9]+)(?:\s|$)')
        {
            $cur.errors = [long]::Parse($Matches[1], $invariant)
        }
        if ($line -match '(?:^|\s)writeErrors=([0-9]+)(?:\s|$)')
        {
            $cur.writeErrors = [long]::Parse($Matches[1], $invariant)
        }
    }
    elseif ($line -match '^\s+server: ')
    {
        if ($cur.serverSeen) { throw 'A result block contains multiple server lines.' }
        $cur.serverSeen = $true
        if ($line -match '(?:^|\s)cpuMs=([0-9.]+)(?:\s|$)')
        {
            $cur.serverCpuMs = [decimal]::Parse($Matches[1], $invariant)
        }
        $tickFields = [regex]::Matches($line, '(?:^|\s)cpuTicks=(\S*)')
        if ($tickFields.Count -gt 1)
        {
            throw 'The server report contains multiple CPU tick counts.'
        }
        if ($tickFields.Count -eq 1)
        {
            $ticks = $tickFields[0].Groups[1].Value
            if ($ticks -notmatch '^[0-9]+$')
            {
                throw 'The server CPU tick count is malformed.'
            }
            $cur.serverCpuTicks = [long]::Parse($ticks, $invariant)
        }
        if ($line -match '(?:^|\s)pauseMs=([0-9]+)(?:\s|$)')
        {
            $cur.pause = [decimal]::Parse($Matches[1], $invariant)
        }
        if ($line -match '(?:^|\s)contention=([0-9]+)(?:\s|$)')
        {
            $cur.cont = [decimal]::Parse($Matches[1], $invariant)
        }
    }
    elseif ($line -match '^\s+client-process: ')
    {
        if ($line -match '(?:^|\s)pauseMs=([0-9]+)(?:\s|$)')
        {
            $cur.cpause = [decimal]::Parse($Matches[1], $invariant)
        }
        if ($line -match '(?:^|\s)contention=([0-9]+)(?:\s|$)')
        {
            $cur.ccont = [decimal]::Parse($Matches[1], $invariant)
        }
    }
    elseif ($line -match '^\s+per-op: allocB=([0-9]+) cpuUs=([0-9.]+)\s+\(([^)]+)\)')
    {
        if ($null -ne $cur.cpu) { throw 'A result block contains multiple server per-op lines.' }
        $cur.alloc = [decimal]::Parse($Matches[1], $invariant)
        $cur.cpu = [decimal]::Parse($Matches[2], $invariant)
        $cur.reportedCpu = $cur.cpu
        $cur.cpuUnit = $Matches[3]
    }
    elseif ($line -match '^\s+client-per-op: allocB=([0-9]+) cpuUs=([0-9.]+)\s+\(([^)]+)\)')
    {
        if ($null -ne $cur.ccpu) { throw 'A result block contains multiple client per-op lines.' }
        $cur.calloc = [decimal]::Parse($Matches[1], $invariant)
        $cur.ccpu = [decimal]::Parse($Matches[2], $invariant)
        $cur.clientCpuUnit = $Matches[3]
    }
}

if (-not $done)
{
    throw 'The result file is missing the done marker.'
}
if ($rows.Count -eq 0)
{
    throw 'The result file contains no completed result blocks.'
}

$groups = @($rows | Group-Object -Property scenario -CaseSensitive)
foreach ($group in $groups)
{
    $rounds = @($group.Group | Group-Object -Property round | Sort-Object { [int]$_.Name })
    if ($ExpectedRounds -gt 0 -and $rounds.Count -ne $ExpectedRounds)
    {
        throw "Scenario $($group.Name) has $($rounds.Count) rounds; expected $ExpectedRounds."
    }
    $rateUnit = $null
    $cpuUnit = $null
    $clientCpuUnit = $null
    $resultLine = $null
    for ($index = 0; $index -lt $rounds.Count; $index++)
    {
        $round = $rounds[$index]
        if ([int]$round.Name -ne $index + 1)
        {
            throw "Scenario $($group.Name) is missing round $($index + 1)."
        }
        $a = @($round.Group | Where-Object { $_.side -ceq 'A' })
        $b = @($round.Group | Where-Object { $_.side -ceq 'B' })
        if ($a.Count -ne 1 -or $b.Count -ne 1)
        {
            throw "Scenario $($group.Name) round=$($round.Name) needs exactly one A and one B result."
        }
        foreach ($row in $round.Group)
        {
            if ($null -eq $rateUnit) { $rateUnit = $row.rateUnit }
            if ($null -eq $cpuUnit) { $cpuUnit = $row.cpuUnit }
            if ($null -eq $clientCpuUnit) { $clientCpuUnit = $row.clientCpuUnit }
            if ($null -eq $resultLine) { $resultLine = $row.resultLine }
            if ($rateUnit -cne $row.rateUnit -or $cpuUnit -cne $row.cpuUnit -or
                $clientCpuUnit -cne $row.clientCpuUnit)
            {
                throw "Scenario $($group.Name) has incompatible rate or CPU units."
            }
            if ($resultLine -cne $row.resultLine)
            {
                throw "Scenario $($group.Name) round=$($round.Name) has mismatched RESULT lines."
            }
        }
    }
}

function Format-Number($value)
{
    if ($null -eq $value) { return 'n/a' }
    return [string]::Format($invariant, '{0:0.########}', $value)
}

function Get-Statistics($values)
{
    $sorted = @($values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2 -eq 0)
    {
        ($sorted[$middle - 1] + $sorted[$middle]) / 2
    }
    else
    {
        $sorted[$middle]
    }
    return [pscustomobject]@{
        median = $median; minimum = $sorted[0]; maximum = $sorted[-1]; count = $sorted.Count
    }
}

function Format-Statistics($statistics, $total)
{
    if ($null -eq $statistics) { return "n/a (n=0/$total)" }
    $text = "$(Format-Number $statistics.median) [$(Format-Number $statistics.minimum)-" +
        "$(Format-Number $statistics.maximum)]"
    if ($statistics.count -ne $total) { $text += " (n=$($statistics.count)/$total)" }
    return $text
}

function Format-Comparison($a, $b, $total)
{
    $left = Get-Statistics $a
    $right = Get-Statistics $b
    if ($null -eq $left -and $null -eq $right) { return $null }
    $change = 'n/a'
    if ($null -ne $left -and $null -ne $right -and
        $left.count -eq $total -and $right.count -eq $total -and $left.median -ne 0)
    {
        $change = "$(Format-Number (100 * ($right.median - $left.median) / $left.median))%"
    }
    return "$(Format-Statistics $left $total) -> $(Format-Statistics $right $total) (change=$change)"
}

function Format-Result($row)
{
    $delivery = if ($null -eq $row.deliv) { 'n/a' } else { "$(Format-Number $row.deliv)%" }
    return "round=$($row.round) side=$($row.side) rate=$(Format-Number $row.tput) " +
        "rateUnit=$($row.rateUnit)/s received=$(Format-Number $row.received) " +
        "expected=$(Format-Number $row.expected) delivery=$delivery source=$($row.deliverySource) " +
        "serverCpuMs=$(Format-Number $row.serverCpuMs) serverCpuTicks=$(Format-Number $row.serverCpuTicks) " +
        "serverCpuUs=$(Format-Number $row.cpu) cpuSource=$($row.cpuSource) " +
        "reportedCpuUs=$(Format-Number $row.reportedCpu) " +
        "cpuUnit=[$($row.cpuUnit)] clientCpuUs=$(Format-Number $row.ccpu) " +
        "clientCpuUnit=[$($row.clientCpuUnit)] errors=$(Format-Number $row.errors) " +
        "writeErrors=$(Format-Number $row.writeErrors) exit=$(Format-Number $row.exitCode)"
}

function Get-CpuDirection($a, $b)
{
    if ($a.cpuSource -cne 'ticks/counts' -or $b.cpuSource -cne 'ticks/counts')
    {
        return 'unavailable'
    }

    $difference = (
        [System.Numerics.BigInteger]$b.serverCpuTicks * [System.Numerics.BigInteger]$a.received
    ) - (
        [System.Numerics.BigInteger]$a.serverCpuTicks * [System.Numerics.BigInteger]$b.received
    )
    if ($difference -gt 0) { return 'increased' }
    if ($difference -lt 0) { return 'decreased' }
    return 'unchanged'
}

'Exit status is available only when recorded; source or harness identity and host isolation are not established.'
foreach ($group in $groups)
{
    "### scenario=$($group.Name)"
    $rounds = @($group.Group | Group-Object -Property round | Sort-Object { [int]$_.Name })
    foreach ($round in $rounds)
    {
        $a = @($round.Group | Where-Object { $_.side -ceq 'A' })[0]
        $b = @($round.Group | Where-Object { $_.side -ceq 'B' })[0]
        Format-Result $a
        "round=$($round.Name) side=A $($a.resultLine)"
        Format-Result $b
        "round=$($round.Name) side=B $($b.resultLine)"
        $deliveryDifference = if ($null -eq $a.deliv -or $null -eq $b.deliv)
        {
            'n/a'
        }
        else
        {
            Format-Number ($b.deliv - $a.deliv)
        }
        "round=$($round.Name) side=B-A rate=$(Format-Number ($b.tput - $a.tput)) " +
            "deliveryPoints=$deliveryDifference serverCpuUs=$(Format-Number ($b.cpu - $a.cpu)) " +
            "serverCpuDirection=$(Get-CpuDirection $a $b)"
    }

    $a = @($group.Group | Where-Object { $_.side -ceq 'A' })
    $b = @($group.Group | Where-Object { $_.side -ceq 'B' })
    $hasRoundedDelivery = @($group.Group | Where-Object { $_.deliverySource -eq 'reported-rounded' }).Count -gt 0
    $hasReportedCpu = @($group.Group | Where-Object { $_.cpuSource -eq 'reported-per-op' }).Count -gt 0
    $summary = [ordered]@{ scenario = $group.Name; n = "$($a.Count)/$($b.Count)" }
    foreach ($metric in 'tput', 'deliv', 'p50', 'p90', 'p99', 'calloc', 'ccpu',
        'cpause', 'ccont', 'alloc', 'cpu', 'pause', 'cont')
    {
        $comparison = Format-Comparison @($a | ForEach-Object { $_.$metric }) `
            @($b | ForEach-Object { $_.$metric }) $rounds.Count
        if ($null -ne $comparison)
        {
            if ($metric -eq 'deliv' -and $hasRoundedDelivery)
            {
                $comparison += ' [reported-rounded input]'
            }
            if ($metric -eq 'cpu' -and $hasReportedCpu)
            {
                $comparison += ' [reported-per-op input]'
            }
            $summary[$metric] = $comparison
        }
    }
    [pscustomobject]$summary | Format-List | Out-String -Width 400
}
