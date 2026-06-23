param(
    [switch]$NoBrowser,
    [string]$FfmpegPath,
    [switch]$RequireInstalled
)

$ErrorActionPreference = "Stop"

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Write-Step {
    param([string]$Message)
    Write-Host "[bs-replay-recorder] $Message"
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

function Resolve-DesktopHostCommand {
    $publishedHost = Join-Path $RepoRoot "runtime\desktop-host\BSAutoReplayRecorder.DesktopHost.exe"
    if (Test-Path -LiteralPath $publishedHost -PathType Leaf) {
        return [pscustomobject]@{
            File = $publishedHost
            Arguments = @("--repo-root", $RepoRoot)
        }
    }

    $projectPath = Join-Path $RepoRoot "src\BSAutoReplayRecorder.DesktopHost\BSAutoReplayRecorder.DesktopHost.csproj"
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Desktop host was not found. Expected $publishedHost or $projectPath."
    }

    return [pscustomobject]@{
        File = "dotnet"
        Arguments = @("run", "--project", $projectPath, "--", "--repo-root", $RepoRoot)
    }
}

try {
    Assert-ValidPathArgument -ParameterName "FfmpegPath" -Value $FfmpegPath
    if (-not [string]::IsNullOrWhiteSpace($FfmpegPath)) {
        $env:BSARR_FFMPEG_PATH = Resolve-RepoRelativePath $FfmpegPath
    }

    $hostCommand = Resolve-DesktopHostCommand
    $hostArgs = @($hostCommand.Arguments) + @("start")
    if ($RequireInstalled) {
        $hostArgs += "--require-installed"
    }

    & $hostCommand.File @hostArgs
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Desktop host start failed with exit code $exitCode."
    }

    $statusArgs = @($hostCommand.Arguments) + @("status")
    $statusOutput = & $hostCommand.File @statusArgs 2>&1
    $readyLine = @($statusOutput | Where-Object { ([string]$_) -match '^READY\s+' } | Select-Object -Last 1)
    $dashboardUrl = if ($readyLine.Count -gt 0) {
        ([string]$readyLine[-1]).Substring(6).Trim()
    }
    else {
        "http://127.0.0.1:5770"
    }

    if (-not $NoBrowser) {
        Start-Process $dashboardUrl
        Write-Step "Opened dashboard in your browser."
    }

    Write-Step "Dashboard: $dashboardUrl"
}
catch {
    Write-Host ""
    Write-Host "Start failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    exit 1
}
