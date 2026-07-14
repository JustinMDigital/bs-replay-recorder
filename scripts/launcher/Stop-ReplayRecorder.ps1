param(
    [switch]$SkipDisplayScaleRestore,
    [switch]$StopGames,
    [string]$LogPath
)

$ErrorActionPreference = "Stop"

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Resolve-DesktopHostCommand {
    foreach ($runtimeRoot in @(
        (Join-Path $RepoRoot "runtime"),
        (Join-Path $RepoRoot "dist\runtime")
    )) {
        $publishedHost = Join-Path $runtimeRoot "desktop-host\BSAutoReplayRecorder.DesktopHost.exe"
        if (Test-Path -LiteralPath $publishedHost -PathType Leaf) {
            return [pscustomobject]@{
                File = $publishedHost
                Arguments = @("--repo-root", $RepoRoot)
                RuntimeRoot = $runtimeRoot
            }
        }
    }

    $projectPath = Join-Path $RepoRoot "src\BSAutoReplayRecorder.DesktopHost\BSAutoReplayRecorder.DesktopHost.csproj"
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Desktop host was not found. Expected $publishedHost or $projectPath."
    }

    return [pscustomobject]@{
        File = "dotnet"
        Arguments = @("run", "--project", $projectPath, "--", "--repo-root", $RepoRoot)
        RuntimeRoot = ""
    }
}

function Use-PrivateDotNetRuntime {
    param([string]$RuntimeRoot)

    if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
        return
    }

    $privateDotnetRoot = Join-Path $RuntimeRoot "dotnet"
    if (-not (Test-Path -LiteralPath (Join-Path $privateDotnetRoot "dotnet.exe") -PathType Leaf)) {
        return
    }

    $env:DOTNET_ROOT = $privateDotnetRoot
    $env:DOTNET_ROOT_X64 = $privateDotnetRoot
    $env:DOTNET_MULTILEVEL_LOOKUP = "0"
    Write-Host "[bs-replay-recorder] Using bundled .NET runtime."
}

try {
    if ($SkipDisplayScaleRestore) {
        Write-Host "[bs-replay-recorder] Display-scale restore skipped by request."
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Write-Host "[bs-replay-recorder] Stop logging is handled by DesktopHost; -LogPath is accepted for compatibility."
    }

    $hostCommand = Resolve-DesktopHostCommand
    Use-PrivateDotNetRuntime -RuntimeRoot $hostCommand.RuntimeRoot
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
