param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-beta.1'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$releaseRoot = Join-Path $projectRoot 'release'
$packageName = "CodexHomeMover-v$Version-windows"
$stagingRoot = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot ($packageName + '.zip')
$zipChecksumPath = $zipPath + '.sha256'

function Assert-ChildPath([string]$Parent, [string]$Child) {
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe package path: $childFull"
    }
}

function Write-Utf8NoBom([string]$Path, [string[]]$Lines) {
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllLines($Path, $Lines, $encoding)
}

Write-Host 'Running sandbox tests...'
& (Join-Path $projectRoot 'test.ps1')

Write-Host 'Building canonical release executable...'
& (Join-Path $projectRoot 'build.ps1') -Configuration Release -OutputName 'CodexHomeMover.exe'

$executablePath = Join-Path $projectRoot 'dist\CodexHomeMover.exe'
$configPath = $executablePath + '.config'
$requiredInputs = @(
    $executablePath,
    $configPath,
    (Join-Path $projectRoot '使用说明.txt'),
    (Join-Path $projectRoot 'README.md'),
    (Join-Path $projectRoot 'LICENSE'),
    (Join-Path $projectRoot 'ASSET-LICENSE.md'),
    (Join-Path $projectRoot 'PRIVACY.md'),
    (Join-Path $projectRoot 'SECURITY.md'),
    (Join-Path $projectRoot 'CHANGELOG.md'),
    (Join-Path $projectRoot 'CONTRIBUTING.md'),
    (Join-Path $projectRoot 'docs\ui-preview.png'),
    (Join-Path $projectRoot 'docs\success-dialog-preview.png')
)
foreach ($requiredInput in $requiredInputs) {
    if (-not (Test-Path -LiteralPath $requiredInput -PathType Leaf)) {
        throw "Required release file is missing: $requiredInput"
    }
}

$exeBytes = [IO.File]::ReadAllBytes($executablePath)
$asciiImage = [Text.Encoding]::ASCII.GetString($exeBytes)
$unicodeImage = [Text.Encoding]::Unicode.GetString($exeBytes)
$forbiddenMarkers = @('C:\Users\', '\Documents\Codex\', '.pdb')
foreach ($marker in $forbiddenMarkers) {
    if ($asciiImage.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $unicodeImage.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Release executable contains a forbidden private/debug marker: $marker"
    }
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Assert-ChildPath $releaseRoot $stagingRoot
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'docs') -Force | Out-Null

$copyMap = @{
    $executablePath = 'CodexHomeMover.exe'
    $configPath = 'CodexHomeMover.exe.config'
    (Join-Path $projectRoot '使用说明.txt') = '使用说明.txt'
    (Join-Path $projectRoot 'README.md') = 'README.md'
    (Join-Path $projectRoot 'LICENSE') = 'LICENSE'
    (Join-Path $projectRoot 'ASSET-LICENSE.md') = 'ASSET-LICENSE.md'
    (Join-Path $projectRoot 'PRIVACY.md') = 'PRIVACY.md'
    (Join-Path $projectRoot 'SECURITY.md') = 'SECURITY.md'
    (Join-Path $projectRoot 'CHANGELOG.md') = 'CHANGELOG.md'
    (Join-Path $projectRoot 'CONTRIBUTING.md') = 'CONTRIBUTING.md'
    (Join-Path $projectRoot 'docs\ui-preview.png') = 'docs\ui-preview.png'
    (Join-Path $projectRoot 'docs\success-dialog-preview.png') = 'docs\success-dialog-preview.png'
}
foreach ($sourcePath in $copyMap.Keys) {
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $stagingRoot $copyMap[$sourcePath]) -Force
}

$packagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse)
if (@($packagedFiles | Where-Object { $_.Extension -ieq '.exe' }).Count -ne 1) {
    throw 'Release package must contain exactly one executable.'
}
if (@($packagedFiles | Where-Object { $_.Extension -ieq '.pdb' }).Count -ne 0) {
    throw 'Release package must not contain PDB files.'
}

$hashLines = foreach ($file in $packagedFiles | Sort-Object FullName) {
    $relativePath = $file.FullName.Substring($stagingRoot.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    "$hash  $relativePath"
}
Write-Utf8NoBom (Join-Path $stagingRoot 'SHA256SUMS.txt') $hashLines

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $zipChecksumPath) {
    Remove-Item -LiteralPath $zipChecksumPath -Force
}

Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Write-Utf8NoBom $zipChecksumPath @("$zipHash  $([IO.Path]::GetFileName($zipPath))")

Remove-Item -LiteralPath $stagingRoot -Recurse -Force

Write-Host "Release package: $zipPath"
Write-Host "SHA-256: $zipHash"
