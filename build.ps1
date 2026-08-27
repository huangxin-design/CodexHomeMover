param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputName = 'CodexHomeMover.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw '.NET Framework C# compiler was not found.'
}

$outputDirectory = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or [IO.Path]::GetExtension($OutputName) -ne '.exe') {
    throw 'OutputName must be a simple .exe file name.'
}
$outputPath = Join-Path $outputDirectory $OutputName
$stalePdbPath = [IO.Path]::ChangeExtension($outputPath, '.pdb')
if (Test-Path -LiteralPath $stalePdbPath) {
    Remove-Item -LiteralPath $stalePdbPath -Force
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$optimize = if ($Configuration -eq 'Release') { '/optimize+' } else { '/optimize-' }
$debugSymbols = if ($Configuration -eq 'Release') { '/debug-' } else { '/debug:pdbonly' }

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/codepage:65001',
    $optimize,
    $debugSymbols,
    "/out:$outputPath",
    "/win32icon:$(Join-Path $projectRoot 'assets\app-fluent-v2.ico')",
    "/win32manifest:$(Join-Path $projectRoot 'src\app.manifest')",
    "/resource:$(Join-Path $projectRoot 'assets\mascot-fluent-v2.png'),CodexHomeMover.Mascot.png",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Runtime.Serialization.dll'
) + $sources

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'src\CodexHomeMover.exe.config') -Destination "$outputPath.config" -Force
$guideFiles = @(Get-ChildItem -LiteralPath $projectRoot -File -Filter '*.txt')
if ($guideFiles.Count -ne 1) {
    throw 'Expected exactly one user-guide text file in the project root.'
}
Copy-Item -LiteralPath $guideFiles[0].FullName -Destination $outputDirectory -Force
Write-Host "Built: $outputPath"
