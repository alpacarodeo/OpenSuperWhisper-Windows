[CmdletBinding()]
param(
    [string]$Language,
    [switch]$ShowCurrent,
    [switch]$ValidateOnly,
    [switch]$NoRestart,
    [string]$ApplicationDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $ApplicationDirectory) {
    $ApplicationDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$applicationDirectory = $ApplicationDirectory
$executable = Join-Path $applicationDirectory 'OpenSuperWhisper.Windows.exe'
$configurationDirectory = if ($env:OPENSUPERWHISPER_CONFIG_DIR) {
    $env:OPENSUPERWHISPER_CONFIG_DIR
}
else {
    Join-Path $env:LOCALAPPDATA 'OpenSuperWhisper'
}
$configurationPath = Join-Path $configurationDirectory 'language.txt'
$defaultLanguage = 'en'

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "OpenSuperWhisper.Windows.exe was not found at $executable"
}

function Get-CurrentLanguage {
    if (Test-Path -LiteralPath $configurationPath -PathType Leaf) {
        $configured = (Get-Content -LiteralPath $configurationPath -Raw).Trim()
        if ($configured) {
            return $configured
        }
    }
    return $defaultLanguage
}

function Test-Language([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $false
    }

    $validation = Start-Process `
        -FilePath $executable `
        -ArgumentList ("--validate-language=" + $Candidate.Trim()) `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    return $validation.ExitCode -eq 0
}

if ($ShowCurrent) {
    Write-Output (Get-CurrentLanguage)
    return
}

$interactive = -not $PSBoundParameters.ContainsKey('Language')
$currentLanguage = Get-CurrentLanguage

if ($interactive) {
    Write-Host ''
    Write-Host 'OpenSuperWhisper dictation language' -ForegroundColor Cyan
    Write-Host 'Enter the language you dictate in, so transcription skips language detection.'
    Write-Host 'Examples: en, de, es, fr, ja, auto'
    Write-Host 'Use a language code, a name such as English, or auto to detect each time.'
    Write-Host ''
}

while ($true) {
    if ($interactive) {
        $candidate = Read-Host "Language [$currentLanguage]"
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            $candidate = $currentLanguage
        }
    }
    else {
        $candidate = $Language
    }

    $candidate = $candidate.Trim()
    if (Test-Language $candidate) {
        break
    }

    if (-not $interactive) {
        throw "Unsupported language '$candidate'. Example: en or auto"
    }

    Write-Host "'$candidate' is not supported. Try a code such as en, de, or auto." -ForegroundColor Yellow
}

if ($ValidateOnly) {
    Write-Output $candidate
    return
}

New-Item -ItemType Directory -Path $configurationDirectory -Force | Out-Null
$utf8WithoutBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($configurationPath, $candidate, $utf8WithoutBom)
Write-Host "Language saved: $candidate" -ForegroundColor Green

if (-not $NoRestart) {
    & taskkill.exe /IM 'OpenSuperWhisper.Windows.exe' /F 2>$null | Out-Null
    Start-Sleep -Milliseconds 400
    Start-Process -FilePath $executable -ArgumentList '--background' -WindowStyle Hidden
    Write-Host 'OpenSuperWhisper restarted. The new language is active.' -ForegroundColor Green
}
