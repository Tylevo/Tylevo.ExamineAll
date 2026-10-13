[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SptPath)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$clientRoot=(Resolve-Path -LiteralPath $SptPath).Path
$gameAssembly=Join-Path $clientRoot 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'
$expectedGameHash='EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436'
if((Get-FileHash -LiteralPath $gameAssembly -Algorithm SHA256).Hash -ne $expectedGameHash) { throw 'This test build targets the inspected EFT 40743 assembly.' }
$previousRoot=$env:TEA_SPT_ROOT
try {
    $env:TEA_SPT_ROOT=$clientRoot
    & dotnet run --project (Join-Path $projectRoot 'tests\ExamineAll.Tests.csproj') -c Release
    if($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & dotnet build (Join-Path $projectRoot 'src\Tylevo.ExamineAll.csproj') -c Release --verbosity minimal
    if($LASTEXITCODE -ne 0) { throw 'Client compilation failed.' }
    $output=Join-Path $projectRoot 'dist\BepInEx\plugins\Tylevo.ExamineAll'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\bin\Release\Tylevo.ExamineAll.dll') -Destination $output
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $output
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $output
    Write-Output ('Built test files in '+$output+'. Nothing installed.')
} finally { $env:TEA_SPT_ROOT=$previousRoot }
