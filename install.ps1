# HOOT - One-click Installer & Desktop Setup for Windows
# Windows PowerShell 5.1: download the complete script before running it; do not pipe curl output directly to iex.
# $p = Join-Path $env:TEMP 'hoot-install.ps1'; curl.exe --ssl-no-revoke -fSL 'https://raw.githubusercontent.com/sugan0025/HOOT/main/install.ps1' -o $p; if ($LASTEXITCODE -ne 0 -or !(Test-Path $p) -or (Get-Item $p).Length -lt 1000) { throw 'HOOT installer download failed or was incomplete.' }; powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this command in a normal PowerShell window for the intended user. Only old machine-install removal requests administrator approval.'
}


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
$installParent = Split-Path -Parent $installDir
$installStageDir = Join-Path $installParent ".Hoot-Staging-$([System.Guid]::NewGuid().ToString('N'))"
$installBackupDir = Join-Path $installParent ".Hoot-Backup-$([System.Guid]::NewGuid().ToString('N'))"
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

    $repo = "sugan0025/HOOT"
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

    & {
        if ($registeredInstall -and $registeredInstall.Ambiguous) {
            $paths = ($registeredInstall.Installs | ForEach-Object { "[$($_.Scope)] $($_.Folder)" }) -join [Environment]::NewLine
            throw "More than one registered Filee/HOOT install was found, so no files were changed:`n$paths`nRemove the duplicate entry or update each install deliberately."
        }
        if (-not $setupAsset -or -not $setupChecksumAsset) {
            throw "A registered $($registeredInstall.Scope) Filee/HOOT installation was found at $($registeredInstall.Folder), but the latest release does not provide a HOOT Setup and matching checksum. Nothing was changed."
        }

        Write-Host "Installing HOOT for the current user in $installDir..." -ForegroundColor Cyan
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

        # Always use registered per-user Setup, including first installs.
        $target = [IO.Path]::GetFullPath($installDir)
        if ($registeredInstall -and $registeredInstall.Scope -eq 'user' -and
            -not $registeredInstall.Folder.Equals($target, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Existing per-user installation is at $($registeredInstall.Folder). Setup preserves custom paths; migrate it explicitly before using this command. Nothing was removed."
        }
        $targetRecord = Join-Path $target 'uninstall.json'
        if (Test-Path -LiteralPath $targetRecord) {
            $existingTarget = Get-Content -LiteralPath $targetRecord -Raw | ConvertFrom-Json
            if ([version]$existingTarget.version -gt [version]$latestTag.TrimStart('v','V')) {
                throw "Published release $latestTag is older than the per-user installation. Nothing was changed."
            }
        }
        $migration = $registeredInstall -and $registeredInstall.Scope -eq 'machine'
        $oldUninstaller = $null
        if ($migration) {
            $knownMachineFolders = @(
                (Join-Path $env:ProgramFiles 'Filee'),
                (Join-Path $env:ProgramFiles 'Hoot')
            )
            if ($knownMachineFolders -notcontains $registeredInstall.Folder) {
                throw "Custom machine installation at $($registeredInstall.Folder) requires explicit migration. Nothing was removed."
            }
            $oldUninstaller = Join-Path $registeredInstall.Folder 'uninstall.exe'
            $record = Join-Path $registeredInstall.Folder 'uninstall.json'
            if (!(Test-Path -LiteralPath $oldUninstaller) -or !(Test-Path -LiteralPath $record)) {
                throw 'The old installation has no supported uninstall record. Nothing was removed.'
            }
            $oldRecord = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
            if ($oldRecord.scope -ne 'machine') { throw 'Installation scope mismatch. Nothing was removed.' }
            if ([version]$oldRecord.version -gt [version]$latestTag.TrimStart('v','V')) {
                throw "Published release $latestTag is older than installed $($oldRecord.version). Wait for the newer release; nothing was changed."
            }
        }

        function Invoke-HootSetup([string]$exe, [string]$arguments) {
            $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
            if (-not $process.WaitForExit(900000)) {
                throw "Setup is still running. Retain the installer and migration backup; do not start another installer."
            }
            if ($process.ExitCode -ne 0) { throw "Setup failed with exit code $($process.ExitCode)." }
        }
        $setupArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOLAUNCH /DIR="' + $target + '"'
        # Keep downloaded installer available for repair if migration is interrupted.
        Invoke-HootSetup $tempSetup $setupArgs
        $installed = Join-Path $target 'Hoot.exe'
        if (!(Test-Path -LiteralPath $installed) -or !(Test-Path -LiteralPath (Join-Path $target 'uninstall.json'))) {
            throw 'Per-user Setup did not produce a complete installation. The old installation was not removed.'
        }
        $newRecord = Get-Content -LiteralPath (Join-Path $target 'uninstall.json') -Raw | ConvertFrom-Json
        if ($newRecord.scope -ne 'user') { throw 'Per-user installation validation failed.' }
        $expectedVersion = $latestTag.TrimStart('v','V')
        if ($newRecord.version -ne $expectedVersion) {
            throw "Installed version $($newRecord.version) differs from release $expectedVersion. Old install retained."
        }

        if ($migration) {
            Close-FileeProcesses @($registeredInstall.Folder, $target)
            $backup = Join-Path $env:LOCALAPPDATA ('Hoot-Migration-Backup-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $backup | Out-Null
            $protected = @(
                (Join-Path $env:APPDATA 'Filee'),
                (Join-Path $env:LOCALAPPDATA 'Filee\engines')
            )
            $saved = @()
            foreach ($source in $protected) {
                if (Test-Path -LiteralPath $source) {
                    $destination = Join-Path $backup ([string]$saved.Count)
                    Copy-Item -LiteralPath $source -Destination $destination -Recurse -ErrorAction Stop
                    Get-ChildItem -LiteralPath $source -Recurse -File -Force | ForEach-Object {
                        $relative = $_.FullName.Substring($source.Length).TrimStart('\')
                        $copy = Join-Path $destination $relative
                        if (!(Test-Path -LiteralPath $copy) -or
                            (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) {
                            throw "Backup verification failed for $relative. Old installation retained."
                        }
                    }
                    $saved += [pscustomobject]@{ Source=$source; Backup=$destination }
                }
            }
            Write-Host "Migration recovery data retained at $backup"
            try {
                # No delete-settings flag: preserve user data. Windows requests elevation for this step.
                Invoke-HootSetup $oldUninstaller '/UNINSTALL /VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
            } finally {
                foreach ($entry in $saved) {
                    New-Item -ItemType Directory -Path $entry.Source -Force | Out-Null
                    Get-ChildItem -LiteralPath $entry.Backup -Force | ForEach-Object {
                        Copy-Item -LiteralPath $_.FullName -Destination $entry.Source -Recurse -Force -ErrorAction Stop
                    }
                }
                # Old cleanup removes shared integration; restore it without launching a second instance.
                Invoke-HootSetup $tempSetup $setupArgs
            }
            if ((Test-Path -LiteralPath (Join-Path $registeredInstall.Folder 'Hoot.exe')) -or
                (Test-Path -LiteralPath (Join-Path $registeredInstall.Folder 'Filee.exe'))) {
                throw 'Old executable remains. New per-user HOOT is installed; cleanup needs review.'
            }
        }
        Start-Process -FilePath $installed -ArgumentList '--background' -WindowStyle Hidden
        Write-Host "HOOT installed for this user at $target. Automatic update checks are available." -ForegroundColor Green
    }
    return


}
finally {
    Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $installStageDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
    Remove-Item "$tempZip.sha256sums.txt" -Force -ErrorAction SilentlyContinue
    # Retain the verified Setup for migration recovery.
}
