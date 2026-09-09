[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repository = 'Danix2308/OpenSuperWhisper-Windows'
$assetName = 'OpenSuperWhisper-Windows.zip'
$checksumName = "$assetName.sha256"
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\OpenSuperWhisper'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("OpenSuperWhisper-" + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $temporaryDirectory $assetName
$checksumPath = Join-Path $temporaryDirectory $checksumName
$stageDirectory = Join-Path $temporaryDirectory 'stage'

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'OpenSuperWhisper requires 64-bit Windows.'
}

try {
    New-Item -ItemType Directory -Path $temporaryDirectory, $stageDirectory -Force | Out-Null

    $releaseBase = "https://github.com/$repository/releases/latest/download"
    Write-Host 'Downloading the latest OpenSuperWhisper release...'
    & curl.exe -L --fail --retry 3 --retry-delay 2 -o $archivePath "$releaseBase/$assetName"
    if ($LASTEXITCODE -ne 0) {
        throw "The release download failed with exit code $LASTEXITCODE"
    }
    & curl.exe -L --fail --retry 3 --retry-delay 2 -o $checksumPath "$releaseBase/$checksumName"
    if ($LASTEXITCODE -ne 0) {
        throw "The checksum download failed with exit code $LASTEXITCODE"
    }

    $checksumText = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
    if ($checksumText -notmatch '^(?<hash>[A-Fa-f0-9]{64})(?:\s+\*?.+)?$') {
        throw 'The published checksum file has an invalid format.'
    }

    $expectedHash = $Matches.hash.ToUpperInvariant()
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Release checksum mismatch. Expected $expectedHash, got $actualHash."
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $stageDirectory -Force
    $stagedExecutable = Join-Path $stageDirectory 'OpenSuperWhisper.Windows.exe'
    if (-not (Test-Path -LiteralPath $stagedExecutable)) {
        throw 'The verified release does not contain OpenSuperWhisper.Windows.exe.'
    }

    Get-Process -Name 'OpenSuperWhisper.Windows' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($installDirectory, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force

    $backupDirectory = "$installDirectory.previous"
    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $installDirectory) {
        Move-Item -LiteralPath $installDirectory -Destination $backupDirectory
    }

    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $installDirectory) -Force | Out-Null
        Move-Item -LiteralPath $stageDirectory -Destination $installDirectory
    }
    catch {
        if ((Test-Path -LiteralPath $backupDirectory) -and -not (Test-Path -LiteralPath $installDirectory)) {
            Move-Item -LiteralPath $backupDirectory -Destination $installDirectory
        }
        throw
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }

    $userShellFolders = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
    $desktop = [Environment]::ExpandEnvironmentVariables($userShellFolders.Desktop)
    $startup = [Environment]::ExpandEnvironmentVariables($userShellFolders.Startup)
    $executable = Join-Path $installDirectory 'OpenSuperWhisper.Windows.exe'
    $settingsCommand = Join-Path $installDirectory 'OpenSuperWhisper.cmd'
    $configureScript = Join-Path $installDirectory 'configure-hotkey.ps1'
    $shell = New-Object -ComObject WScript.Shell

    & $configureScript -NoRestart

    foreach ($shortcutSpec in @(
        @{ Path = (Join-Path $desktop 'OpenSuperWhisper.lnk'); Target = $executable; Arguments = ''; Description = 'OpenSuperWhisper for Windows dictation' },
        @{ Path = (Join-Path $desktop 'OpenSuperWhisper Settings.lnk'); Target = $settingsCommand; Arguments = ''; Description = 'Change the OpenSuperWhisper microphone shortcut in CMD' },
        @{ Path = (Join-Path $startup 'OpenSuperWhisper.lnk'); Target = $executable; Arguments = '--background'; Description = 'Start OpenSuperWhisper in the background' }
    )) {
        $shortcut = $shell.CreateShortcut($shortcutSpec.Path)
        $shortcut.TargetPath = $shortcutSpec.Target
        $shortcut.Arguments = $shortcutSpec.Arguments
        $shortcut.WorkingDirectory = $installDirectory
        $shortcut.Description = $shortcutSpec.Description
        $shortcut.Save()
    }

    Start-Process -FilePath $executable -ArgumentList '--background' -WindowStyle Hidden
    $configuredHotkey = (& $configureScript -ShowCurrent | Select-Object -Last 1)
    Write-Host ''
    Write-Host 'OpenSuperWhisper is installed and running.' -ForegroundColor Green
    Write-Host "Press $configuredHotkey to start recording, then press it again to transcribe."
    Write-Host 'Run OpenSuperWhisper.cmd or the Desktop Settings shortcut to change it anytime.'
    Write-Host "Installed at: $installDirectory"
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
