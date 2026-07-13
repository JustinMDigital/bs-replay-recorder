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

function Publish-PrivateDotNetRuntime {
    param([string]$MajorMinorVersion = "10.0")

    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetRoot = Split-Path -Parent $dotnetCommand.Source
    $targetRoot = Join-Path $runtimeFullPath "dotnet"

    function Resolve-LatestRuntimeDirectory {
        param([string]$RelativeRoot)

        $root = Join-Path $dotnetRoot $RelativeRoot
        $match = Get-ChildItem -LiteralPath $root -Directory -ErrorAction Stop |
            Where-Object { $_.Name -like "$MajorMinorVersion.*" } |
            Sort-Object { [Version]$_.Name } -Descending |
            Select-Object -First 1
        if ($null -eq $match) {
            throw "The packaging machine does not have $RelativeRoot $MajorMinorVersion installed under $dotnetRoot."
        }

        return $match
    }

    $coreRuntime = Resolve-LatestRuntimeDirectory "shared\Microsoft.NETCore.App"
    $aspNetRuntime = Resolve-LatestRuntimeDirectory "shared\Microsoft.AspNetCore.App"
    $hostFxr = Resolve-LatestRuntimeDirectory "host\fxr"

    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $dotnetRoot "dotnet.exe") -Destination $targetRoot -Force
    foreach ($legalFile in @("LICENSE.txt", "ThirdPartyNotices.txt")) {
        $source = Join-Path $dotnetRoot $legalFile
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination $targetRoot -Force
        }
    }

    $coreTarget = Join-Path $targetRoot "shared\Microsoft.NETCore.App"
    $aspNetTarget = Join-Path $targetRoot "shared\Microsoft.AspNetCore.App"
    $hostFxrTarget = Join-Path $targetRoot "host\fxr"
    New-Item -ItemType Directory -Path $coreTarget, $aspNetTarget, $hostFxrTarget -Force | Out-Null
    Copy-Item -LiteralPath $coreRuntime.FullName -Destination $coreTarget -Recurse -Force
    Copy-Item -LiteralPath $aspNetRuntime.FullName -Destination $aspNetTarget -Recurse -Force
    Copy-Item -LiteralPath $hostFxr.FullName -Destination $hostFxrTarget -Recurse -Force

    Write-Host "Bundled private .NET runtime: Core $($coreRuntime.Name), ASP.NET $($aspNetRuntime.Name), hostfxr $($hostFxr.Name)."
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
    $prebuiltVersions = @("bs-1.40.6", "bs-1.44.1")
    $pluginOutput = Join-Path $runtimeFullPath "worker-plugin\Release\netstandard2.1"

    if ([string]::IsNullOrWhiteSpace($PluginBeatSaberDir)) {
        $copiedVersions = @()
        foreach ($version in $prebuiltVersions) {
            $prebuiltRoot = Join-Path $repoRoot ("artifacts\worker-plugin\" + $version)
            $prebuiltPlugin = Join-Path $prebuiltRoot "BSAutoReplayRecorder.Plugin.dll"
            $prebuiltCore = Join-Path $prebuiltRoot "BSAutoReplayRecorder.Core.dll"
            if (-not (Test-Path -LiteralPath $prebuiltPlugin -PathType Leaf) -or -not (Test-Path -LiteralPath $prebuiltCore -PathType Leaf)) { continue }
            $versionOutput = Join-Path $runtimeFullPath ("worker-plugin\" + $version + "\Release\netstandard2.1")
            New-Item -ItemType Directory -Path $versionOutput -Force | Out-Null
            Copy-Item -LiteralPath $prebuiltPlugin -Destination $versionOutput -Force
            Copy-Item -LiteralPath $prebuiltCore -Destination $versionOutput -Force
            $copiedVersions += $version
            if ($version -eq "bs-1.40.6") {
                New-Item -ItemType Directory -Path $pluginOutput -Force | Out-Null
                Copy-Item -LiteralPath $prebuiltPlugin -Destination $pluginOutput -Force
                Copy-Item -LiteralPath $prebuiltCore -Destination $pluginOutput -Force
            }
        }
        if ($copiedVersions.Count -gt 0) {
            Write-Host ("Using bundled worker plugin artifacts: " + ($copiedVersions -join ", ") + ".")
            return
        }
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

Publish-PrivateDotNetRuntime

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
