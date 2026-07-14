# Downloads the bundled tools for the USB phone feature:
#   - Google platform-tools (adb.exe)     -> third_party/platform-tools/
#   - Genymobile scrcpy-server            -> third_party/scrcpy-server
# Both are Apache-2.0 licensed. Run once, then build the app.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$tp = Join-Path $root 'third_party'
New-Item -ItemType Directory -Force $tp | Out-Null

Write-Host 'Downloading Android platform-tools (adb)...'
$ptZip = Join-Path $env:TEMP 'audiomixer-platform-tools.zip'
Invoke-WebRequest 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip' -OutFile $ptZip -UseBasicParsing
if (Test-Path (Join-Path $tp 'platform-tools')) { Remove-Item -Recurse -Force (Join-Path $tp 'platform-tools') }
Expand-Archive $ptZip -DestinationPath $tp -Force
Remove-Item $ptZip

Write-Host 'Downloading scrcpy-server...'
$versions = @('3.3.1', '3.3', '3.2', '3.1')
$ok = $false
foreach ($ver in $versions) {
    try {
        Invoke-WebRequest "https://github.com/Genymobile/scrcpy/releases/download/v$ver/scrcpy-server-v$ver" `
            -OutFile (Join-Path $tp 'scrcpy-server') -UseBasicParsing
        Set-Content (Join-Path $tp 'scrcpy-server-version.txt') $ver -Encoding ascii -NoNewline
        Write-Host "scrcpy-server v$ver downloaded."
        $ok = $true
        break
    } catch {
        Write-Host "v$ver not available, trying next..."
    }
}
if (-not $ok) { throw 'Could not download scrcpy-server from GitHub.' }

Write-Host 'Done. Rebuild the app so the tools get copied next to AudioMixer.exe.'
