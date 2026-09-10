[CmdletBinding()]
param(
    [ValidateSet('auto', 'cuda', 'cpu')]
    [string]$Engine = 'auto',
    # For build machines without a GPU (CI, repackaging): download the CUDA
    # engine without requiring a local NVIDIA driver.
    [switch]$SkipGpuCheck
)

$ErrorActionPreference = 'Stop'

$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDirectory = Join-Path $projectDirectory 'dist'
$downloadsDirectory = Join-Path $projectDirectory '.downloads'
$whisperDestination = Join-Path $distDirectory 'whisper'
$modelDirectory = Join-Path $distDirectory 'models'
$modelPath = Join-Path $modelDirectory 'ggml-base.bin'

$whisperReleaseBaseUrl = 'https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.1'
$engineArchives = @{
    cpu = @{
        ArchiveName          = 'whisper-bin-x64.zip'
        Url                  = "$whisperReleaseBaseUrl/whisper-bin-x64.zip"
        ExpectedSha256       = '7D8BE46ECD31828E1EB7A2ECDD0D6B314FEAFD82163038AB6092594B0A063539'
        MinimumDriverVersion = $null
    }
    cuda = @{
        ArchiveName          = 'whisper-cublas-12.4.0-bin-x64.zip'
        Url                  = "$whisperReleaseBaseUrl/whisper-cublas-12.4.0-bin-x64.zip"
        ExpectedSha256       = '106A2030EFF8998E4EF320FE72E263A78449E9040386EE27C41EA80B001B601B'
        # CUDA 12.4 requires at least driver 551.61 on Windows.
        MinimumDriverVersion = [version]'551.61'
    }
}

$modelUrl = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true'
$expectedModelSha256 = '60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE'

function Get-NvidiaDriverVersion {
    $candidates = @()
    $pathCandidate = Get-Command 'nvidia-smi' -ErrorAction SilentlyContinue
    if ($pathCandidate) {
        $candidates += $pathCandidate.Source
    }
    $candidates += Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'

    foreach ($candidate in $candidates) {
        if (-not $candidate -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        try {
            $output = & $candidate --query-gpu=driver_version --format=csv,noheader 2>$null
            if ($LASTEXITCODE -eq 0 -and $output) {
                $version = $null
                $line = ($output | Select-Object -First 1).Trim()
                if ([version]::TryParse($line, [ref]$version)) {
                    return $version
                }
            }
        }
        catch {
            # A broken driver install counts as no usable driver; try the next candidate.
        }
    }

    return $null
}

function Resolve-Engine {
    if ($Engine -eq 'cpu') {
        return 'cpu'
    }

    if ($SkipGpuCheck) {
        if ($Engine -ne 'cuda') {
            throw 'SkipGpuCheck requires -Engine cuda.'
        }
        Write-Host 'Skipping NVIDIA detection (-SkipGpuCheck); downloading the CUDA whisper.cpp build.'
        return 'cuda'
    }

    $driverVersion = Get-NvidiaDriverVersion
    if ($null -eq $driverVersion) {
        if ($Engine -eq 'cuda') {
            throw 'Engine cuda was requested, but no NVIDIA driver with nvidia-smi was found.'
        }
        Write-Host 'No NVIDIA driver detected; using the CPU whisper.cpp build.'
        return 'cpu'
    }

    $minimumDriver = $engineArchives.cuda.MinimumDriverVersion
    if ($driverVersion -lt $minimumDriver) {
        if ($Engine -eq 'cuda') {
            throw "Engine cuda was requested, but driver $driverVersion is older than the minimum $minimumDriver required by the CUDA build."
        }
        Write-Host "NVIDIA driver $driverVersion is too old for the CUDA build (minimum $minimumDriver); using the CPU whisper.cpp build."
        return 'cpu'
    }

    Write-Host "NVIDIA driver $driverVersion detected; using the CUDA (GPU-accelerated) whisper.cpp build."
    return 'cuda'
}

$selectedEngine = Resolve-Engine
$archive = $engineArchives[$selectedEngine]
$whisperZip = Join-Path $downloadsDirectory $archive.ArchiveName
$whisperExtract = Join-Path $downloadsDirectory ($archive.ArchiveName -replace '\.zip$', '')

New-Item -ItemType Directory -Path $downloadsDirectory, $distDirectory, $modelDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $whisperZip)) {
    Write-Host "Downloading whisper.cpp $selectedEngine build for Windows..."
    & curl.exe -L --fail --retry 3 --retry-delay 2 -o $whisperZip $archive.Url
    if ($LASTEXITCODE -ne 0) {
        throw "whisper.cpp download failed with exit code $LASTEXITCODE"
    }
}

$actualWhisperSha256 = (Get-FileHash -LiteralPath $whisperZip -Algorithm SHA256).Hash
if ($actualWhisperSha256 -ne $archive.ExpectedSha256) {
    throw "whisper.cpp archive hash mismatch. Expected $($archive.ExpectedSha256), got $actualWhisperSha256"
}

if (Test-Path -LiteralPath $whisperExtract) {
    Remove-Item -LiteralPath $whisperExtract -Recurse -Force
}
New-Item -ItemType Directory -Path $whisperExtract -Force | Out-Null
Expand-Archive -LiteralPath $whisperZip -DestinationPath $whisperExtract -Force

$releaseDirectory = Join-Path $whisperExtract 'Release'
if (-not (Test-Path -LiteralPath (Join-Path $releaseDirectory 'whisper-cli.exe'))) {
    throw 'whisper-cli.exe was not found in the verified release archive.'
}

if (Test-Path -LiteralPath $whisperDestination) {
    Remove-Item -LiteralPath $whisperDestination -Recurse -Force
}
Copy-Item -LiteralPath $releaseDirectory -Destination $whisperDestination -Recurse

if (-not (Test-Path -LiteralPath $modelPath)) {
    Write-Host 'Downloading the multilingual Whisper base model (about 142 MiB)...'
    & curl.exe -L --fail --retry 3 --retry-delay 2 -o $modelPath $modelUrl
    if ($LASTEXITCODE -ne 0) {
        throw "Whisper model download failed with exit code $LASTEXITCODE"
    }
}

$actualModelSha256 = (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash
if ($actualModelSha256 -ne $expectedModelSha256) {
    throw "Whisper model hash mismatch. Expected $expectedModelSha256, got $actualModelSha256"
}

& (Join-Path $projectDirectory 'build-windows.ps1')
Write-Host ''
Write-Host "Selected engine: $selectedEngine"
Write-Host "Windows app ready: $(Join-Path $distDirectory 'OpenSuperWhisper.Windows.exe')"
