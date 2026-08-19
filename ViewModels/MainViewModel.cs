using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace ToolHelper.ViewModels;

public partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// 视图缓存：每个 ToolItem 对应一个视图实例，切换工具时保留数据
    /// </summary>
    private readonly Dictionary<ToolItem, object> _viewCache = new();

    [ObservableProperty]
    private List<ToolCategory> _categories = new();

    [ObservableProperty]
    private ToolCategory? _selectedCategory;

    [ObservableProperty]
    private ToolItem? _selectedTool;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private List<ToolCategory> _filteredCategories = new();

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    public MainViewModel()
    {
        RegisterAllTools();
        FilteredCategories = Categories;
        if (Categories.Count > 0)
        {
            SelectedCategory = Categories[0];
            if (SelectedCategory.Tools.Count > 0)
            {
                SelectedTool = SelectedCategory.Tools[0];
                CurrentView = GetOrCreateView(SelectedTool);
            }
        }
    }

    partial void OnSelectedCategoryChanged(ToolCategory? value)
    {
        // 分类切换时不做工具切换，由 SelectionChanged 事件处理
    }

    partial void OnSelectedToolChanged(ToolItem? value)
    {
        if (value != null)
        {
            CurrentView = GetOrCreateView(value);
        }
    }

    /// <summary>
    /// 获取或创建视图实例（缓存机制，保留工具操作状态）
    /// </summary>
    private object GetOrCreateView(ToolItem tool)
    {
        if (_viewCache.TryGetValue(tool, out var cached))
            return cached;

        var view = tool.GetView();
        _viewCache[tool] = view;
        return view;
    }

    /// <summary>
    /// 退出应用时释放所有缓存视图中的连接资源
    /// </summary>
    public void DisposeAllViews()
    {
        foreach (var view in _viewCache.Values)
        {
            if (view is Views.Remote.VncView vnc)
                vnc.SafeDisconnect();
            else if (view is Views.Remote.RdpView rdp)
                rdp.SafeDisconnect();
            else if (view is Views.Remote.SshView ssh)
                ssh.SafeDisconnect();
            else if (view is Views.Remote.FtpView ftp)
                ftp.SafeDisconnect();
            else if (view is Views.Database.MySqlView mysql)
                mysql.SafeDisconnect();
            else if (view is Views.Database.OpenGaussView og)
                og.SafeDisconnect();
            else if (view is Views.Serial.SerialDebugView serial)
                serial.SafeDisconnect();
            else if (view is Views.Security.KylinOsScanView kylin)
                kylin.SafeDisconnect();
            else if (view is Views.Security.KylinOsOptimizeView kylinOpt)
                kylinOpt.SafeDisconnect();
            else if (view is Views.Security.KylinOsDeployView kylinDeploy)
                kylinDeploy.SafeDisconnect();
        }
        _viewCache.Clear();
    }

    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredCategories = Categories;
        }
        else
        {
            var keyword = SearchText.Trim().ToLower();
            var filtered = Categories
                .Select(c => new ToolCategory(c.Name, c.Icon)
                {
                    Tools = c.Tools.Where(t =>
                        t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        t.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList()
                })
                .Where(c => c.Tools.Count > 0)
                .ToList();
            FilteredCategories = filtered;
        }
    }

    private void RegisterAllTools()
    {
        // ===== 代码格式化 =====
        var fmtCat = new ToolCategory("代码格式化", "FormatText");
        fmtCat.Tools.Add(new ToolItem("SQL语法格式化", "格式化和美化 SQL 代码，支持加载本地文件",
            () => new Views.Format.SqlFormatView()));
        fmtCat.Tools.Add(new ToolItem("SQL 生成器", "可视化表单驱动 SQL 生成，支持 SELECT/INSERT/UPDATE/DELETE/CREATE 五种模式",
            () => new Views.Format.SqlGeneratorView()));
        Categories.Add(fmtCat);

        // ===== 远程连接工具 =====
        var remoteCat = new ToolCategory("远程连接工具", "RemoteDesktop");
        remoteCat.Tools.Add(new ToolItem("VNC 连接", "VNC 远程桌面连接，支持缩放、剪贴板共享、快捷键发送",
            () => new Views.Remote.VncView()));
        remoteCat.Tools.Add(new ToolItem("Windows远程桌面", "通过 RDP 协议连接 Windows 远程桌面，支持全屏、剪贴板共享",
            () => new Views.Remote.RdpView()));
        remoteCat.Tools.Add(new ToolItem("SSH 终端", "SSH 远程终端，支持命令执行、实时输出、交互式 Shell",
            () => new Views.Remote.SshView()));
        remoteCat.Tools.Add(new ToolItem("SFTP 文件管理", "通过 SFTP 协议连接远程服务器，浏览目录、上传、下载、删除文件",
            () => new Views.Remote.FtpView()));
        Categories.Add(remoteCat);

        // ===== 数据库连接工具 =====
        var dbCat = new ToolCategory("数据库连接工具", "Database");
        dbCat.Tools.Add(new ToolItem("MySQL 连接", "MySQL 数据库连接，支持浏览表结构、执行 SQL 查询、查看结果",
            () => new Views.Database.MySqlView()));
        dbCat.Tools.Add(new ToolItem("openGauss 连接", "openGauss 国产数据库连接（PostgreSQL 协议），支持浏览表结构、执行 SQL 查询",
            () => new Views.Database.OpenGaussView()));
        Categories.Add(dbCat);

        // ===== 接口测试工具 =====
        var apiCat = new ToolCategory("接口测试工具", "Api");
        apiCat.Tools.Add(new ToolItem("接口验证", "验证火灾探测系统所有接口的连通性，自动登录并逐一访问接口",
            () => new Views.Api.ApiValidationView()));
        apiCat.Tools.Add(new ToolItem("获取设备ID", "极早期火灾探测系统 API，支持登录、获取设备名称/通信地址/设备ID",
            () => new Views.Api.DeviceApiTestView()));
        Categories.Add(apiCat);

        // ===== 漏洞检测与系统优化 =====
        var secCat = new ToolCategory("漏洞检测与系统优化", "ShieldAlert");
        secCat.Tools.Add(new ToolItem("Druid漏洞检测",
            "Alibaba Druid 未授权访问漏洞检测，支持单目标/批量扫描、弱口令探测、报告导出",
            () => new Views.Security.DruidScanView()));
        secCat.Tools.Add(new ToolItem("KylinOS漏洞扫描",
            "检测麒麟系统 kylin-offline-upgrade 组件的本地权限提升漏洞，支持扫描、修复和验证",
            () => new Views.Security.KylinOsScanView()));
        secCat.Tools.Add(new ToolItem("KylinOS运维策略",
            "向麒麟系统远程部署定时重启（免密登录）和日志清理策略，支持扫描/部署/卸载/验证",
            () => new Views.Security.KylinOsDeployView()));
        secCat.Tools.Add(new ToolItem("KylinOS系统优化",
            "通过SSH远程优化麒麟系统，扫描并精简不必要的后台服务、进程和定时任务，提升系统性能与安全性",
            () => new Views.Security.KylinOsOptimizeView()));
        Categories.Add(secCat);

        // ===== 对称加密 =====
        var cryptoCat = new ToolCategory("对称加密", "Lock");
        cryptoCat.Tools.Add(new ToolItem("AES 加密/解密", "AES 对称加密，支持多种运算模式、填充模式、密钥长度",
            () => new Views.Crypto.AesView()));
        Categories.Add(cryptoCat);

        // ===== 串口调试工具 =====
        var serialCat = new ToolCategory("串口调试工具", "Serial");
        serialCat.Tools.Add(new ToolItem("基本串口调试", "串口通信调试，支持COM口配置、波特率设置、文本/十六进制收发、自动发送",
            () => new Views.Serial.SerialDebugView()));
        Categories.Add(serialCat);

        // ===== 日期工具 =====
        var dateCat = new ToolCategory("日期工具", "Calendar");
        dateCat.Tools.Add(new ToolItem("Cron 表达式", "Cron 定时表达式生成与解析",
            () => new Views.Date.CronExpressionView()));
        dateCat.Tools.Add(new ToolItem("获取设备MAC地址", "输入 IP 地址通过 SendARP 获取设备 MAC 地址，支持数据保存与导出",
            () => new Views.Date.DeviceMacView()));
        Categories.Add(dateCat);
    }
}
