@echo off
setlocal
cd /d "%~dp0"
call "%~dp0CrcInventoryServer\CrcInventoryServer.cmd" %*
exit /b %ERRORLEVEL%
