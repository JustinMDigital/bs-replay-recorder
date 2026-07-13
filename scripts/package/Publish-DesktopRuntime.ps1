param(
    [string]$Configuration = "Release",
    [string]$PluginBeatSaberDir = $env:BSARR_PLUGIN_BEATSABER_DIR
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
        "true",
        "-o",
        $OutputPath,
        "--nologo"
    ) + $ExtraArguments

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name publish failed."
    }
}

function Resolve-PluginBeatSaberDirectory {
    function Test-PluginBuildDirectory {
        param([string]$Candidate)

        return -not [string]::IsNullOrWhiteSpace($Candidate) -and
            (Test-Path -LiteralPath (Join-Path $Candidate "Beat Saber.exe") -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $Candidate "Plugins\BeatLeader.dll") -PathType Leaf)
    }

    if (-not [string]::IsNullOrWhiteSpace($PluginBeatSaberDir)) {
        $candidate = [IO.Path]::GetFullPath($PluginBeatSaberDir.Trim().Trim('"'))
        if (Test-PluginBuildDirectory -Candidate $candidate) {
            return $candidate
        }

        throw "PluginBeatSaberDir must contain Beat Saber.exe and Plugins\\BeatLeader.dll: $candidate"
    }

    $settingsPath = Join-Path $repoRoot "settings.json"
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        try {
            $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            $sourcePath = [string]$settings.sourceBeatSaberPath
            if (-not [string]::IsNullOrWhiteSpace($sourcePath)) {
                $candidate = [IO.Path]::GetFullPath($sourcePath.Trim().Trim('"'))
                if (Test-PluginBuildDirectory -Candidate $candidate) {
                    return $candidate
                }
            }

            $instancesRoot = [string]$settings.beatSaberInstancesRoot
            if (-not [string]::IsNullOrWhiteSpace($instancesRoot) -and (Test-Path -LiteralPath $instancesRoot -PathType Container)) {
                $candidate = Get-ChildItem -LiteralPath $instancesRoot -Directory -ErrorAction SilentlyContinue |
                    Where-Object { Test-PluginBuildDirectory -Candidate $_.FullName } |
                    Sort-Object Name |
                    Select-Object -First 1 -ExpandProperty FullName
                if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                    return [IO.Path]::GetFullPath($candidate)
                }
            }
        }
        catch {
            Write-Host "Could not read settings.json while resolving the plugin build target: $($_.Exception.Message)"
        }
    }

    throw "A Beat Saber 1.40.6 folder with BeatLeader is required to build the bundled worker plugin. Pass -PluginBeatSaberDir or set BSARR_PLUGIN_BEATSABER_DIR."
}

function Publish-WorkerPlugin {
    $prebuiltRoot = Join-Path $repoRoot "artifacts\worker-plugin\bs-1.40.6"
    $prebuiltPlugin = Join-Path $prebuiltRoot "BSAutoReplayRecorder.Plugin.dll"
    $prebuiltCore = Join-Path $prebuiltRoot "BSAutoReplayRecorder.Core.dll"
    $pluginOutput = Join-Path $runtimeFullPath "worker-plugin\Release\netstandard2.1"

    if ([string]::IsNullOrWhiteSpace($PluginBeatSaberDir) -and
        (Test-Path -LiteralPath $prebuiltPlugin -PathType Leaf) -and
        (Test-Path -LiteralPath $prebuiltCore -PathType Leaf)) {
        New-Item -ItemType Directory -Path $pluginOutput -Force | Out-Null
        Copy-Item -LiteralPath $prebuiltPlugin -Destination $pluginOutput -Force
        Copy-Item -LiteralPath $prebuiltCore -Destination $pluginOutput -Force
        Write-Host "Using bundled Beat Saber 1.40.6 worker plugin artifact."
        return
    }

    $beatSaberDirectory = Resolve-PluginBeatSaberDirectory
    $pluginProject = Join-Path $repoRoot "src\BSAutoReplayRecorder.Plugin\BSAutoReplayRecorder.Plugin.csproj"
    $pluginOutputRoot = Join-Path $runtimeFullPath "worker-plugin"

    Write-Host "Building bundled worker plugin against: $beatSaberDirectory"
    & dotnet build $pluginProject `
        -c $Configuration `
        "-p:BeatSaberDir=$beatSaberDirectory" `
        "-p:BaseOutputPath=$pluginOutputRoot\" `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled worker plugin build failed."
    }

    $pluginOutput = Join-Path $pluginOutputRoot "$Configuration\netstandard2.1"
    foreach ($requiredFile in @("BSAutoReplayRecorder.Plugin.dll", "BSAutoReplayRecorder.Core.dll")) {
        if (-not (Test-Path -LiteralPath (Join-Path $pluginOutput $requiredFile) -PathType Leaf)) {
            throw "Bundled worker plugin output is missing $requiredFile at $pluginOutput."
        }
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

Publish-WorkerPlugin

$processAudioSource = Join-Path $repoRoot "tools\ProcessAudioCapture"
$processAudioTarget = Join-Path $runtimeFullPath "tools\ProcessAudioCapture"
if (Test-Path -LiteralPath $processAudioSource) {
    Copy-Item -LiteralPath $processAudioSource -Destination $processAudioTarget -Recurse -Force
}

Write-Host "Published desktop runtime: $runtimeFullPath"
