@echo off
taskkill /F /IM QuickNotch.exe /IM QuickNotch.old.exe >nul 2>&1
del /F /Q "%~dp0publish_v2\QuickNotch.old.exe" >nul 2>&1
start "" "%~dp0publish_v2\QuickNotch.exe"

