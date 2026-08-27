param(
    [ValidateRange(80, 200)]
    [int]$Zoom = 100,
    [switch]$SuccessDialog
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

$artifacts = Join-Path $projectRoot 'tests\artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$snapshotExecutable = Join-Path $artifacts 'UiSnapshot.exe'
$snapshotImage = Join-Path $artifacts $(if ($SuccessDialog) { 'success-dialog-preview.png' } else { 'ui-preview.png' })
$sources = @(
    (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' |
        Where-Object { $_.Name -ne 'Program.cs' } |
        Select-Object -ExpandProperty FullName)
    (Join-Path $projectRoot 'tests\UiSnapshot.cs')
)

$arguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/codepage:65001',
    "/out:$snapshotExecutable",
    "/win32manifest:$(Join-Path $projectRoot 'tests\snapshot.manifest')",
    "/resource:$(Join-Path $projectRoot 'assets\mascot-fluent-v2.png'),CodexHomeMover.Mascot.png",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Runtime.Serialization.dll'
) + $sources

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Snapshot build failed with exit code $LASTEXITCODE."
}

if ($SuccessDialog) {
    & $snapshotExecutable $snapshotImage 'success'
}
else {
    & $snapshotExecutable $snapshotImage $Zoom
}
if ($LASTEXITCODE -ne 0) {
    throw "Snapshot render failed with exit code $LASTEXITCODE."
}
