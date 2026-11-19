<#
.SYNOPSIS
    Builds and packages Gnostic Game Link. Add -Install to copy it to the game's Mods folder.
.PARAMETER GamePath
    The 7 Days to Die install folder. Defaults to the path in game-path.local.txt beside this script.
.PARAMETER Install
    Installs or updates Gnostic Game Link in the user's 7 Days to Die Mods folder.
#>
param(
    [string]$GamePath,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$settings = Join-Path $PSScriptRoot 'game-path.local.txt'
if (-not $GamePath -and (Test-Path -LiteralPath $settings)) {
    $GamePath = ([string](Get-Content -LiteralPath $settings -Raw)).Trim()
}
if (-not $GamePath -or -not (Test-Path -LiteralPath (Join-Path $GamePath '7DaysToDie_Data/Managed/Assembly-CSharp.dll') -PathType Leaf)) {
    throw 'Pass -GamePath or put the 7 Days to Die install folder in game-path.local.txt beside this script.'
}
$GamePath = (Resolve-Path -LiteralPath $GamePath).ProviderPath

$name = 'GnosticGameLink'
$project = Join-Path $PSScriptRoot "$name.csproj"
$modInfo = Join-Path $PSScriptRoot 'ModInfo.xml'
$version = ([xml](Get-Content -LiteralPath $modInfo -Raw)).xml.Version.value
$dll = Join-Path $PSScriptRoot "bin/Release/net48/$name.dll"

Push-Location $PSScriptRoot
try {
    dotnet build $project -c Release "-p:GamePath=$GamePath" "-p:Version=$version"
    if ($LASTEXITCODE -ne 0) {
        throw 'Gnostic Game Link build failed.'
    }
}
finally {
    Pop-Location
}

$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$archivePath = Join-Path $dist "$name-$version.zip"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveFile = [IO.File]::Create($archivePath)
try {
    $archive = [IO.Compression.ZipArchive]::new($archiveFile, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $dll, "$name/$name.dll") | Out-Null
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $modInfo, "$name/ModInfo.xml") | Out-Null
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $archiveFile.Dispose()
}
Write-Host "Package: $archivePath"

if ($Install) {
    $destination = Join-Path ([Environment]::GetFolderPath('ApplicationData')) "7DaysToDie/Mods/$name"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath $dll, $modInfo -Destination $destination -Force
    Write-Host "Installed: $destination"
}
