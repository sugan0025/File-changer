# HOOT - One-click Installer & Desktop Setup for Windows
# Windows PowerShell 5.1: download the complete script before running it; do not pipe curl output directly to iex.
# $p = Join-Path $env:TEMP 'hoot-install.ps1'; curl.exe --ssl-no-revoke -fSL 'https://raw.githubusercontent.com/sugan0025/File-changer/main/install.ps1' -o $p; if ($LASTEXITCODE -ne 0 -or !(Test-Path $p) -or (Get-Item $p).Length -lt 1000) { throw 'HOOT installer download failed or was incomplete.' }; powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p

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
$tempSetup = Join-Path $env:TEMP "Hoot-Setup-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8)).exe"
$stagingDir = Join-Path $env:TEMP "Hoot-Staging-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"
$checksumUrl = $null
$setupAsset = $null
$setupChecksumAsset = $null
$keepTempSetup = $false

function Test-PathUnderDirectory([string]$path, [string]$directory) {
    if ([string]::IsNullOrWhiteSpace($path) -or [string]::IsNullOrWhiteSpace($directory)) { return $false }
    try {
        $fullPath = [System.IO.Path]::GetFullPath($path)
        $fullDirectory = [System.IO.Path]::GetFullPath($directory).TrimEnd('\', '/')
        return $fullPath.Equals($fullDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith($fullDirectory + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith($fullDirectory + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
}

function Get-RegisteredFileeInstall {
    $uninstallKey = '{C8686E06-8327-40A2-B0F7-3DEDD61D9248}_is1'
    $registryKeys = @(
        [pscustomobject]@{ Scope = 'user'; Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKey" },
        [pscustomobject]@{ Scope = 'machine'; Path = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKey" },
        [pscustomobject]@{ Scope = 'machine'; Path = "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKey" }
    )
    $installs = [System.Collections.Generic.List[object]]::new()

    foreach ($registryKey in $registryKeys) {
        $entry = Get-ItemProperty -LiteralPath $registryKey.Path -ErrorAction SilentlyContinue
        if (-not $entry) { continue }

        $folder = $entry.InstallLocation
        if ([string]::IsNullOrWhiteSpace($folder)) { $folder = $entry.'Inno Setup: App Path' }
        if ([string]::IsNullOrWhiteSpace($folder)) { continue }

        try {
            $folder = [Environment]::ExpandEnvironmentVariables($folder.Trim().Trim('"'))
            $folder = [System.IO.Path]::GetFullPath($folder).TrimEnd('\', '/')
        } catch { continue }

        if (-not (Test-Path -LiteralPath (Join-Path $folder 'Hoot.exe')) -and
            -not (Test-Path -LiteralPath (Join-Path $folder 'Filee.exe'))) { continue }

        $identity = "$($registryKey.Scope)|$folder"
        if (-not ($installs | Where-Object { $_.Identity -ieq $identity })) {
            $installs.Add([pscustomobject]@{
                Identity = $identity
                Scope = $registryKey.Scope
                Folder = $folder
            })
        }
    }

    if ($installs.Count -gt 1) {
        return [pscustomobject]@{ Ambiguous = $true; Installs = $installs.ToArray() }
    }
    if ($installs.Count -eq 1) { return $installs[0] }
    return $null
}

function Close-FileeProcesses([string[]]$directories) {
    $running = @(Get-Process -Name 'Hoot', 'Filee' -ErrorAction SilentlyContinue | Where-Object {
        $candidatePath = $_.Path
        if (-not $candidatePath) { return $false }
        foreach ($directory in $directories) {
            if (Test-PathUnderDirectory $candidatePath $directory) { return $true }
        }
        return $false
    })

    foreach ($process in $running) {
        Write-Host "Asking Filee/HOOT to close safely (PID $($process.Id))..." -ForegroundColor Yellow
        try { Start-Process -FilePath $process.Path -ArgumentList '--quit' | Out-Null }
        catch { throw "Could not request a safe shutdown for $($process.Path). No install folders were removed." }

        $deadline = [DateTime]::UtcNow.AddSeconds(40)
        while ((Get-Process -Id $process.Id -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 250
        }
        if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
            throw "Filee/HOOT at $($process.Path) did not close within 40 seconds. No install folders were removed; close it manually and retry."
        }
    }
}
try {
    $registeredInstall = Get-RegisteredFileeInstall
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
                $setupAsset = $release.assets | Where-Object { $_.name -like 'Hoot*win-Setup.exe' } | Select-Object -First 1
                if ($setupAsset) {
                    $setupChecksumName = $setupAsset.name -replace '-win-Setup\.exe$', '-win-SHA256SUMS.txt'
                    $setupChecksumAsset = $release.assets | Where-Object { $_.name -eq $setupChecksumName } | Select-Object -First 1
                }
                $zipAsset = $release.assets | Where-Object { $_.name -like "Hoot*win-Portable.zip" } | Select-Object -First 1
                if ($zipAsset) {
                    $downloadUrl = $zipAsset.browser_download_url
                    $checksumName = $zipAsset.name -replace '-win-Portable\.zip$', '-win-SHA256SUMS.txt'
                    $checksumAsset = $release.assets | Where-Object { $_.name -eq $checksumName } | Select-Object -First 1
                    if ($checksumAsset) { $checksumUrl = $checksumAsset.browser_download_url }
                }
            }
        } catch { }
    }

    # Attempt 2: Query GitHub API using Invoke-RestMethod
    if (-not $downloadUrl) {
        try {
            $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "Hoot-Installer" }
            $latestTag = $release.tag_name
            $setupAsset = $release.assets | Where-Object { $_.name -like 'Hoot*win-Setup.exe' } | Select-Object -First 1
            if ($setupAsset) {
                $setupChecksumName = $setupAsset.name -replace '-win-Setup\.exe$', '-win-SHA256SUMS.txt'
                $setupChecksumAsset = $release.assets | Where-Object { $_.name -eq $setupChecksumName } | Select-Object -First 1
            }
            $zipAsset = $release.assets | Where-Object { $_.name -like "Hoot*win-Portable.zip" } | Select-Object -First 1
            if ($zipAsset) {
                $downloadUrl = $zipAsset.browser_download_url
                $checksumName = $zipAsset.name -replace '-win-Portable\.zip$', '-win-SHA256SUMS.txt'
                $checksumAsset = $release.assets | Where-Object { $_.name -eq $checksumName } | Select-Object -First 1
                if ($checksumAsset) { $checksumUrl = $checksumAsset.browser_download_url }
            }
        } catch {
            Write-Host "Note: GitHub API release check fallback active." -ForegroundColor Gray
        }
    }

    if ($registeredInstall) {
        if ($registeredInstall.Ambiguous) {
            $paths = ($registeredInstall.Installs | ForEach-Object { "[$($_.Scope)] $($_.Folder)" }) -join [Environment]::NewLine
            throw "More than one registered Filee/HOOT install was found, so no files were changed:`n$paths`nRemove the duplicate entry or update each install deliberately."
        }
        if (-not $setupAsset -or -not $setupChecksumAsset) {
            throw "A registered $($registeredInstall.Scope) Filee/HOOT installation was found at $($registeredInstall.Folder), but the latest release does not provide a HOOT Setup and matching checksum. Nothing was changed."
        }

        Write-Host "Found registered $($registeredInstall.Scope) install at $($registeredInstall.Folder). Using HOOT Setup to preserve its install path and scope..." -ForegroundColor Cyan
        $setupDownloaded = $false
        if ($curlExe) {
            try {
                & $curlExe --ssl-no-revoke -sSfL -m 300 $setupAsset.browser_download_url -o $tempSetup 2>$null
                $setupDownloaded = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $tempSetup) -and (Get-Item -LiteralPath $tempSetup).Length -gt 1000000
            } catch { }
        }
        if (-not $setupDownloaded) {
            try {
                Invoke-WebRequest -Uri $setupAsset.browser_download_url -OutFile $tempSetup -UseBasicParsing -TimeoutSec 300
                $setupDownloaded = (Test-Path -LiteralPath $tempSetup) -and (Get-Item -LiteralPath $tempSetup).Length -gt 1000000
            } catch { }
        }
        if (-not $setupDownloaded) { throw "Could not download the verified HOOT Setup for the registered install at $($registeredInstall.Folder). Nothing was changed." }

        $checksumDownloaded = $false
        $checksumPath = "$tempSetup.sha256sums.txt"
        if ($setupChecksumAsset.browser_download_url) {
            try {
                if ($curlExe) {
                    & $curlExe --ssl-no-revoke -sSfL -m 30 $setupChecksumAsset.browser_download_url -o $checksumPath 2>$null
                    $checksumDownloaded = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $checksumPath)
                } else {
                    Invoke-WebRequest -Uri $setupChecksumAsset.browser_download_url -OutFile $checksumPath -UseBasicParsing -TimeoutSec 30
                    $checksumDownloaded = Test-Path -LiteralPath $checksumPath
                }
            } catch { }
        }
        if (-not $checksumDownloaded) { throw 'The release checksum manifest could not be downloaded. Nothing was changed.' }

        $expectedLine = Get-Content -LiteralPath $checksumPath | Where-Object { $_ -match ('\s' + [regex]::Escape($setupAsset.name) + '$') } | Select-Object -First 1
        if (-not $expectedLine -or $expectedLine -notmatch '^\s*([0-9a-fA-F]{64})\s+') {
            throw "The release checksum manifest has no valid SHA-256 entry for $($setupAsset.name). Nothing was changed."
        }
        $expectedHash = $Matches[1]
        $actualHash = (Get-FileHash -LiteralPath $tempSetup -Algorithm SHA256).Hash
        if ($actualHash -ine $expectedHash) { throw "HOOT Setup failed SHA-256 verification. Nothing was changed." }
        Write-Host 'HOOT Setup SHA-256 verification succeeded.' -ForegroundColor Green

        $setupArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AUTOMATICUPDATE'
        if ($registeredInstall.Scope -eq 'user') { $setupArgs += ' /CURRENTUSER' } else { $setupArgs += ' /ALLUSERS' }
        Write-Host 'Starting the supported in-place upgrade. Machine-wide installs will request administrator approval through Windows UAC.' -ForegroundColor Yellow
        $setupProcess = Start-Process -FilePath $tempSetup -ArgumentList $setupArgs -PassThru
        if (-not $setupProcess.WaitForExit(900000)) {
            $keepTempSetup = $true
            Write-Warning "HOOT Setup is still running. Its verified installer is retained at $tempSetup; do not delete it until Setup exits."
            return
        }
        if ($setupProcess.ExitCode -ne 0) {
            throw "HOOT Setup exited with code $($setupProcess.ExitCode). Setup's in-place upgrade and rollback handling has been allowed to finish."
        }
        Write-Host "HOOT Setup completed for $($registeredInstall.Scope) install at $($registeredInstall.Folder)." -ForegroundColor Green
        return
    }

    # Candidate download URLs (API discovery -> releases/latest -> direct releases)
    $candidateUrls = [System.Collections.Generic.List[string]]::new()
    if ($downloadUrl) { $candidateUrls.Add($downloadUrl) }
    $candidateUrls.Add("https://github.com/$repo/releases/latest/download/Hoot-win-Portable.zip")

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
        throw "Failed to download the HOOT portable package. No Filee package was used as a fallback. Check your connection and confirm the latest release contains a Hoot*-win-Portable.zip asset."
    }

    Write-Host "Download successful ($downloadUrl)!" -ForegroundColor Green

    # Verify the archive against the matching release manifest when the release provides one.
    $checksumUrls = [System.Collections.Generic.List[string]]::new()
    if ($checksumUrl) { $checksumUrls.Add($checksumUrl) }
    if ($latestTag) {
        $releaseVersion = $latestTag.TrimStart('v', 'V')
        $releaseUrl = "https://github.com/$repo/releases/download/$latestTag"
        $checksumUrls.Add("$releaseUrl/HOOT-$releaseVersion-win-SHA256SUMS.txt")
    }

    $checksumManifest = $null
    foreach ($url in $checksumUrls | Select-Object -Unique) {
        try {
            $response = Invoke-WebRequest -Uri $url -Headers @{ "User-Agent" = "Hoot-Installer" } -UseBasicParsing -TimeoutSec 15
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                $checksumManifest = $response.Content
                break
            }
        } catch { }
    }

    if ($checksumManifest) {
        $archiveName = [System.IO.Path]::GetFileName(([System.Uri]$downloadUrl).AbsolutePath)
        $expectedHash = $null
        foreach ($line in ($checksumManifest -split "`r?`n")) {
            if ($line -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$' -and
                [System.IO.Path]::GetFileName($Matches[2]) -ieq $archiveName) {
                $expectedHash = $Matches[1]
                break
            }
        }
        if (-not $expectedHash) {
            throw "The release checksum manifest does not contain an entry for $archiveName."
        }

        $actualHash = (Get-FileHash -LiteralPath $tempZip -Algorithm SHA256).Hash
        if ($actualHash -ine $expectedHash) {
            throw "The downloaded archive failed SHA-256 verification. Expected $expectedHash; received $actualHash."
        }
        Write-Host "SHA-256 verification succeeded." -ForegroundColor Green
    } else {
        Write-Warning "No release SHA256SUMS.txt was available; continuing with the HTTPS download."
    }

    Write-Host "Extracting files to $installDir..." -ForegroundColor Cyan
    try {
        Expand-Archive -Path $tempZip -DestinationPath $stagingDir -Force
    } catch {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($tempZip, $stagingDir)
    }

    # Locate Hoot.exe inside the HOOT payload
    $payloadDir = $stagingDir
    if (-not (Test-Path (Join-Path $payloadDir "Hoot.exe"))) {
        $nested = Get-ChildItem -Path $stagingDir -Recurse -Filter "Hoot.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($nested) {
            $payloadDir = $nested.DirectoryName
        }
    }

    if (-not (Test-Path (Join-Path $payloadDir "Hoot.exe"))) {
        throw "Download succeeded, but Hoot.exe was not found inside the HOOT zip archive. No files were installed."
    }

    # Do not disrupt the installed copy until the archive has downloaded, verified (when a manifest is available),
    # extracted, and its application executable has been found.
    # Only close processes whose executable is inside one of the known install directories.
    Close-FileeProcesses @($installDir, $legacyDir)

    # Clean up the legacy folder only after the replacement payload is ready.
    if (Test-Path $legacyDir) {
        $legacyExe = Join-Path $legacyDir 'Hoot.exe'
        if (-not (Test-Path -LiteralPath $legacyExe)) { $legacyExe = Join-Path $legacyDir 'Filee.exe' }
        if (Test-Path -LiteralPath $legacyExe) {
            Close-FileeProcesses @($legacyDir)
            Remove-Item $legacyDir -Recurse -Force
        } else {
            Write-Warning "A folder exists at $legacyDir but it contains no HOOT/Filee executable. Leaving it untouched."
        }
    }

    New-Item -ItemType Directory -Force -Path $installDir | Out-Null

    # Copy payload to install directory
    Copy-Item -Path (Join-Path $payloadDir "*") -Destination $installDir -Recurse -Force

    # Mark as installed copy so automatic silent in-place updating is enabled
    Set-Content -Path (Join-Path $installDir ".installed") -Value "installed" -Force

    # Launch only the HOOT executable.
    $targetExe = Join-Path $installDir "Hoot.exe"

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
    if (-not $keepTempSetup) { Remove-Item $tempSetup -Force -ErrorAction SilentlyContinue }
}
