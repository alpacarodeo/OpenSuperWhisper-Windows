[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $projectDirectory 'dist'
}
$outputDirectory = $OutputDirectory
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $projectDirectory 'OpenSuperWhisper.Windows.cs'
$output = Join-Path $outputDirectory 'OpenSuperWhisper.Windows.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The Windows C# compiler was not found at $compiler"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $compiler `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:x64 `
    /out:$output `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $projectDirectory 'README.md') -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory 'install-windows.ps1') -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory 'configure-hotkey.ps1') -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory 'configure-language.ps1') -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory 'OpenSuperWhisper.cmd') -Destination $outputDirectory -Force
Write-Host "Built: $output"
