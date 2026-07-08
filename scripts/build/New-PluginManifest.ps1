param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [string]$GameVersion,

    [string]$PluginVersion = "0.1.0",

    [string]$BSIPADependencyVersion = "^4.3.6",

    [string]$BeatLeaderDependencyVersion = ">=0.9.33"
)

$ErrorActionPreference = "Stop"

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$manifest = [ordered]@{
    '$schema' = "https://raw.githubusercontent.com/beat-saber-modding-group/BSIPA-MetadataFileSchema/master/Schema.json"
    author = "Auto Replay Recorder"
    description = "Automated Beat Saber replay capture with recorder-host and BeatLeader playback."
    gameVersion = $GameVersion
    id = "BSAutoReplayRecorder"
    name = "Beat Saber Auto Replay Recorder"
    version = $PluginVersion
    dependsOn = [ordered]@{
        BSIPA = $BSIPADependencyVersion
        BeatLeader = $BeatLeaderDependencyVersion
    }
    files = @(
        "Plugins/BSAutoReplayRecorder.Plugin.dll",
        "Libs/BSAutoReplayRecorder.Core.dll"
    )
}

$manifest |
    ConvertTo-Json -Depth 10 |
    Set-Content -Path $OutputPath -Encoding UTF8
