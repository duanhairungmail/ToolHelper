@echo off
chcp 65001 >nul
title 发布 ToolHelper
cd /d "%~dp0"

echo 正在关闭可能运行的 ToolHelper...
taskkill /IM ToolHelper.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

echo 正在发布 ToolHelper...
dotnet clean ToolHelper.csproj -c Release -v q --nologo
dotnet publish ToolHelper.csproj -c Release -o "..\ToolHelper_Publish" --nologo
echo.
echo 发布完成！输出目录: ..\ToolHelper_Publish
pause
