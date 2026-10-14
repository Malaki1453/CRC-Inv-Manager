# Cast Right Catch Inventory

**[Download installer (CRC-Inventory-Setup.msi)](https://github.com/Malaki1453/CRC-Inv-Manager/releases/latest/download/CRC-Inventory-Setup.msi)**

## Install

1. Click the download link above.
2. Run the MSI. Windows may say **Windows protected your PC**.
3. Click **More info**, then **Run anyway**.
4. Allow admin if Windows asks.
5. Open **Cast Right Catch Inventory** from the Start Menu.

## Server

**[Download server (CrcInventoryServer-linux-x64.zip)](https://github.com/Malaki1453/CRC-Inv-Manager/releases/download/server-0.8.9/CrcInventoryServer-linux-x64.zip)**

On the Linux host, after the wrappers are in `/usr/local/bin`:

```
sudo serverHelp
sudo serverui
sudo serverStart
sudo serverStop
sudo serverRestart
sudo serverUpdate
```

Copy them once from the published folder:

```
sudo install -m 755 serverui serverStart serverStop serverRestart serverHelp serverUpdate /usr/local/bin/
```
