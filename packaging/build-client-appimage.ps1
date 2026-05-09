$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $repo "publish"
$linuxOut = Join-Path $publish "netbuddies-client-linux-x64"
$appDir = Join-Path $publish "NetBuddiesClient.AppDir"

dotnet publish (Join-Path $repo "NetBuddies.App\NetBuddies.App.csproj") `
    -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false `
    -o $linuxOut

if (Test-Path $appDir) {
    Remove-Item $appDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $appDir "usr\bin") | Out-Null
Copy-Item -Path (Join-Path $linuxOut "*") -Destination (Join-Path $appDir "usr\bin") -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "linux-client-appdir\AppRun") -Destination (Join-Path $appDir "AppRun") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "linux-client-appdir\netbuddies-client.desktop") -Destination (Join-Path $appDir "netbuddies-client.desktop") -Force
Copy-Item -Path (Join-Path $repo "NetBuddies.App\Assets\netbuddies-256.png") -Destination (Join-Path $appDir "netbuddies-client.png") -Force

Compress-Archive -Path (Join-Path $appDir "*") `
    -DestinationPath (Join-Path $publish "NetBuddies-Client-linux-x64-AppDir.zip") -Force
Compress-Archive -Path (Join-Path $linuxOut "*") `
    -DestinationPath (Join-Path $publish "NetBuddies-Client-linux-x64.zip") -Force

$appImageTool = Get-ChildItem -Path (Join-Path $publish "tools") -Filter "appimagetool*x86_64*.AppImage" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($appImageTool) {
    if (Get-Command wsl -ErrorAction SilentlyContinue) {
        $linuxRepo = "/mnt/" + ($repo.Substring(0,1).ToLowerInvariant()) + $repo.Substring(2).Replace("\", "/")
        wsl bash -lc "cd '$linuxRepo' && chmod +x publish/tools/appimagetool-x86_64.AppImage publish/NetBuddiesClient.AppDir/AppRun publish/NetBuddiesClient.AppDir/usr/bin/NetBuddies.App && ARCH=x86_64 ./publish/tools/appimagetool-x86_64.AppImage publish/NetBuddiesClient.AppDir publish/NetBuddies-Client-x86_64.AppImage"
    }
}

Write-Host "Client Linux packages are in $publish"
