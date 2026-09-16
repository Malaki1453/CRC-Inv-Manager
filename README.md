# Cast Right Catch Inventory

**[Download installer (CRC-Inventory-Setup.msi)](https://github.com/Malaki1453/CRC-Inv-Manager/releases/latest/download/CRC-Inventory-Setup.msi)**

That link always downloads the newest installer. A git push of source code does **not** change it — only a GitHub **Release** with an MSI attached does.

## Install

1. Click the download link above (or open [Releases](https://github.com/Malaki1453/CRC-Inv-Manager/releases/latest) and download `CRC-Inventory-Setup.msi`).
2. Run the MSI on Windows. Allow admin if Windows asks.
3. Open **Cast Right Catch Inventory** from the Start Menu.

## Uninstall

Windows **Settings → Apps → Installed apps** (or Control Panel → Programs and Features). Search **Cast Right Catch Inventory** → Uninstall.

The shared data folder is not deleted.

Installed copies check GitHub on launch. If a newer release exists, they are forced to install it before the workspace opens.

This repo is private. People downloading the MSI must be signed into GitHub with access. PCs that auto-update need a GitHub token (see below).

## If the download 404s (first MSI)

The installer is not stored in git. Attach the MSI you built to a release:

1. In Visual Studio, build **CRCInventorySetup** (Release if you can; Debug also produces the file).
2. Find `installer\Release\CRC-Inventory-Setup.msi` or `installer\Debug\CRC-Inventory-Setup.msi`.
3. Open [Create a new release](https://github.com/Malaki1453/CRC-Inv-Manager/releases/new).
4. Tag: `v1.0.0` (must match the app version).
5. Title: `Cast Right Catch Inventory 1.0.0`.
6. Drag the MSI onto the assets box. The filename must stay **`CRC-Inventory-Setup.msi`**.
7. Publish the release.

The download button on this README then works.

## Ship a new version (forced update)

Installed PCs only update when the **release tag is higher** than the version already installed (example: `1.0.0.0` on the PC, tag `v1.0.1`).

1. In `CastRightCatchInvManagement\CastRightCatchInvManagement.csproj`, bump both:
   - `<Version>1.0.1</Version>`
   - `<AssemblyVersion>1.0.1.0</AssemblyVersion>`
2. In Visual Studio, build **CRCInventorySetup** (Release).
3. From the repo root in PowerShell (needs [GitHub CLI](https://cli.github.com/) and `gh auth login`):

```powershell
.\tools\Publish-Release.ps1
```

That creates GitHub release `v1.0.1` and uploads `CRC-Inventory-Setup.msi`. The README download link now points at that file. The next time an older install launches, it downloads and runs the new MSI.

To do it by hand instead of the script: build the setup project, then [create a release](https://github.com/Malaki1453/CRC-Inv-Manager/releases/new) with tag `vX.Y.Z` and attach `CRC-Inventory-Setup.msi`.

## Forced updates on a private repo

User PCs cannot download the MSI unless they have GitHub access. Put a PAT with **repo read** in:

`%ProgramData%\Cast Right Catch\github-update.token`

One line, no extra text. Without that file, launch skips the update and opens the old version.

## Test a forced update

1. Install `v1.0.0` from the README link (or the MSI you attached).
2. Launch it once. With no newer release, it should open normally after “Checking for updates…”.
3. Ship `v1.0.1` with the steps above.
4. Launch the **Start Menu** copy of `1.0.0` (not F5 in Visual Studio). It should download the new MSI and not open the workspace. After the installer finishes, launch again — it should be `1.0.1`.
