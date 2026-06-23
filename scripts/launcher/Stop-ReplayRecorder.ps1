param(
    [switch]$SkipDisplayScaleRestore,
    [switch]$StopGames,
    [string]$LogPath
)

$ErrorActionPreference = "Stop"

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

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
    if ($SkipDisplayScaleRestore) {
        Write-Host "[bs-replay-recorder] Display-scale restore skipped by request."
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Write-Host "[bs-replay-recorder] Stop logging is handled by DesktopHost; -LogPath is accepted for compatibility."
    }

    $hostCommand = Resolve-DesktopHostCommand
    $hostArgs = @($hostCommand.Arguments) + @("stop")
    if ($StopGames) {
        $hostArgs += "--stop-games"
    }

    $output = & $hostCommand.File @hostArgs 2>&1
    $exitCode = $LASTEXITCODE
    foreach ($line in @($output)) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw "Desktop host stop failed with exit code $exitCode."
    }
}
catch {
    Write-Host ""
    Write-Host "Stop failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    exit 1
}
