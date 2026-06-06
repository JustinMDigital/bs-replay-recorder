param(
    [int]$ScreenIndex = 1,
    [int]$ScalePercent = 0,
    [switch]$List,
    [string]$SetDpiPath
)

$ErrorActionPreference = "Stop"

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Resolve-SetDpiPath {
    if (-not [string]::IsNullOrWhiteSpace($SetDpiPath)) {
        if (-not (Test-Path -LiteralPath $SetDpiPath)) {
            throw "SetDpi.exe was not found: $SetDpiPath"
        }

        return (Resolve-Path -LiteralPath $SetDpiPath).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($env:BSARR_SETDPI_PATH)) {
        if (-not (Test-Path -LiteralPath $env:BSARR_SETDPI_PATH)) {
            throw "BSARR_SETDPI_PATH points to a missing file: $env:BSARR_SETDPI_PATH"
        }

        return (Resolve-Path -LiteralPath $env:BSARR_SETDPI_PATH).Path
    }

    $candidate = Join-Path $RepoRoot "tools\SetDpi\SetDpi.exe"
    if (Test-Path -LiteralPath $candidate) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    throw "SetDpi.exe was not found. Expected tools\SetDpi\SetDpi.exe or BSARR_SETDPI_PATH."
}

function Invoke-SetDpi {
    param([string[]]$Arguments)

    $output = & $script:SetDpiExe @Arguments 2>&1
    $text = ($output -join "`n").Trim()
    if ($LASTEXITCODE -ne 0 -or $text -match "Invalid Monitor") {
        throw "SetDpi failed: $text"
    }

    return $text
}

function Limit-Value {
    param(
        [int]$Value,
        [int]$Minimum,
        [int]$Maximum
    )

    if ($Value -lt $Minimum) {
        return $Minimum
    }

    if ($Value -gt $Maximum) {
        return $Maximum
    }

    return $Value
}

$SetDpiExe = Resolve-SetDpiPath

if ($List) {
    $rows = @()
    for ($monitorNumber = 1; $monitorNumber -le 16; $monitorNumber++) {
        $output = & $SetDpiExe value $monitorNumber 2>&1
        $text = ($output -join "`n").Trim()
        if ($text -match "Invalid Monitor") {
            continue
        }

        $rows += [pscustomobject]@{
            ScreenIndex = $monitorNumber - 1
            MonitorNumber = $monitorNumber
            ScalePercent = [int]$text
        }
    }

    $rows
    return
}

if ($ScalePercent -le 0) {
    $monitorNumber = $ScreenIndex + 1
    [pscustomobject]@{
        ScreenIndex = $ScreenIndex
        MonitorNumber = $monitorNumber
        ScalePercent = [int](Invoke-SetDpi -Arguments @("value", [string]$monitorNumber))
    }
    return
}

$ScalePercent = Limit-Value -Value $ScalePercent -Minimum 100 -Maximum 500
$targetMonitorNumber = $ScreenIndex + 1
Invoke-SetDpi -Arguments @([string]$ScalePercent, [string]$targetMonitorNumber) | Out-Null
[pscustomobject]@{
    ScreenIndex = $ScreenIndex
    MonitorNumber = $targetMonitorNumber
    ScalePercent = [int](Invoke-SetDpi -Arguments @("value", [string]$targetMonitorNumber))
}
