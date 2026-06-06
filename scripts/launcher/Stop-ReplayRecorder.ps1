param(
    [switch]$SkipDisplayScaleRestore,
    [switch]$StopGames
)

$ErrorActionPreference = "Stop"

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$SettingsPath = Join-Path $RepoRoot "settings.json"

function Write-Step {
    param([string]$Message)
    Write-Host "[bs-replay-recorder] $Message"
}

function Read-LocalSettingsFile {
    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Step "Could not read settings.json, so using stop defaults: $($_.Exception.Message)"
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

$LocalSettings = Read-LocalSettingsFile
$SettingsWorkspaceValue = Get-LocalSettingString -Settings $LocalSettings -Names @("workspace", "workspaceDirectory")
$SettingsWorkspace = if ([string]::IsNullOrWhiteSpace($SettingsWorkspaceValue)) {
    $null
}
else {
    Resolve-RepoRelativePath $SettingsWorkspaceValue
}
$RestoreDisplayScalePercent = Get-LocalSettingInt -Settings $LocalSettings -Names @("restoreDisplayScalePercent")
if ($null -eq $RestoreDisplayScalePercent -or $RestoreDisplayScalePercent -lt 100 -or $RestoreDisplayScalePercent -gt 500) {
    $RestoreDisplayScalePercent = 150
}

$ControlPanelProjectDir = Join-Path $RepoRoot "src\BSAutoReplayRecorder.ControlPanel"
$EnvironmentWorkspace = if ([string]::IsNullOrWhiteSpace($env:BSARR_CONTROL_PANEL_WORKSPACE)) {
    $null
}
else {
    [IO.Path]::GetFullPath($env:BSARR_CONTROL_PANEL_WORKSPACE)
}
$WorkspacePaths = @(
    $EnvironmentWorkspace,
    $SettingsWorkspace,
    (Join-Path $ControlPanelProjectDir "ControlPanelWorkspace"),
    (Join-Path $RepoRoot "ControlPanelWorkspace")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

function Test-ManagedProcess {
    param([int]$ProcessId)

    try {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId"
        if (-not $process) {
            return $false
        }

        return $process.CommandLine -and $process.CommandLine.Contains("BSAutoReplayRecorder")
    }
    catch {
        return $false
    }
}

function Get-OrphanRecorderProcesses {
    param([object[]]$KnownProcessIds)

    Get-CimInstance Win32_Process | Where-Object {
        @($KnownProcessIds) -notcontains $_.ProcessId -and
        $_.CommandLine -and
        $_.CommandLine.Contains($RepoRoot) -and
        (
            ($_.Name -eq "dotnet.exe" -and $_.CommandLine -match "BSAutoReplayRecorder\.(ControlPanel|RecorderHost)") -or
            ($_.Name -match "^BSAutoReplayRecorder\.(ControlPanel|RecorderHost)(\.exe)?$")
        )
    }
}

function Get-ControlPanelStates {
    foreach ($workspace in $WorkspacePaths) {
        $stateFile = Join-Path $workspace "control-panel-state.json"
        if (-not (Test-Path $stateFile)) {
            continue
        }

        try {
            Get-Content $stateFile -Raw | ConvertFrom-Json
        }
        catch {
            Write-Step "Could not read control panel state from $stateFile`: $($_.Exception.Message)"
        }
    }
}

function Stop-TrackedBeatSaberProcesses {
    $knownProcessIds = New-Object 'System.Collections.Generic.HashSet[int]'
    $knownExecutablePaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    foreach ($state in Get-ControlPanelStates) {
        foreach ($instance in @($state.instances)) {
            if ($instance.gameProcessId) {
                [void]$knownProcessIds.Add([int]$instance.gameProcessId)
            }

            $launchDirectory = [string]$instance.launchDirectory
            if (-not [string]::IsNullOrWhiteSpace($launchDirectory)) {
                [void]$knownExecutablePaths.Add((Join-Path $launchDirectory "Beat Saber.exe"))
            }
        }
    }

    $candidates = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq "Beat Saber.exe" -and
        (
            $knownProcessIds.Contains([int]$_.ProcessId) -or
            (-not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and $knownExecutablePaths.Contains([string]$_.ExecutablePath))
        )
    })

    if ($candidates.Count -eq 0) {
        Write-Step "No tracked Beat Saber processes were found."
        return
    }

    foreach ($candidate in $candidates) {
        try {
            Stop-Process -Id ([int]$candidate.ProcessId) -Force
            Write-Step "Stopped Beat Saber, pid $($candidate.ProcessId)."
        }
        catch {
            Write-Step "Could not stop Beat Saber pid $($candidate.ProcessId): $($_.Exception.Message)"
        }
    }
}

try {
    $stoppedAny = $false
    $seenProcessIds = @()

    if ($StopGames) {
        Stop-TrackedBeatSaberProcesses
    }

    foreach ($workspace in $WorkspacePaths) {
        $pidFile = Join-Path $workspace "started-processes.json"
        if (-not (Test-Path $pidFile)) {
            continue
        }

        $records = Get-Content $pidFile -Raw | ConvertFrom-Json
        foreach ($record in $records) {
            $processId = [int]$record.pid
            $seenProcessIds += $processId
            $name = [string]$record.name
            $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if (-not $process) {
                Write-Step "$name is not running."
                continue
            }

            if (-not (Test-ManagedProcess $processId)) {
                Write-Step "Skipping pid $processId because it no longer looks like a replay-recorder process."
                continue
            }

            Stop-Process -Id $processId -Force
            $stoppedAny = $true
            Write-Step "Stopped $name, pid $processId."
        }

        Remove-Item -Path $pidFile -Force
    }

    foreach ($process in @(Get-OrphanRecorderProcesses -KnownProcessIds $seenProcessIds)) {
        Stop-Process -Id $process.ProcessId -Force
        $stoppedAny = $true
        Write-Step "Stopped orphaned recorder process $($process.ProcessId)."
    }

    if (-not $stoppedAny) {
        Write-Step "No started recorder processes were found."
    }

    if (-not $SkipDisplayScaleRestore) {
        $scaleScript = Join-Path $RepoRoot "scripts\display\Set-RecordingDisplayScale.ps1"
        if (Test-Path -LiteralPath $scaleScript) {
            try {
                & $scaleScript -ScreenIndex 1 -ScalePercent $RestoreDisplayScalePercent | Out-Null
                Write-Step "Restored recording display scale to $RestoreDisplayScalePercent%."
            }
            catch {
                Write-Step "Could not restore recording display scale: $($_.Exception.Message)"
            }
        }
    }
}
catch {
    Write-Host ""
    Write-Host "Stop failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 1
}
