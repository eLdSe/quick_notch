@echo off
setlocal
if exist "%~dp0..\.venv\Scripts\pythonw.exe" (
    start "" "%~dp0..\.venv\Scripts\pythonw.exe" "%~dp0main.py"
) else (
    start "" pyw -3 "%~dp0main.py"
)
