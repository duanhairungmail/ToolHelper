# ToolHelper_nodered

为 ToolHelper 构建 Node-RED `win-x64` 便携运行时。目标机器无需预装 Node.js 或管理员权限。

## 构建

在 PowerShell 中执行：

```powershell
.\build-nodered-portable.ps1
```

默认固定版本：

- Node.js `22.23.1`
- Node-RED `5.0.4`
- `node-red-node-serialport` `2.0.3`
- `node-red-contrib-modbus` `5.60.2`

输出位于 `dist/`，包括便携 zip 和 SHA256 文件。发布到本仓库：

```powershell
.\build-nodered-portable.ps1 -PublishRelease
```

压缩包解压到 ToolHelper 的 `plugins/nodered/` 后，目录必须包含 `node/node.exe`、`node_modules/node-red/red.js` 和 `data/`。ToolHelper 启动命令为：

```text
node/node.exe node_modules/node-red/red.js --userDir data --port <实际端口>
```
