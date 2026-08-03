[CmdletBinding()]
param(
    [string]$DW2GameDir,
    [switch]$AutoInstall,
    [switch]$Reset
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Summary = [ordered]@{
    DotNetInstalledThisRun = $false
    FfmpegInstalledThisRun = $false
    UsedFfmpegFolderOverride = $false
    WroteLocalProps = $false
    ResetLocalProps = $false
}

function Write-Step {
    param([string]$Message)
    Write-Host "[dw2extract-setup] $Message" -ForegroundColor Cyan
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[dw2extract-setup] $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "[dw2extract-setup] $Message" -ForegroundColor Red
}

function Confirm-YesNo {
    param(
        [Parameter(Mandatory = $true)][string]$Prompt,
        [bool]$DefaultYes = $true
    )

    if ($AutoInstall) {
        return $true
    }

    $suffix = if ($DefaultYes) { '[Y/n]' } else { '[y/N]' }
    $answer = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) {
        return $DefaultYes
    }

    return $answer.Trim().ToLowerInvariant().StartsWith('y')
}

function Test-DotNetSdk8Installed {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        return $false
    }

    $sdks = & dotnet --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $sdks) {
        return $false
    }

    foreach ($sdk in $sdks) {
        if ($sdk -match '^8\.') {
            return $true
        }
    }

    return $false
}

function Install-DotNetSdk8 {
    Write-Step 'Attempting .NET 8 SDK install via winget (Microsoft.DotNet.SDK.8).'

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw 'winget is not available. Install .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0'
    }

    & winget install --id Microsoft.DotNet.SDK.8 --exact --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        throw '.NET 8 SDK install failed via winget.'
    }

    $script:Summary.DotNetInstalledThisRun = $true
}

function Test-FfmpegTools {
    $ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
    $ffprobe = Get-Command ffprobe -ErrorAction SilentlyContinue

    return [bool]($ffmpeg -and $ffprobe)
}

function Install-Ffmpeg {
    Write-Step 'Attempting FFmpeg install via winget (Gyan.FFmpeg).'

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw 'winget is not available. Install FFmpeg manually: https://ffmpeg.org/download.html'
    }

    & winget install --id Gyan.FFmpeg --exact --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        throw 'FFmpeg install failed via winget.'
    }

    $script:Summary.FfmpegInstalledThisRun = $true
}

function Resolve-FfmpegFolderOverride {
    param([string]$RepoRoot)

    Write-Warn 'FFmpeg and/or FFprobe are not currently on PATH.'
    Write-Host 'You can provide an existing FFmpeg bin folder now (contains ffmpeg.exe and ffprobe.exe), or leave blank to skip.'

    $candidate = Read-Host 'FFmpeg bin folder path (optional)'
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        return
    }

    $resolved = Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue
    if (-not $resolved) {
        throw "Provided FFmpeg folder does not exist: $candidate"
    }

    $folder = $resolved.Path
    $ffmpegExe = Join-Path $folder 'ffmpeg.exe'
    $ffprobeExe = Join-Path $folder 'ffprobe.exe'

    if (-not (Test-Path -LiteralPath $ffmpegExe -PathType Leaf) -or -not (Test-Path -LiteralPath $ffprobeExe -PathType Leaf)) {
        throw "Folder does not contain both ffmpeg.exe and ffprobe.exe: $folder"
    }

    $env:PATH = "$folder;$env:PATH"
    $script:Summary.UsedFfmpegFolderOverride = $true
    Write-Step "Using FFmpeg tools from: $folder"
}

function Get-Dw2GameDirFromLocalProps {
    param([string]$LocalPropsPath)

    if (-not (Test-Path -LiteralPath $LocalPropsPath -PathType Leaf)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $LocalPropsPath -Raw
        $node = $xml.Project.PropertyGroup.DW2GameDir
        if ($node) {
            return [string]$node
        }
    }
    catch {
        Write-Warn "Failed to parse existing DW2BT.local.props: $($_.Exception.Message)"
    }

    return $null
}

function Test-ValidDw2GameDir {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $false
    }

    if (-not (Test-Path -LiteralPath $PathValue -PathType Container)) {
        return $false
    }

    $exePath = Join-Path $PathValue 'DistantWorlds2.exe'
    return (Test-Path -LiteralPath $exePath -PathType Leaf)
}

function Confirm-Dw2GameDirCandidate {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$SourceLabel
    )

    Write-Step "$SourceLabel"
    Write-Host $PathValue
    return (Confirm-YesNo -Prompt 'Use this detected DW2 install path?' -DefaultYes $true)
}

function Get-SteamInstallPaths {
    $paths = New-Object System.Collections.Generic.List[string]

    $registryCandidates = @(
        @{ Path = 'HKCU:\Software\Valve\Steam'; Value = 'SteamPath' },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Value = 'InstallPath' },
        @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Value = 'InstallPath' }
    )

    foreach ($candidate in $registryCandidates) {
        try {
            $value = (Get-ItemProperty -LiteralPath $candidate.Path -Name $candidate.Value -ErrorAction Stop).$($candidate.Value)
            if (-not [string]::IsNullOrWhiteSpace($value) -and (Test-Path -LiteralPath $value -PathType Container)) {
                $paths.Add((Resolve-Path -LiteralPath $value).Path)
            }
        }
        catch {
            # Ignore missing registry keys and keep probing other locations.
        }
    }

    foreach ($fallback in @('C:\Steam', 'C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')) {
        if (Test-Path -LiteralPath $fallback -PathType Container) {
            $paths.Add((Resolve-Path -LiteralPath $fallback).Path)
        }
    }

    return $paths | Select-Object -Unique
}

function Get-SteamLibraryRoots {
    param([string[]]$SteamInstallPaths)

    $libraries = New-Object System.Collections.Generic.List[string]

    foreach ($steamPath in $SteamInstallPaths) {
        if ([string]::IsNullOrWhiteSpace($steamPath)) {
            continue
        }

        if (Test-Path -LiteralPath $steamPath -PathType Container) {
            $libraries.Add($steamPath)
        }

        $libraryVdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $libraryVdf -PathType Leaf)) {
            continue
        }

        try {
            $vdfText = Get-Content -LiteralPath $libraryVdf -Raw
            $matches = [System.Text.RegularExpressions.Regex]::Matches(
                $vdfText,
                '"path"\s+"([^"]+)"',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )

            foreach ($match in $matches) {
                $rawPath = $match.Groups[1].Value
                $normalized = $rawPath -replace '\\\\', '\\'
                if (-not [string]::IsNullOrWhiteSpace($normalized) -and (Test-Path -LiteralPath $normalized -PathType Container)) {
                    $libraries.Add((Resolve-Path -LiteralPath $normalized).Path)
                }
            }
        }
        catch {
            Write-Warn "Could not parse Steam library file: $libraryVdf"
        }
    }

    return $libraries | Select-Object -Unique
}

function Find-Dw2GameDirFromSteam {
    $steamInstallPaths = Get-SteamInstallPaths
    if (-not $steamInstallPaths -or $steamInstallPaths.Count -eq 0) {
        return $null
    }

    $libraryRoots = Get-SteamLibraryRoots -SteamInstallPaths $steamInstallPaths
    if (-not $libraryRoots -or $libraryRoots.Count -eq 0) {
        return $null
    }

    # Prefer appmanifest lookup first, then common fallback folder names.
    foreach ($libraryRoot in $libraryRoots) {
        $manifestPath = Join-Path $libraryRoot 'steamapps\appmanifest_1646850.acf'
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            try {
                $manifest = Get-Content -LiteralPath $manifestPath -Raw
                $installDirMatch = [System.Text.RegularExpressions.Regex]::Match(
                    $manifest,
                    '"installdir"\s+"([^"]+)"',
                    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
                )

                if ($installDirMatch.Success) {
                    $installdir = $installDirMatch.Groups[1].Value
                    $candidatePath = Join-Path $libraryRoot (Join-Path 'steamapps\common' $installdir)
                    if (Test-ValidDw2GameDir -PathValue $candidatePath) {
                        return (Resolve-Path -LiteralPath $candidatePath).Path
                    }
                }
            }
            catch {
                Write-Warn "Could not parse Steam manifest: $manifestPath"
            }
        }
    }

    foreach ($libraryRoot in $libraryRoots) {
        foreach ($folderName in @('Distant Worlds 2', 'DistantWorlds2')) {
            $candidatePath = Join-Path $libraryRoot (Join-Path 'steamapps\common' $folderName)
            if (Test-ValidDw2GameDir -PathValue $candidatePath) {
                return (Resolve-Path -LiteralPath $candidatePath).Path
            }
        }
    }

    return $null
}

function Find-Dw2GameDirFromUninstallRegistry {
    $registryRoots = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )

    foreach ($root in $registryRoots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        $subKeys = Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue
        foreach ($subKey in $subKeys) {
            try {
                $props = Get-ItemProperty -LiteralPath $subKey.PSPath -ErrorAction Stop

                $displayName = [string]$props.DisplayName
                if ([string]::IsNullOrWhiteSpace($displayName)) {
                    continue
                }

                if ($displayName -notmatch '(?i)distant\s*worlds\s*2') {
                    continue
                }

                $candidatePaths = New-Object System.Collections.Generic.List[string]
                foreach ($candidate in @([string]$props.InstallLocation, [string]$props.InstallSource)) {
                    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                        $candidatePaths.Add($candidate)
                    }
                }

                $uninstallString = [string]$props.UninstallString
                if (-not [string]::IsNullOrWhiteSpace($uninstallString)) {
                    $firstToken = $uninstallString.Trim()
                    if ($firstToken.StartsWith('"')) {
                        $parts = $firstToken.Split('"', [System.StringSplitOptions]::RemoveEmptyEntries)
                        if ($parts.Count -gt 0) {
                            $firstToken = $parts[0]
                        }
                    }
                    else {
                        $firstToken = $firstToken.Split(' ')[0]
                    }

                    if (-not [string]::IsNullOrWhiteSpace($firstToken) -and (Test-Path -LiteralPath $firstToken -PathType Leaf)) {
                        $candidatePaths.Add((Split-Path -Parent $firstToken))
                    }
                }

                foreach ($candidatePath in ($candidatePaths | Select-Object -Unique)) {
                    if (Test-ValidDw2GameDir -PathValue $candidatePath) {
                        return (Resolve-Path -LiteralPath $candidatePath).Path
                    }
                }
            }
            catch {
                # Keep scanning other uninstall entries.
            }
        }
    }

    return $null
}

function Find-Dw2GameDirFromGogAndCommonLocations {
    $detectedFromRegistry = Find-Dw2GameDirFromUninstallRegistry
    if (Test-ValidDw2GameDir -PathValue $detectedFromRegistry) {
        return (Resolve-Path -LiteralPath $detectedFromRegistry).Path
    }

    $fallbackPaths = New-Object System.Collections.Generic.List[string]
    foreach ($root in @($env:SystemDrive, 'C:')) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }
        $fallbackPaths.Add((Join-Path $root 'GOG Games\Distant Worlds 2'))
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $fallbackPaths.Add((Join-Path $env:ProgramFiles 'GOG Galaxy\Games\Distant Worlds 2'))
    }
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $fallbackPaths.Add((Join-Path ${env:ProgramFiles(x86)} 'GOG Galaxy\Games\Distant Worlds 2'))
    }

    # Native/manual installs often end up in a root-level game folder with this name.
    foreach ($root in @($env:SystemDrive, 'C:')) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }
        $fallbackPaths.Add((Join-Path $root 'Games\Distant Worlds 2'))
    }

    foreach ($candidatePath in ($fallbackPaths | Select-Object -Unique)) {
        if (Test-ValidDw2GameDir -PathValue $candidatePath) {
            return (Resolve-Path -LiteralPath $candidatePath).Path
        }
    }

    return $null
}

function Select-FolderDialog {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [string]$InitialPath
    )

    if (-not $IsWindows) {
        return $null
    }

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    }
    catch {
        Write-Warn "Folder browser unavailable (failed to load WinForms): $($_.Exception.Message)"
        return $null
    }

    $openDialog = {
        param($DialogDescription, $DialogInitialPath)

        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = $DialogDescription
        $dialog.UseDescriptionForTitle = $true
        $dialog.ShowNewFolderButton = $false

        if (-not [string]::IsNullOrWhiteSpace($DialogInitialPath) -and (Test-Path -LiteralPath $DialogInitialPath -PathType Container)) {
            $dialog.InitialDirectory = $DialogInitialPath
            $dialog.SelectedPath = $DialogInitialPath
        }

        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            return $dialog.SelectedPath
        }

        return $null
    }

    if ([Threading.Thread]::CurrentThread.GetApartmentState() -eq [Threading.ApartmentState]::STA) {
        return & $openDialog $Description $InitialPath
    }

    try {
        $state = [pscustomobject]@{
            Description = $Description
            InitialPath = $InitialPath
            Result = $null
            Error = $null
        }

        $thread = [System.Threading.Thread]::new([System.Threading.ParameterizedThreadStart]{
                param($threadState)
                try {
                    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop

                    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
                    $dialog.Description = [string]$threadState.Description
                    $dialog.UseDescriptionForTitle = $true
                    $dialog.ShowNewFolderButton = $false

                    $dialogInitialPath = [string]$threadState.InitialPath
                    if (-not [string]::IsNullOrWhiteSpace($dialogInitialPath) -and (Test-Path -LiteralPath $dialogInitialPath -PathType Container)) {
                        $dialog.InitialDirectory = $dialogInitialPath
                        $dialog.SelectedPath = $dialogInitialPath
                    }

                    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                        $threadState.Result = $dialog.SelectedPath
                    }
                }
                catch {
                    $threadState.Error = $_.Exception.Message
                }
            })

        $thread.SetApartmentState([System.Threading.ApartmentState]::STA)
        $thread.Start($state)
        $thread.Join()

        if ($state.Error) {
            Write-Warn "Folder browser unavailable in this host, falling back to text prompt: $($state.Error)"
            return $null
        }

        return $state.Result
    }
    catch {
        Write-Warn "Folder browser unavailable in this host, falling back to text prompt: $($_.Exception.Message)"
        return $null
    }
}

function Resolve-Dw2GameDir {
    param(
        [string]$SuppliedPath,
        [string]$LocalPropsPath,
        [switch]$IgnoreExistingLocalProps
    )

    if (Test-ValidDw2GameDir -PathValue $SuppliedPath) {
        $resolvedSuppliedPath = (Resolve-Path -LiteralPath $SuppliedPath).Path
        if (Confirm-Dw2GameDirCandidate -PathValue $resolvedSuppliedPath -SourceLabel 'Using path from -DW2GameDir:') {
            return $resolvedSuppliedPath
        }
    }

    if ($SuppliedPath) {
        Write-Warn "Provided -DW2GameDir is invalid or missing DistantWorlds2.exe: $SuppliedPath"
    }

    if (-not $IgnoreExistingLocalProps) {
        $existing = Get-Dw2GameDirFromLocalProps -LocalPropsPath $LocalPropsPath
        if (Test-ValidDw2GameDir -PathValue $existing) {
            $resolvedExistingPath = (Resolve-Path -LiteralPath $existing).Path
            if (Confirm-Dw2GameDirCandidate -PathValue $resolvedExistingPath -SourceLabel 'Detected path from DW2BT.local.props:') {
                return $resolvedExistingPath
            }
        }
    }

    $autoDetectedSteam = Find-Dw2GameDirFromSteam
    if (Test-ValidDw2GameDir -PathValue $autoDetectedSteam) {
        $resolvedSteamPath = (Resolve-Path -LiteralPath $autoDetectedSteam).Path
        if (Confirm-Dw2GameDirCandidate -PathValue $resolvedSteamPath -SourceLabel 'Auto-detected DW2 install from Steam:') {
            return $resolvedSteamPath
        }
    }

    $autoDetectedGogOrNative = Find-Dw2GameDirFromGogAndCommonLocations
    if (Test-ValidDw2GameDir -PathValue $autoDetectedGogOrNative) {
        $resolvedGogOrNativePath = (Resolve-Path -LiteralPath $autoDetectedGogOrNative).Path
        if (Confirm-Dw2GameDirCandidate -PathValue $resolvedGogOrNativePath -SourceLabel 'Auto-detected DW2 install from GOG/native paths:') {
            return $resolvedGogOrNativePath
        }
    }

    Write-Step 'Opening folder browser for Distant Worlds 2 install path (Cancel to type it manually).'
    $dialogInitialPath = $null
    foreach ($candidate in @($autoDetectedSteam, $autoDetectedGogOrNative)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Container)) {
            $dialogInitialPath = $candidate
            break
        }
    }
    $selected = Select-FolderDialog -Description 'Select your Distant Worlds 2 install folder (must contain DistantWorlds2.exe)' -InitialPath $dialogInitialPath
    if (Test-ValidDw2GameDir -PathValue $selected) {
        $resolvedSelectedPath = (Resolve-Path -LiteralPath $selected).Path
        if (Confirm-Dw2GameDirCandidate -PathValue $resolvedSelectedPath -SourceLabel 'Selected in folder browser:') {
            return $resolvedSelectedPath
        }
    }

    if ($selected) {
        Write-Warn 'Selected folder is missing DistantWorlds2.exe. Falling back to text prompt.'
    }

    while ($true) {
        $entered = Read-Host 'Enter full path to your Distant Worlds 2 install folder (must contain DistantWorlds2.exe)'
        if (Test-ValidDw2GameDir -PathValue $entered) {
            $resolvedEnteredPath = (Resolve-Path -LiteralPath $entered).Path
            if (Confirm-Dw2GameDirCandidate -PathValue $resolvedEnteredPath -SourceLabel 'Entered manually:') {
                return $resolvedEnteredPath
            }
        }

        Write-Err 'Invalid folder. Please try again.'
    }
}

function Write-LocalProps {
    param(
        [string]$ExamplePath,
        [string]$LocalPropsPath,
        [string]$Dw2Path
    )

    if (-not (Test-Path -LiteralPath $ExamplePath -PathType Leaf)) {
        throw "Cannot find template props file: $ExamplePath"
    }

    $raw = Get-Content -LiteralPath $ExamplePath -Raw
    $escaped = [System.Security.SecurityElement]::Escape($Dw2Path)

    if ($raw -match '<DW2GameDir>.*?</DW2GameDir>') {
        $updated = [System.Text.RegularExpressions.Regex]::Replace(
            $raw,
            '<DW2GameDir>.*?</DW2GameDir>',
            "<DW2GameDir>$escaped</DW2GameDir>",
            [System.Text.RegularExpressions.RegexOptions]::Singleline
        )
    }
    else {
        throw 'Template does not contain a <DW2GameDir> element.'
    }

    Set-Content -LiteralPath $LocalPropsPath -Value $updated -Encoding UTF8
    $script:Summary.WroteLocalProps = $true
}

Write-Step 'Starting prerequisite checks.'

if (-not $IsWindows) {
    throw 'This build script only supports Windows. See README prerequisites.'
}

$repoRoot = Split-Path -Parent $PSCommandPath
$sourceRoot = Join-Path $repoRoot 'dw2-asset-extractor-source'
$slnPath = Join-Path $sourceRoot 'DistantWorlds2.AssetExtractor.sln'
$examplePropsPath = Join-Path $sourceRoot 'DW2BT.local.props.example'
$localPropsPath = Join-Path $sourceRoot 'DW2BT.local.props'

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "Expected source folder not found: $sourceRoot"
}

if (-not (Test-Path -LiteralPath $slnPath -PathType Leaf)) {
    throw "Solution file not found: $slnPath"
}

if (-not (Test-Path -LiteralPath $examplePropsPath -PathType Leaf)) {
    throw "Template props file not found: $examplePropsPath"
}

if ($Reset) {
    Write-Step 'Reset flag detected: existing DW2BT.local.props will be ignored and rewritten.'
    $script:Summary.ResetLocalProps = $true
}

if (-not (Test-DotNetSdk8Installed)) {
    Write-Warn '.NET 8 SDK not detected.'
    if (Confirm-YesNo -Prompt 'Install .NET 8 SDK now?' -DefaultYes $true) {
        Install-DotNetSdk8

        if (-not (Test-DotNetSdk8Installed)) {
            throw '.NET 8 SDK still not detected after installation. Open a new terminal and re-run this script.'
        }
    }
    else {
        throw 'Cannot continue without .NET 8 SDK.'
    }
}
else {
    Write-Step '.NET 8 SDK detected.'
}

if (-not (Test-FfmpegTools)) {
    if (Confirm-YesNo -Prompt 'FFmpeg (ffmpeg + ffprobe) not found on PATH. Install now?' -DefaultYes $true) {
        Install-Ffmpeg
    }
    else {
        Resolve-FfmpegFolderOverride -RepoRoot $repoRoot
    }

    if (-not (Test-FfmpegTools)) {
        Write-Warn 'FFmpeg tools are still not available on PATH. Build will continue, but sound/PNG conversion at runtime will not work until FFmpeg is available.'
    }
    else {
        Write-Step 'FFmpeg tools detected.'
    }
}
else {
    Write-Step 'FFmpeg tools detected.'
}

$resolvedDw2Path = Resolve-Dw2GameDir -SuppliedPath $DW2GameDir -LocalPropsPath $localPropsPath -IgnoreExistingLocalProps:$Reset
Write-LocalProps -ExamplePath $examplePropsPath -LocalPropsPath $localPropsPath -Dw2Path $resolvedDw2Path
Write-Step "Wrote DW2 path to: $localPropsPath"

Push-Location $sourceRoot
try {
    Write-Step 'Running dotnet restore (downloads NuGet packages).'
    & dotnet restore $slnPath
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet restore failed.'
    }

    Write-Step 'Running dotnet build.'
    & dotnet build $slnPath
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet build failed.'
    }
}
finally {
    Pop-Location
}

$exePath = Join-Path $sourceRoot 'DistantWorlds2.AssetExtractor\bin\Debug\net8.0-windows\dw2extract.exe'

Write-Host ''
Write-Host '========== Summary ==========' -ForegroundColor Green
Write-Host "DW2GameDir: $resolvedDw2Path"
Write-Host "DotNet installed this run: $($script:Summary.DotNetInstalledThisRun)"
Write-Host "FFmpeg installed this run: $($script:Summary.FfmpegInstalledThisRun)"
Write-Host "Used FFmpeg folder override: $($script:Summary.UsedFfmpegFolderOverride)"
Write-Host "Reset local props requested: $($script:Summary.ResetLocalProps)"
Write-Host "Updated DW2BT.local.props: $($script:Summary.WroteLocalProps)"

if (Test-Path -LiteralPath $exePath -PathType Leaf) {
    Write-Host "Build succeeded. Executable: $exePath" -ForegroundColor Green
    Write-Host "Run command: .\run" -ForegroundColor Blue
}
else {
    Write-Warn "Build completed, but expected executable path was not found: $exePath"
}
