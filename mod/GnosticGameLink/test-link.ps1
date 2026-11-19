<#
.SYNOPSIS
    Sends Game Link requests to the game and prints the JSON replies.
.EXAMPLE
    ./test-link.ps1 Status -Count 30 -IntervalMilliseconds 1000
.EXAMPLE
    ./test-link.ps1 Teleport -X -412 -Z 1880 -Facing se -Count 2 -DelaySeconds 5
.EXAMPLE
    ./test-link.ps1 Waypoint -X -412 -Z 1880 -PrefabName farm_16 -Count 2
#>
param(
    [ValidateSet('Status', 'Teleport', 'Waypoint')]
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
    [int]$DelaySeconds = 0
)

$ErrorActionPreference = 'Stop'
if ($Request -ne 'Status' -and (-not $PSBoundParameters.ContainsKey('X') -or -not $PSBoundParameters.ContainsKey('Z'))) {
    throw 'Specify -X and -Z in game coordinates.'
}
if ($Request -eq 'Waypoint' -and -not $PrefabName) {
    throw 'Specify -PrefabName for a waypoint.'
}

$culture = [Globalization.CultureInfo]::InvariantCulture
$coordinates = $X.ToString($culture) + ' ' + $Z.ToString($culture)
$line = switch ($Request) {
    'Status' { 'GNOSTICLINK 1 STATUS' }
    'Teleport' { "GNOSTICLINK 1 TELEPORT $coordinates $($Facing.ToLowerInvariant())" }
    'Waypoint' { "GNOSTICLINK 1 WAYPOINT $coordinates $PrefabName" }
}
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$bytes = $utf8.GetBytes($line + "`n")

if ($DelaySeconds -gt 0) {
    Start-Sleep -Seconds $DelaySeconds
}
for ($i = 0; $i -lt $Count; $i++) {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        try {
            $connect = $client.ConnectAsync([Net.IPAddress]::Loopback, 7790)
            if (-not $connect.Wait(1000)) {
                throw 'Connection timed out.'
            }
        }
        catch {
            throw "Could not connect to Game Link at 127.0.0.1:7790. $($_.Exception.GetBaseException().Message)"
        }

        $stream = $client.GetStream()
        $stream.ReadTimeout = 5000
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
