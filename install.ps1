param (
    [string]$Version,
    [switch]$Latest,
    [string]$DownloadPath, # New parameter for download location
    [switch]$NoProfile # If present, Perla will NOT be added to PATH in $PROFILE
)

$RepoOwner = "AngelMunoz"
$RepoName = "Perla"

# Auto-detect platform
$os = ""
$arch = ""

if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    $os = "win"
}
elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
    $os = "linux"
}
elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
    $os = "osx"
}
else {
    Write-Error "Unsupported operating system."
    exit 1
}

$runtimeArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($runtimeArch) {
    ([System.Runtime.InteropServices.Architecture]::X64) { $arch = "x64" }
    ([System.Runtime.InteropServices.Architecture]::Arm64) { $arch = "arm64" }
    default {
        Write-Error "Unsupported architecture: $runtimeArch"
        exit 1
    }
}

$selectedPlatform = "$os-$arch"
Write-Host "Detected platform: $selectedPlatform"

# Determine the effective directory for download and extraction
$DefaultInstallBaseDir = Join-Path -Path $env:LOCALAPPDATA -ChildPath "Perla" # Default to $env:LOCALAPPDATA/Perla
$EffectiveDownloadDir = $DefaultInstallBaseDir

if ($PSBoundParameters.ContainsKey('DownloadPath') -and -not [string]::IsNullOrEmpty($DownloadPath)) {
    $EffectiveDownloadDir = $DownloadPath
}

# Ensure the target directory exists, create if not
if (-not (Test-Path $EffectiveDownloadDir)) {
    Write-Host "Target directory '$EffectiveDownloadDir' does not exist. Creating it..."
    try {
        New-Item -ItemType Directory -Path $EffectiveDownloadDir -Force -ErrorAction Stop | Out-Null
        Write-Host "Successfully created directory: $EffectiveDownloadDir"
    } catch {
        Write-Error "Failed to create directory '$EffectiveDownloadDir': $_"
        exit 1
    }
}
# Ensure $EffectiveDownloadDir is an absolute path for robustness
$EffectiveDownloadDir = Resolve-Path -Path $EffectiveDownloadDir

# Determine the release tag based on parameters
if ($PSBoundParameters.ContainsKey('Version') -and -not [string]::IsNullOrEmpty($Version)) {
    $releaseTag = $Version
    Write-Host "Using specified version: $releaseTag"
} else {
    # No Version provided, or -Latest was used (which doesn't matter if Version isn't there as latest is default)
    $latestReleaseUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/latest"
    Write-Host "Fetching latest release information (version not specified or -Latest flag used)..."
    try {
        $latestReleaseInfo = Invoke-RestMethod -Uri $latestReleaseUrl -ErrorAction Stop
        $releaseTag = $latestReleaseInfo.tag_name
        Write-Host "Using latest release tag: $releaseTag"
    } catch {
        Write-Error "Failed to fetch latest release information: $_"
        exit 1
    }
}

# $selectedPlatform is like "win-x64", "linux-arm64", etc.
# Asset names in the release are like "win-x64.zip", "linux-arm64.zip"
$targetAssetFilename = "$selectedPlatform.zip"
# $outputFileName = $targetAssetFilename # Local file will be named like "win-x64.zip" # This line is effectively replaced by $ZipFilePath

$downloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/download/$releaseTag/$targetAssetFilename"

$ZipFilePath = Join-Path -Path $EffectiveDownloadDir -ChildPath $targetAssetFilename
$ExtractionDirName = "perla" # Name of the subdirectory for extracted content
$ExtractionDirPath = Join-Path -Path $EffectiveDownloadDir -ChildPath $ExtractionDirName

Write-Host "Downloading $targetAssetFilename to $ZipFilePath from $downloadUrl..."

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $ZipFilePath -ErrorAction Stop
    Write-Host "Successfully downloaded to $ZipFilePath"

    # Extract to a temporary directory first to avoid stale files on update
    $TempSuffix = [System.IO.Path]::GetRandomFileName()
    $TempExtractionDirPath = Join-Path -Path $EffectiveDownloadDir -ChildPath (${ExtractionDirName} + ".tmp.$TempSuffix")
    New-Item -ItemType Directory -Path $TempExtractionDirPath -Force | Out-Null

    Write-Host "Extracting $ZipFilePath to $TempExtractionDirPath..."
    Expand-Archive -Path $ZipFilePath -DestinationPath $TempExtractionDirPath -Force -ErrorAction Stop
    Write-Host "Successfully extracted to $TempExtractionDirPath"

    # Swap directories atomically where possible
    $BackupDirPath = "$ExtractionDirPath.bak.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
    if (Test-Path $ExtractionDirPath) {
        try {
            Write-Host "Renaming existing install '$ExtractionDirPath' to '$BackupDirPath'..."
            Move-Item -Path $ExtractionDirPath -Destination $BackupDirPath -Force -ErrorAction Stop
        } catch {
            Write-Warning "Failed to rename existing install. Attempting to remove it instead: $_"
            try {
                Remove-Item -Path $ExtractionDirPath -Recurse -Force -ErrorAction Stop
            } catch {
                Write-Error "Failed to remove existing install directory. Is Perla running? Please close it and try again. Error: $_"
                # Cleanup temp and zip before exiting
                if (Test-Path $TempExtractionDirPath) { Remove-Item -Path $TempExtractionDirPath -Recurse -Force -ErrorAction SilentlyContinue }
                if (Test-Path $ZipFilePath) { Remove-Item -Path $ZipFilePath -Force -ErrorAction SilentlyContinue }
                exit 1
            }
        }
    }

    try {
        Write-Host "Moving new install from '$TempExtractionDirPath' to '$ExtractionDirPath'..."
        Move-Item -Path $TempExtractionDirPath -Destination $ExtractionDirPath -Force -ErrorAction Stop
    } catch {
        Write-Error "Failed to place new install into target directory: $_"
        # Try to restore backup if it exists
        if (Test-Path $BackupDirPath) {
            Write-Warning "Attempting to restore previous install from backup..."
            try { Move-Item -Path $BackupDirPath -Destination $ExtractionDirPath -Force -ErrorAction Stop } catch { Write-Warning "Restore failed: $_" }
        }
        if (Test-Path $TempExtractionDirPath) { Remove-Item -Path $TempExtractionDirPath -Recurse -Force -ErrorAction SilentlyContinue }
        if (Test-Path $ZipFilePath) { Remove-Item -Path $ZipFilePath -Force -ErrorAction SilentlyContinue }
        exit 1
    }

    # Best-effort: remove backup now that new install is in place
    if (Test-Path $BackupDirPath) {
        try { Remove-Item -Path $BackupDirPath -Recurse -Force -ErrorAction Stop } catch { Write-Warning "Could not remove backup '$BackupDirPath': $_" }
    }

    # Cleanup zip
    if (Test-Path $ZipFilePath) { Remove-Item -Path $ZipFilePath -Force -ErrorAction SilentlyContinue }
    Write-Host "Removed $ZipFilePath"

    # Create proxy script
    $ProxyScriptFilePath = Join-Path -Path $EffectiveDownloadDir -ChildPath "perla.ps1"
    $ProxyScriptContent = @"
param(
    [Parameter(ValueFromRemainingArguments = `$true)]
    [object[]]`$Arguments
)
# This script assumes Perla.exe is in a subdirectory named '$ExtractionDirName' relative to this script's location.
`$ExePath = Join-Path `$PSScriptRoot "$ExtractionDirName" "Perla.exe"
& `$ExePath `$Arguments
"@
    Set-Content -Path $ProxyScriptFilePath -Value $ProxyScriptContent
    Write-Host "Created Perla proxy script at $ProxyScriptFilePath"

    # Add to profile by default, unless -NoProfile is specified
    if (-not $NoProfile) { # If $NoProfile is $false (i.e., -NoProfile switch not used), then add to profile
        Write-Host "Adding Perla to PATH in PowerShell profile: $PROFILE..."

        $MigrondiProxyDirActualResolved = $EffectiveDownloadDir # This is the actual resolved path where perla.ps1 is

        $PathStringForProfileFile = "" # This will hold either the literal '$env:...' or the resolved custom path
        $PathExistenceCheckRegex = "" # Regex to check if it's already there

        # Resolve the conceptual default directory for comparison
        $ConceptualDefaultInstallDirResolved = ""
        try {
            $ConceptualDefaultInstallDirResolved = Resolve-Path (Join-Path -Path $env:LOCALAPPDATA -ChildPath "Perla") -ErrorAction Stop
        } catch {
            # This case should be rare, means $env:LOCALAPPDATA might be problematic or non-existent.
            # Fallback to using the $EffectiveDownloadDir as a literal path if resolution fails.
            Write-Warning "Could not resolve default install directory based on \$env:LOCALAPPDATA. Using absolute path for profile update."
            $ConceptualDefaultInstallDirResolved = "" # Ensure it doesn't match if resolution failed
        }

        if (($ConceptualDefaultInstallDirResolved -ne "") -and ($MigrondiProxyDirActualResolved -eq $ConceptualDefaultInstallDirResolved)) {
            # Default directory case: use literal $env:LOCALAPPDATA in the profile string
            $PathStringForProfileFile = Join-Path -Path '$env:LOCALAPPDATA' -ChildPath "Perla" # Literal string for profile
            $EscapedPathForRegex = [regex]::Escape($PathStringForProfileFile) # Escape the literal string for regex
            $PathExistenceCheckRegex = ('\$env:PATH\s*[+\-]?=\s*.*' + $EscapedPathForRegex)
        } else {
            # Custom directory case (or if default path resolution failed): use the resolved $EffectiveDownloadDir
            $PathStringForProfileFile = $MigrondiProxyDirActualResolved
            $EscapedPathForRegex = [regex]::Escape($MigrondiProxyDirActualResolved)
            $PathExistenceCheckRegex = ('\$env:PATH\s*[+\-]?=\s*.*' + $EscapedPathForRegex)
        }

        Write-Host "Attempting to add '$PathStringForProfileFile' to PATH in PowerShell profile ($PROFILE)..."

        # Ensure the profile file exists, create if not
        if (-not (Test-Path $PROFILE)) {
            try {
                Write-Host "Profile file ($PROFILE) does not exist. Creating it..."
                New-Item -Path $PROFILE -ItemType File -Force -ErrorAction Stop | Out-Null
                Write-Host "Successfully created profile file: $PROFILE"
            } catch {
                Write-Error "Failed to create profile file ($PROFILE): $_. Please create it manually and add '$PathStringForProfileFile' to your PATH."
            }
        }

        if (Test-Path $PROFILE) {
            try {
                $ProfileContent = Get-Content $PROFILE -Raw -ErrorAction SilentlyContinue

                # Check if the directory is already in a line that modifies $env:PATH
                if ($ProfileContent -match $PathExistenceCheckRegex) {
                    Write-Host "'$PathStringForProfileFile' appears to be already configured in the PATH in $PROFILE."
                } else {
                    $PathSeparator = [System.IO.Path]::PathSeparator
                    $Comment = "# Added by perla_install.ps1 to include Perla CLI"
                    $MigrondiHomeEnvVarCommand = "`$env:PERLA_HOME = '$PathStringForProfileFile'"
                    $PathAddCommand = "`$env:PATH += '$PathSeparator`$env:PERLA_HOME'" # Use PERLA_HOME var

                    $FinalCommandToAdd = ""
                    # Add PERLA_HOME first, then modify PATH
                    $BaseContentToAdd = "`n$Comment`n$MigrondiHomeEnvVarCommand`n$PathAddCommand"

                    if (-not [string]::IsNullOrEmpty($ProfileContent) -and $ProfileContent[-1] -ne "`n" -and $ProfileContent[-1] -ne "`r") {
                        $FinalCommandToAdd = "`n" + $BaseContentToAdd # Extra newline if profile not empty and no trailing newline
                    } else {
                        $FinalCommandToAdd = $BaseContentToAdd
                    }

                    Add-Content -Path $PROFILE -Value $FinalCommandToAdd -ErrorAction Stop
                    Write-Host "Successfully added '$PathStringForProfileFile' to PATH in $PROFILE."
                    Write-Host "Please restart your PowerShell session or run '. $PROFILE' to apply the changes."
                }
            } catch {
                Write-Error "Failed to update $($PROFILE): $_"
            }
        }
    }

} catch {
    Write-Error "Failed to download the asset: $_"
    # Attempt to list available assets for the release tag if download fails
    $releaseAssetsUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/tags/$releaseTag"
    try {
        Write-Host "Fetching available assets for tag $releaseTag..."
        $releaseInfo = Invoke-RestMethod -Uri $releaseAssetsUrl
        Write-Host "Available assets for release $($releaseTag):" # Corrected variable interpolation
        $releaseInfo.assets | ForEach-Object { Write-Host "- $($_.name)" }
    } catch {
        Write-Warning "Could not retrieve asset list for tag $releaseTag."
    }
    exit 1
}
