<#
.SYNOPSIS
    Updates the RsyncZilla project version across all project files.

.DESCRIPTION
    Updates the version in:
    - src/RsyncZilla/RsyncZilla.csproj (<Version>, <AssemblyVersion>, <FileVersion>)
    - src/RsyncAskPass/RsyncAskPass.csproj (<Version>)
    - dist/version.json (version, release_name)
    - dist/RELEASE_NOTES.md (adds version section if not present)
    - dist/RsyncZilla/ (synchronizes manifest if build directory exists)

.PARAMETER Version
    The new semantic version (e.g. "1.0.2").

.PARAMETER Patch
    Increments the patch number (e.g. 1.0.1 -> 1.0.2).

.PARAMETER Minor
    Increments the minor number (e.g. 1.0.1 -> 1.1.0).

.PARAMETER Major
    Increments the major number (e.g. 1.0.1 -> 2.0.0).

.EXAMPLE
    .\scripts\set-version.ps1 1.0.2
    .\scripts\set-version.ps1 -Patch
#>

[CmdletBinding(DefaultParameterSetName = "Explicit")]
param(
    [Parameter(Position = 0, ParameterSetName = "Explicit")]
    [string]$Version,

    [Parameter(ParameterSetName = "Patch")]
    [switch]$Patch,

    [Parameter(ParameterSetName = "Minor")]
    [switch]$Minor,

    [Parameter(ParameterSetName = "Major")]
    [switch]$Major
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$mainCsproj = Join-Path $repoRoot "src\RsyncZilla\RsyncZilla.csproj"
$askPassCsproj = Join-Path $repoRoot "src\RsyncAskPass\RsyncAskPass.csproj"
$manifestJson = Join-Path $repoRoot "dist\version.json"
$releaseNotes = Join-Path $repoRoot "dist\RELEASE_NOTES.md"
$distDir = Join-Path $repoRoot "dist\RsyncZilla"

if (-not (Test-Path $mainCsproj)) {
    Write-Error "Could not find $mainCsproj"
}

# Read current version from RsyncZilla.csproj
$csprojContent = Get-Content $mainCsproj -Raw
$currentVersion = "1.0.0"
if ($csprojContent -match '<Version>(.*?)</Version>') {
    $currentVersion = $matches[1].Trim()
}

Write-Host "Current RsyncZilla version: $currentVersion" -ForegroundColor Cyan

# Determine next version
$newVersion = ""
if ($Patch -or $Minor -or $Major) {
    $parts = $currentVersion.Split('.')
    [int]$majorPart = if ($parts.Length -gt 0) { [int]$parts[0] } else { 1 }
    [int]$minorPart = if ($parts.Length -gt 1) { [int]$parts[1] } else { 0 }
    [int]$patchPart = if ($parts.Length -gt 2) { [int]$parts[2] } else { 0 }

    if ($Major) {
        $majorPart++
        $minorPart = 0
        $patchPart = 0
    } elseif ($Minor) {
        $minorPart++
        $patchPart = 0
    } else {
        $patchPart++
    }
    $newVersion = "$majorPart.$minorPart.$patchPart"
} elseif (-not [string]::IsNullOrWhiteSpace($Version)) {
    $newVersion = $Version.Trim().TrimStart('v', 'V')
} else {
    # Calculate default patch increment
    $parts = $currentVersion.Split('.')
    [int]$majorPart = if ($parts.Length -gt 0) { [int]$parts[0] } else { 1 }
    [int]$minorPart = if ($parts.Length -gt 1) { [int]$parts[1] } else { 0 }
    [int]$patchPart = if ($parts.Length -gt 2) { [int]$parts[2] } else { 0 }
    $defaultNext = "$majorPart.$minorPart.$($patchPart + 1)"

    $inputVersion = Read-Host "Enter new version [press Enter for $defaultNext]"
    if ([string]::IsNullOrWhiteSpace($inputVersion)) {
        $newVersion = $defaultNext
    } else {
        $newVersion = $inputVersion.Trim().TrimStart('v', 'V')
    }
}

if (-not ($newVersion -match '^\d+\.\d+\.\d+(\.\d+)?$')) {
    Write-Error "Invalid version format '$newVersion'. Expected format: X.Y.Z (e.g. 1.0.2)"
}

Write-Host "Updating RsyncZilla to version: $newVersion..." -ForegroundColor Yellow

# 1. Update src/RsyncZilla/RsyncZilla.csproj
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace(
    $csprojContent,
    '<Version>.*?</Version>',
    "<Version>$newVersion</Version>"
)
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace(
    $csprojContent,
    '<AssemblyVersion>.*?</AssemblyVersion>',
    "<AssemblyVersion>$newVersion.0</AssemblyVersion>"
)
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace(
    $csprojContent,
    '<FileVersion>.*?</FileVersion>',
    "<FileVersion>$newVersion.0</FileVersion>"
)
Set-Content -Path $mainCsproj -Value $csprojContent -NoNewline
Write-Host "  [OK] Updated $mainCsproj" -ForegroundColor Green

# 2. Update src/RsyncAskPass/RsyncAskPass.csproj
if (Test-Path $askPassCsproj) {
    $askPassContent = Get-Content $askPassCsproj -Raw
    if ($askPassContent -match '<Version>.*?</Version>') {
        $askPassContent = [System.Text.RegularExpressions.Regex]::Replace(
            $askPassContent,
            '<Version>.*?</Version>',
            "<Version>$newVersion</Version>"
        )
    } else {
        $askPassContent = $askPassContent -replace '(<PropertyGroup>)', "`$1`r`n    <Version>$newVersion</Version>"
    }
    Set-Content -Path $askPassCsproj -Value $askPassContent -NoNewline
    Write-Host "  [OK] Updated $askPassCsproj" -ForegroundColor Green
}

# 3. Update dist/version.json
if (Test-Path $manifestJson) {
    $manifestContent = Get-Content $manifestJson -Raw
    $manifestContent = [System.Text.RegularExpressions.Regex]::Replace(
        $manifestContent,
        '"version":\s*".*?"',
        "`"version`": `"$newVersion`""
    )
    $manifestContent = [System.Text.RegularExpressions.Regex]::Replace(
        $manifestContent,
        '"release_name":\s*".*?"',
        "`"release_name`": `"RsyncZilla $newVersion`""
    )
    $manifestContent = [System.Text.RegularExpressions.Regex]::Replace(
        $manifestContent,
        '"installer_url":\s*".*?"',
        "`"installer_url`": `"https://github.com/kanowins/RsyncZilla/releases/download/v$newVersion/RsyncZilla-Setup-v$newVersion-win-x64.exe`""
    )
    Set-Content -Path $manifestJson -Value $manifestContent -NoNewline
    Write-Host "  [OK] Updated $manifestJson" -ForegroundColor Green
}

# 4. Update dist/RELEASE_NOTES.md if version section is missing
if (Test-Path $releaseNotes) {
    $notesContent = Get-Content $releaseNotes -Raw
    if (-not ($notesContent -match "## Version $newVersion\b")) {
        $today = (Get-Date).ToString("yyyy-MM-dd")
        $template = @"
## Version $newVersion ($today)

### ✨ Changes & Improvements
- Release notes for version $newVersion.

---

"@
        if ($notesContent -match '(# RsyncZilla Release Notes\r?\n\r?\n)') {
            $notesContent = $notesContent -replace '(# RsyncZilla Release Notes\r?\n\r?\n)', "`$1$template`r`n"
        } else {
            $notesContent = "$template`r`n" + $notesContent
        }
        Set-Content -Path $releaseNotes -Value $notesContent -NoNewline
        Write-Host "  [OK] Added version section in $releaseNotes" -ForegroundColor Green
    }
}

# 5. Synchronize dist/RsyncZilla if it exists
if (Test-Path $distDir) {
    if (Test-Path $manifestJson) {
        Copy-Item $manifestJson -Destination (Join-Path $distDir "version.json") -Force
    }
    if (Test-Path $releaseNotes) {
        Copy-Item $releaseNotes -Destination (Join-Path $distDir "RELEASE_NOTES.md") -Force
    }
    Write-Host "  [OK] Synchronized distribution folder $distDir" -ForegroundColor Green
}

Write-Host "`nSuccessfully updated project version to $newVersion!" -ForegroundColor Green
return $newVersion
