# ToolHelper

基于 **.NET 8 + WPF** 的桌面工具集，面向极早期火灾探测系统的研发与运维场景，将数据库管理、远程连接、接口测试、安全检测、加解密等分散工具整合到单一应用中。

## 功能一览

| 分类 | 工具 |
|------|------|
| 代码格式化 | SQL 语法格式化、SQL 生成器（SELECT/INSERT/UPDATE/DELETE/CREATE） |
| 远程连接 | VNC 远程桌面、Windows 远程桌面 (RDP)、SSH 终端、SFTP 文件管理、SSH 外挂（electerm，按需下载） |
| 数据库 | MySQL 连接、openGauss 连接（经本地 Java HTTP 代理桥接）、数据库外挂（DBX，按需下载） |
| 接口测试 | 接口验证（自动登录批量检测）、获取设备 ID / MQTT 主题 |
| 漏洞检测与系统优化 | Druid 漏洞检测、KylinOS 漏洞扫描、KylinOS 运维策略（定时重启/日志优化/VNC Server 部署）、KylinOS 系统优化 |
| 对称加密 | AES 加密/解密 |
| 串口调试 | 基本串口调试、极早期 Modbus 调试（申弘/南瑞怡和双协议） |
| 日期工具 | Cron 表达式、获取设备 MAC 地址 |

## 环境要求

- **运行时**：.NET 8.0 SDK / Desktop Runtime（Windows）
- **Java**：openGauss 连接功能需要本机可运行 `java`（本地代理进程依赖）
- **Access Database Engine（可选）**：读取 Access/Excel 文件（System.Data.OleDb）时需要
- **联网（可选）**：SSH 外挂（electerm）/ 数据库外挂（DBX）发布包不包含插件本体，首次使用点击「下载插件」时联网从 GitHub 下载便携版到 `plugins/` 下（支持删除/版本更新）
- **WebView2 Runtime（可选）**：DBX 基于 Tauri 2，Windows 10/11 一般已自带

### Access Database Engine 获取方式

仓库中不包含 `plugins/accessdatabaseengine_X64.exe`（79.5MB 微软官方安装包），请按以下步骤自行下载并放置：

1. 前往微软官方下载页：[Microsoft Access Database Engine 2016 Redistributable](https://www.microsoft.com/en-us/download/details.aspx?id=54920)
2. 下载 `AccessDatabaseEngine_X64.exe`
3. 安装该引擎（命令行安装可加 `/quiet` 参数；若已装 32 位 Office，需加 `/quiet` 强制安装 64 位）
4. （可选）将安装包复制到 `plugins/accessdatabaseengine_X64.exe`，便于分发给其他机器

## 构建与运行

```powershell
# 开发调试
dotnet run --project ToolHelper.csproj

# 发布（输出到 ..\ToolHelper_Publish，发布前自动终止运行中的实例）
.\发布工具.bat
```

或直接双击仓库根目录的 `启动工具.bat` / `发布工具.bat`。

## 项目结构

```
ToolHelper/
├── ViewModels/          # MainViewModel（工具注册/视图缓存/搜索过滤）
├── Views/               # 各工具视图（按功能域分目录），基类 StandardToolView / SshToolBaseView
├── Services/            # FileLogger（按天分文件日志）、OpenGaussProxyClient（Java 代理客户端）、PluginDownloader（外挂下载）
├── Resources/           # 图标、x11vnc 静态二进制（KylinOS VNC Server 部署用）
├── plugins/             # 随发布复制的插件（KylinOS 补丁等）；electerm/dbx 为运行时按需下载目录，不随发布打包
└── openGaussProxy/      # openGauss Java HTTP 代理（随发布复制）
```

## 技术栈

.NET 8.0-windows · WPF · MaterialDesignThemes · CommunityToolkit.Mvvm · SSH.NET · FluentFTP · Lemutec.RemoteViewing.WPF · MySqlConnector · Npgsql / OpenGauss_NET · BouncyCastle · EPPlus · DocumentFormat.OpenXml
