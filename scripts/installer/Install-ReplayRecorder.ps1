param(
    [ValidateSet("4k-monitor-2x2", "1440p-monitor-2x2", "single-1080p", "single-1440p", "single-4k")]
    [string]$Preset = "4k-monitor-2x2",
    [string]$SourceBeatSaberPath,
    [int]$InstanceCount = 0,
    [string]$InstancesRoot = "",
    [string]$Workspace = "",
    [string]$InstanceNamePrefix = "I-",
    [string]$ControlPanelUrl = "http://127.0.0.1:5770",
    [int]$RecorderHostBasePort = 5757,
    [string]$FfmpegPath,
    [switch]$CopyInstances,
    [switch]$CopyExistingSongs,
    [switch]$SkipPluginDeploy,
    [switch]$SkipSharedFolderRepair,
    [switch]$KeepExistingRecorderStack,
    [switch]$NoStart,
    [switch]$NoBrowser,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$InstallerBoundParameters = @{} + $PSBoundParameters

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$SettingsPath = Join-Path $RepoRoot "settings.json"
$SettingsExamplePath = Join-Path $RepoRoot "settings.example.json"
$LocalSettings = $null

function Write-Step {
    param([string]$Message)
    Write-Host "[installer] $Message"
}

function Ensure-LocalSettingsFile {
    if (Test-Path -LiteralPath $SettingsPath) {
        return
    }

    if (-not (Test-Path -LiteralPath $SettingsExamplePath)) {
        return
    }

    Copy-Item -LiteralPath $SettingsExamplePath -Destination $SettingsPath
    Write-Step "Created local settings from settings.example.json."
}

function Add-UniquePath {
    param(
        [System.Collections.Generic.List[string]]$Paths,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    try {
        $fullPath = [IO.Path]::GetFullPath($Path.Trim().Trim('"'))
        if (-not $Paths.Contains($fullPath)) {
            $Paths.Add($fullPath)
        }
    }
    catch {
    }
}

function Get-SteamRootCandidates {
    $paths = [System.Collections.Generic.List[string]]::new()

    foreach ($registryPath in @(
        "HKCU:\Software\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam"
    )) {
        try {
            $item = Get-ItemProperty -Path $registryPath -ErrorAction SilentlyContinue
            if ($item) {
                Add-UniquePath -Paths $paths -Path ([string]$item.SteamPath)
                Add-UniquePath -Paths $paths -Path ([string]$item.InstallPath)
            }
        }
        catch {
        }
    }

    Add-UniquePath -Paths $paths -Path (Join-Path ${env:ProgramFiles(x86)} "Steam")
    Add-UniquePath -Paths $paths -Path (Join-Path $env:ProgramFiles "Steam")
    Add-UniquePath -Paths $paths -Path (Join-Path $env:LOCALAPPDATA "Steam")
    Add-UniquePath -Paths $paths -Path (Join-Path $env:USERPROFILE "Steam")
    Add-UniquePath -Paths $paths -Path (Join-Path $env:USERPROFILE "SteamLibrary")
    Add-UniquePath -Paths $paths -Path (Join-Path $env:USERPROFILE "Games\SteamLibrary")

    return $paths
}

function Get-SteamLibraryCandidates {
    $paths = [System.Collections.Generic.List[string]]::new()

    foreach ($steamRoot in Get-SteamRootCandidates) {
        if (-not (Test-Path -LiteralPath $steamRoot)) {
            continue
        }

        Add-UniquePath -Paths $paths -Path $steamRoot
        $libraryFile = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (-not (Test-Path -LiteralPath $libraryFile)) {
            continue
        }

        try {
            foreach ($line in Get-Content -LiteralPath $libraryFile) {
                $match = [regex]::Match($line, '^\s*"(?:path|\d+)"\s+"(?<path>[^"]+)"')
                if (-not $match.Success) {
                    continue
                }

                $libraryPath = $match.Groups["path"].Value -replace '\\\\', '\'
                Add-UniquePath -Paths $paths -Path $libraryPath
            }
        }
        catch {
        }
    }

    return $paths
}

function Find-SteamBeatSaberPath {
    foreach ($libraryPath in Get-SteamLibraryCandidates) {
        $candidate = Join-Path $libraryPath "steamapps\common\Beat Saber"
        if (Test-Path -LiteralPath (Join-Path $candidate "Beat Saber.exe")) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    return ""
}

function Set-DetectedSourceBeatSaberPathDefault {
    param([object]$Settings)

    if ($null -eq $Settings) {
        return
    }

    $existingSource = Get-LocalSettingString -Settings $Settings -Names @("sourceBeatSaberPath")
    if (-not [string]::IsNullOrWhiteSpace($existingSource)) {
        return
    }

    $detectedSource = Find-SteamBeatSaberPath
    if ([string]::IsNullOrWhiteSpace($detectedSource)) {
        return
    }

    $Settings | Add-Member -NotePropertyName "sourceBeatSaberPath" -NotePropertyValue $detectedSource -Force
    $Settings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8
    Write-Step "Set sourceBeatSaberPath in settings.json to detected Steam install: $detectedSource"
}

function Read-LocalSettingsFile {
    Ensure-LocalSettingsFile
    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return $null
    }

    try {
        $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
        Set-DetectedSourceBeatSaberPathDefault -Settings $settings
        return $settings
    }
    catch {
        Write-Step "Could not read settings.json, so using script defaults: $($_.Exception.Message)"
        return $null
    }
}

function Get-LocalSettingValue {
    param(
        [object]$Settings,
        [string[]]$Names
    )

    if ($null -eq $Settings) {
        return $null
    }

    foreach ($name in $Names) {
        $property = $Settings.PSObject.Properties |
            Where-Object { [string]::Equals($_.Name, $name, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($property -and $null -ne $property.Value) {
            return $property.Value
        }
    }

    return $null
}

function Get-LocalSettingString {
    param(
        [object]$Settings,
        [string[]]$Names
    )

    $value = Get-LocalSettingValue -Settings $Settings -Names $Names
    if ($null -eq $value) {
        return $null
    }

    $text = ([string]$value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    return $text
}

function Resolve-RepoRelativePath {
    param([string]$Path)

    $trimmed = $Path.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $trimmed
    }

    if ([IO.Path]::IsPathRooted($trimmed)) {
        return [IO.Path]::GetFullPath($trimmed)
    }

    return [IO.Path]::GetFullPath((Join-Path $RepoRoot $trimmed))
}

function Test-BeatSaberSourcePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    try {
        return Test-Path -LiteralPath (Join-Path ([IO.Path]::GetFullPath($Path.Trim().Trim('"'))) "Beat Saber.exe")
    }
    catch {
        return $false
    }
}

function Save-LocalSourceBeatSaberPath {
    param([string]$Path)

    if ($InstallerBoundParameters.ContainsKey("SourceBeatSaberPath") -or $null -eq $LocalSettings) {
        return
    }

    $fullPath = [IO.Path]::GetFullPath($Path.Trim().Trim('"'))
    $LocalSettings | Add-Member -NotePropertyName "sourceBeatSaberPath" -NotePropertyValue $fullPath -Force
    $LocalSettings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8
    $script:SourceBeatSaberPath = $fullPath
}

function Save-LocalFfmpegPath {
    param([string]$Path)

    if ($InstallerBoundParameters.ContainsKey("FfmpegPath") -or $null -eq $LocalSettings) {
        return
    }

    $fullPath = [IO.Path]::GetFullPath($Path.Trim().Trim('"'))
    $LocalSettings | Add-Member -NotePropertyName "ffmpegPath" -NotePropertyValue $fullPath -Force
    $LocalSettings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8
    $script:FfmpegPath = $fullPath
}

function Assert-ValidPathArgument {
    param(
        [string]$ParameterName,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    if ($Value.TrimStart().StartsWith("-")) {
        throw "Missing value after -$ParameterName. Pass -$ParameterName `"C:\path\to\ffmpeg.exe`" or set ffmpegPath in settings.json."
    }
}

function Select-BeatSaberSourcePath {
    param([string]$DefaultPath)

    if ($Force) {
        return $DefaultPath
    }

    while ($true) {
        if (-not [string]::IsNullOrWhiteSpace($DefaultPath)) {
            $answer = Read-Host "Beat Saber source folder. Press Enter for detected default, or paste a different folder [$DefaultPath]"
            $selectedPath = if ([string]::IsNullOrWhiteSpace($answer)) { $DefaultPath } else { $answer }
        }
        else {
            $answer = Read-Host "Beat Saber source folder. Paste a folder, or press Enter if the managed baseline instance already exists"
            if ([string]::IsNullOrWhiteSpace($answer)) {
                return ""
            }

            $selectedPath = $answer
        }

        $resolvedPath = Resolve-RepoRelativePath $selectedPath
        if (Test-BeatSaberSourcePath -Path $resolvedPath) {
            Save-LocalSourceBeatSaberPath -Path $resolvedPath
            return [IO.Path]::GetFullPath($resolvedPath)
        }

        Write-Step "Beat Saber.exe was not found in: $resolvedPath"
    }
}

function Get-LocalSettingInt {
    param(
        [object]$Settings,
        [string[]]$Names
    )

    $value = Get-LocalSettingValue -Settings $Settings -Names $Names
    if ($null -eq $value) {
        return $null
    }

    $parsed = 0
    if ([int]::TryParse(([string]$value).Trim(), [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Get-LocalSettingBool {
    param(
        [object]$Settings,
        [string[]]$Names
    )

    $value = Get-LocalSettingValue -Settings $Settings -Names $Names
    if ($null -eq $value) {
        return $null
    }

    if ($value -is [bool]) {
        return [bool]$value
    }

    $parsed = $false
    if ([bool]::TryParse(([string]$value).Trim(), [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Apply-StringSettingDefault {
    param(
        [string]$ParameterName,
        [string[]]$SettingNames
    )

    if ($InstallerBoundParameters.ContainsKey($ParameterName)) {
        return
    }

    $value = Get-LocalSettingString -Settings $LocalSettings -Names $SettingNames
    if ($null -ne $value) {
        Set-Variable -Name $ParameterName -Value $value -Scope Script
    }
}

function Apply-PathSettingDefault {
    param(
        [string]$ParameterName,
        [string[]]$SettingNames
    )

    if ($InstallerBoundParameters.ContainsKey($ParameterName)) {
        return
    }

    $value = Get-LocalSettingString -Settings $LocalSettings -Names $SettingNames
    if ($null -ne $value) {
        Set-Variable -Name $ParameterName -Value (Resolve-RepoRelativePath $value) -Scope Script
    }
}

function Apply-IntSettingDefault {
    param(
        [string]$ParameterName,
        [string[]]$SettingNames
    )

    if ($InstallerBoundParameters.ContainsKey($ParameterName)) {
        return
    }

    $value = Get-LocalSettingInt -Settings $LocalSettings -Names $SettingNames
    if ($null -ne $value) {
        Set-Variable -Name $ParameterName -Value $value -Scope Script
    }
}

function Apply-SwitchSettingDefault {
    param(
        [string]$ParameterName,
        [string[]]$SettingNames
    )

    if ($InstallerBoundParameters.ContainsKey($ParameterName)) {
        return
    }

    $value = Get-LocalSettingBool -Settings $LocalSettings -Names $SettingNames
    if ($null -ne $value) {
        Set-Variable -Name $ParameterName -Value ([bool]$value) -Scope Script
    }
}

function Apply-LocalSettingsDefaults {
    $script:LocalSettings = Read-LocalSettingsFile
    if ($null -eq $LocalSettings) {
        return
    }

    if (-not $InstallerBoundParameters.ContainsKey("Preset")) {
        $presetValue = Get-LocalSettingString -Settings $LocalSettings -Names @("preset")
        if ($presetValue -and @("4k-monitor-2x2", "1440p-monitor-2x2", "single-1080p", "single-1440p", "single-4k").Contains($presetValue)) {
            $script:Preset = $presetValue
        }
    }

    Apply-PathSettingDefault -ParameterName "SourceBeatSaberPath" -SettingNames @("sourceBeatSaberPath")
    Apply-IntSettingDefault -ParameterName "InstanceCount" -SettingNames @("instanceCount")
    Apply-PathSettingDefault -ParameterName "InstancesRoot" -SettingNames @("instancesRoot", "beatSaberInstancesRoot")
    Apply-PathSettingDefault -ParameterName "Workspace" -SettingNames @("workspace", "workspaceDirectory")
    Apply-StringSettingDefault -ParameterName "InstanceNamePrefix" -SettingNames @("instanceNamePrefix", "beatSaberInstanceNamePrefix")
    Apply-StringSettingDefault -ParameterName "ControlPanelUrl" -SettingNames @("controlPanelUrl", "bindUrl")
    Apply-IntSettingDefault -ParameterName "RecorderHostBasePort" -SettingNames @("recorderHostBasePort")
    Apply-PathSettingDefault -ParameterName "FfmpegPath" -SettingNames @("ffmpegPath")
    Apply-SwitchSettingDefault -ParameterName "CopyInstances" -SettingNames @("copyInstances")
    Apply-SwitchSettingDefault -ParameterName "CopyExistingSongs" -SettingNames @("copyExistingSongs")
    Apply-SwitchSettingDefault -ParameterName "SkipPluginDeploy" -SettingNames @("skipPluginDeploy")
    Apply-SwitchSettingDefault -ParameterName "SkipSharedFolderRepair" -SettingNames @("skipSharedFolderRepair")
    Apply-SwitchSettingDefault -ParameterName "KeepExistingRecorderStack" -SettingNames @("keepExistingRecorderStack")
    Apply-SwitchSettingDefault -ParameterName "NoStart" -SettingNames @("noStart")
    Apply-SwitchSettingDefault -ParameterName "NoBrowser" -SettingNames @("noBrowser")
}

function Apply-PresetSettingsOverrides {
    param([object]$PresetSettings)

    if ($null -eq $LocalSettings) {
        return
    }

    foreach ($mapping in @(
        @{ Property = "MaxConcurrentRecordings"; Names = @("maxConcurrentRecordings") },
        @{ Property = "TargetFps"; Names = @("targetFps") },
        @{ Property = "CaptureWidth"; Names = @("captureWidth") },
        @{ Property = "CaptureHeight"; Names = @("captureHeight") },
        @{ Property = "VideoBitrateKbps"; Names = @("videoBitrateKbps") },
        @{ Property = "MonitorIndex"; Names = @("monitorIndex") },
        @{ Property = "RecordingDisplayScalePercent"; Names = @("recordingDisplayScalePercent") },
        @{ Property = "RestoreDisplayScalePercent"; Names = @("restoreDisplayScalePercent") }
    )) {
        $value = Get-LocalSettingInt -Settings $LocalSettings -Names $mapping.Names
        if ($null -ne $value) {
            $PresetSettings.($mapping.Property) = $value
        }
    }

    foreach ($mapping in @(
        @{ Property = "QualityMode"; Names = @("qualityMode") },
        @{ Property = "LaunchArguments"; Names = @("beatSaberLaunchArguments", "launchArguments") }
    )) {
        $value = Get-LocalSettingString -Settings $LocalSettings -Names $mapping.Names
        if ($null -ne $value) {
            $PresetSettings.($mapping.Property) = $value
        }
    }

    foreach ($mapping in @(
        @{ Property = "ManageDisplayScale"; Names = @("manageDisplayScale") },
        @{ Property = "HideTaskbarDuringRun"; Names = @("hideTaskbarDuringRun") },
        @{ Property = "RequireMatchingInstanceBaseline"; Names = @("requireMatchingInstanceBaseline") }
    )) {
        $value = Get-LocalSettingBool -Settings $LocalSettings -Names $mapping.Names
        if ($null -ne $value) {
            $PresetSettings.($mapping.Property) = [bool]$value
        }
    }
}

Apply-LocalSettingsDefaults

if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = Join-Path $RepoRoot "ControlPanelWorkspace"
}
$Workspace = [IO.Path]::GetFullPath($Workspace)
if ([string]::IsNullOrWhiteSpace($InstancesRoot)) {
    $InstancesRoot = Join-Path $Workspace "Instances"
}
$InstancesRoot = [IO.Path]::GetFullPath($InstancesRoot)
$StatePath = Join-Path $Workspace "control-panel-state.json"
$ControlPanelProject = Join-Path $RepoRoot "src\BSAutoReplayRecorder.ControlPanel\BSAutoReplayRecorder.ControlPanel.csproj"
$RecorderHostProject = Join-Path $RepoRoot "src\BSAutoReplayRecorder.RecorderHost\BSAutoReplayRecorder.RecorderHost.csproj"
$ProcessLoopbackProject = Join-Path $RepoRoot "tools\ProcessLoopbackCapture.Managed\ProcessLoopbackCapture.Managed.csproj"
$PluginProject = Join-Path $RepoRoot "src\BSAutoReplayRecorder.Plugin\BSAutoReplayRecorder.Plugin.csproj"
$PluginOutput = Join-Path $RepoRoot "src\BSAutoReplayRecorder.Plugin\bin\Debug\netstandard2.1"
$StartScript = Join-Path $RepoRoot "scripts\launcher\Start-ReplayRecorder.ps1"
$StopScript = Join-Path $RepoRoot "scripts\launcher\Stop-ReplayRecorder.ps1"
$CopyExistingSongsSelected = [bool]$CopyExistingSongs

function Write-Section {
    param([string]$Message)
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. Install it and run install.bat again."
    }
}

function Confirm-Step {
    param(
        [string]$Prompt,
        [switch]$DefaultYes
    )

    if ($Force) {
        return $true
    }

    $suffix = if ($DefaultYes) { " [Y/n]" } else { " [y/N]" }
    $answer = Read-Host ($Prompt + $suffix)
    if ([string]::IsNullOrWhiteSpace($answer)) {
        return [bool]$DefaultYes
    }

    return $answer.Trim().ToLowerInvariant().StartsWith("y")
}

function Resolve-ExecutableCandidate {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return ""
    }

    $trimmed = $Candidate.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return ""
    }

    if ([IO.Path]::IsPathRooted($trimmed) -or $trimmed.Contains("\") -or $trimmed.Contains("/")) {
        $path = if ([IO.Path]::IsPathRooted($trimmed)) { $trimmed } else { Resolve-RepoRelativePath $trimmed }
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return (Resolve-Path -LiteralPath $path).Path
        }

        return ""
    }

    $command = Get-Command $trimmed -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return ""
}

function Get-WinGetPackageExecutableCandidates {
    param([string]$ExecutableName)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $wingetPackages = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
        if (Test-Path -LiteralPath $wingetPackages) {
            $candidates += Get-ChildItem -LiteralPath $wingetPackages -Recurse -Filter $ExecutableName -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*Gyan.FFmpeg*" } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -ExpandProperty FullName
        }

        $candidates += Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\$ExecutableName"
    }

    return $candidates
}

function Get-CommonFfmpegExecutableCandidates {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:BSARR_FFMPEG_PATH)) {
        $candidates += $env:BSARR_FFMPEG_PATH
    }

    $candidates += Get-WinGetPackageExecutableCandidates -ExecutableName "ffmpeg.exe"
    $candidates += @(
        "C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        "C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
        "C:\ProgramData\chocolatey\bin\ffmpeg.exe",
        "C:\Program Files\ShareX\ffmpeg.exe",
        "ffmpeg"
    )

    return $candidates
}

function Get-CommonFfprobeExecutableCandidates {
    param([string]$ResolvedFfmpegPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:BSARR_FFPROBE_PATH)) {
        $candidates += $env:BSARR_FFPROBE_PATH
    }

    if (-not [string]::IsNullOrWhiteSpace($ResolvedFfmpegPath)) {
        $ffmpegDirectory = Split-Path -Parent $ResolvedFfmpegPath
        if (-not [string]::IsNullOrWhiteSpace($ffmpegDirectory)) {
            $candidates += Join-Path $ffmpegDirectory "ffprobe.exe"
        }
    }

    $candidates += Get-WinGetPackageExecutableCandidates -ExecutableName "ffprobe.exe"
    $candidates += @(
        "C:\Program Files\ffmpeg\bin\ffprobe.exe",
        "C:\Program Files (x86)\ffmpeg\bin\ffprobe.exe",
        "C:\ProgramData\chocolatey\bin\ffprobe.exe",
        "C:\Program Files\ShareX\ffprobe.exe",
        "ffprobe"
    )

    return $candidates
}

function Test-ExecutableVersion {
    param(
        [string]$Path,
        [string]$Name,
        [switch]$Quiet
    )

    try {
        & $Path -hide_banner -version *> $null
        if ($LASTEXITCODE -eq 0) {
            return $true
        }

        if (-not $Quiet) {
            Write-Step "$Name was found but did not run successfully: $Path"
        }
    }
    catch {
        if (-not $Quiet) {
            Write-Step "$Name could not be started: $Path"
        }
    }

    return $false
}

function Resolve-FfprobeForFfmpeg {
    param(
        [string]$ResolvedFfmpegPath,
        [switch]$Quiet
    )

    foreach ($candidate in Get-CommonFfprobeExecutableCandidates -ResolvedFfmpegPath $ResolvedFfmpegPath) {
        $resolved = Resolve-ExecutableCandidate -Candidate $candidate
        if ([string]::IsNullOrWhiteSpace($resolved)) {
            continue
        }

        if (Test-ExecutableVersion -Path $resolved -Name "ffprobe" -Quiet:$Quiet) {
            return $resolved
        }
    }

    if (-not $Quiet) {
        Write-Step "ffprobe.exe was not found next to FFmpeg or in a known install location."
    }

    return ""
}

function Resolve-FfmpegToolchainCandidate {
    param(
        [string]$Candidate,
        [switch]$Quiet
    )

    $resolvedFfmpegPath = Resolve-ExecutableCandidate -Candidate $Candidate
    if ([string]::IsNullOrWhiteSpace($resolvedFfmpegPath)) {
        if (-not $Quiet) {
            Write-Step "ffmpeg.exe was not found at: $Candidate"
        }

        return $null
    }

    if (-not (Test-ExecutableVersion -Path $resolvedFfmpegPath -Name "FFmpeg" -Quiet:$Quiet)) {
        return $null
    }

    $resolvedFfprobePath = Resolve-FfprobeForFfmpeg -ResolvedFfmpegPath $resolvedFfmpegPath -Quiet:$Quiet
    if ([string]::IsNullOrWhiteSpace($resolvedFfprobePath)) {
        return $null
    }

    return [pscustomobject]@{
        FfmpegPath = $resolvedFfmpegPath
        FfprobePath = $resolvedFfprobePath
    }
}

function Find-FfmpegToolchain {
    foreach ($candidate in Get-CommonFfmpegExecutableCandidates) {
        $toolchain = Resolve-FfmpegToolchainCandidate -Candidate $candidate -Quiet
        if ($null -ne $toolchain) {
            return $toolchain
        }
    }

    return $null
}

function Install-FfmpegWithWinget {
    $winget = Get-Command "winget" -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Step "WinGet was not found, so the installer cannot install FFmpeg automatically."
        return $false
    }

    Write-Step "Installing FFmpeg with WinGet package Gyan.FFmpeg..."
    & $winget.Source install --id Gyan.FFmpeg --exact --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Step "WinGet FFmpeg install exited with code $LASTEXITCODE."
        return $false
    }

    return $true
}

function Use-FfmpegToolchain {
    param([object]$Toolchain)

    $script:FfmpegPath = [string]$Toolchain.FfmpegPath
    $env:BSARR_FFMPEG_PATH = [string]$Toolchain.FfmpegPath
    $env:BSARR_FFPROBE_PATH = [string]$Toolchain.FfprobePath
    Save-LocalFfmpegPath -Path ([string]$Toolchain.FfmpegPath)
    Write-Step "Using FFmpeg: $($Toolchain.FfmpegPath)"
    Write-Step "Using ffprobe: $($Toolchain.FfprobePath)"
    return $Toolchain
}

function Resolve-FfmpegPrerequisite {
    $ffmpegFromCommandLine = $InstallerBoundParameters.ContainsKey("FfmpegPath")

    if (-not [string]::IsNullOrWhiteSpace($FfmpegPath)) {
        $toolchain = Resolve-FfmpegToolchainCandidate -Candidate $FfmpegPath
        if ($null -ne $toolchain) {
            return Use-FfmpegToolchain -Toolchain $toolchain
        }

        if ($ffmpegFromCommandLine -or $Force) {
            throw "Configured FFmpeg was not usable. Pass a path to ffmpeg.exe from a full FFmpeg install that also includes ffprobe.exe."
        }

        Write-Step "Configured ffmpegPath was not usable, so checking installed FFmpeg locations."
    }

    $detectedToolchain = Find-FfmpegToolchain
    if ($null -ne $detectedToolchain) {
        return Use-FfmpegToolchain -Toolchain $detectedToolchain
    }

    if ($Force) {
        throw "FFmpeg and ffprobe were not found. Install FFmpeg or set ffmpegPath in settings.json."
    }

    $offerWinget = $true
    while ($true) {
        if ($offerWinget -and (Get-Command "winget" -ErrorAction SilentlyContinue)) {
            if (Confirm-Step -Prompt "FFmpeg/ffprobe were not found. Install Gyan.FFmpeg with WinGet now?" -DefaultYes) {
                Install-FfmpegWithWinget | Out-Null
                $installedToolchain = Find-FfmpegToolchain
                if ($null -ne $installedToolchain) {
                    return Use-FfmpegToolchain -Toolchain $installedToolchain
                }

                Write-Step "FFmpeg was still not found after the WinGet install attempt."
            }

            $offerWinget = $false
        }

        $answer = Read-Host "Paste the full path to ffmpeg.exe, or press Enter to retry detection"
        if ([string]::IsNullOrWhiteSpace($answer)) {
            $retriedToolchain = Find-FfmpegToolchain
            if ($null -ne $retriedToolchain) {
                return Use-FfmpegToolchain -Toolchain $retriedToolchain
            }

            Write-Step "FFmpeg/ffprobe still were not found."
            continue
        }

        $selectedToolchain = Resolve-FfmpegToolchainCandidate -Candidate $answer
        if ($null -ne $selectedToolchain) {
            return Use-FfmpegToolchain -Toolchain $selectedToolchain
        }
    }
}

function Resolve-BeatSaberPath {
    $sourceFromCommandLine = $InstallerBoundParameters.ContainsKey("SourceBeatSaberPath")
    if (-not [string]::IsNullOrWhiteSpace($SourceBeatSaberPath)) {
        $path = [IO.Path]::GetFullPath($SourceBeatSaberPath.Trim('"'))
        if (Test-BeatSaberSourcePath -Path $path) {
            if ($sourceFromCommandLine -or $Force) {
                return $path
            }

            return Select-BeatSaberSourcePath -DefaultPath $path
        }

        if ($sourceFromCommandLine -or $Force) {
            throw "Beat Saber.exe was not found in SourceBeatSaberPath: $path"
        }

        Write-Step "Configured sourceBeatSaberPath does not contain Beat Saber.exe: $path"
    }

    $steamBeatSaberPath = Find-SteamBeatSaberPath
    if (-not [string]::IsNullOrWhiteSpace($steamBeatSaberPath)) {
        return Select-BeatSaberSourcePath -DefaultPath $steamBeatSaberPath
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Steam\steamapps\common\Beat Saber"),
        (Join-Path $env:ProgramFiles "Steam\steamapps\common\Beat Saber"),
        (Join-Path $env:USERPROFILE "Beat Saber")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-BeatSaberSourcePath -Path $candidate)) {
            return Select-BeatSaberSourcePath -DefaultPath ([IO.Path]::GetFullPath($candidate))
        }
    }

    return Select-BeatSaberSourcePath -DefaultPath ""
}

function Resolve-InstanceCount {
    param([object]$PresetSettings)

    if ($InstanceCount -ne 0) {
        if ($InstanceCount -lt 1 -or $InstanceCount -gt 4) {
            throw "InstanceCount must be between 1 and 4."
        }

        return $InstanceCount
    }

    $defaultCount = [Math]::Min(4, [Math]::Max(1, [int]$PresetSettings.InstanceCount))
    if ($Force) {
        return $defaultCount
    }

    while ($true) {
        $answer = Read-Host "How many Beat Saber instances do you want to create? Enter 1-4 [default: $defaultCount]"
        if ([string]::IsNullOrWhiteSpace($answer)) {
            return $defaultCount
        }

        $parsed = 0
        if ([int]::TryParse($answer.Trim(), [ref]$parsed) -and $parsed -ge 1 -and $parsed -le 4) {
            return $parsed
        }

        Write-Step "Enter a number from 1 to 4."
    }
}

function Set-PresetInstanceCount {
    param(
        [object]$PresetSettings,
        [int]$SelectedInstanceCount
    )

    $PresetSettings.InstanceCount = $SelectedInstanceCount
    $PresetSettings.MaxConcurrentRecordings = $SelectedInstanceCount

    if ($SelectedInstanceCount -eq 1) {
        $PresetSettings.RequireMatchingInstanceBaseline = $false
        $PresetSettings.ManageDisplayScale = $false
        $PresetSettings.HideTaskbarDuringRun = $false
    }
}

function Get-PresetSettings {
    param([string]$PresetId)

    $defaultArgs = "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 --no-yeet fpfc --verbose"
    $smallArgs = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 --no-yeet fpfc --verbose"
    $mediumArgs = "-screen-fullscreen 0 -screen-width 2560 -screen-height 1440 --no-yeet fpfc --verbose"
    $largeArgs = "-screen-fullscreen 0 -screen-width 3840 -screen-height 2160 --no-yeet fpfc --verbose"

    switch ($PresetId) {
        "4k-monitor-2x2" {
            return [pscustomobject]@{
                InstanceCount = 3
                MaxConcurrentRecordings = 3
                TargetFps = 60
                CaptureWidth = 1920
                CaptureHeight = 1080
                VideoBitrateKbps = 12000
                MonitorIndex = 1
                QualityMode = "Performance"
                LaunchArguments = $defaultArgs
                ManageDisplayScale = $true
                RecordingDisplayScalePercent = 100
                RestoreDisplayScalePercent = 150
                HideTaskbarDuringRun = $true
                RequireMatchingInstanceBaseline = $true
            }
        }
        "1440p-monitor-2x2" {
            return [pscustomobject]@{
                InstanceCount = 3
                MaxConcurrentRecordings = 3
                TargetFps = 60
                CaptureWidth = 1280
                CaptureHeight = 720
                VideoBitrateKbps = 8000
                MonitorIndex = 1
                QualityMode = "Performance"
                LaunchArguments = $smallArgs
                ManageDisplayScale = $true
                RecordingDisplayScalePercent = 100
                RestoreDisplayScalePercent = 150
                HideTaskbarDuringRun = $true
                RequireMatchingInstanceBaseline = $true
            }
        }
        "single-1440p" {
            return [pscustomobject]@{
                InstanceCount = 1
                MaxConcurrentRecordings = 1
                TargetFps = 60
                CaptureWidth = 2560
                CaptureHeight = 1440
                VideoBitrateKbps = 18000
                MonitorIndex = 1
                QualityMode = "Balanced"
                LaunchArguments = $mediumArgs
                ManageDisplayScale = $false
                RecordingDisplayScalePercent = 100
                RestoreDisplayScalePercent = 150
                HideTaskbarDuringRun = $false
                RequireMatchingInstanceBaseline = $false
            }
        }
        "single-4k" {
            return [pscustomobject]@{
                InstanceCount = 1
                MaxConcurrentRecordings = 1
                TargetFps = 60
                CaptureWidth = 3840
                CaptureHeight = 2160
                VideoBitrateKbps = 32000
                MonitorIndex = 1
                QualityMode = "Quality"
                LaunchArguments = $largeArgs
                ManageDisplayScale = $false
                RecordingDisplayScalePercent = 100
                RestoreDisplayScalePercent = 150
                HideTaskbarDuringRun = $false
                RequireMatchingInstanceBaseline = $false
            }
        }
        default {
            return [pscustomobject]@{
                InstanceCount = 1
                MaxConcurrentRecordings = 1
                TargetFps = 60
                CaptureWidth = 1920
                CaptureHeight = 1080
                VideoBitrateKbps = 12000
                MonitorIndex = 1
                QualityMode = "Balanced"
                LaunchArguments = $defaultArgs
                ManageDisplayScale = $false
                RecordingDisplayScalePercent = 100
                RestoreDisplayScalePercent = 150
                HideTaskbarDuringRun = $false
                RequireMatchingInstanceBaseline = $false
            }
        }
    }
}

function Get-InstanceDirectory {
    param([int]$Index)

    return Join-Path $InstancesRoot ($InstanceNamePrefix + ($Index + 1))
}

function Copy-BeatSaberInstance {
    param(
        [string]$Source,
        [string]$Target,
        [switch]$CopySongs
    )

    if (Test-Path $Target) {
        return
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Target) -Force | Out-Null
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    $songMode = if ($CopySongs) { "including existing songs" } else { "excluding existing songs" }
    Write-Step "Copying Beat Saber to $Target ($songMode)"
    Copy-DirectoryContents -SourceRoot $Source -TargetRoot $Target -RelativeRoot "" -CopySongs:$CopySongs
}

function Test-PreviousSongLibraryPath {
    param([string]$RelativePath)

    $normalized = $RelativePath.Replace("\", "/").Trim("/")
    foreach ($songLibrary in @("Beat Saber_Data/CustomLevels", "Beat Saber_Data/CustomWIPLevels")) {
        if ([string]::Equals($normalized, $songLibrary, [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith($songLibrary + "/", [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Test-ProvisionTransientPath {
    param([string]$RelativePath)

    $normalized = $RelativePath.Replace("\", "/").Trim("/")
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $false
    }

    foreach ($songLibraryBackup in @("Beat Saber_Data/CustomLevels.local-", "Beat Saber_Data/CustomWIPLevels.local-")) {
        if ($normalized.StartsWith($songLibraryBackup, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    foreach ($transientPath in @(
        "BSWC Recording Files",
        "Logs",
        "UserData/BeatLeader/Replays",
        "UserData/BSWorldCupReplayRecorder/Recordings",
        "UserData/BSAutoReplayRecorder/Recordings"
    )) {
        if ([string]::Equals($normalized, $transientPath, [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith($transientPath + "/", [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Copy-DirectoryContents {
    param(
        [string]$SourceRoot,
        [string]$TargetRoot,
        [string]$RelativeRoot,
        [switch]$CopySongs
    )

    $sourceDirectory = if ([string]::IsNullOrWhiteSpace($RelativeRoot)) {
        $SourceRoot
    }
    else {
        Join-Path $SourceRoot $RelativeRoot
    }

    foreach ($directory in Get-ChildItem -LiteralPath $sourceDirectory -Directory -Force) {
        $relativePath = if ([string]::IsNullOrWhiteSpace($RelativeRoot)) {
            $directory.Name
        }
        else {
            Join-Path $RelativeRoot $directory.Name
        }

        if (Test-ProvisionTransientPath -RelativePath $relativePath) {
            continue
        }

        if (-not $CopySongs -and (Test-PreviousSongLibraryPath -RelativePath $relativePath)) {
            continue
        }

        New-Item -ItemType Directory -Path (Join-Path $TargetRoot $relativePath) -Force | Out-Null
        Copy-DirectoryContents -SourceRoot $SourceRoot -TargetRoot $TargetRoot -RelativeRoot $relativePath -CopySongs:$CopySongs
    }

    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -File -Force) {
        $relativePath = if ([string]::IsNullOrWhiteSpace($RelativeRoot)) {
            $file.Name
        }
        else {
            Join-Path $RelativeRoot $file.Name
        }

        if (Test-ProvisionTransientPath -RelativePath $relativePath) {
            continue
        }

        if (-not $CopySongs -and (Test-PreviousSongLibraryPath -RelativePath $relativePath)) {
            continue
        }

        $targetPath = Join-Path $TargetRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
    }
}

function Backup-File {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = "$Path.$timestamp.bak"
    for ($index = 2; Test-Path -LiteralPath $backupPath; $index++) {
        $backupPath = "$Path.$timestamp.$index.bak"
    }

    Copy-Item -LiteralPath $Path -Destination $backupPath
}

function Backup-BeatSaberSettingsIni {
    param([string]$InstanceDirectory)

    $settingsCandidates = @(
        (Join-Path $InstanceDirectory "settings.ini"),
        (Join-Path $InstanceDirectory "UserData\settings.ini")
    )

    $backedUp = $false
    foreach ($settingsPath in $settingsCandidates) {
        if (-not (Test-Path -LiteralPath $settingsPath)) {
            continue
        }

        Backup-File -Path $settingsPath
        Write-Step "Backed up $settingsPath"
        $backedUp = $true
    }

    if (-not $backedUp) {
        Write-Step "No Beat Saber settings.ini found under $InstanceDirectory; skipping settings.ini backup."
    }
}

function Copy-IfExists {
    param(
        [string]$Source,
        [string]$DestinationDirectory,
        [switch]$Optional
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        if ($Optional) {
            return
        }

        throw "Build output file is missing: $Source"
    }

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $DestinationDirectory -Force
}

function Remove-IfExists {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Get-ExistingWorkerId {
    param([string]$SettingsPath)

    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return ""
    }

    try {
        $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
        return [string]$settings.ControlPanelWorker.WorkerId
    }
    catch {
        return ""
    }
}

function New-PluginSettings {
    param(
        [int]$Index,
        [object]$PresetSettings,
        [string]$WorkerId = ""
    )

    $displayIndex = $Index + 1
    $recorderHostPort = $RecorderHostBasePort + $Index
    $columns = if ($PresetSettings.InstanceCount -eq 1) { 1 } else { 2 }
    $rows = if ($PresetSettings.InstanceCount -eq 1) { 1 } else { 2 }

    return [ordered]@{
        RequirePreflightReplayValidation = $true
        RecordingOutputDirectory = "UserData/BSAutoReplayRecorder/Recordings"
        ReplayFinishTimeoutPaddingSeconds = 30.0
        LagSpikeDetectionEnabled = $true
        LagSpikeThresholdMilliseconds = 250.0
        LagSpikeConsecutiveFrameCount = 1
        LagSpikeStartupGraceSeconds = 3.0
        StartRecordingRetryCount = 5
        StartRecordingRetryDelaySeconds = 2.0
        RecorderHost = [ordered]@{
            BaseUrl = "http://127.0.0.1:$recorderHostPort"
            WindowTitle = "Beat Saber"
            OutputDirectory = ""
            TargetFps = $null
            CaptureWidth = $null
            CaptureHeight = $null
            Encoder = ""
            VideoBitrateKbps = $null
            OutputFormat = ""
            MonitorIndex = $null
            QualityMode = ""
            AudioMode = ""
            AudioDeviceName = ""
            AudioBitrateKbps = $null
            AudioSampleRate = $null
            AudioChannels = $null
            AudioLevelMode = ""
            AudioTargetLevelDb = $null
            TargetProcessId = $null
            TimeoutSeconds = 300.0
        }
        ControlPanelWorker = [ordered]@{
            Enabled = $true
            BaseUrl = $ControlPanelUrl.TrimEnd("/")
            WorkerId = $WorkerId
            WorkerName = "BSARR I-$displayIndex"
            PreferredInstanceIndex = $Index
            PollIntervalSeconds = 1.0
            HeartbeatIntervalSeconds = 2.0
            RequestTimeoutSeconds = 10.0
        }
        WindowPlacement = [ordered]@{
            Enabled = $true
            InstanceIndex = $Index
            MonitorIndex = [int]$PresetSettings.MonitorIndex
            Columns = $columns
            Rows = $rows
            Width = 0
            Height = 0
            ApplyDelaySeconds = 1.0
            RetryCount = 60
            RetryIntervalSeconds = 0.5
            UseNativeWindowMove = $true
            UseBorderlessWindow = $true
        }
    }
}

function Deploy-Plugin {
    param(
        [string]$InstanceDirectory,
        [int]$Index,
        [object]$PresetSettings
    )

    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\BSWorldCupReplayRecorder.Plugin.dll")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\BSWorldCupReplayRecorder.Plugin.pdb")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Libs\BSWorldCupReplayRecorder.Core.dll")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Libs\BSWorldCupReplayRecorder.Core.pdb")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\BSAutoReplayRecorder.Core.dll")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\BSAutoReplayRecorder.Core.pdb")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\.cache\BSAutoReplayRecorder.Core.dll")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\.cache\BSAutoReplayRecorder.Core.pdb")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\.cache\BSAutoReplayRecorder.Plugin.dll")
    Remove-IfExists -Path (Join-Path $InstanceDirectory "Plugins\.cache\BSAutoReplayRecorder.Plugin.pdb")

    Copy-IfExists -Source (Join-Path $PluginOutput "BSAutoReplayRecorder.Plugin.dll") -DestinationDirectory (Join-Path $InstanceDirectory "Plugins")
    Copy-IfExists -Source (Join-Path $PluginOutput "BSAutoReplayRecorder.Plugin.pdb") -DestinationDirectory (Join-Path $InstanceDirectory "Plugins") -Optional
    Copy-IfExists -Source (Join-Path $PluginOutput "BSAutoReplayRecorder.Core.dll") -DestinationDirectory (Join-Path $InstanceDirectory "Libs")
    Copy-IfExists -Source (Join-Path $PluginOutput "BSAutoReplayRecorder.Core.pdb") -DestinationDirectory (Join-Path $InstanceDirectory "Libs") -Optional

    $settingsDirectory = Join-Path $InstanceDirectory "UserData\BSAutoReplayRecorder"
    $settingsPath = Join-Path $settingsDirectory "settings.json"
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $workerId = Get-ExistingWorkerId -SettingsPath $settingsPath
    Backup-File -Path $settingsPath
    New-PluginSettings -Index $Index -PresetSettings $PresetSettings -WorkerId $workerId |
        ConvertTo-Json -Depth 12 |
        Set-Content -Path $settingsPath -Encoding UTF8
}

function New-ControlPanelState {
    param([object]$PresetSettings)

    $workspaceFullPath = [IO.Path]::GetFullPath($Workspace)
    $recordingRoot = Join-Path $workspaceFullPath "Recordings"
    $provisionSourceDirectory = ""
    if (-not [string]::IsNullOrWhiteSpace($SourceBeatSaberPath)) {
        $provisionSourceDirectory = [IO.Path]::GetFullPath($SourceBeatSaberPath.Trim('"'))
    }

    $instances = @()

    for ($index = 0; $index -lt $PresetSettings.InstanceCount; $index++) {
        $instances += [ordered]@{
            index = $index
            name = "Instance " + ($index + 1)
            status = "Idle"
            recorderHostUrl = "http://127.0.0.1:" + ($RecorderHostBasePort + $index)
            outputDirectory = Join-Path $recordingRoot ("Instance " + ($index + 1))
            currentReplayId = $null
            workerId = $null
            gameDirectory = $null
            launchDirectory = Get-InstanceDirectory -Index $index
            launchArguments = [string]$PresetSettings.LaunchArguments
            gameProcessId = $null
            gameLaunchStatus = "Idle"
            gameLaunchedAtUtc = $null
            gameLaunchError = $null
            audioRoutingStatus = "Idle"
            audioRoutingError = $null
            pluginVersion = $null
            registeredAtUtc = $null
            lastHeartbeatUtc = $null
            activeAssignmentId = $null
            lastForceStopCommandId = 0
        }
    }

    return [ordered]@{
        settings = [ordered]@{
            bindUrl = $ControlPanelUrl.TrimEnd("/")
            workspaceDirectory = $workspaceFullPath
            recordingOutputDirectory = $recordingRoot
            sharedCustomLevelsDirectory = Join-Path $workspaceFullPath "SharedSongs\CustomLevels"
            sharedCustomWipLevelsDirectory = Join-Path $workspaceFullPath "SharedSongs\CustomWIPLevels"
            shareCustomSabers = $true
            sharedCustomSabersDirectory = Join-Path $workspaceFullPath "SharedContent\CustomSabers"
            shareCustomNotes = $true
            sharedCustomNotesDirectory = Join-Path $workspaceFullPath "SharedContent\CustomNotes"
            shareCustomPlatforms = $true
            sharedCustomPlatformsDirectory = Join-Path $workspaceFullPath "SharedContent\CustomPlatforms"
            shareCustomAvatars = $true
            sharedCustomAvatarsDirectory = Join-Path $workspaceFullPath "SharedContent\CustomAvatars"
            shareCustomWalls = $true
            sharedCustomWallsDirectory = Join-Path $workspaceFullPath "SharedContent\CustomWalls"
            shareCustomBombs = $true
            sharedCustomBombsDirectory = Join-Path $workspaceFullPath "SharedContent\CustomBombs"
            instanceCount = [int]$PresetSettings.InstanceCount
            maxConcurrentRecordings = [int]$PresetSettings.MaxConcurrentRecordings
            requireAllWorkersReady = $true
            requireMatchingInstanceBaseline = [bool]$PresetSettings.RequireMatchingInstanceBaseline
            targetFps = [int]$PresetSettings.TargetFps
            captureWidth = [int]$PresetSettings.CaptureWidth
            captureHeight = [int]$PresetSettings.CaptureHeight
            encoder = "h264_nvenc"
            videoBitrateKbps = [int]$PresetSettings.VideoBitrateKbps
            outputFormat = "mkv"
            monitorIndex = [int]$PresetSettings.MonitorIndex
            qualityMode = [string]$PresetSettings.QualityMode
            audioMode = "ProcessLoopback"
            requireAudioForRun = $true
            audioBitrateKbps = 192
            audioSampleRate = 48000
            audioChannels = 2
            audioLevelMode = "Loudness"
            audioTargetLevelDb = -12
            beatSaberInstancesRoot = [IO.Path]::GetFullPath($InstancesRoot)
            beatSaberInstanceNamePrefix = $InstanceNamePrefix
            beatSaberLaunchPreset = $Preset
            beatSaberLaunchArguments = [string]$PresetSettings.LaunchArguments
            manageDisplayScale = [bool]$PresetSettings.ManageDisplayScale
            recordingDisplayScalePercent = [int]$PresetSettings.RecordingDisplayScalePercent
            restoreDisplayScalePercent = [int]$PresetSettings.RestoreDisplayScalePercent
            hideTaskbarDuringRun = [bool]$PresetSettings.HideTaskbarDuringRun
        }
        queue = @()
        instances = $instances
        instanceProvision = [ordered]@{
            status = "Ready"
            summary = if ($CopyExistingSongsSelected) { "Managed Beat Saber instances are ready with the worker plugin installed, and existing songs imported." } else { "Managed Beat Saber instances are ready with the worker plugin installed, without importing existing songs." }
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
            sourceDirectory = $provisionSourceDirectory
            targetRootDirectory = [IO.Path]::GetFullPath($InstancesRoot)
            copyExistingSongs = [bool]$CopyExistingSongsSelected
            instances = @($instances | ForEach-Object {
                [ordered]@{
                    index = [int]$_.index
                    name = [string]$_.name
                    directory = [string]$_.launchDirectory
                    status = "Ready"
                    detail = if ([int]$_.index -eq 0 -and $CopyExistingSongsSelected) { "Managed by installer with existing songs imported." } else { "Managed by installer without existing songs." }
                }
            })
        }
        instanceBaseline = [ordered]@{
            status = "Unchecked"
            summary = "Baseline has not been checked."
            checkedAtUtc = $null
            baselineInstanceIndex = 0
            baselineInstanceName = ""
            instances = @()
        }
        songFolders = [ordered]@{
            status = "Unchecked"
            summary = "Song folders have not been checked."
            checkedAtUtc = $null
            sharedCustomLevelsDirectory = ""
            sharedCustomWipLevelsDirectory = ""
            links = @()
        }
        run = [ordered]@{
            isRunning = $false
            cancellationRequested = $false
            cancellationReason = $null
            startedAtUtc = $null
            finishedAtUtc = $null
            completedCount = 0
            failedCount = 0
            status = "Idle"
            forceStopCommandId = 0
        }
    }
}

function Invoke-ControlPanelPost {
    param([string]$Path)

    $url = $ControlPanelUrl.TrimEnd("/") + $Path
    Invoke-RestMethod -Method Post -Uri $url -ContentType "application/json" -Body "{}" -TimeoutSec 30 | Out-Null
}

function Test-HttpEndpoint {
    param([string]$Url)

    try {
        Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Test-InstalledStateReady {
    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return $false
    }

    if (-not (Test-Path -LiteralPath $StatePath)) {
        return $false
    }

    try {
        $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    }
    catch {
        return $false
    }

    if ($null -eq $state.settings -or $null -eq $state.instances) {
        return $false
    }

    $provisionStatus = [string]$state.instanceProvision.status
    if (-not [string]::Equals($provisionStatus, "Ready", [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $instanceCount = 0
    if ($state.settings.instanceCount) {
        $instanceCount = [Math]::Min([Math]::Max([int]$state.settings.instanceCount, 1), 4)
    }
    else {
        $instanceCount = @($state.instances).Count
    }

    $instances = @($state.instances | Select-Object -First $instanceCount)
    if ($instances.Count -eq 0) {
        return $false
    }

    foreach ($instance in $instances) {
        $launchDirectory = [string]$instance.launchDirectory
        if ([string]::IsNullOrWhiteSpace($launchDirectory)) {
            return $false
        }

        $beatSaberExe = Join-Path $launchDirectory "Beat Saber.exe"
        if (-not (Test-Path -LiteralPath $beatSaberExe -PathType Leaf)) {
            return $false
        }
    }

    return $true
}

function Wait-BeforeExit {
    Read-Host "Press Enter to close"
}

try {
    $runningControlPanelUrl = $ControlPanelUrl.TrimEnd("/")
    if (-not $Force -and (Test-HttpEndpoint "$runningControlPanelUrl/api/state")) {
        Write-Step "Control panel is already running."
        if (-not $NoBrowser) {
            Start-Process $runningControlPanelUrl
        }

        Write-Step "Dashboard: $runningControlPanelUrl"
        Write-Step "Re-run install.bat with -Force if you need to reinstall while the stack is running."
        Wait-BeforeExit
        exit 0
    }

    if (-not $Force -and (Test-InstalledStateReady)) {
        Write-Step "Replay recorder already appears to be installed."
        Write-Step "Run start.bat to open the control panel, or re-run install.bat with -Force to reinstall."
        Wait-BeforeExit
        exit 0
    }

    Write-Section "Prerequisites"
    Assert-Command "dotnet"
    Assert-Command "powershell.exe"
    Resolve-FfmpegPrerequisite | Out-Null
    $resolvedSource = Resolve-BeatSaberPath
    $presetSettings = Get-PresetSettings -PresetId $Preset
    Apply-PresetSettingsOverrides -PresetSettings $presetSettings
    $selectedInstanceCount = Resolve-InstanceCount -PresetSettings $presetSettings
    Set-PresetInstanceCount -PresetSettings $presetSettings -SelectedInstanceCount $selectedInstanceCount

    Write-Step "Preset: $Preset"
    Write-Step "Instance count: $selectedInstanceCount"
    Write-Step "Instances root: $([IO.Path]::GetFullPath($InstancesRoot))"
    if ($resolvedSource) {
        Write-Step "Source Beat Saber: $resolvedSource"
    }
    else {
        Write-Step "Source Beat Saber: not detected"
    }

    Write-Section "Instance folders"
    $instanceDirectories = @()
    $missingDirectories = @()
    for ($index = 0; $index -lt $presetSettings.InstanceCount; $index++) {
        $instanceDirectory = Get-InstanceDirectory -Index $index
        $instanceDirectories += $instanceDirectory
        if (-not (Test-Path (Join-Path $instanceDirectory "Beat Saber.exe"))) {
            $missingDirectories += $instanceDirectory
        }
    }

    if ($missingDirectories.Count -gt 0) {
        $baselineDirectory = $instanceDirectories[0]
        $baselineMissing = -not (Test-Path (Join-Path $baselineDirectory "Beat Saber.exe"))
        if ($baselineMissing -and -not $resolvedSource) {
            throw "Instance 1 is missing and no source Beat Saber install was detected. Re-run with -SourceBeatSaberPath `"C:\Path\To\Beat Saber`" or create the baseline folder first."
        }

        if (-not $CopyInstances) {
            $CopyInstances = Confirm-Step -Prompt "Create missing Beat Saber instance copies now? This can take a while." -DefaultYes
        }

        if (-not $CopyInstances) {
            throw "Install cannot continue until the instance folders exist."
        }

        if ($baselineMissing -and -not $CopyExistingSongsSelected -and -not $Force) {
            $CopyExistingSongsSelected = Confirm-Step -Prompt "Import existing CustomLevels and CustomWIPLevels from the source Beat Saber folder? Recommended: no."
        }

        if ($baselineMissing) {
            Copy-BeatSaberInstance -Source $resolvedSource -Target $baselineDirectory -CopySongs:$CopyExistingSongsSelected
        }

        foreach ($target in $missingDirectories) {
            if ([string]::Equals([IO.Path]::GetFullPath($target), [IO.Path]::GetFullPath($baselineDirectory), [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            Copy-BeatSaberInstance -Source $baselineDirectory -Target $target
        }
    }

    foreach ($instanceDirectory in $instanceDirectories) {
        if (-not (Test-Path (Join-Path $instanceDirectory "Beat Saber.exe"))) {
            throw "Beat Saber.exe was not found in instance folder: $instanceDirectory"
        }

        Write-Step "Instance ready: $instanceDirectory"
    }

    Write-Section "Stop existing recorder stack"
    $env:BSARR_CONTROL_PANEL_WORKSPACE = $Workspace
    $env:BSARR_CONTROL_PANEL_URL = $ControlPanelUrl.TrimEnd("/")
    if ($KeepExistingRecorderStack) {
        Write-Step "Leaving existing recorder processes alone."
    }
    elseif (Test-Path -LiteralPath $StopScript) {
        & $StopScript -SkipDisplayScaleRestore
    }

    Write-Section "Build"
    Set-Location $RepoRoot
    & dotnet build $ControlPanelProject --nologo
    if ($LASTEXITCODE -ne 0) { throw "Control panel build failed." }

    & dotnet build $RecorderHostProject --nologo
    if ($LASTEXITCODE -ne 0) { throw "Recorder host build failed." }

    & dotnet build $ProcessLoopbackProject -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "ProcessLoopback helper build failed." }

    if (-not $SkipPluginDeploy) {
        & dotnet build $PluginProject --nologo -p:BeatSaberDir="$($instanceDirectories[0])"
        if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }
    }

    if (-not $SkipPluginDeploy) {
        Write-Section "Plugin deployment"
        for ($index = 0; $index -lt $instanceDirectories.Count; $index++) {
            Backup-BeatSaberSettingsIni -InstanceDirectory $instanceDirectories[$index]
            Deploy-Plugin -InstanceDirectory $instanceDirectories[$index] -Index $index -PresetSettings $presetSettings
            Write-Step "Configured worker plugin in $($instanceDirectories[$index])"
        }
    }

    Write-Section "Control panel state"
    New-Item -ItemType Directory -Path $Workspace -Force | Out-Null
    Backup-File -Path $StatePath
    New-ControlPanelState -PresetSettings $presetSettings |
        ConvertTo-Json -Depth 20 |
        Set-Content -Path $StatePath -Encoding UTF8
    Write-Step "Wrote $StatePath"

    if ($NoStart) {
        Write-Section "Done"
        Write-Step "Install state is ready. Start later with start.bat."
        exit 0
    }

    Write-Section "Start recorder stack"
    $startArgs = @()
    if ($NoBrowser) {
        $startArgs += "-NoBrowser"
    }

    $startFfmpegPath = if (-not [string]::IsNullOrWhiteSpace($env:BSARR_FFMPEG_PATH)) {
        [string]$env:BSARR_FFMPEG_PATH
    }
    else {
        [string]$FfmpegPath
    }
    Assert-ValidPathArgument -ParameterName "FfmpegPath" -Value $startFfmpegPath
    if (-not [string]::IsNullOrWhiteSpace($startFfmpegPath)) {
        $startArgs += "-FfmpegPath"
        $startArgs += $startFfmpegPath
    }

    & $StartScript @startArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Recorder stack failed to start."
    }

    if (-not $SkipSharedFolderRepair) {
        Write-Section "Shared folders"
        if (Confirm-Step -Prompt "Repair shared song/content folder links now? Existing local folders are backed up first." -DefaultYes) {
            Invoke-ControlPanelPost -Path "/api/song-folders/repair"
            Write-Step "Shared folder repair requested."
        }
    }

    Write-Section "Final checks"
    Invoke-ControlPanelPost -Path "/api/instances/baseline/check"
    Write-Step "Baseline check requested."
    Write-Step "Dashboard: $ControlPanelUrl"
    Write-Step "Use Launch Games or Setup > Launch + Verify to start Beat Saber instances."
}
catch {
    Write-Host ""
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 1
}
