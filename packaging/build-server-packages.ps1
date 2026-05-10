param(
    [int]$Port = 5050
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$publish = Join-Path $repo "publish"
$winOut = Join-Path $publish "netbuddies-server-win-x64"
$winGuiOut = Join-Path $publish "netbuddies-server-gui-win-x64"
$linuxOut = Join-Path $publish "netbuddies-server-linux-x64"
$linuxGuiOut = Join-Path $publish "netbuddies-server-gui-linux-x64"
$appDir = Join-Path $publish "NetBuddiesServer.AppDir"

New-Item -ItemType Directory -Force -Path $publish | Out-Null

dotnet publish (Join-Path $repo "NetBuddies.Server\NetBuddies.Server.csproj") `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -o $winOut

dotnet publish (Join-Path $repo "NetBuddies.Server.Gui\NetBuddies.Server.Gui.csproj") `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -o $winGuiOut

dotnet publish (Join-Path $repo "NetBuddies.Server\NetBuddies.Server.csproj") `
    -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true `
    -o $linuxOut

dotnet publish (Join-Path $repo "NetBuddies.Server.Gui\NetBuddies.Server.Gui.csproj") `
    -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true `
    -o $linuxGuiOut

Compress-Archive -Path (Join-Path $winOut "*") `
    -DestinationPath (Join-Path $publish "NetBuddies-Server-win-x64.zip") -Force

Compress-Archive -Path (Join-Path $winGuiOut "*") `
    -DestinationPath (Join-Path $publish "NetBuddies-Server-Gui-win-x64.zip") -Force

Compress-Archive -Path (Join-Path $linuxOut "*") `
    -DestinationPath (Join-Path $publish "NetBuddies-Server-linux-x64.zip") -Force

if (Test-Path $appDir) {
    Remove-Item -LiteralPath $appDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $appDir "usr\bin") | Out-Null
Copy-Item -Path (Join-Path $linuxOut "*") -Destination (Join-Path $appDir "usr\bin") -Recurse -Force
Copy-Item -Path (Join-Path $linuxGuiOut "*") -Destination (Join-Path $appDir "usr\bin") -Recurse -Force
Copy-Item -Path (Join-Path $repo "packaging\linux-server-appdir\AppRun") -Destination (Join-Path $appDir "AppRun") -Force
Copy-Item -Path (Join-Path $repo "packaging\linux-server-appdir\netbuddies-server.desktop") -Destination (Join-Path $appDir "netbuddies-server.desktop") -Force
Copy-Item -Path (Join-Path $repo "packaging\linux-server-appdir\netbuddies-server.svg") -Destination (Join-Path $appDir "netbuddies-server.svg") -Force

Compress-Archive -Path (Join-Path $appDir "*") `
    -DestinationPath (Join-Path $publish "NetBuddies-Server-linux-x64-AppDir.zip") -Force

$appImageTool = Get-Command appimagetool -ErrorAction SilentlyContinue
if ($appImageTool) {
    & $appImageTool.Source $appDir (Join-Path $publish "NetBuddies-Server-x86_64.AppImage")
}

Write-Host "Server packages are in $publish"
