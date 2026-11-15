<#
.SYNOPSIS
    Builds the self-contained editor and packs a Velopack installer (Setup.exe), portable zip and update feed.

.EXAMPLE
    ./build/release.ps1                 # version from Directory.Build.props
    ./build/release.ps1 -Version 1.0.1

    Output goes to out/releases: Gnostic-7D2DMapEditor-win-Setup.exe, Gnostic-7D2DMapEditor-win-Portable.zip, the .nupkg update
    packages and releases.win.json. A copy of the portable zip, named by version, is kept in dist/
    (Gnostic-7D2DMapEditor-<version>-win-Portable.zip). Upload all of out/releases to a GitHub release to publish (a v* tag does this on GitHub
    Actions); installed copies find later versions through the repository's releases (Updates.Repository).
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $Version) {
        $Version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.Version
    }
    Write-Host "Releasing Gnostic - 7D2D Map Editor $Version"

    # Make sure owner-only tools are not in a release.
    if ($env:PrivateBuild) { throw 'PrivateBuild is set in the environment; unset it for a release.' }

    dotnet test MapEditor.slnx -c Release
    if ($LASTEXITCODE) { throw 'Tests failed.' }

    $publish = Join-Path $root 'out/publish'
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    dotnet publish src/MapEditor.App -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
    if ($LASTEXITCODE) { throw 'Publish failed.' }

    # Make sure owner-only tools are not in a release.
    # Latin-1 by code page, as Encoding.Latin1 is missing in Windows PowerShell 5.1.
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    foreach ($dll in 'Gnostic.dll', 'MapEditor.Core.dll') {
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $publish $dll))
        $text = $latin1.GetString($bytes) + [System.Text.Encoding]::Unicode.GetString($bytes)
        foreach ($name in 'MapEditor.Core.Private', 'MapEditor.App.Private', 'PackageDialog', 'MapBundle', 'AddExtraTools') {
            if ($text.Contains($name)) { throw "$dll contains owner-only code ($name). Release stopped." }
        }
    }

    # Velopack's CLI (vpk) from dotnet-tools.json. Some SDKs wrongly refuse that manifest as "from another
    # computer"; a vpk installed globally (dotnet tool install -g vpk --version <same>) is used instead when present.
    $toolVersion = (Get-Content (Join-Path $root 'dotnet-tools.json') -Raw | ConvertFrom-Json).tools.vpk.version
    $globalVpk = Get-Command vpk -ErrorAction SilentlyContinue
    if ($globalVpk) {
        $globalVersion = (& vpk --help | Select-String 'Velopack CLI (\S+),' | Select-Object -First 1).Matches.Groups[1].Value
        if ($globalVersion -ne $toolVersion) { throw "Global vpk is $globalVersion; dotnet-tools.json wants $toolVersion." }
        function Invoke-Vpk { vpk @args }
    }
    else {
        dotnet tool restore
        if ($LASTEXITCODE) { throw "dotnet tool restore failed. If it says dotnet-tools.json came from another computer, run: dotnet tool install -g vpk --version $toolVersion" }
        function Invoke-Vpk { dotnet vpk @args }
    }
    Invoke-Vpk pack `
        --packId 7D2DMapEditor `
        --packVersion $Version `
        --packTitle 'Gnostic - 7D2D Map Editor' `
        --packAuthors 'Jason Edwards' `
        --runtime win-x64 `
        --packDir $publish `
        --mainExe Gnostic.exe `
        --icon src/MapEditor.App/app.ico `
        --outputDir (Join-Path $root 'out/releases')
    if ($LASTEXITCODE) { throw 'Packing failed.' }

    # Velopack names the installer and zip after the pack id, which must stay 7D2DMapEditor so installed copies keep
    # updating. Updates only use the .nupkg files and releases.win.json, so the downloads can take the product's name.
    $releases = Join-Path $root 'out/releases'
    foreach ($kind in 'Setup.exe', 'Portable.zip') {
        Move-Item (Join-Path $releases "7D2DMapEditor-win-$kind") (Join-Path $releases "Gnostic-7D2DMapEditor-win-$kind") -Force
    }
    $assets = Join-Path $releases 'assets.win.json'
    if (Test-Path $assets) {
        (Get-Content $assets -Raw) -replace '"7D2DMapEditor-win-', '"Gnostic-7D2DMapEditor-win-' | Set-Content $assets -NoNewline -Encoding utf8
    }

    # Keep each release's portable zip in dist/, named by version, so older ones aren't overwritten.
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $archive = Join-Path $dist "Gnostic-7D2DMapEditor-$Version-win-Portable.zip"
    Copy-Item (Join-Path $releases 'Gnostic-7D2DMapEditor-win-Portable.zip') $archive -Force

    Write-Host "Done: $(Join-Path $root 'out/releases')"
    Write-Host "Archive: $archive"
}
finally {
    Pop-Location
}
