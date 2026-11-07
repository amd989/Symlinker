<#
.SYNOPSIS
Builds the Microsoft Store package: Symlinker-<version>.msixbundle (x64 + arm64) in
Symlinker/bin/msix. The Store signs it, so it stays unsigned. Upload it in Partner Center >
Symlinker > the submission > Packages.

.DESCRIPTION
The package identity in packaging/msix/AppxManifest.xml must match Partner Center > Symlinker >
Product identity.

The version comes from the latest Git tag (v4.1.0 gives 4.1.0.0). The Store needs the last part
to be 0 and each upload to have a higher version than the last, so tag a new release (or pass
-Version) before uploading again.

.PARAMETER Version
Version to build, like 4.1.0. Defaults to the latest Git tag.

.PARAMETER Architectures
Architectures to build. Defaults to x64 and arm64.

.PARAMETER Install
Also install this PC's build for a test run (registers the unpacked package; needs Developer
Mode). Replaces an earlier test copy.

.PARAMETER Wack
Also install this PC's build and run the Windows App Certification Kit on it (needs an
administrator shell; the report goes to Symlinker/bin/msix). The kit needs a properly installed
package, so this signs a copy with a test certificate for the package's publisher, trusts that
certificate on this PC, and installs the copy. The bundle to upload stays unsigned.

.EXAMPLE
./build-msix.ps1 -Install
#>

[CmdletBinding(PositionalBinding=$false)]
param (
    [string]$Version = "",
    [string[]]$Architectures = @("x64", "arm64"),
    [switch]$Install,
    [switch]$Wack
)

Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$projDir = Join-Path $root "Symlinker"
$templateDir = Join-Path $root "packaging/msix"
$outDir = Join-Path $projDir "bin/msix"
$bundleInput = Join-Path $outDir "packages"

# Determine version. MSIX needs four parts and the Store needs the last one to be 0.
if ([string]::IsNullOrEmpty($Version)) {
    $tag = $(git describe --tags --abbrev=0 2>$null)
    if ($LASTEXITCODE -ne 0) {
        Write-Error "No Git tag found. Please specify -Version or create a Git tag."
    }
    $Version = $tag.TrimStart('v')
}
$Version = $Version.Split('-')[0]
$parts = @($Version.Split('.'))
while ($parts.Count -lt 3) { $parts += "0" }
$msixVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0"
Write-Output "Version: $Version (package $msixVersion)"

[xml]$template = Get-Content (Join-Path $templateDir "AppxManifest.xml")
$identity = $template.Package.Identity

# The machine's own architecture, not this PowerShell's (x64 PowerShell can run emulated on Arm64).
# RuntimeInformation.OSArchitecture would do this, but Windows PowerShell 5.1 doesn't have it.
$nativeArch = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE
$hostArch = if ($nativeArch -eq 'ARM64') { 'arm64' } else { 'x64' }
if (($Install -or $Wack) -and $Architectures -notcontains $hostArch) {
    Write-Error "-Install and -Wack need the $hostArch build. Add it to -Architectures."
}

if ($Wack) {
    $appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/App Certification Kit/appcert.exe'
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
        IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not (Test-Path $appcert)) {
        Write-Error "No Windows App Certification Kit; it comes with the Windows SDK."
    }
    if (-not $isAdmin) { Write-Error "The certification kit needs an administrator shell." }
}

# Find the Windows SDK tools.
function Find-SdkTool([string]$name) {
    $tool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter $name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" -and $_.Directory.Parent.Name -match '^\d+(\.\d+){3}$' } |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if (-not $tool) { Write-Error "$name not found. Install the Windows 10/11 SDK." }
    return $tool.FullName
}
$makeAppx = Find-SdkTool "makeappx.exe"
$mt = Find-SdkTool "mt.exe"

# A test copy registered from the output folder would break when its files are deleted below.
# Windows also won't register the same version again once its files change. So remove an
# earlier test copy first (this closes it and clears its data).
$installed = Get-AppxPackage -Name $identity.Name
if ($installed -and $installed.SignatureKind -ne 'Store') {
    Write-Output "Removing the earlier test copy..."
    Remove-AppxPackage $installed.PackageFullName
}

if (Test-Path $outDir) {
    try { Remove-Item -Path $outDir -Recurse -Force }
    catch { Write-Error "Couldn't clear $outDir. Close any Symlinker started from it and try again." }
}
New-Item -ItemType Directory -Force $bundleInput | Out-Null
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

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
        -p:PublishSingleFile=false `
        -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed for $arch" }

    # Debug symbols and XML docs only bloat the package.
    Get-ChildItem $layout -Include *.pdb, Symlinker.xml -Recurse | Remove-Item -Force

    Copy-Item -Path (Join-Path $templateDir "Assets") -Destination $layout -Recurse
    (Get-Content (Join-Path $templateDir "AppxManifest.xml") -Raw) `
        -replace '\$VERSION\$', $msixVersion `
        -replace '\$ARCH\$', $arch |
        Set-Content -Path (Join-Path $layout "AppxManifest.xml") -Encoding utf8

    # The window must start unelevated so drag and drop works. Only the link helper
    # (Symlinker.exe --link-helper) elevates, through allowElevation.
    $exeManifest = Join-Path $outDir "$arch/Symlinker.exe.manifest"
    & $mt -nologo -inputresource:"$(Join-Path $layout 'Symlinker.exe');#1" -out:$exeManifest | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Select-String -Path $exeManifest -Pattern 'level="asInvoker"' -Quiet)) {
        Write-Error "Symlinker.exe for $arch asks for admin at launch. The window must start unelevated."
    }

    $msix = Join-Path $bundleInput "Symlinker-$msixVersion-$arch.msix"
    & $makeAppx pack /d $layout /p $msix /o | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "makeappx pack failed for $arch" }

    # Check what the Store will see.
    $zip = [System.IO.Compression.ZipFile]::OpenRead($msix)
    try {
        $reader = New-Object System.IO.StreamReader($zip.GetEntry('AppxManifest.xml').Open())
        [xml]$built = $reader.ReadToEnd()
        $reader.Dispose()
    } finally { $zip.Dispose() }
    $builtIdentity = $built.Package.Identity
    if ($builtIdentity.Version -ne $msixVersion) {
        Write-Error "Package version is $($builtIdentity.Version), expected $msixVersion."
    }
    if ($builtIdentity.ProcessorArchitecture -ne $arch) {
        Write-Error "Package is for $($builtIdentity.ProcessorArchitecture), expected $arch."
    }
    $capabilities = @($built.Package.Capabilities.ChildNodes |
        Where-Object { $_.NodeType -eq 'Element' } | ForEach-Object { $_.GetAttribute('Name') })
    foreach ($needed in 'runFullTrust', 'allowElevation', 'unvirtualizedResources') {
        if ($capabilities -notcontains $needed) {
            Write-Error "The package doesn't declare the $needed capability."
        }
    }
    Write-Output "Packed $msix"
}

$bundle = Join-Path $outDir "Symlinker-$msixVersion.msixbundle"
Write-Output ""
Write-Output "Bundling..."
& $makeAppx bundle /d $bundleInput /p $bundle /bv $msixVersion /o | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "makeappx bundle failed" }
$size = (Get-Item $bundle).Length / 1MB
Write-Output ("Created MSIX bundle: {0:N2} MB" -f $size)

$package = Join-Path $bundleInput "Symlinker-$msixVersion-$hostArch.msix"

if ($Wack) {
    # The kit can't find a package registered from a folder, so install the real .msix. Windows
    # only installs a signed one: sign a copy with a test certificate for the package's publisher
    # and trust it on this PC.
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $identity.Publisher -and $_.FriendlyName -eq 'Symlinker test signing' -and $_.NotAfter -gt (Get-Date).AddDays(1) } |
        Select-Object -First 1
    if (-not $cert) {
        Write-Output "Making a test signing certificate..."
        $cert = New-SelfSignedCertificate -Type Custom -Subject $identity.Publisher -KeyUsage DigitalSignature `
            -FriendlyName 'Symlinker test signing' -CertStoreLocation Cert:\CurrentUser\My `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    if (-not (Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint)) {
        $cer = Join-Path ([IO.Path]::GetTempPath()) 'symlinker-test-signing.cer'
        Export-Certificate -Cert $cert -FilePath $cer | Out-Null
        Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Remove-Item $cer
    }

    $signTool = Find-SdkTool "signtool.exe"
    $signed = Join-Path $outDir "test-signed/Symlinker-$msixVersion-$hostArch.msix"
    New-Item -ItemType Directory -Force (Split-Path $signed) | Out-Null
    Copy-Item $package $signed -Force
    & $signTool sign /q /fd SHA256 /sha1 $cert.Thumbprint $signed
    if ($LASTEXITCODE -ne 0) { Write-Error "signtool failed." }
    Add-AppxPackage -Path $signed -ForceApplicationShutdown
}
elseif ($Install) {
    # Register the unpacked .msix, so the test copy is exactly what gets uploaded.
    $installLayout = Join-Path $outDir "install-$hostArch"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($package, $installLayout)
    Add-AppxPackage -Register (Join-Path $installLayout 'AppxManifest.xml') -ForceApplicationShutdown
}

if ($Install -or $Wack) {
    Write-Output "Symlinker is in the Start menu. Remove it with: Get-AppxPackage $($identity.Name) | Remove-AppxPackage"
}

if ($Wack) {
    $fullName = (Get-AppxPackage -Name $identity.Name).PackageFullName
    $report = Join-Path $outDir "wack-$hostArch.xml"
    Write-Output "Running the certification kit on $fullName (takes a few minutes)..."
    # The kit won't overwrite a report, and reading an old one would give the last run's result.
    Remove-Item $report -ErrorAction SilentlyContinue
    & $appcert reset | Out-Null
    & $appcert test -packagefullname $fullName -reportoutputpath $report
    if (-not (Test-Path $report)) { Write-Error "The certification kit didn't write a report (exit code $LASTEXITCODE)." }
    [xml]$result = Get-Content $report
    $overall = $result.REPORT.OVERALL_RESULT
    Write-Output "Certification kit: $overall ($report)"
    if ($overall -eq 'FAIL') { Write-Error "The certification kit reported failures; see $report." }
}

Write-Output ""
Write-Output "Upload this in Partner Center > Symlinker > the submission > Packages:"
Write-Output "  $bundle"
