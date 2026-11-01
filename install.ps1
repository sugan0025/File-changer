# Filee - One-click Installer & Desktop Setup for Windows
# Run via: curl.exe -sL https://raw.githubusercontent.com/sugan0025/File-changer/main/install.ps1 | iex

$ErrorActionPreference = 'Stop'

# Ensure modern TLS 1.2 and 1.3 are enabled on Windows PowerShell 5.1
try {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12
} catch {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
}

Write-Host ""
Write-Host " ===================================================" -ForegroundColor Cyan
Write-Host "            Filee - Desktop Installation            " -ForegroundColor White
Write-Host " ===================================================" -ForegroundColor Cyan
Write-Host ""

$installDir = Join-Path $env:LOCALAPPDATA "Programs\Filee"
$tempZip = Join-Path $env:TEMP "Filee-Setup-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8)).zip"
$stagingDir = Join-Path $env:TEMP "Filee-Staging-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"

try {
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

    $curlCmd = Get-Command curl.exe -ErrorAction SilentlyContinue
    $curlExe = if ($curlCmd) { $curlCmd.Source } else { $null }

    # Attempt 1: Query GitHub API using curl.exe
    if ($curlExe) {
        try {
            $json = & $curlExe -sL -H "User-Agent: Filee-Installer" "https://api.github.com/repos/$repo/releases/latest"
            if ($json) {
                $release = $json | ConvertFrom-Json
                $latestTag = $release.tag_name
                $zipAsset = $release.assets | Where-Object {
                    $_.name -like "*win-Portable.zip" -or ($_.name -like "*.zip" -and $_.name -notlike "*linux*" -and $_.name -notlike "*osx*" -and $_.name -notlike "*mac*")
                } | Select-Object -First 1
                if ($zipAsset) {
                    $downloadUrl = $zipAsset.browser_download_url
                }
            }
        } catch { }
    }

    # Attempt 2: Query GitHub API using Invoke-RestMethod
    if (-not $downloadUrl) {
        try {
            $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "Filee-Installer" }
            $latestTag = $release.tag_name
            $zipAsset = $release.assets | Where-Object {
                $_.name -like "*win-Portable.zip" -or ($_.name -like "*.zip" -and $_.name -notlike "*linux*" -and $_.name -notlike "*osx*" -and $_.name -notlike "*mac*")
            } | Select-Object -First 1
            if ($zipAsset) {
                $downloadUrl = $zipAsset.browser_download_url
            }
        } catch {
            Write-Host "Note: GitHub API release check fallback active." -ForegroundColor Gray
        }
    }

    # Fallback to direct latest release asset URL
    if (-not $downloadUrl) {
        $downloadUrl = "https://github.com/$repo/releases/latest/download/Filee-win-Portable.zip"
    }

    Write-Host "Downloading Filee ($downloadUrl)..." -ForegroundColor Cyan

    $downloaded = $false
    if ($curlExe) {
        try {
            & $curlExe -sSfL "$downloadUrl" -o "$tempZip" 2>$null
            if ((Test-Path $tempZip) -and (Get-Item $tempZip).Length -gt 10000) {
                $downloaded = $true
            }
        } catch { }
    }

    if (-not $downloaded) {
        try {
            $webClient = New-Object System.Net.WebClient
            $webClient.Headers.Add("User-Agent", "Filee-Installer")
            $webClient.DownloadFile($downloadUrl, $tempZip)
            if ((Test-Path $tempZip) -and (Get-Item $tempZip).Length -gt 10000) {
                $downloaded = $true
            }
        } catch { }
    }

    if (-not (Test-Path $tempZip) -or (Get-Item $tempZip).Length -lt 1000) {
        throw "Failed to download Filee zip from $downloadUrl. Please verify your internet connection."
    }

    Write-Host "Extracting files to $installDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null

    try {
        Expand-Archive -Path $tempZip -DestinationPath $stagingDir -Force
    } catch {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($tempZip, $stagingDir)
    }

    # Locate Filee.exe inside extracted structure
    $payloadDir = $stagingDir
    if (-not (Test-Path (Join-Path $payloadDir "Filee.exe"))) {
        $nested = Get-ChildItem -Path $stagingDir -Recurse -Filter "Filee.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($nested) {
            $payloadDir = $nested.DirectoryName
        }
    }

    if (-not (Test-Path (Join-Path $payloadDir "Filee.exe"))) {
        throw "Download succeeded, but Filee.exe was not found inside the zip archive."
    }

    # Copy payload to install directory
    Copy-Item -Path (Join-Path $payloadDir "*") -Destination $installDir -Recurse -Force

    # Mark as installed copy so automatic silent in-place updating is enabled
    Set-Content -Path (Join-Path $installDir ".installed") -Value "installed" -Force

    # Create Desktop Shortcut
    $targetExe = Join-Path $installDir "Filee.exe"
    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcutFile = Join-Path $desktopPath "Filee.lnk"
    $wscript = New-Object -ComObject WScript.Shell
    $shortcut = $wscript.CreateShortcut($shortcutFile)
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$targetExe,0"
    $shortcut.Description = "Filee - Fast Local File Converter"
    $shortcut.Save()

    Write-Host "Desktop shortcut created: $shortcutFile" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installation Complete! Launching Filee..." -ForegroundColor Green
    Write-Host ""

    Start-Process -FilePath $targetExe
}
finally {
    Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
}
