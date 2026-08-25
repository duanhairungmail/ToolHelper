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

rem 清理已废弃的 Java 代理进程（仅匹配命令行中的 openGaussProxy，不影响其他 Java 程序）
echo 正在关闭已废弃的数据库代理进程...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0StopLegacyOpenGaussProxy.ps1"
timeout /t 2 /nobreak >nul

set "PUBLISH_DIR=%~dp0..\ToolHelper_Publish"

rem 当前项目不再发布代理，清理旧发布目录中的残留文件；保留 data 等用户数据
if exist "%PUBLISH_DIR%\openGaussProxy" rmdir /s /q "%PUBLISH_DIR%\openGaussProxy"
if exist "%PUBLISH_DIR%\MySqlConnector.dll" del /q "%PUBLISH_DIR%\MySqlConnector.dll"
if exist "%PUBLISH_DIR%\Npgsql.dll" del /q "%PUBLISH_DIR%\Npgsql.dll"
if exist "%PUBLISH_DIR%\OpenGauss.NET.dll" del /q "%PUBLISH_DIR%\OpenGauss.NET.dll"
if exist "%PUBLISH_DIR%\openGaussProxy" (
    echo.
    echo [错误] 旧 openGaussProxy 仍被其他进程占用，请关闭相关 Java 进程后重试。
    pause
    exit /b 1
)

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

dotnet publish ToolHelper.csproj -c Release -o "%PUBLISH_DIR%" --nologo
if errorlevel 1 (
    echo.
    echo [错误] dotnet publish 失败！请确认已安装 .NET 8 SDK 且项目文件完整。
    echo.
    pause
    exit /b 1
)

echo.
echo 发布完成！输出目录: %PUBLISH_DIR%
pause
