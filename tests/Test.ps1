$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$requiredFiles = @(
    'OpenSuperWhisper.Windows.cs',
    'build-windows.ps1',
    'setup-windows.ps1',
    'get-opensuperwhisper.ps1',
    'configure-hotkey.ps1',
    'configure-language.ps1',
    'OpenSuperWhisper.cmd',
    'uninstall.ps1',
    'README.md',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md'
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $relativePath"
    }
}

$parseFailures = @()
Get-ChildItem -LiteralPath $repositoryRoot -Recurse -Filter '*.ps1' |
    Where-Object { $_.FullName -notlike '*\.downloads\*' -and $_.FullName -notlike '*\dist\*' } |
    ForEach-Object {
        $tokens = $null
        $errors = $null
        [Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count -gt 0) {
            $parseFailures += "$($_.FullName): $($errors -join '; ')"
        }
    }
if ($parseFailures.Count -gt 0) {
    throw ($parseFailures -join [Environment]::NewLine)
}

$trackedText = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File |
    Where-Object {
        $_.FullName -notlike '*\.git\*' -and
        $_.FullName -notlike '*\.downloads\*' -and
        $_.FullName -notlike '*\dist\*' -and
        $_.Extension -in '.cs', '.ps1', '.md', '.yml', '.yaml', '.txt'
    } |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
if (($trackedText -join "`n") -match 'C:\\Users\\') {
    throw 'A user-specific absolute path was found in publishable files.'
}

$source = Get-Content -LiteralPath (Join-Path $repositoryRoot 'OpenSuperWhisper.Windows.cs') -Raw
foreach ($expectedText in @('HotkeyDefinition', 'LanguageDefinition', 'WhisperServer', '--validate-hotkey=', '--validate-language=', 'hotkey.txt', 'language.txt', 'RegisterHotKey', 'whisper-cli.exe', 'whisper-server.exe', 'ggml-base.bin')) {
    if (-not $source.Contains($expectedText)) {
        throw "Expected implementation marker is missing: $expectedText"
    }
}

$testBuildDirectory = Join-Path ([IO.Path]::GetTempPath()) ("OpenSuperWhisper-Test-" + [Guid]::NewGuid().ToString('N'))
try {
    & (Join-Path $repositoryRoot 'build-windows.ps1') -OutputDirectory $testBuildDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }

    $builtExecutable = Join-Path $testBuildDirectory 'OpenSuperWhisper.Windows.exe'
    if (-not (Test-Path -LiteralPath $builtExecutable -PathType Leaf)) {
        throw 'Build did not create OpenSuperWhisper.Windows.exe.'
    }

    foreach ($validHotkey in @('Shift+|', 'Ctrl+Alt+M', 'Alt+F8', 'Ctrl+Shift+Space', 'Win+Slash')) {
        $validation = Start-Process -FilePath $builtExecutable -ArgumentList ("--validate-hotkey=$validHotkey") -WindowStyle Hidden -Wait -PassThru
        if ($validation.ExitCode -ne 0) {
            throw "Valid hotkey was rejected: $validHotkey"
        }
    }

    foreach ($invalidHotkey in @('M', 'Ctrl+', 'Ctrl+Ctrl+M', 'Ctrl+Mouse4')) {
        $validation = Start-Process -FilePath $builtExecutable -ArgumentList ("--validate-hotkey=$invalidHotkey") -WindowStyle Hidden -Wait -PassThru
        if ($validation.ExitCode -eq 0) {
            throw "Invalid hotkey was accepted: $invalidHotkey"
        }
    }

    foreach ($validLanguage in @('en', 'auto', 'de', 'English', 'Portuguese')) {
        $validation = Start-Process -FilePath $builtExecutable -ArgumentList ("--validate-language=$validLanguage") -WindowStyle Hidden -Wait -PassThru
        if ($validation.ExitCode -ne 0) {
            throw "Valid language was rejected: $validLanguage"
        }
    }

    foreach ($invalidLanguage in @('xx', 'Klingon', 'Englishx')) {
        $validation = Start-Process -FilePath $builtExecutable -ArgumentList ("--validate-language=$invalidLanguage") -WindowStyle Hidden -Wait -PassThru
        if ($validation.ExitCode -eq 0) {
            throw "Invalid language was accepted: $invalidLanguage"
        }
    }

    $configureScript = Join-Path $repositoryRoot 'configure-hotkey.ps1'
    $validated = & $configureScript -Hotkey 'Ctrl+Alt+M' -ValidateOnly -ApplicationDirectory $testBuildDirectory
    if (($validated | Select-Object -Last 1) -ne 'Ctrl+Alt+M') {
        throw 'The CMD hotkey configuration helper did not validate Ctrl+Alt+M.'
    }

    $languageScript = Join-Path $repositoryRoot 'configure-language.ps1'
    $validatedLanguage = & $languageScript -Language 'en' -ValidateOnly -ApplicationDirectory $testBuildDirectory
    if (($validatedLanguage | Select-Object -Last 1) -ne 'en') {
        throw 'The CMD language configuration helper did not validate en.'
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'configure-hotkey.ps1') -Destination $testBuildDirectory -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'configure-language.ps1') -Destination $testBuildDirectory -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'OpenSuperWhisper.cmd') -Destination $testBuildDirectory -Force
    $previousLocalAppData = $env:LOCALAPPDATA
    $previousConfigurationDirectory = $env:OPENSUPERWHISPER_CONFIG_DIR
    try {
        $env:LOCALAPPDATA = Join-Path $testBuildDirectory 'AppData'
        $env:OPENSUPERWHISPER_CONFIG_DIR = Join-Path $testBuildDirectory 'AppData\OpenSuperWhisper'
        $cmdOutput = & $env:ComSpec /d /c call (Join-Path $testBuildDirectory 'OpenSuperWhisper.cmd') show 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $cmdOutput -notmatch 'Shift\+\|') {
            throw "The CMD settings command failed: $cmdOutput"
        }

        & (Join-Path $testBuildDirectory 'configure-hotkey.ps1') -Hotkey 'Alt+F8' -NoRestart
        $savedHotkey = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'OpenSuperWhisper\hotkey.txt') -Raw
        if ($savedHotkey -ne 'Alt+F8') {
            throw "The shortcut was not saved correctly: $savedHotkey"
        }

        $configuredCheck = Start-Process -FilePath $builtExecutable -ArgumentList '--expect-configured-hotkey=Alt+F8' -WindowStyle Hidden -Wait -PassThru
        if ($configuredCheck.ExitCode -ne 0) {
            throw 'The Windows app did not load the shortcut saved by the CMD configuration helper.'
        }

        & (Join-Path $testBuildDirectory 'configure-language.ps1') -Language 'de' -NoRestart
        $savedLanguage = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'OpenSuperWhisper\language.txt') -Raw
        if ($savedLanguage -ne 'de') {
            throw "The language was not saved correctly: $savedLanguage"
        }

        $configuredLanguageCheck = Start-Process -FilePath $builtExecutable -ArgumentList '--expect-configured-language=de' -WindowStyle Hidden -Wait -PassThru
        if ($configuredLanguageCheck.ExitCode -ne 0) {
            throw 'The Windows app did not load the language saved by the CMD configuration helper.'
        }

        $changedOutput = & $env:ComSpec /d /c call (Join-Path $testBuildDirectory 'OpenSuperWhisper.cmd') show 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $changedOutput -notmatch 'Alt\+F8') {
            throw "The CMD settings command did not show the changed shortcut: $changedOutput"
        }

        $menuOutput = '7' | & $env:ComSpec /d /c call (Join-Path $testBuildDirectory 'OpenSuperWhisper.cmd') 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $menuOutput -notmatch 'Current dictation language:') {
            throw "The CMD settings menu did not show the dictation language: $menuOutput"
        }
    }
    finally {
        $env:LOCALAPPDATA = $previousLocalAppData
        $env:OPENSUPERWHISPER_CONFIG_DIR = $previousConfigurationDirectory
    }
}
finally {
    if (Test-Path -LiteralPath $testBuildDirectory) {
        Remove-Item -LiteralPath $testBuildDirectory -Recurse -Force
    }
}

Write-Host 'All repository checks passed.' -ForegroundColor Green
