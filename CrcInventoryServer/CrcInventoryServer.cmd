@echo off
setlocal
set "HERE=%~dp0"
set "EXE=%HERE%CrcInventoryServer.exe"
if not exist "%EXE%" set "EXE=%HERE%publish\CrcInventoryServer.exe"
if not exist "%EXE%" (
  echo Publishing CrcInventoryServer.exe ...
  dotnet publish "%HERE%CrcInventoryServer.csproj" -c Release -p:PublishProfile=FolderProfile
  if errorlevel 1 exit /b 1
  set "EXE=%HERE%CrcInventoryServer.exe"
)
"%EXE%" %*
exit /b %ERRORLEVEL%
