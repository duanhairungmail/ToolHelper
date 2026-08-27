# ToolHelper

基于 **.NET 8 + WPF** 的桌面工具集，面向极早期火灾探测系统的研发与运维场景，将远程连接、数据库管理、接口测试、安全检测、开发辅助等分散工具整合到单一应用中。采用 MVVM 架构 + MaterialDesign 主题，通过分类+搜索导航，新增工具只需注册即可纳入主界面。

## 功能一览（7 分类 13 工具）

| 分类 | 工具 |
|------|------|
| 远程连接工具 | 远程外挂连接（electerm：SSH/SFTP/RDP/VNC 深链唤起，按需下载） |
| 数据库连接工具 | 数据库外挂连接（DBX：MySQL/postgresql 填参与深链唤起，按需下载） |
| 接口测试工具 | 极早期接口验证（登录 + 设备ID/MQTT主题 + 16接口批量验证 + cron 自动检测）、获取设备MAC地址 |
| MQTT测试工具 | Node-RED 可视化编排（串口、Modbus、HTTP 流程） |
| 漏洞检测与系统优化 | Druid 漏洞检测（HTTP 扫描，xlsx/docx 报告导出）、KylinOS 运维策略（6 Tab：定时重启/日志优化/VNC Server/PostgreSQL/漏洞扫描/系统优化） |
| 串口调试工具 | 基本串口调试、极早期 Modbus 调试（申弘/南瑞怡和双协议） |
| 其他工具 | Cron 表达式、SQL语句生成与格式化（五模式表单生成 + 关键字美化）、AES 加密/解密、群 Ping（CIDR 网段扫描/并发 ping/CSV+Excel 导出） |

## 环境要求

- **运行时**：.NET 8.0 Desktop Runtime（Windows）
- **Access Database Engine（可选）**：读取 Access/Excel 文件（System.Data.OleDb）时需要
- **联网（可选）**：electerm / DBX / Node-RED 外挂发布包不包含插件本体，首次使用点击「下载插件」时联网从 GitHub 下载便携版到 `plugins/` 下

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
├── ViewModels/          # MainViewModel（工具注册/分类/搜索过滤），ToolCategory/ToolItem
├── Views/               # 各工具视图（按功能域分目录），基类 StandardToolView / SshToolBaseView
├── Services/            # FileLogger（按天分文件日志）、PluginDownloader（外挂下载）
├── Resources/           # 图标、x11vnc 静态二进制（KylinOS VNC Server 部署用）
└── plugins/             # 随发布复制的 KylinOS 资产；electerm/dbx 按需下载
```

## 技术栈

.NET 8.0-windows · WPF · MaterialDesignThemes · CommunityToolkit.Mvvm · SSH.NET · EPPlus · DocumentFormat.OpenXml · BouncyCastle · Newtonsoft.Json · System.IO.Ports · System.Data.OleDb
