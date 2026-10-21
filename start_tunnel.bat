@echo off
title Cloudflare Tunnel - DeptChat
echo Starting Cloudflare Tunnel (HTTP/2 mode) pointing to http://127.0.0.1:5051...
echo.
"%~dp0tools\cloudflared.exe" tunnel --protocol http2 --url http://127.0.0.1:5051
pause
