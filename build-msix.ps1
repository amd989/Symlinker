# Build an MSIX bundle for the Microsoft Store.
#
# Produces Symlinker/bin/msix/Symlinker-<version>.msixbundle (unsigned).
# Partner Center signs Store packages, so no certificate is needed to submit.
#
# Local test without signing (needs Developer Mode):
#   Add-AppxPackage -Register Symlinker/bin/msix/x64/layout/AppxManifest.xml

[CmdletBinding(PositionalBinding=$false)]
param (
    [string]$Version = "",
    [string[]]$Architectures = @("x64", "arm64")
)

Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

# Determine version. MSIX needs four parts and the Store needs the last one to be 0.
if ([string]::IsNullOrEmpty($Version)) {
    $tag = $(git describe --tags --abbrev=0 2>$null)
    if ($LASTEXITCODE -ne 0) {
        Write-Error "No Git tag found. Please specify -Version or create a Git tag."
        exit 1
    }
    $Version = $tag.TrimStart('v')
}
$Version = $Version.Split('-')[0]
$parts = @($Version.Split('.'))
while ($parts.Count -lt 3) { $parts += "0" }
$msixVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0"
Write-Output "Version: $Version (package $msixVersion)"

# Find the Windows SDK packaging tool.
$sdkBin = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$makeAppx = Get-ChildItem -Path $sdkBin -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq "x64" -and $_.Directory.Parent.Name -match '^\d+(\.\d+){3}$' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1
if (-not $makeAppx) {
    Write-Error "makeappx.exe not found. Install the Windows 10/11 SDK."
    exit 1
}
Write-Output "MakeAppx: $($makeAppx.FullName)"

$root = $PSScriptRoot
$projDir = Join-Path $root "Symlinker"
$templateDir = Join-Path $root "packaging/msix"
$outDir = Join-Path $projDir "bin/msix"
$bundleInput = Join-Path $outDir "packages"

if (Test-Path $outDir) {
    Remove-Item -Path $outDir -Recurse -Force
}
New-Item -ItemType Directory -Force $bundleInput | Out-Null

foreach ($arch in $Architectures) {
    Write-Output ""
    Write-Output "=== $arch ==="
    $layout = Join-Path $outDir "$arch/layout"

    # The Store has no .NET desktop runtime package to depend on, so ship self-contained.
    dotnet publish (Join-Path $projDir "Symlinker.csproj") `
        --configuration Release `
        --runtime "win-$arch" `
        --self-contained true `
        --output $layout `
        -p:MsixBuild=true `
        -p:PublishSingleFile=false `
        -p:Version=$Version
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed for $arch"
        exit 1
    }

    # Debug symbols and XML docs only bloat the package.
    Get-ChildItem $layout -Include *.pdb, Symlinker.xml -Recurse | Remove-Item -Force

    Copy-Item -Path (Join-Path $templateDir "Assets") -Destination $layout -Recurse
    (Get-Content (Join-Path $templateDir "AppxManifest.xml") -Raw) `
        -replace '\$VERSION\$', $msixVersion `
        -replace '\$ARCH\$', $arch |
        Set-Content -Path (Join-Path $layout "AppxManifest.xml") -Encoding utf8

    $msix = Join-Path $bundleInput "Symlinker-$msixVersion-$arch.msix"
    & $makeAppx.FullName pack /d $layout /p $msix /o
    if ($LASTEXITCODE -ne 0) {
        Write-Error "makeappx pack failed for $arch"
        exit 1
    }
}

$bundle = Join-Path $outDir "Symlinker-$msixVersion.msixbundle"
Write-Output ""
Write-Output "Bundling..."
& $makeAppx.FullName bundle /d $bundleInput /p $bundle /bv $msixVersion /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "makeappx bundle failed"
    exit 1
}

$size = (Get-Item $bundle).Length / 1MB
Write-Output ("Created MSIX bundle: {0:N2} MB" -f $size)
Write-Output "Location: $bundle"
