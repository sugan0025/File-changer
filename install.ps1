# HOOT - One-click Installer & Desktop Setup for Windows
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
Write-Host "            HOOT - Desktop Installation             " -ForegroundColor White
Write-Host " ===================================================" -ForegroundColor Cyan
Write-Host ""

$installDir = Join-Path $env:LOCALAPPDATA "Programs\Hoot"
$legacyDir = Join-Path $env:LOCALAPPDATA "Programs\Filee"
$tempZip = Join-Path $env:TEMP "Hoot-Setup-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8)).zip"
$stagingDir = Join-Path $env:TEMP "Hoot-Staging-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"

try {
    # Gracefully close running HOOT / Filee if any
    $running = Get-Process -Name "Hoot", "Filee" -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Closing running instances of HOOT / Filee..." -ForegroundColor Yellow
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }

    # Clean up legacy Programs\Filee folder if present
    if (Test-Path $legacyDir) {
        Remove-Item $legacyDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Finding latest HOOT release from GitHub..." -ForegroundColor Cyan

    $repo = "sugan0025/File-changer"
    $downloadUrl = $null
    $latestTag = $null

    $curlCmd = Get-Command curl.exe -ErrorAction SilentlyContinue
    $curlExe = if ($curlCmd) { $curlCmd.Source } else { $null }

    # Attempt 1: Query GitHub API using curl.exe
    if ($curlExe) {
        try {
            $json = & $curlExe --ssl-no-revoke -sL -m 15 -H "User-Agent: Hoot-Installer" "https://api.github.com/repos/$repo/releases/latest"
            if ($json) {
                $release = $json | ConvertFrom-Json
                $latestTag = $release.tag_name
                $zipAsset = $release.assets | Where-Object { $_.name -like "Hoot*win-Portable.zip" } | Select-Object -First 1
                if (-not $zipAsset) {
                    $zipAsset = $release.assets | Where-Object {
                        $_.name -like "*win-Portable.zip" -or ($_.name -like "*.zip" -and $_.name -notlike "*linux*" -and $_.name -notlike "*osx*" -and $_.name -notlike "*mac*")
                    } | Select-Object -First 1
                }
                if ($zipAsset) {
                    $downloadUrl = $zipAsset.browser_download_url
                }
            }
        } catch { }
    }

    # Attempt 2: Query GitHub API using Invoke-RestMethod
    if (-not $downloadUrl) {
        try {
            $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "Hoot-Installer" }
            $latestTag = $release.tag_name
            $zipAsset = $release.assets | Where-Object { $_.name -like "Hoot*win-Portable.zip" } | Select-Object -First 1
            if (-not $zipAsset) {
                $zipAsset = $release.assets | Where-Object {
                    $_.name -like "*win-Portable.zip" -or ($_.name -like "*.zip" -and $_.name -notlike "*linux*" -and $_.name -notlike "*osx*" -and $_.name -notlike "*mac*")
                } | Select-Object -First 1
            }
            if ($zipAsset) {
                $downloadUrl = $zipAsset.browser_download_url
            }
        } catch {
            Write-Host "Note: GitHub API release check fallback active." -ForegroundColor Gray
        }
    }

    # Candidate download URLs (API discovery -> releases/latest -> direct releases)
    $candidateUrls = [System.Collections.Generic.List[string]]::new()
    if ($downloadUrl) { $candidateUrls.Add($downloadUrl) }
    $candidateUrls.Add("https://github.com/$repo/releases/latest/download/Hoot-win-Portable.zip")
    $candidateUrls.Add("https://github.com/$repo/releases/latest/download/Filee-win-Portable.zip")

    $downloaded = $false
    foreach ($url in $candidateUrls) {
        Write-Host "Attempting download from $url..." -ForegroundColor Cyan
        if ($curlExe) {
            try {
                & $curlExe --ssl-no-revoke -sSfL -m 180 "$url" -o "$tempZip" 2>$null
                if ((Test-Path $tempZip) -and (Get-Item $tempZip).Length -gt 1000000) {
                    $downloaded = $true
                    $downloadUrl = $url
                    break
                }
            } catch { }
        }

        if (-not $downloaded) {
            try {
                $webClient = New-Object System.Net.WebClient
                $webClient.Headers.Add("User-Agent", "Hoot-Installer")
                $webClient.DownloadFile($url, $tempZip)
                if ((Test-Path $tempZip) -and (Get-Item $tempZip).Length -gt 1000000) {
                    $downloaded = $true
                    $downloadUrl = $url
                    break
                }
            } catch { }
        }
    }

    if (-not $downloaded -or -not (Test-Path $tempZip) -or (Get-Item $tempZip).Length -lt 1000000) {
        throw "Failed to download HOOT zip from available sources. Please check your internet connection."
    }

    Write-Host "Download successful ($downloadUrl)!" -ForegroundColor Green

    Write-Host "Extracting files to $installDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null

    try {
        Expand-Archive -Path $tempZip -DestinationPath $stagingDir -Force
    } catch {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($tempZip, $stagingDir)
    }

    # Locate Hoot.exe or Filee.exe inside extracted structure
    $payloadDir = $stagingDir
    if (-not (Test-Path (Join-Path $payloadDir "Hoot.exe")) -and -not (Test-Path (Join-Path $payloadDir "Filee.exe"))) {
        $nested = Get-ChildItem -Path $stagingDir -Recurse -Filter "Hoot.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $nested) {
            $nested = Get-ChildItem -Path $stagingDir -Recurse -Filter "Filee.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        }
        if ($nested) {
            $payloadDir = $nested.DirectoryName
        }
    }

    if (-not (Test-Path (Join-Path $payloadDir "Hoot.exe")) -and -not (Test-Path (Join-Path $payloadDir "Filee.exe"))) {
        throw "Download succeeded, but neither Hoot.exe nor Filee.exe was found inside the zip archive."
    }

    # Copy payload to install directory
    Copy-Item -Path (Join-Path $payloadDir "*") -Destination $installDir -Recurse -Force

    # If only Filee.exe exists in payload, ensure Hoot.exe exists as primary
    if (-not (Test-Path (Join-Path $installDir "Hoot.exe")) -and (Test-Path (Join-Path $installDir "Filee.exe"))) {
        Copy-Item (Join-Path $installDir "Filee.exe") (Join-Path $installDir "Hoot.exe") -Force
    }

    # Mark as installed copy so automatic silent in-place updating is enabled
    Set-Content -Path (Join-Path $installDir ".installed") -Value "installed" -Force

    # Set target executable: look for Hoot.exe first, fallback to Filee.exe
    $targetExe = if (Test-Path (Join-Path $installDir "Hoot.exe")) {
        Join-Path $installDir "Hoot.exe"
    } else {
        Join-Path $installDir "Filee.exe"
    }

    # Shortcuts: Create HOOT.lnk pointing to Hoot.exe; remove old Filee.lnk if present
    $desktopPaths = @(
        [Environment]::GetFolderPath("Desktop"),
        (Join-Path $env:USERPROFILE "Desktop"),
        (Join-Path $env:USERPROFILE "OneDrive\Desktop")
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

    $wscript = New-Object -ComObject WScript.Shell

    foreach ($dp in $desktopPaths) {
        $oldLnk = Join-Path $dp "Filee.lnk"
        if (Test-Path $oldLnk) { Remove-Item $oldLnk -Force -ErrorAction SilentlyContinue }
        $sc = $wscript.CreateShortcut((Join-Path $dp "HOOT.lnk"))
        $sc.TargetPath = $targetExe
        $sc.WorkingDirectory = $installDir
        $sc.IconLocation = "$targetExe,0"
        $sc.Description = "HOOT - Fast Local File Converter"
        $sc.Save()
    }

    # Start Menu Shortcut
    $startMenu = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
    if (Test-Path $startMenu) {
        $oldStartLnk = Join-Path $startMenu "Filee.lnk"
        if (Test-Path $oldStartLnk) { Remove-Item $oldStartLnk -Force -ErrorAction SilentlyContinue }
        $startSc = $wscript.CreateShortcut((Join-Path $startMenu "HOOT.lnk"))
        $startSc.TargetPath = $targetExe
        $startSc.WorkingDirectory = $installDir
        $startSc.IconLocation = "$targetExe,0"
        $startSc.Description = "HOOT - Fast Local File Converter"
        $startSc.Save()
    }

    Write-Host "Desktop and Start Menu shortcuts created for HOOT!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installation Complete! Launching HOOT..." -ForegroundColor Green
    Write-Host ""

    # Anonymous installation counter ping (Option B)
    try { if (Get-Command curl.exe -ErrorAction SilentlyContinue) { & curl.exe -sL -m 3 "https://hits.sh/github.com/sugan0025/File-changer/installs.svg" 2>$null | Out-Null } else { (New-Object System.Net.WebClient).DownloadString("https://hits.sh/github.com/sugan0025/File-changer/installs.svg") | Out-Null } } catch { }

    Start-Process -FilePath $targetExe
}
finally {
    Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
}
