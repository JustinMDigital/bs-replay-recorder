param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$runtimeRoot = Join-Path $repoRoot "dist\runtime"
$runtimeFullPath = [IO.Path]::GetFullPath($runtimeRoot)
$distFullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot "dist"))

if (-not $runtimeFullPath.StartsWith($distFullPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean unexpected runtime path: $runtimeFullPath"
}

if (Test-Path -LiteralPath $runtimeFullPath) {
    Remove-Item -LiteralPath $runtimeFullPath -Recurse -Force
}

New-Item -ItemType Directory -Path $runtimeFullPath -Force | Out-Null

function Publish-Project {
    param(
        [string]$Name,
        [string]$ProjectPath,
        [string]$OutputPath,
        [string[]]$ExtraArguments = @()
    )

    Write-Host "Publishing $Name..."
    $arguments = @(
        "publish",
        $ProjectPath,
        "-c",
        $Configuration,
        "-r",
        "win-x64",
        "--self-contained",
        "false",
        "-o",
        $OutputPath,
        "--nologo"
    ) + $ExtraArguments

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name publish failed."
    }
}

Publish-Project `
    -Name "control panel" `
    -ProjectPath (Join-Path $repoRoot "src\BSAutoReplayRecorder.ControlPanel\BSAutoReplayRecorder.ControlPanel.csproj") `
    -OutputPath (Join-Path $runtimeFullPath "control-panel")

Publish-Project `
    -Name "recorder host" `
    -ProjectPath (Join-Path $repoRoot "src\BSAutoReplayRecorder.RecorderHost\BSAutoReplayRecorder.RecorderHost.csproj") `
    -OutputPath (Join-Path $runtimeFullPath "recorder-host")

Publish-Project `
    -Name "desktop host" `
    -ProjectPath (Join-Path $repoRoot "src\BSAutoReplayRecorder.DesktopHost\BSAutoReplayRecorder.DesktopHost.csproj") `
    -OutputPath (Join-Path $runtimeFullPath "desktop-host")

Publish-Project `
    -Name "process-loopback helper" `
    -ProjectPath (Join-Path $repoRoot "tools\ProcessLoopbackCapture.Managed\ProcessLoopbackCapture.Managed.csproj") `
    -OutputPath (Join-Path $runtimeFullPath "tools\ProcessLoopbackCapture.Managed")

Publish-Project `
    -Name "windows-graphics-capture helper" `
    -ProjectPath (Join-Path $repoRoot "tools\WindowsGraphicsCapture.Managed\WindowsGraphicsCapture.Managed.csproj") `
    -OutputPath (Join-Path $runtimeFullPath "tools\WindowsGraphicsCapture.Managed")

$processAudioSource = Join-Path $repoRoot "tools\ProcessAudioCapture"
$processAudioTarget = Join-Path $runtimeFullPath "tools\ProcessAudioCapture"
if (Test-Path -LiteralPath $processAudioSource) {
    Copy-Item -LiteralPath $processAudioSource -Destination $processAudioTarget -Recurse -Force
}

Write-Host "Published desktop runtime: $runtimeFullPath"
