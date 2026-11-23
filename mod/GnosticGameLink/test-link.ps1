<#
.SYNOPSIS
    Sends Game Link requests to the game and prints the JSON replies.
.EXAMPLE
    ./test-link.ps1 Status -Count 30 -IntervalMilliseconds 1000
.EXAMPLE
    ./test-link.ps1 Teleport -X -412 -Z 1880 -Facing se -Count 2 -DelaySeconds 5
.EXAMPLE
    ./test-link.ps1 Waypoint -X -412 -Z 1880 -PrefabName farm_16 -Count 2
.EXAMPLE
    ./test-link.ps1 Picture -X -412 -Y 115 -Z 1820 -LookX -412 -LookY 65 -LookZ 1880
.EXAMPLE
    ./test-link.ps1 -CheckLoopback
.EXAMPLE
    ./test-link.ps1 Status -NoToken
#>
param(
    [ValidateSet('Status', 'Teleport', 'Waypoint', 'Picture')]
    [string]$Request = 'Status',
    [int]$X,
    [int]$Z,
    [ValidateSet('-', 'n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw')]
    [string]$Facing = '-',
    [ValidatePattern('\A[A-Za-z0-9_-]{1,64}\z')]
    [string]$PrefabName,
    [ValidateRange(1, 10000)]
    [int]$Count = 1,
    [ValidateRange(0, 60000)]
    [int]$IntervalMilliseconds = 0,
    [ValidateRange(0, 60)]
    [int]$DelaySeconds = 0,
    [int]$Y,
    [int]$LookX,
    [int]$LookY,
    [int]$LookZ,
    [switch]$CheckLoopback,
    [switch]$NoToken
)

$ErrorActionPreference = 'Stop'
$port = 7790
if ($CheckLoopback) {
    $listeners = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Port -eq $port })
    if ($listeners.Count -ne 1 -or -not $listeners[0].Address.Equals([Net.IPAddress]::Loopback)) {
        throw "Expected only 127.0.0.1:${port}. Found $($listeners -join ', ')."
    }
    "Game Link listens only on 127.0.0.1:$port."
    return
}

if ($Request -ne 'Status' -and (-not $PSBoundParameters.ContainsKey('X') -or -not $PSBoundParameters.ContainsKey('Z'))) {
    throw 'Specify -X and -Z in game coordinates.'
}
if ($Request -eq 'Waypoint' -and -not $PrefabName) {
    throw 'Specify -PrefabName for a waypoint.'
}
if ($Request -eq 'Picture') {
    foreach ($coordinate in 'Y', 'LookX', 'LookY', 'LookZ') {
        if (-not $PSBoundParameters.ContainsKey($coordinate)) {
            throw "Specify -$coordinate for a picture."
        }
    }
}

$culture = [Globalization.CultureInfo]::InvariantCulture
$coordinates = $X.ToString($culture) + ' ' + $Z.ToString($culture)
$command = switch ($Request) {
    'Status' { 'STATUS' }
    'Teleport' { "TELEPORT $coordinates $($Facing.ToLowerInvariant())" }
    'Waypoint' { "WAYPOINT $coordinates $PrefabName" }
    'Picture' {
        [string]::Format($culture, 'PICTURE {0} {1} {2} {3} {4} {5}',
            [object[]]@($X, $Y, $Z, $LookX, $LookY, $LookZ))
    }
}
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$tokenPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) '7DaysToDie/GnosticGameLink/session-token'

if ($DelaySeconds -gt 0) {
    Start-Sleep -Seconds $DelaySeconds
}
for ($i = 0; $i -lt $Count; $i++) {
    $prefix = 'GNOSTICLINK 2 '
    if (-not $NoToken) {
        $token = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($tokenPath))
        if ($token -cnotmatch '\A[0-9a-f]{32}\z') {
            throw 'Invalid Game Link token file. Restart the game.'
        }
        $prefix += "$token "
    }
    $bytes = $utf8.GetBytes($prefix + $command + "`n")
    $client = [Net.Sockets.TcpClient]::new()
    try {
        try {
            $connect = $client.ConnectAsync([Net.IPAddress]::Loopback, $port)
            if (-not $connect.Wait(1000)) {
                throw 'Connection timed out.'
            }
        }
        catch {
            throw "Could not connect to Game Link at 127.0.0.1:${port}. $($_.Exception.GetBaseException().Message)"
        }

        $stream = $client.GetStream()
        $stream.ReadTimeout = if ($Request -eq 'Picture') { 30000 } else { 5000 }
        $stream.WriteTimeout = 2000
        $stream.Write($bytes, 0, $bytes.Length)
        $reader = [IO.StreamReader]::new($stream, $utf8)
        try {
            $reply = $reader.ReadLine()
            if ($null -eq $reply) {
                throw 'Game Link closed the connection before replying.'
            }
            $reply
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }

    if ($IntervalMilliseconds -gt 0 -and $i + 1 -lt $Count) {
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
}
