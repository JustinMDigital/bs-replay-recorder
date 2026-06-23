param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$projectPath = Join-Path $repoRoot "src\BSAutoReplayRecorder.RootLauncher\BSAutoReplayRecorder.RootLauncher.csproj"
$targetExe = Join-Path $repoRoot "Replay Recorder.exe"
$publishRoot = Join-Path $repoRoot "tmp\root-launcher-publish"

if (-not (Test-Path (Join-Path $repoRoot "dist\electron\win-unpacked\Replay Recorder.exe"))) {
    throw "Packaged Electron app was not found. Run electron-builder before publishing the root launcher."
}

if (Test-Path $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $publishRoot `
    --nologo

$publishedExe = Join-Path $publishRoot "Replay Recorder.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Root launcher publish did not produce $publishedExe."
}

Copy-Item -LiteralPath $publishedExe -Destination $targetExe -Force
Write-Host "Published root launcher: $targetExe"
