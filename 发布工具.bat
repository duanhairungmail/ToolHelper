@echo off
chcp 65001 >nul
title 发布 ToolHelper
cd /d "%~dp0"

rem 检查 .NET SDK 是否可用
where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo [错误] 未找到 dotnet 命令，请先安装 .NET 8 SDK。
    echo 下载地址: https://aka.ms/dotnet-download
    echo.
    pause
    exit /b 1
)

echo 正在关闭可能运行的 ToolHelper...
taskkill /IM ToolHelper.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

echo 正在发布 ToolHelper...
dotnet clean ToolHelper.csproj -c Release -v q --nologo
if errorlevel 1 (
    echo.
    echo [错误] dotnet clean 失败！可能未安装 .NET 8 SDK。
    echo 请运行 dotnet --version 验证，或访问 https://aka.ms/dotnet-download 安装。
    echo.
    pause
    exit /b 1
)

dotnet publish ToolHelper.csproj -c Release -o "..\ToolHelper_Publish" --nologo
if errorlevel 1 (
    echo.
    echo [错误] dotnet publish 失败！请确认已安装 .NET 8 SDK 且项目文件完整。
    echo.
    pause
    exit /b 1
)

echo.
echo 发布完成！输出目录: ..\ToolHelper_Publish
pause
