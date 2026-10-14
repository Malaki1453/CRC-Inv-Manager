# Publish CrcInventoryServer-linux-x64.zip and attach it to a GitHub Release
# that sudo serverUpdate can find.
#
# From the repo root, in PowerShell:  .\tools\Publish-Server.ps1
# Requires: gh auth login
#
# Tag is server-X.Y.Z from CrcInventoryServer.csproj <Version>.
# Asset name must stay CrcInventoryServer-linux-x64.zip.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csproj = Join-Path $root "CrcInventoryServer\CrcInventoryServer.csproj"
$pubxml = Join-Path $root "CrcInventoryServer\Properties\PublishProfiles\LinuxProfile.pubxml"
$publishDir = Join-Path $root "CrcInventoryServer\publish-linux"
$zipName = "CrcInventoryServer-linux-x64.zip"
$zipPath = Join-Path $root $zipName

function Read-CsprojVersion([string]$path) {
    [xml]$xml = Get-Content $path
    $ver = $xml.SelectSingleNode("//Version")
    if ($ver -eq $null -or [string]::IsNullOrWhiteSpace($ver.InnerText)) {
        throw "Set <Version> in CrcInventoryServer.csproj before publishing."
    }
    return $ver.InnerText.Trim()
}

$version = Read-CsprojVersion $csproj
$tag = "server-$version"
Write-Host "Server version $version  (GitHub tag $tag)"

Write-Host "Publishing self-contained linux-x64…"
dotnet publish (Join-Path $root "CrcInventoryServer\CrcInventoryServer.csproj") `
    -c Release `
    -f net8.0 `
    -p:PublishProfileFullPath=$pubxml
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

if (-not (Test-Path (Join-Path $publishDir "CrcInventoryServer"))) {
    throw "Publish did not produce CrcInventoryServer in $publishDir"
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "Zipping $zipName…"
Push-Location $publishDir
try {
    Compress-Archive -Path * -DestinationPath $zipPath -Force
}
finally {
    Pop-Location
}

Write-Host "Creating GitHub release $tag (not Latest, so AutoUpdater still sees the client MSI)…"
gh release create $tag `
    --repo Malaki1453/CRC-Inv-Manager `
    --target database-creation `
    --latest=false `
    --title "Inventory server $version" `
    --notes "Linux x64 host for Cast Right Catch Inventory. Install with sudo serverUpdate, or unzip into /opt/crc-inventory and copy the wrappers to /usr/local/bin. Data in /var/lib/crc-inventory is not in this zip." `
    $zipPath
if ($LASTEXITCODE -ne 0) {
    throw "gh release create failed. Run: gh auth login"
}

Write-Host "Done. sudo serverUpdate looks for $zipName on $tag."
