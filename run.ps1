[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Dw2GameDirFromLocalProps {
    param([string]$PropsPath)

    if (-not (Test-Path -LiteralPath $PropsPath -PathType Leaf)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $PropsPath -Raw
        $value = [string]$xml.Project.PropertyGroup.DW2GameDir
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $null
        }
        return $value.Trim()
    }
    catch {
        return $null
    }
}

function Test-ValidDw2InstallDir {
    param([string]$InstallDir)

    if ([string]::IsNullOrWhiteSpace($InstallDir)) {
        return $false
    }

    if (-not (Test-Path -LiteralPath $InstallDir -PathType Container)) {
        return $false
    }

    return (Test-Path -LiteralPath (Join-Path $InstallDir 'DistantWorlds2.exe') -PathType Leaf)
}

function Test-HasProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    return $Object.PSObject.Properties.Match($PropertyName).Count -gt 0
}

function Initialize-UserSettings {
    param([string]$RepoRoot)

    $localPropsPath = Join-Path $RepoRoot 'dw2-asset-extractor-source\DW2BT.local.props'
    $defaultOutputDir = Join-Path $RepoRoot 'Output'
    if (-not (Test-Path -LiteralPath $defaultOutputDir -PathType Container)) {
        New-Item -ItemType Directory -Path $defaultOutputDir | Out-Null
    }

    $dw2GameDir = Get-Dw2GameDirFromLocalProps -PropsPath $localPropsPath
    $settingsPath = Join-Path $env:LOCALAPPDATA 'dw2extract\settings.json'

    $currentSettings = $null
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        try {
            $currentSettings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        }
        catch {
            $currentSettings = $null
        }
    }

    if ($currentSettings -and -not ($currentSettings -is [pscustomobject]) -and -not ($currentSettings -is [hashtable])) {
        $currentSettings = $null
    }

    if (-not $currentSettings) {
        $currentSettings = [pscustomobject]@{}
    }

    $settingsChanged = $false

    $needsInstallDir = $true
    if ($currentSettings -and (Test-HasProperty -Object $currentSettings -PropertyName 'InstallDir')) {
        $currentInstallDir = [string]$currentSettings.InstallDir
        if (Test-ValidDw2InstallDir -InstallDir $currentInstallDir) {
            $needsInstallDir = $false
        }
    }

    if ($needsInstallDir -and (Test-ValidDw2InstallDir -InstallDir $dw2GameDir)) {
        $resolvedDw2GameDir = (Resolve-Path -LiteralPath $dw2GameDir).Path
        if (Test-HasProperty -Object $currentSettings -PropertyName 'InstallDir') {
            $currentSettings.InstallDir = $resolvedDw2GameDir
        }
        else {
            $currentSettings | Add-Member -NotePropertyName InstallDir -NotePropertyValue $resolvedDw2GameDir
        }
        $settingsChanged = $true
    }

    $needsOutputDir = $true
    if (Test-HasProperty -Object $currentSettings -PropertyName 'OutputDir') {
        $currentOutputDir = [string]$currentSettings.OutputDir
        if (-not [string]::IsNullOrWhiteSpace($currentOutputDir) -and (Test-Path -LiteralPath $currentOutputDir -PathType Container)) {
            $needsOutputDir = $false
        }
    }

    if ($needsOutputDir) {
        $resolvedOutputDir = (Resolve-Path -LiteralPath $defaultOutputDir).Path
        if (Test-HasProperty -Object $currentSettings -PropertyName 'OutputDir') {
            $currentSettings.OutputDir = $resolvedOutputDir
        }
        else {
            $currentSettings | Add-Member -NotePropertyName OutputDir -NotePropertyValue $resolvedOutputDir
        }
        $settingsChanged = $true
    }

    $settingsDir = Split-Path -Parent $settingsPath
    if (-not (Test-Path -LiteralPath $settingsDir -PathType Container)) {
        New-Item -ItemType Directory -Path $settingsDir | Out-Null
    }

    if ($settingsChanged) {
        $currentSettings | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    }
}

$exePath = Join-Path $PSScriptRoot 'dw2-asset-extractor-source\DistantWorlds2.AssetExtractor\bin\Debug\net8.0-windows\dw2extract.exe'
$env:DW2EXTRACT_LOG_PATH = Join-Path $PSScriptRoot 'dw2extractor.log'

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Built executable not found: $exePath`nRun .\\build.ps1 first."
}

Initialize-UserSettings -RepoRoot $PSScriptRoot

$exeDir = Split-Path -Parent $exePath

Push-Location $exeDir
try {
    & $exePath @Args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
