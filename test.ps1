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

$testOutput = Join-Path $projectRoot 'tests\CodexHomeMover.CoreTests.exe'
$sources = @(
    (Join-Path $projectRoot 'src\MigrationModels.cs'),
    (Join-Path $projectRoot 'src\NativeMethods.cs'),
    (Join-Path $projectRoot 'src\MigrationEngine.cs'),
    (Join-Path $projectRoot 'tests\CoreTests.cs')
)
$arguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/codepage:65001',
    '/optimize+',
    "/out:$testOutput",
    "/win32manifest:$(Join-Path $projectRoot 'tests\snapshot.manifest')",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Runtime.Serialization.dll'
) + $sources

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Test build failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'src\CodexHomeMover.exe.config') -Destination "$testOutput.config" -Force
& $testOutput
if ($LASTEXITCODE -ne 0) {
    throw "Core tests failed with exit code $LASTEXITCODE."
}
