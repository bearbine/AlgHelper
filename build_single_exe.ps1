$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $projectDir "AlgHelper.csproj"
[xml]$project = Get-Content -LiteralPath $projectPath
$version = $project.Project.PropertyGroup.Version
if (-not $version) {
    $version = "local"
}

dotnet publish $projectPath -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$publishExe = Join-Path $projectDir "bin\Release\net8.0-windows\win-x64\publish\AlgHelper.exe"
$releaseDir = Join-Path $projectDir "release"
$releaseExe = Join-Path $releaseDir "AlgHelper-$version.exe"

if (-not (Test-Path -LiteralPath $publishExe)) {
    throw "Publish output was not found: $publishExe"
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Copy-Item -LiteralPath $publishExe -Destination $releaseExe -Force

Write-Host ""
Write-Host "Gotowe: $releaseExe"
Write-Host "To jest zwykly plik .exe, bez ZIP-a i bez wyodrebniania."
