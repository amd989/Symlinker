# From https://janjones.me/posts/clickonce-installer-build-publish-github/.

[CmdletBinding(PositionalBinding=$false)]
param (
    [switch]$OnlyBuild=$false,
    [string]$SignedArtifactDir=""
)

$appName = "Symlinker" 
$projDir = "Symlinker"

Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

Write-Output "Working directory: $pwd"

# Find MSBuild.
$msBuildPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe `
    -prerelease | select-object -first 1
Write-Output "MSBuild: $((Get-Command $msBuildPath).Path)"

# Load current Git tag.
$tag = $(git describe --tags)
Write-Output "Tag: $tag"

# Parse tag into a three-number version.
$version = $tag.Split('-')[0].TrimStart('v')
$version = "$version.0"
Write-Output "Version: $version"

# Clean output directory.
$publishDir = "bin/publish"
$outDir = "$projDir/$publishDir"
if (Test-Path $outDir) {
    Remove-Item -Path $outDir -Recurse
}

# Publish the application.
Push-Location $projDir
try {
    Write-Output "Restoring:"
    dotnet restore -r win-x64
    Write-Output "Publishing:"
    $msBuildVerbosityArg = "/v:m"
    if ($env:CI) {
        $msBuildVerbosityArg = ""
    }
    & $msBuildPath /target:publish /p:PublishProfile=ClickOnceProfile `
        /p:ApplicationVersion=$version /p:Configuration=Release `
        /p:PublishDir=$publishDir /p:PublishUrl=$publishDir `
        $msBuildVerbosityArg

    # Measure publish size.
    $publishSize = (Get-ChildItem -Path "$publishDir/Application Files" -Recurse |
        Measure-Object -Property Length -Sum).Sum / 1Mb
    Write-Output ("Published size: {0:N2} MB" -f $publishSize)
}
finally {
    Pop-Location
}

if ($OnlyBuild) {
    Write-Output "Build finished."
    exit
}

# Determine which directory to deploy (signed or unsigned).
$deployDir = $outDir
if ($SignedArtifactDir) {
    if (-Not (Test-Path $SignedArtifactDir)) {
        throw "Signed artifact directory not found: $SignedArtifactDir"
    }
    $deployDir = $SignedArtifactDir
    Write-Output "Using signed artifacts from: $SignedArtifactDir"
} else {
    Write-Output "Using unsigned artifacts from: $outDir"
}

# Clone `gh-pages` branch.
$ghPagesDir = "gh-pages"
if (-Not (Test-Path $ghPagesDir)) {
    git clone $(git config --get remote.origin.url) -b gh-pages `
        --depth 1 --single-branch $ghPagesDir
}

Push-Location $ghPagesDir
try {
    # Remove previous application files.
    Write-Output "Removing previous files..."
    if (Test-Path "Application Files") {
        Remove-Item -Path "Application Files" -Recurse
    }
    if (Test-Path "$appName.application") {
        Remove-Item -Path "$appName.application"
    }

    # Copy new application files.
    Write-Output "Copying new files..."
    Copy-Item -Path "../$deployDir/Application Files","../$deployDir/$appName.application" `
        -Destination . -Recurse

    # Stage and commit.
    Write-Output "Staging..."
    git add -A
    Write-Output "Committing..."
    git commit -m "Update to v$version"

    # Push.
    git push
} finally {
    Pop-Location
}