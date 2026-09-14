<#
.SYNOPSIS
    Publishes a new release of RsyncZilla to GitHub.

.DESCRIPTION
    1. Optionally updates version files (if a version or bump switch is passed).
    2. Runs test suite (dotnet test).
    3. Commits any modified files (chore(release): vX.Y.Z).
    4. Pushes commits to origin main.
    5. Creates and pushes Git tag (vX.Y.Z) to GitHub.
    6. Triggers GitHub Actions workflow to build, package ZIP, and publish the GitHub Release.

.PARAMETER Version
    Optional target version (e.g. "1.0.2"). If omitted and no bump switch is passed, uses the current project version.

.PARAMETER Patch
    Increments patch version before releasing (e.g. 1.0.1 -> 1.0.2).

.PARAMETER Minor
    Increments minor version before releasing (e.g. 1.0.1 -> 1.1.0).

.PARAMETER Major
    Increments major version before releasing (e.g. 1.0.1 -> 2.0.0).

.PARAMETER SkipTests
    Skips running dotnet test before publishing.

.PARAMETER Yes
    Skip confirmation prompts (non-interactive).

.EXAMPLE
    .\scripts\publish-release.ps1
    .\scripts\publish-release.ps1 1.0.2
    .\scripts\publish-release.ps1 -Patch
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
    [switch]$Major,

    [switch]$SkipTests,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

# 1. Update version if requested
if ($Patch -or $Minor -or $Major -or (-not [string]::IsNullOrWhiteSpace($Version))) {
    $setVerScript = Join-Path $PSScriptRoot "set-version.ps1"
    if ($Patch) {
        & $setVerScript -Patch
    } elseif ($Minor) {
        & $setVerScript -Minor
    } elseif ($Major) {
        & $setVerScript -Major
    } else {
        & $setVerScript $Version
    }
}

# 2. Read current version from RsyncZilla.csproj
$mainCsproj = Join-Path $repoRoot "src\RsyncZilla\RsyncZilla.csproj"
$csprojContent = Get-Content $mainCsproj -Raw
if (-not ($csprojContent -match '<Version>(.*?)</Version>')) {
    Write-Error "Could not determine current version from $mainCsproj"
}
$currentVersion = $matches[1].Trim()
$tag = "v$currentVersion"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  Publishing RsyncZilla Release: $tag" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 3. Check if tag already exists in remote
Write-Host "Checking remote tags on GitHub..." -ForegroundColor Yellow
$remoteTags = git ls-remote --tags origin "refs/tags/$tag" 2>&1
if ($remoteTags -match "refs/tags/$tag") {
    Write-Error "Tag '$tag' already exists on GitHub remote! Please bump version first with .\scripts\set-version.ps1"
}

# 4. Run automated tests
if (-not $SkipTests) {
    Write-Host "`nRunning automated test suite (dotnet test)..." -ForegroundColor Yellow
    dotnet test --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed! Release aborted."
    }
    Write-Host "  [OK] All tests passed." -ForegroundColor Green
}

# 5. Confirm with user
if (-not $Yes) {
    $confirm = Read-Host "`nReady to commit, create tag '$tag' and push to GitHub? [Y/n]"
    if ($confirm -and $confirm.Trim().ToLower() -ne "y" -and $confirm.Trim().ToLower() -ne "yes") {
        Write-Host "Release publication cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# 6. Commit pending changes if any
$status = git status --porcelain
if (-not [string]::IsNullOrWhiteSpace($status)) {
    Write-Host "`nCommitting changes for $tag..." -ForegroundColor Yellow
    git add .
    git commit -m "chore(release): $tag"
}

# 7. Push branch to origin
Write-Host "`nPushing branch to origin main..." -ForegroundColor Yellow
git push origin main
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to push commits to origin main."
}

# 8. Create local tag if it doesn't already exist
$localTag = git tag -l $tag
if ($localTag -eq $tag) {
    Write-Host "Tag $tag already exists locally." -ForegroundColor Yellow
} else {
    Write-Host "Creating local git tag $tag..." -ForegroundColor Yellow
    git tag -a $tag -m "Release $tag"
}

# 9. Push tag to GitHub
Write-Host "Pushing tag $tag to GitHub origin..." -ForegroundColor Yellow
git push origin $tag
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to push tag $tag to origin."
}

# 10. Summary and next steps
Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "  Release $tag successfully triggered on GitHub!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host "GitHub Actions workflow is now compiling, packaging the ZIP,"
Write-Host "and creating the release with notes on GitHub."
Write-Host ""
Write-Host "Track workflow: https://github.com/kanowins/RsyncZilla/actions" -ForegroundColor Cyan
Write-Host "View releases:  https://github.com/kanowins/RsyncZilla/releases" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Green
