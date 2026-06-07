param(
    [switch]$NoBrowser,
    [string]$FfmpegPath,
    [switch]$RequireInstalled
)

$ErrorActionPreference = "Stop"
$LauncherBoundParameters = @{} + $PSBoundParameters

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$SettingsPath = Join-Path $RepoRoot "settings.json"
$SettingsExamplePath = Join-Path $RepoRoot "settings.example.json"

function Write-Step {
    param([string]$Message)
    Write-Host "[bs-replay-recorder] $Message"
}

function Ensure-LocalSettingsFile {
    if (Test-Path -LiteralPath $SettingsPath) {
        return
    }

    if ($RequireInstalled) {
        throw "settings.json was not found. Run install.bat first, then start with start.bat."
    }

    if (-not (Test-Path -LiteralPath $SettingsExamplePath)) {
        return
    }

    Copy-Item -LiteralPath $SettingsExamplePath -Destination $SettingsPath
    Write-Step "Created local settings from settings.example.json."
}

function Read-LocalSettingsFile {
    Ensure-LocalSettingsFile
    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Step "Could not read settings.json, so using launcher defaults: $($_.Exception.Message)"
        return $null
    }
}

function Get-LocalSettingString {
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
            $text = ([string]$property.Value).Trim()
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                return $text
            }
        }
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

$LocalSettings = Read-LocalSettingsFile
if (-not $LauncherBoundParameters.ContainsKey("FfmpegPath")) {
    $settingsFfmpegPath = Get-LocalSettingString -Settings $LocalSettings -Names @("ffmpegPath")
    if ($settingsFfmpegPath) {
        $FfmpegPath = Resolve-RepoRelativePath $settingsFfmpegPath
    }
}
Assert-ValidPathArgument -ParameterName "FfmpegPath" -Value $FfmpegPath

$ControlPanelProject = Join-Path $RepoRoot "src\BSAutoReplayRecorder.ControlPanel\BSAutoReplayRecorder.ControlPanel.csproj"
$RecorderHostProject = Join-Path $RepoRoot "src\BSAutoReplayRecorder.RecorderHost\BSAutoReplayRecorder.RecorderHost.csproj"
$ProcessLoopbackHelperProject = Join-Path $RepoRoot "tools\ProcessLoopbackCapture.Managed\ProcessLoopbackCapture.Managed.csproj"
$ControlPanelProjectDir = Split-Path -Parent $ControlPanelProject
$Workspace = if ([string]::IsNullOrWhiteSpace($env:BSARR_CONTROL_PANEL_WORKSPACE)) {
    $settingsWorkspace = Get-LocalSettingString -Settings $LocalSettings -Names @("workspace", "workspaceDirectory")
    if ([string]::IsNullOrWhiteSpace($settingsWorkspace)) {
        Join-Path $RepoRoot "ControlPanelWorkspace"
    }
    else {
        Resolve-RepoRelativePath $settingsWorkspace
    }
}
else {
    [IO.Path]::GetFullPath($env:BSARR_CONTROL_PANEL_WORKSPACE)
}
$ControlPanelUrl = if ([string]::IsNullOrWhiteSpace($env:BSARR_CONTROL_PANEL_URL)) {
    $settingsControlPanelUrl = Get-LocalSettingString -Settings $LocalSettings -Names @("controlPanelUrl", "bindUrl")
    if ([string]::IsNullOrWhiteSpace($settingsControlPanelUrl)) {
        "http://127.0.0.1:5770"
    }
    else {
        $settingsControlPanelUrl.TrimEnd("/")
    }
}
else {
    $env:BSARR_CONTROL_PANEL_URL.TrimEnd("/")
}
$PidFile = Join-Path $Workspace "started-processes.json"
$LogDirectory = Join-Path $Workspace "Logs"
$BrowserProfileDirectory = Join-Path $Workspace "ControlPanelBrowserProfile"

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. Install the .NET SDK and try again."
    }
}

function Convert-ToArgumentString {
    param([string[]]$Arguments)

    $quoted = foreach ($argument in $Arguments) {
        if ($argument -match '[\s"]') {
            '"' + ($argument -replace '"', '\"') + '"'
        }
        else {
            $argument
        }
    }

    return ($quoted -join " ")
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

function Wait-ForHttpEndpoint {
    param(
        [string]$Name,
        [string]$Url,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-HttpEndpoint $Url) {
            Write-Step "$Name is ready at $Url"
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    Write-Step "$Name did not answer within $TimeoutSeconds seconds. Check its log files in $LogDirectory."
    return $false
}

function Resolve-ControlPanelBrowserPath {
    if (-not [string]::IsNullOrWhiteSpace($env:BSARR_BROWSER_PATH)) {
        if (Test-Path -LiteralPath $env:BSARR_BROWSER_PATH -PathType Leaf) {
            return (Resolve-Path -LiteralPath $env:BSARR_BROWSER_PATH).Path
        }

        Write-Step "BSARR_BROWSER_PATH points to a missing file, so using the default browser fallback: $env:BSARR_BROWSER_PATH"
        return $null
    }

    foreach ($name in @("msedge.exe", "chrome.exe")) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) {
            return $command.Source
        }
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft\Edge\Application\msedge.exe"),
        (Join-Path $env:ProgramFiles "Microsoft\Edge\Application\msedge.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Google\Chrome\Application\chrome.exe"),
        (Join-Path $env:ProgramFiles "Google\Chrome\Application\chrome.exe"),
        (Join-Path $env:LOCALAPPDATA "Google\Chrome\Application\chrome.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Start-ControlPanelBrowser {
    Start-Process $ControlPanelUrl
    Write-Step "Opened dashboard in your browser."
    return $null
}

function Add-StartedProcessRecords {
    param([object[]]$Records)

    if ($null -eq $Records -or $Records.Count -eq 0) {
        return
    }

    $recordsToWrite = @()
    if (Test-Path -LiteralPath $PidFile) {
        try {
            $recordsToWrite += @(Get-Content -LiteralPath $PidFile -Raw | ConvertFrom-Json)
        }
        catch {
            Write-Step "Replacing unreadable process tracking file: $PidFile"
        }
    }

    $recordsToWrite += @($Records)
    $recordsToWrite | ConvertTo-Json -Depth 6 | Set-Content -Path $PidFile -Encoding UTF8
}

function Assert-InstalledStateReady {
    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        throw "settings.json was not found. Run install.bat first, then start with start.bat."
    }

    if ($null -eq $LocalSettings) {
        throw "settings.json could not be read. Fix it or re-run install.bat before using start.bat."
    }

    $statePath = Join-Path $Workspace "control-panel-state.json"
    if (-not (Test-Path -LiteralPath $statePath)) {
        throw "Installer state was not found at $statePath. Run install.bat first."
    }

    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Installer state could not be read at $statePath. Re-run install.bat before using start.bat."
    }

    if ($null -eq $state.settings -or $null -eq $state.instances) {
        throw "Installer state is incomplete. Re-run install.bat before using start.bat."
    }

    $provisionStatus = [string]$state.instanceProvision.status
    if (-not [string]::Equals($provisionStatus, "Ready", [StringComparison]::OrdinalIgnoreCase)) {
        if ([string]::IsNullOrWhiteSpace($provisionStatus)) {
            $provisionStatus = "missing"
        }

        throw "Managed instance provisioning is not ready (status: $provisionStatus). Re-run install.bat."
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
        throw "Installer state has no managed Beat Saber instances. Re-run install.bat."
    }

    foreach ($instance in $instances) {
        $launchDirectory = [string]$instance.launchDirectory
        if ([string]::IsNullOrWhiteSpace($launchDirectory)) {
            throw "Installer state is missing a managed Beat Saber launch directory. Re-run install.bat."
        }

        $beatSaberExe = Join-Path $launchDirectory "Beat Saber.exe"
        if (-not (Test-Path -LiteralPath $beatSaberExe -PathType Leaf)) {
            throw "Managed Beat Saber instance is missing Beat Saber.exe: $launchDirectory. Re-run install.bat."
        }
    }
}

function Resolve-PreferredFfmpegPath {
    if (-not [string]::IsNullOrWhiteSpace($FfmpegPath)) {
        if (-not (Test-Path $FfmpegPath)) {
            throw "Configured FFmpeg path was not found: $FfmpegPath"
        }

        return (Resolve-Path $FfmpegPath).Path
    }

    $candidates = @()
    $wingetPackages = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    if (Test-Path $wingetPackages) {
        $candidates += Get-ChildItem -LiteralPath $wingetPackages -Recurse -Filter "ffmpeg.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*Gyan.FFmpeg*" } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -ExpandProperty FullName
    }

    $candidates += @(
        (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\ffmpeg.exe"),
        "C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        "C:\ProgramData\chocolatey\bin\ffmpeg.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    $command = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "FFmpeg was not found. Install FFmpeg or pass -FfmpegPath."
}

function Resolve-PreferredFfprobePath {
    param([string]$ResolvedFfmpegPath)

    if (-not [string]::IsNullOrWhiteSpace($env:BSARR_FFPROBE_PATH)) {
        if (-not (Test-Path $env:BSARR_FFPROBE_PATH)) {
            throw "Configured ffprobe path was not found: $env:BSARR_FFPROBE_PATH"
        }

        return (Resolve-Path $env:BSARR_FFPROBE_PATH).Path
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($ResolvedFfmpegPath)) {
        $ffmpegDirectory = Split-Path -Parent $ResolvedFfmpegPath
        if (-not [string]::IsNullOrWhiteSpace($ffmpegDirectory)) {
            $candidates += Join-Path $ffmpegDirectory "ffprobe.exe"
        }
    }

    $wingetPackages = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    if (Test-Path $wingetPackages) {
        $candidates += Get-ChildItem -LiteralPath $wingetPackages -Recurse -Filter "ffprobe.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*Gyan.FFmpeg*" } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -ExpandProperty FullName
    }

    $candidates += @(
        (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\ffprobe.exe"),
        "C:\Program Files\ffmpeg\bin\ffprobe.exe",
        "C:\Program Files (x86)\ffmpeg\bin\ffprobe.exe",
        "C:\ProgramData\chocolatey\bin\ffprobe.exe",
        "C:\Program Files\ShareX\ffprobe.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    $command = Get-Command ffprobe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "ffprobe was not found. Install FFmpeg/ffprobe or set BSARR_FFPROBE_PATH."
}

function Read-InstancePlan {
    $statePath = Join-Path $Workspace "control-panel-state.json"
    $instanceCount = 3
    $instances = @()

    if (Test-Path $statePath) {
        try {
            $state = Get-Content $statePath -Raw | ConvertFrom-Json
            if ($state.settings.instanceCount) {
                $instanceCount = [Math]::Min([Math]::Max([int]$state.settings.instanceCount, 1), 4)
            }

            if ($state.instances) {
                $instances = @($state.instances | Select-Object -First $instanceCount)
            }
        }
        catch {
            Write-Step "Could not read saved control-panel state, so defaulting to 3 recorder hosts."
        }
    }

    if ($instances.Count -eq 0) {
        for ($index = 0; $index -lt $instanceCount; $index++) {
            $port = 5757 + $index
            $instances += [pscustomobject]@{
                index = $index
                recorderHostUrl = "http://127.0.0.1:$port"
                outputDirectory = Join-Path $Workspace ("Recordings\Instance " + ($index + 1))
            }
        }
    }

    return $instances
}

function Read-CaptureDefaults {
    $defaults = [pscustomobject]@{
        targetFps = 60
        captureWidth = 1920
        captureHeight = 1080
        encoder = "h264_nvenc"
        videoBitrateKbps = 16000
        outputFormat = "mkv"
        monitorIndex = 1
        qualityMode = "Balanced"
        audioMode = "ProcessLoopback"
        audioBitrateKbps = 192
        audioSampleRate = 48000
        audioChannels = 2
    }

    $statePath = Join-Path $Workspace "control-panel-state.json"
    if (-not (Test-Path $statePath)) {
        return $defaults
    }

    try {
        $state = Get-Content $statePath -Raw | ConvertFrom-Json
        if ($state.settings) {
            foreach ($name in @("targetFps", "captureWidth", "captureHeight", "encoder", "videoBitrateKbps", "outputFormat", "monitorIndex", "qualityMode", "audioMode", "audioBitrateKbps", "audioSampleRate", "audioChannels")) {
                $value = $state.settings.$name
                if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
                    $defaults.$name = $value
                }
            }
        }
    }
    catch {
        Write-Step "Could not read saved capture settings, so using recorder defaults."
    }

    return $defaults
}

function Get-PortFromUrl {
    param([string]$Url)

    $uri = [Uri]$Url
    if ($uri.Port -le 0) {
        throw "Recorder host URL does not include a port: $Url"
    }

    return $uri.Port
}

function Ensure-RecorderHostConfig {
    param(
        [int]$Index,
        [int]$Port,
        [string]$OutputDirectory,
        [string]$ResolvedFfmpegPath,
        [pscustomobject]$CaptureDefaults
    )

    $configPath = Join-Path $Workspace "recorder-host-$Port.settings.json"
    $offsetColumn = $Index % 2
    $offsetRow = [Math]::Floor($Index / 2)
    $offsetX = $offsetColumn * 1920
    $offsetY = $offsetRow * 1080
    $argumentTemplate = '-hide_banner -y -f lavfi -i "ddagrab=output_idx={monitorIndex}:draw_mouse=0:framerate={fps}:offset_x=' + $offsetX + ':offset_y=' + $offsetY + ':video_size={videoSize}" {audioInput} -map 0:v:0 {audioMap} -c:v {encoder} -preset {encoderPreset} -b:v {videoBitrate} {audioOutputOptions} {containerFlags} {output}'
    $outputFormat = ([string]$CaptureDefaults.outputFormat).Trim().TrimStart(".").ToLowerInvariant()
    if ($outputFormat -ne "mp4" -and $outputFormat -ne "mkv") {
        $outputFormat = "mkv"
    }
    $requestedAudioMode = [string]$CaptureDefaults.audioMode
    $audioDeviceName = ""
    $audioMode = if ([string]::Equals($requestedAudioMode, "ProcessLoopback", [StringComparison]::OrdinalIgnoreCase)) {
        "ProcessLoopback"
    }
    else {
        "None"
    }
    $processLoopbackCapturePath = Join-Path $RepoRoot "tools\ProcessLoopbackCapture.Managed\bin\Release\net10.0-windows10.0.20348.0\win-x64\ProcessLoopbackCapture.exe"

    $settings = [ordered]@{
        bindUrl = "http://127.0.0.1:$Port"
        ffmpegPath = $ResolvedFfmpegPath
        processLoopbackCapturePath = $processLoopbackCapturePath
        outputDirectory = $OutputDirectory
        outputExtension = ".$outputFormat"
        overwriteExisting = $false
        stopTimeoutSeconds = 30
        startupProbeMilliseconds = 500
        defaultWindowTitle = "Beat Saber"
        defaultTargetFps = [int]$CaptureDefaults.targetFps
        defaultCaptureWidth = [int]$CaptureDefaults.captureWidth
        defaultCaptureHeight = [int]$CaptureDefaults.captureHeight
        defaultEncoder = [string]$CaptureDefaults.encoder
        defaultVideoBitrateKbps = [int]$CaptureDefaults.videoBitrateKbps
        defaultMonitorIndex = [int]$CaptureDefaults.monitorIndex
        defaultQualityMode = [string]$CaptureDefaults.qualityMode
        defaultAudioMode = $audioMode
        defaultAudioDeviceName = $audioDeviceName
        defaultAudioBitrateKbps = [int]$CaptureDefaults.audioBitrateKbps
        defaultAudioSampleRate = [int]$CaptureDefaults.audioSampleRate
        defaultAudioChannels = [int]$CaptureDefaults.audioChannels
        argumentTemplate = $argumentTemplate
    }

    if (Test-Path $configPath) {
        try {
            $existing = Get-Content $configPath -Raw | ConvertFrom-Json
            $existing.bindUrl = "http://127.0.0.1:$Port"
            $existing.ffmpegPath = $ResolvedFfmpegPath
            $existing | Add-Member -NotePropertyName "processLoopbackCapturePath" -NotePropertyValue $processLoopbackCapturePath -Force
            $existing | Add-Member -NotePropertyName "stopTimeoutSeconds" -NotePropertyValue 30 -Force
            $existing.outputDirectory = $OutputDirectory
            $existing | Add-Member -NotePropertyName "outputExtension" -NotePropertyValue ".$outputFormat" -Force
            $existing | Add-Member -NotePropertyName "defaultTargetFps" -NotePropertyValue ([int]$CaptureDefaults.targetFps) -Force
            $existing | Add-Member -NotePropertyName "defaultCaptureWidth" -NotePropertyValue ([int]$CaptureDefaults.captureWidth) -Force
            $existing | Add-Member -NotePropertyName "defaultCaptureHeight" -NotePropertyValue ([int]$CaptureDefaults.captureHeight) -Force
            $existing | Add-Member -NotePropertyName "defaultEncoder" -NotePropertyValue ([string]$CaptureDefaults.encoder) -Force
            $existing | Add-Member -NotePropertyName "defaultVideoBitrateKbps" -NotePropertyValue ([int]$CaptureDefaults.videoBitrateKbps) -Force
            $existing | Add-Member -NotePropertyName "defaultMonitorIndex" -NotePropertyValue ([int]$CaptureDefaults.monitorIndex) -Force
            $existing | Add-Member -NotePropertyName "defaultQualityMode" -NotePropertyValue ([string]$CaptureDefaults.qualityMode) -Force
            $existing | Add-Member -NotePropertyName "defaultAudioMode" -NotePropertyValue $audioMode -Force
            $existing | Add-Member -NotePropertyName "defaultAudioDeviceName" -NotePropertyValue $audioDeviceName -Force
            $existing | Add-Member -NotePropertyName "defaultAudioBitrateKbps" -NotePropertyValue ([int]$CaptureDefaults.audioBitrateKbps) -Force
            $existing | Add-Member -NotePropertyName "defaultAudioSampleRate" -NotePropertyValue ([int]$CaptureDefaults.audioSampleRate) -Force
            $existing | Add-Member -NotePropertyName "defaultAudioChannels" -NotePropertyValue ([int]$CaptureDefaults.audioChannels) -Force
            $existing.argumentTemplate = $argumentTemplate

            $existing | ConvertTo-Json -Depth 8 | Set-Content -Path $configPath -Encoding UTF8
            return $configPath
        }
        catch {
            Write-Step "Replacing unreadable recorder host config: $configPath"
        }
    }

    $settings | ConvertTo-Json -Depth 8 | Set-Content -Path $configPath -Encoding UTF8
    return $configPath
}

function Start-DotNetService {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [string]$OutLog,
        [string]$ErrLog,
        [string]$WorkingDirectory = $RepoRoot
    )

    $argumentString = Convert-ToArgumentString $Arguments
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $argumentString `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $OutLog `
        -RedirectStandardError $ErrLog `
        -WindowStyle Hidden `
        -PassThru

    Write-Step "Started $Name, pid $($process.Id)"
    return [pscustomobject]@{
        name = $Name
        pid = $process.Id
        startedAt = (Get-Date).ToString("o")
        outLog = $OutLog
        errLog = $ErrLog
    }
}

try {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null

    if (Test-HttpEndpoint "$ControlPanelUrl/api/state") {
        Write-Step "Control panel is already running."
        if (-not $NoBrowser) {
            $browserRecord = Start-ControlPanelBrowser
            if ($browserRecord) {
                Add-StartedProcessRecords -Records @($browserRecord)
            }
        }

        Write-Step "Dashboard: $ControlPanelUrl"
        Write-Step "Logs: $LogDirectory"
        exit 0
    }

    if ($RequireInstalled) {
        Assert-InstalledStateReady
    }

    Assert-Command "dotnet"
    New-Item -ItemType Directory -Path $Workspace -Force | Out-Null
    $env:BSARR_CONTROL_PANEL_WORKSPACE = $Workspace
    $env:BSARR_CONTROL_PANEL_URL = $ControlPanelUrl
    Set-Location $RepoRoot

    Write-Step "Building control panel..."
    & dotnet build $ControlPanelProject --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Control panel build failed."
    }

    Write-Step "Building recorder host..."
    & dotnet build $RecorderHostProject --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Recorder host build failed."
    }

    Write-Step "Building process-loopback helper..."
    & dotnet build $ProcessLoopbackHelperProject -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Process-loopback helper build failed."
    }

    $started = @()
    $instances = Read-InstancePlan
    $captureDefaults = Read-CaptureDefaults
    $resolvedFfmpegPath = Resolve-PreferredFfmpegPath
    $resolvedFfprobePath = Resolve-PreferredFfprobePath -ResolvedFfmpegPath $resolvedFfmpegPath
    $env:BSARR_FFMPEG_PATH = $resolvedFfmpegPath
    $env:BSARR_FFPROBE_PATH = $resolvedFfprobePath
    Write-Step "Using FFmpeg: $resolvedFfmpegPath"
    Write-Step "Using ffprobe: $resolvedFfprobePath"

    foreach ($instance in $instances) {
        $url = [string]$instance.recorderHostUrl
        if ([string]::IsNullOrWhiteSpace($url)) {
            continue
        }

        $port = Get-PortFromUrl $url
        $outputDirectory = [string]$instance.outputDirectory
        if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
            $outputDirectory = Join-Path $Workspace ("Recordings\Instance " + ([int]$instance.index + 1))
        }

        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        $configPath = Ensure-RecorderHostConfig -Index ([int]$instance.index) -Port $port -OutputDirectory $outputDirectory -ResolvedFfmpegPath $resolvedFfmpegPath -CaptureDefaults $captureDefaults

        $healthUrl = "$url/health"
        if (Test-HttpEndpoint $healthUrl) {
            Write-Step "Recorder host $port is already running."
            continue
        }

        $started += Start-DotNetService `
            -Name "recorder host $port" `
            -Arguments @("run", "--no-build", "--project", $RecorderHostProject, "--", "serve", "--config", $configPath) `
            -OutLog (Join-Path $LogDirectory "recorder-host-$port.out.log") `
            -ErrLog (Join-Path $LogDirectory "recorder-host-$port.err.log")
    }

    if (Test-HttpEndpoint "$ControlPanelUrl/api/state") {
        Write-Step "Control panel is already running."
    }
    else {
        $started += Start-DotNetService `
            -Name "control panel" `
            -Arguments @("run", "--no-build", "--project", $ControlPanelProject) `
            -OutLog (Join-Path $LogDirectory "control-panel.out.log") `
            -ErrLog (Join-Path $LogDirectory "control-panel.err.log") `
            -WorkingDirectory $ControlPanelProjectDir
    }

    if ($started.Count -gt 0) {
        Add-StartedProcessRecords -Records $started
    }

    foreach ($instance in $instances) {
        $url = [string]$instance.recorderHostUrl
        if (-not [string]::IsNullOrWhiteSpace($url)) {
            Wait-ForHttpEndpoint -Name ("Recorder host " + (Get-PortFromUrl $url)) -Url "$url/health" -TimeoutSeconds 20 | Out-Null
        }
    }

    Wait-ForHttpEndpoint -Name "Control panel" -Url "$ControlPanelUrl/api/state" -TimeoutSeconds 30 | Out-Null

    if (-not $NoBrowser) {
        $browserRecord = Start-ControlPanelBrowser
        if ($browserRecord) {
            Add-StartedProcessRecords -Records @($browserRecord)
        }
    }

    Write-Step "Dashboard: $ControlPanelUrl"
    Write-Step "Logs: $LogDirectory"
}
catch {
    Write-Host ""
    Write-Host "Start failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 1
}
