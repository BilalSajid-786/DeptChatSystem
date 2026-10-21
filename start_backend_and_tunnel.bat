@echo off
title DeptChat Launcher
echo ========================================================
echo   Launching DeptChat WebApi and Cloudflare Tunnel...
echo ========================================================
echo.

start "DeptChat WebApi" cmd /k "cd /d "%~dp0DeptChat.WebApi" && bin\Debug\net10.0\DeptChat.WebApi.exe --urls http://0.0.0.0:5051"

timeout /t 3 /nobreak >nul

start "Cloudflare Tunnel" cmd /k ""%~dp0tools\cloudflared.exe" tunnel --protocol http2 --url http://127.0.0.1:5051"

echo Both services launched in separate windows!
echo Keep both windows open while testing the mobile app.
pause
