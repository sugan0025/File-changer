# Filee - One-click Installer & Desktop Setup for Windows
# Run via: irm https://raw.githubusercontent.com/sugan0025/File-changer/main/install.ps1 | iex

$ErrorActionPreference = 'Stop'

# Ensure modern TLS 1.2 and 1.3 are enabled on Windows PowerShell 5.1
try {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
} catch {
    [System.Net.ServicePointManager]::SecurityProtocol = 3072
}

Write-Host ""
Write-Host " ===================================================" -ForegroundColor Cyan
Write-Host "            Filee - Desktop Installation            " -ForegroundColor White
Write-Host " ===================================================" -ForegroundColor Cyan
Write-Host ""

$installDir = Join-Path $env:LOCALAPPDATA "Programs\Filee"
$tempZip = Join-Path $env:TEMP "Filee-Setup-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8)).zip"

# Gracefully close running Filee if any
$running = Get-Process -Name "Filee" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing running instance of Filee..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Write-Host "Finding latest Filee release from GitHub..." -ForegroundColor Cyan

$repo = "sugan0025/File-changer"
$downloadUrl = $null
$latestTag = $null

try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "Filee-Installer" }
    $latestTag = $release.tag_name
    $zipAsset = $release.assets | Where-Object {
        $_.name -like "*win-Portable.zip" -or ($_.name -like "*.zip" -and $_.name -notlike "*linux*" -and $_.name -notlike "*osx*" -and $_.name -notlike "*mac*")
    } | Select-Object -First 1
    if ($zipAsset) {
        $downloadUrl = $zipAsset.browser_download_url
    }
}
catch {
    Write-Host "Note: GitHub API release check fallback active." -ForegroundColor Gray
}

if (-not $downloadUrl) {
    $downloadUrl = "https://github.com/$repo/releases/latest/download/Filee-win-Portable.zip"
}

Write-Host "Downloading Filee ($downloadUrl)..." -ForegroundColor Cyan

$downloaded = $false
$curlExe = (Get-Command curl.exe -ErrorAction SilentlyContinue)?.Source
if ($curlExe) {
    try {
        & $curlExe -fL "$downloadUrl" -o "$tempZip"
        if ((Test-Path $tempZip) -and (Get-Item $tempZip).Length -gt 10000) {
            $downloaded = $true
        }
    }
    catch { }
}

if (-not $downloaded) {
    $webClient = New-Object System.Net.WebClient
    $webClient.Headers.Add("User-Agent", "Filee-Installer")
    $webClient.DownloadFile($downloadUrl, $tempZip)
}

Write-Host "Extracting files to $installDir..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

$stagingDir = Join-Path $env:TEMP "Filee-Staging-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"
Expand-Archive -Path $tempZip -DestinationPath $stagingDir -Force

# Locate Filee.exe inside extracted structure
$payloadDir = $stagingDir
if (-not (Test-Path (Join-Path $payloadDir "Filee.exe"))) {
    $nested = Get-ChildItem -Path $stagingDir -Recurse -Filter "Filee.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($nested) {
        $payloadDir = $nested.DirectoryName
    }
}

if (-not (Test-Path (Join-Path $payloadDir "Filee.exe"))) {
    Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
    throw "Download succeeded, but Filee.exe was not found inside the zip archive."
}

# Copy payload to install directory
Copy-Item -Path (Join-Path $payloadDir "*") -Destination $installDir -Recurse -Force
Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $tempZip -Force -ErrorAction SilentlyContinue

# Mark as installed copy so automatic silent in-place updating is enabled
Set-Content -Path (Join-Path $installDir ".installed") -Value "installed" -Force

# Create Desktop Shortcut
$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutFile = Join-Path $desktopPath "Filee.lnk"
$wscript = New-Object -ComObject WScript.Shell
$shortcut = $wscript.CreateShortcut($shortcutFile)
$shortcut.TargetPath = Join-Path $installDir "Filee.exe"
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$installDir\Filee.exe,0"
$shortcut.Description = "Filee - Fast Local File Converter"
$shortcut.Save()

Write-Host "Desktop shortcut created: $shortcutFile" -ForegroundColor Green
Write-Host ""
Write-Host "Installation Complete! Launching Filee..." -ForegroundColor Green
Write-Host ""

Start-Process -FilePath (Join-Path $installDir "Filee.exe")

