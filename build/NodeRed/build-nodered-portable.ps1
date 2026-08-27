[CmdletBinding()]
param(
    [string]$NodeVersion = "22.23.1",
    [string]$NodeRedVersion = "5.0.4",
    [string]$SerialPortVersion = "2.0.3",
    [string]$ModbusVersion = "5.60.2",
    [string]$OutputDirectory = "dist",
    [switch]$PublishRelease,
    [string]$Repository = "duanhairungmail/ToolHelper_nodered"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot ".")).Path
$work = Join-Path $root ".work"
$out = Join-Path $root $OutputDirectory
$stage = Join-Path $work "stage"
$nodeZip = Join-Path $work "node-v$NodeVersion-win-x64.zip"
$nodeUrl = "https://nodejs.org/dist/v$NodeVersion/node-v$NodeVersion-win-x64.zip"
$assetName = "nodered-portable-v$NodeRedVersion-win-x64.zip"
$assetPath = Join-Path $out $assetName

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败（$LASTEXITCODE）：$FilePath $($Arguments -join ' ')"
    }
}

New-Item -ItemType Directory -Force -Path $work, $out | Out-Null
if (-not (Test-Path $nodeZip)) {
    Write-Host "下载 Node.js $NodeVersion ..."
    Invoke-WebRequest -Uri $nodeUrl -OutFile $nodeZip
}

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$extract = Join-Path $work "node-extract"
Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive -Path $nodeZip -DestinationPath $extract
$nodeRoot = Join-Path $extract "node-v$NodeVersion-win-x64"
if (-not (Test-Path (Join-Path $nodeRoot "node.exe"))) {
    throw "Node.js 压缩包中未找到 node.exe：$nodeRoot"
}

$portableNode = Join-Path $stage "node"
Copy-Item $nodeRoot $portableNode -Recurse -Force
$npm = Join-Path $portableNode "npm.cmd"
$packageConfig = @{ name = "toolhelper-nodered-runtime"; private = $true; allowScripts = @( "@serialport/bindings-cpp@10.8.0", "@serialport/bindings-cpp@12.0.1", "@serialport/bindings-cpp@13.0.0" ) }
$packageConfig | ConvertTo-Json | Set-Content -Path (Join-Path $stage "package.json") -Encoding UTF8
Set-Content -Path (Join-Path $stage ".npmrc") -Value "allow-scripts=@serialport/bindings-cpp" -Encoding ASCII

Write-Host "安装 Node-RED 及串口/Modbus 节点 ..."
Invoke-Native $npm @(
    "install", "--prefix", $stage, "--omit=dev", "--no-audit", "--no-fund", "--allow-remote", "all",
    "node-red@$NodeRedVersion",
    "node-red-node-serialport@$SerialPortVersion",
    "node-red-contrib-modbus@$ModbusVersion"
)

Write-Host "构建串口原生绑定 ..."
Invoke-Native $npm @("rebuild", "@serialport/bindings-cpp", "--foreground-scripts", "--allow-remote", "all")
if (-not (Get-ChildItem (Join-Path $stage "node_modules\@serialport\bindings-cpp\prebuilds\win32-x64") -Filter "*.node" -ErrorAction SilentlyContinue)) { throw "串口原生绑定构建失败：未找到 win32-x64 .node 文件" }
New-Item -ItemType Directory -Force -Path (Join-Path $stage "data") | Out-Null
Set-Content -Path (Join-Path $stage "data\.gitkeep") -Value "" -Encoding ASCII
Set-Content -Path (Join-Path $stage "version.txt") -Value "v$NodeRedVersion" -Encoding UTF8
$manifest = [ordered]@{
    node = $NodeVersion
    nodeRed = $NodeRedVersion
    serialPort = $SerialPortVersion
    modbus = $ModbusVersion
    architecture = "win-x64"
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
}
$manifest | ConvertTo-Json | Set-Content -Path (Join-Path $stage "manifest.json") -Encoding UTF8

# npm 缓存和临时文件不属于运行时，避免把发布资产做大。
Remove-Item (Join-Path $stage "node_modules\.cache") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $stage "node_modules\**\*.log") -Force -ErrorAction SilentlyContinue

Remove-Item $assetPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $assetPath -CompressionLevel Optimal
$hash = (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "${assetPath}.sha256" -Value "$hash  $assetName" -Encoding ASCII
Write-Host "已生成：$assetPath"
Write-Host "SHA256：$hash"

if ($PublishRelease) {
    $tag = "v$NodeRedVersion"
    $releaseExists = $false
    try { & gh release view $tag --repo $Repository 2>$null; $releaseExists = $LASTEXITCODE -eq 0 } catch { $releaseExists = $false }
    if ($releaseExists) {
        throw "Release $tag 已存在，请提高 NodeRedVersion 后再发布。"
    }
    $hashPath = Join-Path $out ($assetName + ".sha256")
    Invoke-Native "gh" @("release", "create", $tag, $assetPath, $hashPath, "--repo", $Repository, "--title", "Node-RED $NodeRedVersion 便携运行时", "--notes", "Node.js $NodeVersion；Node-RED $NodeRedVersion；node-red-node-serialport $SerialPortVersion；node-red-contrib-modbus $ModbusVersion。")
    if ($LASTEXITCODE -ne 0) { throw "GitHub Release 发布失败" }
}












