# Publish a GitHub Release that AutoUpdater.NET will force-install on launch.
#
# 1. Bump <Version> / <AssemblyVersion> in CastRightCatchInvManagement.csproj (must be higher than installed PCs).
# 2. From the repo root, in PowerShell:  .\tools\Publish-Release.ps1
# 3. The script publishes the app, builds CRC-Inventory-Setup.msi, writes update.xml,
#    and runs: gh release create vX.Y.Z -- CRC-Inventory-Setup.msi update.xml
#
# A git push of source is not enough. Users update only when a GitHub *Release* exists
# with a higher tag than their AssemblyVersion (example: installed 1.0.0.0, tag v1.0.1).
#
# Requires: Visual Studio 2022 + Installer Projects extension, gh auth login, git.
# Private repo: PCs need a PAT in %ProgramData%\Cast Right Catch\github-update.token
# (repo read) so AutoUpdater can download the MSI.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csproj = Join-Path $root "CastRightCatchInvManagement\CastRightCatchInvManagement.csproj"
$vdproj = Join-Path $root "installer\CRCInventorySetup.vdproj"
$xmlPath = Join-Path $root "installer\update.xml"

function Read-CsprojVersion([string]$path) {
    [xml]$xml = Get-Content $path
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
    $ver = $xml.SelectSingleNode("//Version")
    if ($ver -eq $null -or [string]::IsNullOrWhiteSpace($ver.InnerText)) {
        throw "Set <Version> in CastRightCatchInvManagement.csproj before publishing."
    }
    return $ver.InnerText.Trim()
}

function To-FourPart([string]$ver) {
    $parts = $ver.Split('.')
    while ($parts.Length -lt 4) { $parts += "0" }
    return ($parts[0..3] -join '.')
}

function To-ThreePart([string]$ver) {
    $parts = $ver.Split('.')
    while ($parts.Length -lt 3) { $parts += "0" }
    return ($parts[0..2] -join '.')
}

$version = Read-CsprojVersion $csproj
$four = To-FourPart $version
$three = To-ThreePart $version
$tag = "v$three"

Write-Host "Version $four  (GitHub tag $tag)"

# Windows Installer requires a new ProductCode (and PackageCode) whenever ProductVersion changes.
$vdprojText = Get-Content -Raw $vdproj
$vdprojText = [regex]::Replace($vdprojText, '"ProductVersion" = "8:[^"]+"', '"ProductVersion" = "8:' + $three + '"')
$vdprojText = [regex]::Replace($vdprojText, '"ProductCode" = "8:\{[0-9A-Fa-f-]+\}"', '"ProductCode" = "8:{' + [guid]::NewGuid().ToString().ToUpper() + '}"')
$vdprojText = [regex]::Replace($vdprojText, '"PackageCode" = "8:\{[0-9A-Fa-f-]+\}"', '"PackageCode" = "8:{' + [guid]::NewGuid().ToString().ToUpper() + '}"')
Set-Content -Path $vdproj -Value $vdprojText -NoNewline

$pubxml = Join-Path $root "CastRightCatchInvManagement\Properties\PublishProfiles\InstallerProfile.pubxml"
Write-Host "Publishing self-contained win-x64…"
dotnet publish (Join-Path $root "CastRightCatchInvManagement\CastRightCatchInvManagement.csproj") `
    -c Release `
    -p:PublishProfileFullPath=$pubxml
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$devenv = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.com"
$disableOop = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\VSI\DisableOutOfProcBuild\DisableOutOfProcBuild.exe"
if (Test-Path $disableOop) {
    & $disableOop | Out-Null
}

$msi = Join-Path $root "installer\Release\CRC-Inventory-Setup.msi"
Write-Host "Building MSI (Visual Studio Installer Projects)…"
if (-not (Test-Path $devenv)) {
    throw "devenv.com not found. Open the solution in Visual Studio, right-click CRCInventorySetup, Build."
}
& $devenv (Join-Path $root "CastRightCatchInvManagement.sln") /Build "Release" /Project CRCInventorySetup
if (-not (Test-Path $msi)) {
    throw "MSI was not produced at $msi. In Visual Studio: CRCInventorySetup → Add → Project Output → Publish Items (not Primary Output), then Build."
}

$msiName = "CRC-Inventory-Setup.msi"
$feedUrl = "https://github.com/Malaki1453/CRC-Inv-Manager/releases/download/$tag/$msiName"
$notesUrl = "https://github.com/Malaki1453/CRC-Inv-Manager/releases/tag/$tag"
@"
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>$four</version>
  <url>$feedUrl</url>
  <changelog>$notesUrl</changelog>
  <mandatory mode="2">true</mandatory>
</item>
"@ | Set-Content -Path $xmlPath -Encoding UTF8

Write-Host "Creating GitHub release $tag…"
gh release create $tag --title "Cast Right Catch Inventory $three" --generate-notes $msi $xmlPath
if ($LASTEXITCODE -ne 0) {
    throw "gh release create failed. Run: gh auth login"
}

Write-Host "Done. Installed apps will be forced to $four on next launch."
