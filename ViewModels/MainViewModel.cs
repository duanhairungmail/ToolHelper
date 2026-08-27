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
            if (view is Views.Serial.SerialDebugView serial)
                serial.SafeDisconnect();
            else if (view is Views.Serial.EarlyWarningModbusView modbus)
                modbus.SafeDisconnect();
            else if (view is Views.Security.KylinOsDeployView kylinDeploy)
                kylinDeploy.SafeDisconnect();
            else if (view is Views.Other.NodeRedLauncherView nodeRed)
                nodeRed.SafeDisconnect();
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
        // ===== 远程连接工具 =====
        var remoteCat = new ToolCategory("远程连接工具", "RemoteDesktop");
        remoteCat.Tools.Add(new ToolItem("远程外挂连接",
            "通过 electerm 外挂连接 SSH/SFTP/RDP/VNC（填参数自动连接，首次使用需联网下载）",
            () => new Views.Remote.ElectermLauncherView()));
        Categories.Add(remoteCat);

        // ===== 数据库连接工具 =====
        var dbCat = new ToolCategory("数据库连接工具", "Database");
        dbCat.Tools.Add(new ToolItem("数据库外挂连接",
            "通过 DBX 外挂连接 MySQL/postgresql（填参数自动连接，首次使用需联网下载）",
            () => new Views.Database.DbxLauncherView()));
        Categories.Add(dbCat);

        // ===== 接口测试工具 =====
        var apiCat = new ToolCategory("接口测试工具", "Api");
        apiCat.Tools.Add(new ToolItem("极早期接口验证",
            "登录并获取设备ID、MQTT主题，批量验证接口连通性，支持自动检测",
            () => new Views.Api.EarlyWarningApiView()));
        apiCat.Tools.Add(new ToolItem("获取设备MAC地址",
            "输入 IP 地址通过 SendARP 获取设备 MAC 地址，支持数据保存与导出",
            () => new Views.Date.DeviceMacView()));
        Categories.Add(apiCat);

        // ===== 漏洞检测与系统优化 =====
        var secCat = new ToolCategory("漏洞检测与系统优化", "ShieldAlert");
        secCat.Tools.Add(new ToolItem("Druid漏洞检测",
            "Alibaba Druid 未授权访问漏洞检测，支持单目标/批量扫描、弱口令探测、报告导出",
            () => new Views.Security.DruidScanView()));
        secCat.Tools.Add(new ToolItem("KylinOS运维策略",
            "向麒麟系统远程部署运维策略（定时重启/日志优化/VNC/openGauss）、扫描并修复系统补丁漏洞、优化系统服务与进程",
            () => new Views.Security.KylinOsDeployView()));
        Categories.Add(secCat);

        // ===== 串口调试工具 =====
        var serialCat = new ToolCategory("串口调试工具", "Serial");
        serialCat.Tools.Add(new ToolItem("基本串口调试", "串口通信调试，支持COM口配置、波特率设置、文本/十六进制收发、自动发送",
            () => new Views.Serial.SerialDebugView()));
        serialCat.Tools.Add(new ToolItem("极早期Modbus调试", "申弘/南瑞怡和双协议 Modbus RTU，扫描下辖设备、一键读取全量数据并解析",
            () => new Views.Serial.EarlyWarningModbusView()));
        Categories.Add(serialCat);

        // ===== 其他工具（原日期工具，吸收 SQL 生成/格式化 与 AES 加密）=====
        var otherCat = new ToolCategory("其他工具", "Calendar");
        otherCat.Tools.Add(new ToolItem("Cron 表达式", "Cron 定时表达式生成与解析",
            () => new Views.Date.CronExpressionView()));
        otherCat.Tools.Add(new ToolItem("SQL语句生成与格式化",
            "可视化表单生成 SQL（SELECT/INSERT/UPDATE/DELETE/CREATE）+ SQL 语法格式化",
            () => new Views.Format.SqlToolView()));
        otherCat.Tools.Add(new ToolItem("AES 加密/解密", "AES 对称加密，支持多种运算模式、填充模式、密钥长度",
            () => new Views.Crypto.AesView()));
        otherCat.Tools.Add(new ToolItem("群 Ping", "批量 ping 多个主机/IP，显示状态、延迟、丢包率，支持网段扫描与导出",
            () => new Views.Other.GroupPingView()));
        otherCat.Tools.Add(new ToolItem("Node-RED 可视化编排", "拖拽编排串口、Modbus、HTTP 等流程",
            () => new Views.Other.NodeRedLauncherView()));
        Categories.Add(otherCat);
    }
}
