using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using WpfBorder = System.Windows.Controls.Border;
using DataGridTextColumn = System.Windows.Controls.DataGridTextColumn;

namespace ToolHelper.Views.Security;

// ================== 数据模型 ==================

public class DeployItem
{
    public string ItemName { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public string StatusIcon { get; set; } = "⬜";
    public string Detail { get; set; } = "";
    public bool IsDeployed { get; set; }
}

public enum FeatureState
{
    Unknown, NotDeployed, Partial, Deployed, Failed
}

// ================== 主类 ==================

public class KylinOsDeployView : SshToolBaseView
{
    // ===== 字段 =====
    private TextBox _desktopUserBox = new(), _desktopUserBox2 = new();
    private TabControl _tabControl = new();
    private DataGrid _tab1Dg = new(), _tab2Dg = new();
    private Button _tab1ScanBtn = new(), _tab1DeployBtn = new(), _tab1UninstallBtn = new(), _tab1VerifyBtn = new();
    private Button _tab2ScanBtn = new(), _tab2DeployBtn = new(), _tab2UninstallBtn = new(), _tab2VerifyBtn = new();
    private ObservableCollection<DeployItem> _tab1Items = new(), _tab2Items = new();
    private FeatureState _feature1State = FeatureState.Unknown, _feature2State = FeatureState.Unknown;
    private TextBox _tab1Log = new(), _tab2Log = new();

    // ===== Tab3: VNC Server (x11vnc) =====
    private PasswordBox _vncPasswordBox = new();   // VNC 连接密码
    private TextBox _vncPortBox = new();           // 监听端口（默认 5901）
    private DataGrid _tab3Dg = new();
    private Button _tab3ScanBtn = new(), _tab3DeployBtn = new(), _tab3StartBtn = new(), _tab3StopBtn = new(), _tab3UninstallBtn = new();
    private TextBox _tab3Log = new();
    private ObservableCollection<DeployItem> _tab3Items = new();
    private FeatureState _feature3State = FeatureState.Unknown;
    private bool _vncServiceRunning;

    // ===== Tab4: PostgreSQL 连接配置 =====
    private DataGrid _tab4Dg = new();
    private Button _tab4ScanBtn = new(), _tab4DeployBtn = new(), _tab4UninstallBtn = new();
    private TextBox _tab4Log = new();
    private ObservableCollection<DeployItem> _tab4Items = new();
    private FeatureState _feature4State = FeatureState.Unknown;

    // Tab4: openGauss 配置文件部署目标（麒麟系统 openGauss 数据目录）
    private const string OpenGaussDataDir = "/data/usershare/firestation/db/opengauss/data/single_node";
    private const string OpenGaussBackupDir = OpenGaussDataDir + "/backup_conf";

    // ===== Tab6: KylinOS 漏洞扫描（kylin-offline-upgrade 本地提权，依赖 SSH）=====
    private Button _vulScanBtn = new(), _vulRepairBtn = new(), _vulVerifyBtn = new();
    private VulnerabilityResult? _vulLastResult;
    private TextBox _vulLogBox = new();

    // ===== Tab7: KylinOS 系统优化（服务/进程/定时任务，依赖 SSH）=====
    private Button _optScanBtn = new(), _optOptimizeBtn = new(), _optVerifyBtn = new(), _optRestoreBtn = new();
    private ObservableCollection<OptimizationItem> _optItems = new();
    private DataGrid _optDataGrid = new();
    private TextBox _optLogBox = new();
    private TextBlock _optInfoText = new(), _optSystemInfoText = new();

    /// <summary>本地开放配置目录：从 BaseDirectory 向上逐级查找 plugins/opengauss_conf（与 KylinOsScanView.PatchDir 同策略）</summary>
    private static string OpenGaussConfDir
    {
        get
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(dir, "plugins", "opengauss_conf");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetFullPath(Path.Combine(dir, ".."));
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "opengauss_conf");
        }
    }

    // ===== 抽象属性实现 =====
    protected override PackIconKind TitleIcon => PackIconKind.Cog;
    protected override string TitleText => "KylinOS 运维策略";
    protected override string DescriptionText => "向麒麟系统远程部署定时重启（自动登录）和日志清理策略，支持扫描 / 部署 / 卸载 / 验证";
    protected override bool ShowSharedResultBox => false; // 使用各 Tab 独立日志区

    // ================== UI 构建 ==================

    protected override void BuildToolContent(DockPanel root, StackPanel topPanel)
    {
        // 初始化数据
        InitTab1Items();
        InitTab2Items();
        InitTab3Items();
        InitTab4Items();
        InitTab7Items();

        // 顶部按钮行：定时重启 + 日志优化 + VNC Server + PostgreSQL连接 + 漏洞扫描 + 系统优化 + 复制结果（同一行，统一样式）
        var topBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
        var tab1Btn = MakeButton("定时重启", () => _tabControl.SelectedIndex = 0, false, PackIconKind.ClockOutline);
        var tab2Btn = MakeButton("日志优化", () => _tabControl.SelectedIndex = 1, false, PackIconKind.DeleteSweep);
        var tab3Btn = MakeButton("VNC Server", () => _tabControl.SelectedIndex = 2, false, PackIconKind.Monitor);
        var tab4Btn = MakeButton("PostgreSQL连接", () => _tabControl.SelectedIndex = 3, false, PackIconKind.Database);
        var tab6Btn = MakeButton("漏洞扫描", () => _tabControl.SelectedIndex = 4, false, PackIconKind.ShieldAlert);
        var tab7Btn = MakeButton("系统优化", () => _tabControl.SelectedIndex = 5, false, PackIconKind.Flash);
        topBtnRow.Children.Add(tab1Btn);
        topBtnRow.Children.Add(tab2Btn);
        topBtnRow.Children.Add(tab3Btn);
        topBtnRow.Children.Add(tab4Btn);
        topBtnRow.Children.Add(tab6Btn);
        topBtnRow.Children.Add(tab7Btn);
        topBtnRow.Children.Add(MakeButton("复制结果", CopyResult, false, PackIconKind.ContentCopy));
        StatusText.VerticalAlignment = VerticalAlignment.Center;
        StatusText.Margin = new Thickness(16, 0, 0, 0);
        StatusText.FontSize = 13;
        topBtnRow.Children.Add(StatusText);
        topPanel.Children.Add(topBtnRow);

        // TabControl（隐藏默认 Tab 标题，由上方按钮切换）
        _tabControl.Margin = new Thickness(0, 4, 0, 4);

        var tab1 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab1.Content = BuildTab1Content();
        _tabControl.Items.Add(tab1);

        var tab2 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab2.Content = BuildTab2Content();
        _tabControl.Items.Add(tab2);

        var tab3 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab3.Content = BuildTab3Content();
        _tabControl.Items.Add(tab3);

        var tab4 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab4.Content = BuildTab4Content();
        _tabControl.Items.Add(tab4);

        var tab6 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab6.Content = BuildTab6Content();
        _tabControl.Items.Add(tab6);

        var tab7 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab7.Content = BuildTab7Content();
        _tabControl.Items.Add(tab7);

        // TabControl 直接挂到根容器（顶部面板之后 → 成为填充子元素，高度随窗口缩放）
        root.Children.Add(_tabControl);

        AppendTab1("点击 [连接SSH] 连接到麒麟系统，然后在对应 Tab 中执行扫描/部署/卸载/验证操作。");
    }

    private DockPanel BuildTab1Content()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();

        // 信息卡片
        _desktopUserBox = MakeBox("用户名", "", 100);
        top.Children.Add(MakeInfoCard(new[]
        {
            "📅 执行时间：每月 1 日 00:00（cron 以 root 身份触发）",
            "🔐 仅该次重启免密登录桌面，其余所有重启必须输入密码",
            "📁 部署 5 个文件（脚本/cron/sudoers/XDG自启动）"
        },
        "👤 桌面登录用户:", _desktopUserBox));

        // DataGrid
        _tab1Dg = BuildItemGrid(_tab1Items);
        top.Children.Add(_tab1Dg);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _tab1ScanBtn = MakeButton("扫描", ScanFeature1, false, PackIconKind.SearchWeb);
        _tab1ScanBtn.IsEnabled = false;
        btnRow.Children.Add(_tab1ScanBtn);
        _tab1DeployBtn = MakeButton("部署", DeployFeature1, false, PackIconKind.PackageDown);
        _tab1DeployBtn.IsEnabled = false;
        btnRow.Children.Add(_tab1DeployBtn);
        _tab1UninstallBtn = MakeButton("卸载", UninstallFeature1, false, PackIconKind.Delete);
        _tab1UninstallBtn.IsEnabled = false;
        btnRow.Children.Add(_tab1UninstallBtn);
        _tab1VerifyBtn = MakeButton("验证", VerifyFeature1, false, PackIconKind.CheckCircle);
        _tab1VerifyBtn.IsEnabled = false;
        btnRow.Children.Add(_tab1VerifyBtn);
        btnRow.Children.Add(MakeButton("日志清理", () => _tab1Log.Clear(), false, PackIconKind.NotificationClearAll));
        top.Children.Add(btnRow);

        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        // 操作日志区（填充剩余高度，随窗口缩放）
        panel.Children.Add(BuildTabLogPanel(ref _tab1Log));

        return panel;
    }

    private DockPanel BuildTab2Content()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();

        // 信息卡片
        _desktopUserBox2 = MakeBox("用户名", "", 100);
        top.Children.Add(MakeInfoCard(new[]
        {
            "📅 执行时间：每月 1 日 01:00（比重启晚 1 小时）",
            "🗑️ 删除 >365 天的 /var/log 日志和 /tmp 临时文件",
            "📋 journal 保留 30 天，超大日志(>500MB) truncate 清空"
        },
        "👤 桌面登录用户:", _desktopUserBox2));

        // DataGrid
        _tab2Dg = BuildItemGrid(_tab2Items);
        top.Children.Add(_tab2Dg);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _tab2ScanBtn = MakeButton("扫描", ScanFeature2, false, PackIconKind.SearchWeb);
        _tab2ScanBtn.IsEnabled = false;
        btnRow.Children.Add(_tab2ScanBtn);
        _tab2DeployBtn = MakeButton("部署", DeployFeature2, false, PackIconKind.PackageDown);
        _tab2DeployBtn.IsEnabled = false;
        btnRow.Children.Add(_tab2DeployBtn);
        _tab2UninstallBtn = MakeButton("卸载", UninstallFeature2, false, PackIconKind.Delete);
        _tab2UninstallBtn.IsEnabled = false;
        btnRow.Children.Add(_tab2UninstallBtn);
        _tab2VerifyBtn = MakeButton("验证", VerifyFeature2, false, PackIconKind.CheckCircle);
        _tab2VerifyBtn.IsEnabled = false;
        btnRow.Children.Add(_tab2VerifyBtn);
        btnRow.Children.Add(MakeButton("日志清理", () => _tab2Log.Clear(), false, PackIconKind.NotificationClearAll));
        top.Children.Add(btnRow);

        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        // 操作日志区（填充剩余高度，随窗口缩放）
        panel.Children.Add(BuildTabLogPanel(ref _tab2Log));

        return panel;
    }

    private DockPanel BuildTab3Content()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();

        // 信息卡片
        top.Children.Add(MakeInfoCard(new[]
        {
            "🖥️ 部署 x11vnc 到麒麟系统，共享真实桌面供远程 VNC 连接",
            "🔐 使用 VNC 密码认证，监听端口可自定义",
            "📁 部署 3 个文件（二进制 / 密码文件 / systemd 服务）"
        }));

        // 配置行：VNC 密码 + 随机生成 + 端口
        var configRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        configRow.Children.Add(new TextBlock { Text = "VNC 密码:", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _vncPasswordBox = MakePasswordBox("至少6位", 120);
        configRow.Children.Add(_vncPasswordBox);
        configRow.Children.Add(MakeButton("随机生成", GenerateRandomPassword, false, PackIconKind.Refresh));
        configRow.Children.Add(new TextBlock { Text = "端口:", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) });
        _vncPortBox = MakeBox("端口", "5901", 70);
        configRow.Children.Add(_vncPortBox);
        top.Children.Add(configRow);

        // DataGrid
        _tab3Dg = BuildItemGrid(_tab3Items);
        top.Children.Add(_tab3Dg);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _tab3ScanBtn = MakeButton("扫描", ScanFeature3, false, PackIconKind.SearchWeb);
        _tab3ScanBtn.IsEnabled = false;
        btnRow.Children.Add(_tab3ScanBtn);
        _tab3DeployBtn = MakeButton("部署", DeployFeature3, false, PackIconKind.PackageDown);
        _tab3DeployBtn.IsEnabled = false;
        btnRow.Children.Add(_tab3DeployBtn);
        _tab3StartBtn = MakeButton("启动", StartVncServer, false, PackIconKind.PlayCircle);
        _tab3StartBtn.IsEnabled = false;
        btnRow.Children.Add(_tab3StartBtn);
        _tab3StopBtn = MakeButton("停止", StopVncServer, false, PackIconKind.StopCircle);
        _tab3StopBtn.IsEnabled = false;
        btnRow.Children.Add(_tab3StopBtn);
        _tab3UninstallBtn = MakeButton("卸载", UninstallFeature3, false, PackIconKind.Delete);
        _tab3UninstallBtn.IsEnabled = false;
        btnRow.Children.Add(_tab3UninstallBtn);
        btnRow.Children.Add(MakeButton("日志清理", () => _tab3Log.Clear(), false, PackIconKind.NotificationClearAll));
        top.Children.Add(btnRow);

        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        // 操作日志区（填充剩余高度，随窗口缩放）
        panel.Children.Add(BuildTabLogPanel(ref _tab3Log));

        return panel;
    }

    private void InitTab4Items()
    {
        _tab4Items.Clear();
        _tab4Items.Add(new DeployItem { ItemName = "pg_hba.conf", RemotePath = $"{OpenGaussDataDir}/pg_hba.conf" });
        _tab4Items.Add(new DeployItem { ItemName = "postgresql.conf", RemotePath = $"{OpenGaussDataDir}/postgresql.conf" });
        _tab4Items.Add(new DeployItem { ItemName = "backup_conf/", RemotePath = $"{OpenGaussBackupDir}/" });
        // 补齐到 5 行，与其他 Tab 表格行数一致（样式统一）
        _tab4Items.Add(new DeployItem { StatusIcon = "" });
        _tab4Items.Add(new DeployItem { StatusIcon = "" });
    }

    private DockPanel BuildTab4Content()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();

        // 信息卡片
        top.Children.Add(MakeInfoCard(new[]
        {
            "🗄️ 将开放配置（pg_hba.conf + postgresql.conf）部署到 openGauss 数据目录",
            "📁 部署前自动备份原文件到 backup_conf/ 目录",
            "🔄 部署后需重启 openGauss 或执行 gs_ctl reload 使配置生效"
        }));

        // DataGrid
        _tab4Dg = BuildItemGrid(_tab4Items);
        top.Children.Add(_tab4Dg);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _tab4ScanBtn = MakeButton("扫描", ScanFeature4, false, PackIconKind.SearchWeb);
        _tab4ScanBtn.IsEnabled = false;
        btnRow.Children.Add(_tab4ScanBtn);
        _tab4DeployBtn = MakeButton("部署", DeployFeature4, false, PackIconKind.PackageDown);
        _tab4DeployBtn.IsEnabled = false;
        btnRow.Children.Add(_tab4DeployBtn);
        _tab4UninstallBtn = MakeButton("卸载", UninstallFeature4, false, PackIconKind.Delete);
        _tab4UninstallBtn.IsEnabled = false;
        btnRow.Children.Add(_tab4UninstallBtn);
        btnRow.Children.Add(MakeButton("重启服务", RestartOpenGauss, false, PackIconKind.Restart));
        btnRow.Children.Add(MakeButton("日志清理", () => _tab4Log.Clear(), false, PackIconKind.NotificationClearAll));
        top.Children.Add(btnRow);

        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        // 操作日志区（填充剩余高度，随窗口缩放）
        panel.Children.Add(BuildTabLogPanel(ref _tab4Log));

        return panel;
    }

    #region Tab6: KylinOS 漏洞扫描（依赖 SSH）

    private DockPanel BuildTab6Content()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();

        // 信息卡片
        top.Children.Add(MakeInfoCard(new[]
        {
            "🛡️ 漏洞名称: 麒麟离线升级工具本地权限提升漏洞",
            "📦 影响组件: kylin-offline-upgrade",
            "⚠️ 风险等级: 高危（本地提权 LPE）",
            $"📁 修复方式: 安装官方安全补丁 (.deb) — plugins{Path.DirectorySeparatorChar}Security patch{Path.DirectorySeparatorChar}"
        }));

        // 操作按钮行
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _vulScanBtn = MakeButton("扫描", DoVulScan, true, PackIconKind.SearchWeb);
        _vulScanBtn.IsEnabled = false;
        actionRow.Children.Add(_vulScanBtn);
        _vulRepairBtn = MakeButton("修复", DoVulRepair, false, PackIconKind.Wrench);
        _vulRepairBtn.IsEnabled = false;
        actionRow.Children.Add(_vulRepairBtn);
        _vulVerifyBtn = MakeButton("验证", DoVulVerify, false, PackIconKind.CheckCircle);
        _vulVerifyBtn.IsEnabled = false;
        actionRow.Children.Add(_vulVerifyBtn);
        actionRow.Children.Add(MakeButton("日志清理", () => _vulLogBox.Clear(), false, PackIconKind.NotificationClearAll));
        top.Children.Add(actionRow);

        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        // 操作日志区（填充剩余高度，随窗口缩放）
        panel.Children.Add(BuildTabLogPanel(ref _vulLogBox));

        AppendTab6("点击 [连接SSH] 连接到麒麟系统，然后点击 [扫描] 开始检测漏洞。");
        return panel;
    }

    #endregion

    #region Tab7: KylinOS 系统优化（依赖 SSH）

    private void InitTab7Items()
    {
        _optItems = new ObservableCollection<OptimizationItem>(GetOptimizationItems());
    }

    private DockPanel BuildTab7Content()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();

        // 信息区（动态文本）
        var infoBox = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        _optInfoText.Text = $"优化项数量: {_optItems.Count} 项  |  点击 [扫描] 开始检测目标系统";
        _optInfoText.FontSize = 12;
        _optSystemInfoText.Text = "目标系统: 未连接";
        _optSystemInfoText.FontSize = 12;
        _optSystemInfoText.FontWeight = FontWeights.SemiBold;
        _optSystemInfoText.Margin = new Thickness(0, 0, 0, 2);
        infoBox.Children.Add(_optSystemInfoText);
        infoBox.Children.Add(_optInfoText);
        infoBox.Children.Add(new TextBlock { Text = "风险提示: mask 为不可逆级停用（可用 [恢复选中] 还原），中风险项请根据业务需求谨慎选择", FontSize = 11, Foreground = Brushes.Orange, Margin = new Thickness(0, 2, 0, 0) });
        top.Children.Add(infoBox);

        // DataGrid
        _optDataGrid.ItemsSource = _optItems;
        _optDataGrid.AutoGenerateColumns = false;
        _optDataGrid.CanUserAddRows = false;
        _optDataGrid.CanUserReorderColumns = false;
        _optDataGrid.IsReadOnly = false;
        _optDataGrid.SelectionMode = DataGridSelectionMode.Single;
        _optDataGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _optDataGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _optDataGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250));
        _optDataGrid.MaxHeight = 300;
        _optDataGrid.MinHeight = 160;

        var colSelect = new DataGridCheckBoxColumn
        {
            Header = "选择",
            Binding = new System.Windows.Data.Binding("IsSelected") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
            IsReadOnly = false
        };
        _optDataGrid.Columns.Add(colSelect);
        _optDataGrid.Columns.Add(new DataGridTextColumn { Header = "序号", Binding = new System.Windows.Data.Binding("Id"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });
        _optDataGrid.Columns.Add(new DataGridTextColumn { Header = "优化项目名称", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });
        _optDataGrid.Columns.Add(new DataGridTextColumn { Header = "风险", Binding = new System.Windows.Data.Binding("RiskLevel"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });

        // 状态列（彩色字体）
        var statusTemplate = new DataTemplate();
        var tbFactory = new FrameworkElementFactory(typeof(TextBlock));
        tbFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Status"));
        tbFactory.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Status") { Converter = new OptStatusColorConverter() });
        tbFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        statusTemplate.VisualTree = tbFactory;
        _optDataGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "状态",
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
            IsReadOnly = true,
            CellTemplate = statusTemplate
        });
        _optDataGrid.Columns.Add(new DataGridTextColumn { Header = "类别", Binding = new System.Windows.Data.Binding("Category"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });

        // 详情列（显示具体未优化原因，长文本省略+悬浮提示）
        var detailStyle = new Style(typeof(TextBlock));
        detailStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        detailStyle.Setters.Add(new Setter(System.Windows.Controls.ToolTipService.ToolTipProperty, new System.Windows.Data.Binding("ScanDetail")));
        _optDataGrid.Columns.Add(new DataGridTextColumn { Header = "详情", Binding = new System.Windows.Data.Binding("ScanDetail"), Width = new DataGridLength(240), IsReadOnly = true, ElementStyle = detailStyle });
        top.Children.Add(_optDataGrid);

        // 选择按钮行
        var selectRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        selectRow.Children.Add(MakeButton("全选", OptSelectAll, false, PackIconKind.SelectAll));
        selectRow.Children.Add(MakeButton("全不选", OptSelectNone, false, PackIconKind.SelectOff));
        selectRow.Children.Add(MakeButton("反选", OptInvertSelection, false, PackIconKind.SwapHorizontal));
        top.Children.Add(selectRow);

        // 操作按钮行
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _optScanBtn = MakeButton("扫描", DoOptScan, true, PackIconKind.SearchWeb);
        _optScanBtn.IsEnabled = false;
        actionRow.Children.Add(_optScanBtn);
        _optOptimizeBtn = MakeButton("优化选中", DoOptOptimize, false, PackIconKind.Flash);
        _optOptimizeBtn.IsEnabled = false;
        actionRow.Children.Add(_optOptimizeBtn);
        _optVerifyBtn = MakeButton("验证", DoOptVerify, false, PackIconKind.CheckCircle);
        _optVerifyBtn.IsEnabled = false;
        actionRow.Children.Add(_optVerifyBtn);
        _optRestoreBtn = MakeButton("恢复选中", DoOptRestore, false, PackIconKind.Undo);
        _optRestoreBtn.IsEnabled = false;
        actionRow.Children.Add(_optRestoreBtn);
        actionRow.Children.Add(MakeButton("日志清理", () => _optLogBox.Clear(), false, PackIconKind.NotificationClearAll));
        top.Children.Add(actionRow);

        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        // 操作日志区（填充剩余高度，随窗口缩放）
        panel.Children.Add(BuildTabLogPanel(ref _optLogBox));

        AppendTab7("点击 [连接SSH] 连接到麒麟系统，然后点击 [扫描] 开始检测待优化项。");
        return panel;
    }

    #endregion

    /// <summary>构建 Tab 内操作日志面板：「操作日志」标签 + 填充剩余高度的深色日志区</summary>
    private static DockPanel BuildTabLogPanel(ref TextBox logBox)
    {
        var panel = new DockPanel();

        var label = new TextBlock
        {
            Text = "操作日志",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Margin = new Thickness(0, 8, 0, 4)
        };
        DockPanel.SetDock(label, Dock.Top);
        panel.Children.Add(label);

        logBox = MakeLogBox();
        var scroll = new ScrollViewer
        {
            Content = logBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 100
        };
        panel.Children.Add(new WpfBorder
        {
            Child = scroll,
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 65, 75)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            MinHeight = 100
        });

        return panel;
    }

    // ================== 辅助 UI ==================

    private static TextBox MakeLogBox()
    {
        return new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
            Foreground = new SolidColorBrush(Color.FromRgb(171, 178, 191)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            MinHeight = 60
        };
    }

    private void AppendTab1(string text) { _tab1Log.AppendText(text + "\n"); _tab1Log.ScrollToEnd(); }
    private void AppendTab2(string text) { _tab2Log.AppendText(text + "\n"); _tab2Log.ScrollToEnd(); }
    private void AppendTab3(string text) { _tab3Log.AppendText(text + "\n"); _tab3Log.ScrollToEnd(); }
    private void AppendTab4(string text) { _tab4Log.AppendText(text + "\n"); _tab4Log.ScrollToEnd(); }
    private void AppendTab6(string text) { _vulLogBox.AppendText(text + "\n"); _vulLogBox.ScrollToEnd(); }
    private void AppendTab7(string text) { _optLogBox.AppendText(text + "\n"); _optLogBox.ScrollToEnd(); }

    /// <summary>线程安全日志（在 Task.Run 内部调用时投递到 UI 线程）</summary>
    private void AppendTab3ThreadSafe(string text)
    {
        Dispatcher.BeginInvoke(() => AppendTab3(text));
    }

    /// <summary>生成 8 位随机 VNC 密码（不含易混淆字符），写入密码框并展示在日志区便于复制</summary>
    private void GenerateRandomPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var sb = new StringBuilder(8);
        var rng = new Random();
        for (int i = 0; i < 8; i++)
            sb.Append(chars[rng.Next(chars.Length)]);
        var pwd = sb.ToString();
        _vncPasswordBox.Password = pwd;
        AppendTab3($"  🔑 已生成随机 VNC 密码: {pwd}（已填入密码框，请复制保存）");
    }

    private WpfBorder MakeInfoCard(string[] lines, string? inputLabel = null, TextBox? inputBox = null)
    {
        var sp = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        foreach (var line in lines)
            sp.Children.Add(new TextBlock { Text = line, FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap });

        if (inputLabel != null && inputBox != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(new TextBlock { Text = inputLabel, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(inputBox);
            sp.Children.Add(row);
        }

        return new WpfBorder
        {
            Background = new SolidColorBrush(Color.FromRgb(237, 242, 247)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(189, 206, 223)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 8),
            Child = sp
        };
    }

    private DataGrid BuildItemGrid(ObservableCollection<DeployItem> items)
    {
        var dg = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserReorderColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
            MaxHeight = 220,
            MinHeight = 80
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "文件名", Binding = new System.Windows.Data.Binding("ItemName"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });
        // 远程路径列限宽 + 省略号 + 悬浮提示（超长路径不再挤压「备注」列，四个 Tab 表格观感一致）
        var pathStyle = new Style(typeof(TextBlock));
        pathStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        pathStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        pathStyle.Setters.Add(new Setter(System.Windows.Controls.ToolTipService.ToolTipProperty, new System.Windows.Data.Binding("RemotePath")));
        dg.Columns.Add(new DataGridTextColumn { Header = "远程路径", Binding = new System.Windows.Data.Binding("RemotePath"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), MaxWidth = 340, IsReadOnly = true, ElementStyle = pathStyle });
        dg.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding("StatusIcon"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });
        dg.Columns.Add(new DataGridTextColumn { Header = "备注", Binding = new System.Windows.Data.Binding("Detail"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        return dg;
    }

    private void InitTab1Items()
    {
        _tab1Items.Clear();
        _tab1Items.Add(new DeployItem { ItemName = "scheduled-reboot.sh", RemotePath = "/usr/local/bin/scheduled-reboot.sh" });
        _tab1Items.Add(new DeployItem { ItemName = "clear-autologin.sh", RemotePath = "/usr/local/bin/clear-autologin.sh" });
        _tab1Items.Add(new DeployItem { ItemName = "clear-autologin.desktop", RemotePath = "/etc/xdg/autostart/clear-autologin.desktop" });
        _tab1Items.Add(new DeployItem { ItemName = "auto-reboot (cron)", RemotePath = "/etc/cron.d/auto-reboot" });
        _tab1Items.Add(new DeployItem { ItemName = "sudoers.d/auto-reboot", RemotePath = "/etc/sudoers.d/auto-reboot" });
    }

    private void InitTab2Items()
    {
        _tab2Items.Clear();
        _tab2Items.Add(new DeployItem { ItemName = "clean-logs.sh", RemotePath = "/usr/local/bin/clean-logs.sh" });
        _tab2Items.Add(new DeployItem { ItemName = "clean-logs (cron)", RemotePath = "/etc/cron.d/clean-logs" });
        _tab2Items.Add(new DeployItem { StatusIcon = "" });
        _tab2Items.Add(new DeployItem { StatusIcon = "" });
        _tab2Items.Add(new DeployItem { StatusIcon = "" });
    }

    private void InitTab3Items()
    {
        _tab3Items.Clear();
        _tab3Items.Add(new DeployItem { ItemName = "x11vnc (二进制)", RemotePath = "/usr/local/bin/x11vnc" });
        _tab3Items.Add(new DeployItem { ItemName = "x11vnc.passwd", RemotePath = "/etc/x11vnc.passwd" });
        _tab3Items.Add(new DeployItem { ItemName = "x11vnc.service", RemotePath = "/etc/systemd/system/x11vnc.service" });
        // 补齐到 5 行，与其他 Tab 表格行数一致（样式统一）
        _tab3Items.Add(new DeployItem { StatusIcon = "" });
        _tab3Items.Add(new DeployItem { StatusIcon = "" });
    }

    // ================== 连接状态回调 ==================

    protected override void OnConnected()
    {
        _tab1ScanBtn.IsEnabled = true;
        _tab2ScanBtn.IsEnabled = true;
        _tab3ScanBtn.IsEnabled = true;
        _tab4ScanBtn.IsEnabled = true;
        _vulScanBtn.IsEnabled = true;
        _optScanBtn.IsEnabled = true;
        _optRestoreBtn.IsEnabled = true;
    }

    protected override void OnDisconnected()
    {
        DisableAllButtons();
    }

    private void DisableAllButtons()
    {
        _tab1ScanBtn.IsEnabled = _tab1DeployBtn.IsEnabled = _tab1UninstallBtn.IsEnabled = _tab1VerifyBtn.IsEnabled = false;
        _tab2ScanBtn.IsEnabled = _tab2DeployBtn.IsEnabled = _tab2UninstallBtn.IsEnabled = _tab2VerifyBtn.IsEnabled = false;
        _tab3ScanBtn.IsEnabled = _tab3DeployBtn.IsEnabled = _tab3StartBtn.IsEnabled = _tab3StopBtn.IsEnabled = _tab3UninstallBtn.IsEnabled = false;
        _tab4ScanBtn.IsEnabled = _tab4DeployBtn.IsEnabled = _tab4UninstallBtn.IsEnabled = false;
        _vulScanBtn.IsEnabled = _vulRepairBtn.IsEnabled = _vulVerifyBtn.IsEnabled = false;
        _optScanBtn.IsEnabled = _optOptimizeBtn.IsEnabled = _optVerifyBtn.IsEnabled = _optRestoreBtn.IsEnabled = false;
    }

    private void UpdateTab6Buttons()
    {
        var connected = Ssh != null && Ssh.IsConnected;
        _vulScanBtn.IsEnabled = connected;
        if (!connected)
        {
            _vulRepairBtn.IsEnabled = false;
            _vulVerifyBtn.IsEnabled = false;
            return;
        }
        _vulRepairBtn.IsEnabled = _vulLastResult?.IsVulnerable == true;
        _vulVerifyBtn.IsEnabled = _vulLastResult?.Status is VulnerabilityStatus.Vulnerable or VulnerabilityStatus.Fixed;
    }

    private void UpdateTab7Buttons()
    {
        var connected = Ssh != null && Ssh.IsConnected;
        _optScanBtn.IsEnabled = connected;
        _optRestoreBtn.IsEnabled = connected;
        if (!connected)
        {
            _optOptimizeBtn.IsEnabled = false;
            _optVerifyBtn.IsEnabled = false;
            return;
        }
        _optOptimizeBtn.IsEnabled = _optItems.Any(i => i.IsSelected && i.Status == "可优化");
        _optVerifyBtn.IsEnabled = true;
    }

    private void UpdateTab4Buttons()
    {
        var connected = Ssh != null && Ssh.IsConnected;
        _tab4ScanBtn.IsEnabled = connected;
        _tab4DeployBtn.IsEnabled = connected && (_feature4State == FeatureState.NotDeployed || _feature4State == FeatureState.Partial);
        _tab4UninstallBtn.IsEnabled = connected && (_feature4State == FeatureState.Deployed || _feature4State == FeatureState.Partial);
    }

    private void UpdateTab3Buttons()
    {
        var connected = Ssh != null && Ssh.IsConnected;
        _tab3ScanBtn.IsEnabled = connected;
        _tab3DeployBtn.IsEnabled = connected && (_feature3State == FeatureState.NotDeployed || _feature3State == FeatureState.Partial);
        _tab3StartBtn.IsEnabled = connected && _feature3State == FeatureState.Deployed && !_vncServiceRunning;
        _tab3StopBtn.IsEnabled = connected && _feature3State == FeatureState.Deployed && _vncServiceRunning;
        _tab3UninstallBtn.IsEnabled = connected && (_feature3State == FeatureState.Deployed || _feature3State == FeatureState.Partial);
    }

    private void UpdateTab1Buttons()
    {
        var connected = Ssh != null && Ssh.IsConnected;
        _tab1ScanBtn.IsEnabled = connected;
        _tab1DeployBtn.IsEnabled = connected && (_feature1State == FeatureState.NotDeployed || _feature1State == FeatureState.Partial);
        _tab1UninstallBtn.IsEnabled = connected && (_feature1State == FeatureState.Deployed || _feature1State == FeatureState.Partial);
        _tab1VerifyBtn.IsEnabled = connected && _feature1State == FeatureState.Deployed;
    }

    private void UpdateTab2Buttons()
    {
        var connected = Ssh != null && Ssh.IsConnected;
        _tab2ScanBtn.IsEnabled = connected;
        _tab2DeployBtn.IsEnabled = connected && (_feature2State == FeatureState.NotDeployed || _feature2State == FeatureState.Partial);
        _tab2UninstallBtn.IsEnabled = connected && (_feature2State == FeatureState.Deployed || _feature2State == FeatureState.Partial);
        _tab2VerifyBtn.IsEnabled = connected && _feature2State == FeatureState.Deployed;
    }

    // ================== SFTP 上传 ==================

    private void UploadViaSftp(string content, string remotePath, int mode, string username, string password)
    {
        var tmpFile = $"/tmp/deploy_{Guid.NewGuid():N}";
        // 确保 Unix 换行符（LF），避免 Windows CRLF 导致 Linux 工具解析失败
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n"))))
            Sftp!.UploadFile(ms, tmpFile, true);
        // mv 到系统目录需要 root 权限
        RunCommandSudo(Ssh!, $"mv {tmpFile} {remotePath} && chmod {mode} {remotePath}", username, password);
    }

    // ================== 功能一：定时重启 ==================

    private async void ScanFeature1()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (string.IsNullOrWhiteSpace(_desktopUserBox.Text)) { AppendTab1("⚠️ 请先输入桌面登录用户"); SetStatus("请输入桌面登录用户", false); return; }
        _tab1ScanBtn.IsEnabled = false;
        SetStatus("正在扫描功能一...", true);
        AppendTab1("\n━━━━ 功能一：扫描定时重启 ━━━━");

        try
        {
            var ssh = Ssh;
            await Task.Run(() =>
            {
                var scanCmds = new (string cmd, int idx)[]
                {
                    ("test -x /usr/local/bin/scheduled-reboot.sh && echo OK || echo MISSING", 0),
                    ("test -x /usr/local/bin/clear-autologin.sh && echo OK || echo MISSING", 1),
                    ("test -f /etc/xdg/autostart/clear-autologin.desktop && echo OK || echo MISSING", 2),
                    ("test -f /etc/cron.d/auto-reboot && echo OK || echo MISSING", 3),
                    ("test -f /etc/sudoers.d/auto-reboot && echo OK || echo MISSING", 4),
                };

                int deployed = 0;
                foreach (var (cmd, idx) in scanCmds)
                {
                    var output = RunCommand(ssh, cmd).Trim();
                    var ok = output.Contains("OK");
                    Dispatcher.Invoke(() =>
                    {
                        _tab1Items[idx].IsDeployed = ok;
                        _tab1Items[idx].StatusIcon = ok ? "✅" : "❌";
                        _tab1Items[idx].Detail = ok ? "已部署" : "未部署";
                        _tab1Dg.Items.Refresh();
                        AppendTab1($"  {_tab1Items[idx].ItemName} → {(ok ? "✅ 已部署" : "❌ 未部署")}");
                    });
                    if (ok) deployed++;
                }

                Dispatcher.Invoke(() =>
                {
                    if (deployed == 5) _feature1State = FeatureState.Deployed;
                    else if (deployed == 0) _feature1State = FeatureState.NotDeployed;
                    else _feature1State = FeatureState.Partial;
                    UpdateTab1Buttons();
                    SetStatus($"功能一扫描完成: {deployed}/5", deployed > 0);
                });
            });
        }
        catch (Exception ex)
        {
            AppendTab1($"❌ 扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
            _feature1State = FeatureState.Failed;
            UpdateTab1Buttons();
        }
        finally { _tab1ScanBtn.IsEnabled = true; }
    }

    private async void DeployFeature1()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (Sftp == null || !Sftp.IsConnected) { SetStatus("SFTP 未连接", false); return; }

        var username = _desktopUserBox.Text.Trim();
        if (string.IsNullOrEmpty(username)) { SetStatus("请输入桌面登录用户名", false); return; }
        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        _tab1DeployBtn.IsEnabled = false;
        SetStatus("正在部署功能一...", true);
        AppendTab1("\n━━━━ 功能一：部署定时重启 ━━━━");

        try
        {
            var ssh = Ssh;
            var sftp = Sftp;

            // DM 检测
            var dmCheck = await Task.Run(() => RunCommand(ssh, "cat /etc/X11/default-display-manager 2>/dev/null || echo UNKNOWN"));
            if (!dmCheck.Contains("lightdm"))
                AppendTab1("  ⚠️ 警告: 当前显示管理器可能不是 LightDM，autologin 机制可能不生效");

            await Task.Run(() =>
            {
                // Step 1: scheduled-reboot.sh
                var rebootScript = SCHEDULED_REBOOT_SCRIPT_TEMPLATE.Replace("{{DESKTOP_USERNAME}}", username);
                UploadViaSftp(rebootScript, "/usr/local/bin/scheduled-reboot.sh", 755, sshUser, sshPass);
                Dispatcher.Invoke(() => { _tab1Items[0].StatusIcon = "✅"; _tab1Items[0].Detail = "部署中"; _tab1Dg.Items.Refresh(); AppendTab1("  ✅ scheduled-reboot.sh 已部署"); });

                // Step 2: clear-autologin.sh
                UploadViaSftp(CLEAR_AUTOLOGIN_SCRIPT, "/usr/local/bin/clear-autologin.sh", 755, sshUser, sshPass);
                Dispatcher.Invoke(() => { _tab1Items[1].StatusIcon = "✅"; _tab1Items[1].Detail = "部署中"; _tab1Dg.Items.Refresh(); AppendTab1("  ✅ clear-autologin.sh 已部署"); });

                // Step 3: xdg autostart
                UploadViaSftp(AUTOSTART_DESKTOP, "/etc/xdg/autostart/clear-autologin.desktop", 644, sshUser, sshPass);
                Dispatcher.Invoke(() => { _tab1Items[2].StatusIcon = "✅"; _tab1Items[2].Detail = "部署中"; _tab1Dg.Items.Refresh(); AppendTab1("  ✅ clear-autologin.desktop 已部署"); });

                // Step 4: cron
                UploadViaSftp(CRON_AUTO_REBOOT, "/etc/cron.d/auto-reboot", 644, sshUser, sshPass);
                Dispatcher.Invoke(() => { _tab1Items[3].StatusIcon = "✅"; _tab1Items[3].Detail = "部署中"; _tab1Dg.Items.Refresh(); AppendTab1("  ✅ /etc/cron.d/auto-reboot 已部署"); });

                // Step 5: sudoers (with visudo validation)
                var sudoersContent = SUDOERS_TEMPLATE.Replace("{{DESKTOP_USERNAME}}", username).Replace("\r\n", "\n");
                var tmpSudoers = $"/tmp/sudoers-test_{Guid.NewGuid():N}";
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(sudoersContent)))
                    sftp.UploadFile(ms, tmpSudoers, true);
                var validation = RunCommand(ssh, $"visudo -c -f {tmpSudoers} 2>&1");
                if (validation.Contains("parsed OK") || validation.Contains("syntax OK") || validation.Contains("解析正确"))
                {
                    RunCommandSudo(ssh, $"mv {tmpSudoers} /etc/sudoers.d/auto-reboot && chmod 440 /etc/sudoers.d/auto-reboot", sshUser, sshPass);
                    Dispatcher.Invoke(() => { _tab1Items[4].StatusIcon = "✅"; _tab1Items[4].Detail = "语法校验通过"; _tab1Dg.Items.Refresh(); AppendTab1("  ✅ /etc/sudoers.d/auto-reboot 已部署（语法校验通过）"); });
                }
                else
                {
                    RunCommand(ssh, $"rm -f {tmpSudoers}");
                    throw new Exception($"sudoers 语法校验失败！\n\n校验输出:\n{validation}\n\n部署已中止，前 4 步已成功的文件需手动清理。");
                }
            });

            AppendTab1("━━━━ 功能一部署完成 ━━━━\n");
            SetStatus("功能一部署完成", true);
            ScanFeature1(); // 自动扫描刷新状态
        }
        catch (Exception ex)
        {
            AppendTab1($"❌ 部署失败: {ex.Message}");
            SetStatus($"部署失败: {ex.Message}", false);
            _tab1DeployBtn.IsEnabled = true;
        }
    }

    private async void UninstallFeature1()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (string.IsNullOrWhiteSpace(_desktopUserBox.Text)) { AppendTab1("⚠️ 请先输入桌面登录用户"); SetStatus("请输入桌面登录用户", false); return; }
        if (MessageBox.Show("确认卸载功能一的 5 个文件？", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        _tab1UninstallBtn.IsEnabled = false;
        _tab1ScanBtn.IsEnabled = false;
        _tab1DeployBtn.IsEnabled = false;
        SetStatus("正在卸载功能一...", true);
        AppendTab1("\n━━━━ 功能一：卸载定时重启 ━━━━");

        try
        {
            var ssh = Ssh;
            int remaining = 0;
            await Task.Run(() =>
            {
                // 执行删除
                var rmOutput = RunCommandSudo(ssh,
                    "rm -f /usr/local/bin/scheduled-reboot.sh /usr/local/bin/clear-autologin.sh /etc/xdg/autostart/clear-autologin.desktop /etc/cron.d/auto-reboot /etc/sudoers.d/auto-reboot",
                    sshUser, sshPass);
                Dispatcher.Invoke(() => AppendTab1($"  rm 输出: {(string.IsNullOrWhiteSpace(rmOutput) ? "(无输出)" : rmOutput.Trim())}"));

                // 逐文件验证是否真的被删除了
                var verifyCmds = new[]
                {
                    "test -f /usr/local/bin/scheduled-reboot.sh && echo STILL_EXISTS || echo REMOVED",
                    "test -f /usr/local/bin/clear-autologin.sh && echo STILL_EXISTS || echo REMOVED",
                    "test -f /etc/xdg/autostart/clear-autologin.desktop && echo STILL_EXISTS || echo REMOVED",
                    "test -f /etc/cron.d/auto-reboot && echo STILL_EXISTS || echo REMOVED",
                    "test -f /etc/sudoers.d/auto-reboot && echo STILL_EXISTS || echo REMOVED",
                };
                foreach (var cmd in verifyCmds)
                {
                    var result = RunCommand(ssh, cmd).Trim();
                    if (result.Contains("STILL_EXISTS")) remaining++;
                    Dispatcher.Invoke(() => AppendTab1($"  验证: {cmd.Split(' ')[2]} → {result}"));
                }
            });

            if (remaining == 0)
            {
                AppendTab1("━━━━ 功能一卸载完成 ━━━━\n");
                SetStatus("功能一卸载完成", true);
                _feature1State = FeatureState.NotDeployed;
                UpdateTab1Buttons();
                ScanFeature1();
            }
            else
            {
                AppendTab1($"⚠️ 仍有 {remaining} 个文件未删除（sudo 权限可能不足）\n");
                SetStatus($"卸载不完整: {remaining} 个文件残留", false);
                _tab1UninstallBtn.IsEnabled = true;
                _tab1ScanBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            AppendTab1($"❌ 卸载失败: {ex.Message}");
            SetStatus($"卸载失败: {ex.Message}", false);
            _tab1UninstallBtn.IsEnabled = true;
            _tab1ScanBtn.IsEnabled = true;
        }
    }

    private void VerifyFeature1()
    {
        if (string.IsNullOrWhiteSpace(_desktopUserBox.Text)) { AppendTab1("⚠️ 请先输入桌面登录用户"); SetStatus("请输入桌面登录用户", false); return; }
        ScanFeature1();
    }

    // ================== 功能二：日志清理 ==================

    private async void ScanFeature2()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (string.IsNullOrWhiteSpace(_desktopUserBox2.Text)) { AppendTab2("⚠️ 请先输入桌面登录用户"); SetStatus("请输入桌面登录用户", false); return; }
        _tab2ScanBtn.IsEnabled = false;
        SetStatus("正在扫描功能二...", true);
        AppendTab2("\n━━━━ 功能二：扫描日志清理 ━━━━");

        try
        {
            var ssh = Ssh;
            await Task.Run(() =>
            {
                int deployed = 0;

                // 检查脚本
                var scriptOk = RunCommand(ssh, "test -x /usr/local/bin/clean-logs.sh && echo OK || echo MISSING").Trim().Contains("OK");
                Dispatcher.Invoke(() =>
                {
                    _tab2Items[0].IsDeployed = scriptOk;
                    _tab2Items[0].StatusIcon = scriptOk ? "✅" : "❌";
                    _tab2Items[0].Detail = scriptOk ? "已部署" : "未部署";
                    _tab2Dg.Items.Refresh();
                    AppendTab2($"  clean-logs.sh → {(scriptOk ? "✅ 已部署" : "❌ 未部署")}");
                });
                if (scriptOk) deployed++;

                // 检查 cron
                var cronOk = RunCommand(ssh, "test -f /etc/cron.d/clean-logs && echo OK || echo MISSING").Trim().Contains("OK");
                Dispatcher.Invoke(() =>
                {
                    _tab2Items[1].IsDeployed = cronOk;
                    _tab2Items[1].StatusIcon = cronOk ? "✅" : "❌";
                    _tab2Items[1].Detail = cronOk ? "已部署" : "未部署";
                    _tab2Dg.Items.Refresh();
                    AppendTab2($"  /etc/cron.d/clean-logs → {(cronOk ? "✅ 已部署" : "❌ 未部署")}");
                });
                if (cronOk) deployed++;

                // 读取上次执行记录
                var history = RunCommand(ssh, "tail -5 /var/log/clean-logs.log 2>/dev/null || echo NO_HISTORY").Trim();
                if (!history.Contains("NO_HISTORY") && !string.IsNullOrWhiteSpace(history))
                {
                    Dispatcher.Invoke(() =>
                    {
                        AppendTab2("  📋 上次清理记录:");
                        foreach (var line in history.Split('\n').Take(5))
                            AppendTab2($"     {line.Trim()}");
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    if (deployed == 2) _feature2State = FeatureState.Deployed;
                    else if (deployed == 0) _feature2State = FeatureState.NotDeployed;
                    else _feature2State = FeatureState.Partial;
                    UpdateTab2Buttons();
                    SetStatus($"功能二扫描完成: {deployed}/2", deployed > 0);
                });
            });
        }
        catch (Exception ex)
        {
            AppendTab2($"❌ 扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
            _feature2State = FeatureState.Failed;
            UpdateTab2Buttons();
        }
        finally { _tab2ScanBtn.IsEnabled = true; }
    }

    private async void DeployFeature2()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (Sftp == null || !Sftp.IsConnected) { SetStatus("SFTP 未连接", false); return; }

        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        _tab2DeployBtn.IsEnabled = false;
        SetStatus("正在部署功能二...", true);
        AppendTab2("\n━━━━ 功能二：部署日志清理 ━━━━");

        try
        {
            var ssh = Ssh;
            await Task.Run(() =>
            {
                // Step 1: clean-logs.sh
                UploadViaSftp(CLEAN_LOGS_SCRIPT, "/usr/local/bin/clean-logs.sh", 755, sshUser, sshPass);
                Dispatcher.Invoke(() => { _tab2Items[0].StatusIcon = "✅"; _tab2Items[0].Detail = "已部署"; _tab2Dg.Items.Refresh(); AppendTab2("  ✅ clean-logs.sh 已部署"); });

                // Step 2: cron
                UploadViaSftp(CRON_CLEAN_LOGS, "/etc/cron.d/clean-logs", 644, sshUser, sshPass);
                Dispatcher.Invoke(() => { _tab2Items[1].StatusIcon = "✅"; _tab2Items[1].Detail = "已部署"; _tab2Dg.Items.Refresh(); AppendTab2("  ✅ /etc/cron.d/clean-logs 已部署"); });
            });

            AppendTab2("━━━━ 功能二部署完成 ━━━━\n");
            SetStatus("功能二部署完成", true);
            ScanFeature2();
        }
        catch (Exception ex)
        {
            AppendTab2($"❌ 部署失败: {ex.Message}");
            SetStatus($"部署失败: {ex.Message}", false);
            _tab2DeployBtn.IsEnabled = true;
        }
    }

    private async void UninstallFeature2()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (string.IsNullOrWhiteSpace(_desktopUserBox2.Text)) { AppendTab2("⚠️ 请先输入桌面登录用户"); SetStatus("请输入桌面登录用户", false); return; }
        if (MessageBox.Show("确认卸载功能二的 2 个文件？", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        _tab2UninstallBtn.IsEnabled = false;
        _tab2ScanBtn.IsEnabled = false;
        _tab2DeployBtn.IsEnabled = false;
        SetStatus("正在卸载功能二...", true);
        AppendTab2("\n━━━━ 功能二：卸载日志清理 ━━━━");

        try
        {
            var ssh = Ssh;
            int remaining = 0;
            await Task.Run(() =>
            {
                // 执行删除
                var rmOutput = RunCommandSudo(ssh,
                    "rm -f /usr/local/bin/clean-logs.sh /etc/cron.d/clean-logs",
                    sshUser, sshPass);
                Dispatcher.Invoke(() => AppendTab2($"  rm 输出: {(string.IsNullOrWhiteSpace(rmOutput) ? "(无输出)" : rmOutput.Trim())}"));

                // 逐文件验证是否真的被删除了
                var verifyCmds = new[]
                {
                    "test -f /usr/local/bin/clean-logs.sh && echo STILL_EXISTS || echo REMOVED",
                    "test -f /etc/cron.d/clean-logs && echo STILL_EXISTS || echo REMOVED",
                };
                foreach (var cmd in verifyCmds)
                {
                    var result = RunCommand(ssh, cmd).Trim();
                    if (result.Contains("STILL_EXISTS")) remaining++;
                    Dispatcher.Invoke(() => AppendTab2($"  验证: {cmd.Split(' ')[2]} → {result}"));
                }
            });

            if (remaining == 0)
            {
                AppendTab2("━━━━ 功能二卸载完成 ━━━━\n");
                SetStatus("功能二卸载完成", true);
                _feature2State = FeatureState.NotDeployed;
                UpdateTab2Buttons();
                ScanFeature2();
            }
            else
            {
                AppendTab2($"⚠️ 仍有 {remaining} 个文件未删除（sudo 权限可能不足）\n");
                SetStatus($"卸载不完整: {remaining} 个文件残留", false);
                _tab2UninstallBtn.IsEnabled = true;
                _tab2ScanBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            AppendTab2($"❌ 卸载失败: {ex.Message}");
            SetStatus($"卸载失败: {ex.Message}", false);
            _tab2UninstallBtn.IsEnabled = true;
            _tab2ScanBtn.IsEnabled = true;
        }
    }

    private void VerifyFeature2()
    {
        if (string.IsNullOrWhiteSpace(_desktopUserBox2.Text)) { AppendTab2("⚠️ 请先输入桌面登录用户"); SetStatus("请输入桌面登录用户", false); return; }
        ScanFeature2();
    }

    // ================== 功能三：VNC Server (x11vnc) ==================

    private async void ScanFeature3()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        _tab3ScanBtn.IsEnabled = false;
        SetStatus("正在扫描 VNC Server...", true);
        AppendTab3("\n━━━━ VNC Server：扫描 ━━━━");

        try
        {
            var ssh = Ssh;
            await Task.Run(() =>
            {
                var scanCmds = new (string cmd, int idx)[]
                {
                    ("test -x /usr/local/bin/x11vnc && echo OK || echo MISSING", 0),
                    ("test -f /etc/x11vnc.passwd && echo OK || echo MISSING", 1),
                    ("test -f /etc/systemd/system/x11vnc.service && echo OK || echo MISSING", 2),
                };

                int deployed = 0;
                foreach (var (cmd, idx) in scanCmds)
                {
                    var output = RunCommand(ssh, cmd).Trim();
                    var ok = output.Contains("OK");
                    Dispatcher.Invoke(() =>
                    {
                        _tab3Items[idx].IsDeployed = ok;
                        _tab3Items[idx].StatusIcon = ok ? "✅" : "❌";
                        _tab3Items[idx].Detail = ok ? "已部署" : "未部署";
                        _tab3Dg.Items.Refresh();
                        AppendTab3($"  {_tab3Items[idx].ItemName} → {(ok ? "✅ 已部署" : "❌ 未部署")}");
                    });
                    if (ok) deployed++;
                }

                // 检查服务运行状态
                var svcStatus = RunCommand(ssh, "systemctl is-active x11vnc 2>/dev/null || echo unknown").Trim();
                Dispatcher.Invoke(() =>
                {
                    _vncServiceRunning = svcStatus == "active";
                    AppendTab3($"  服务状态: {(_vncServiceRunning ? "🟢 运行中" : "⚫ 已停止")} (systemctl: {svcStatus})");

                    if (deployed == 3) _feature3State = FeatureState.Deployed;
                    else if (deployed == 0) _feature3State = FeatureState.NotDeployed;
                    else _feature3State = FeatureState.Partial;
                    UpdateTab3Buttons();
                    SetStatus($"VNC Server 扫描完成: {deployed}/3", deployed > 0);
                });
            });
        }
        catch (Exception ex)
        {
            AppendTab3($"❌ 扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
            _feature3State = FeatureState.Failed;
            UpdateTab3Buttons();
        }
        finally { _tab3ScanBtn.IsEnabled = true; }
    }

    private async void DeployFeature3()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (Sftp == null || !Sftp.IsConnected) { SetStatus("SFTP 未连接", false); return; }

        var vncPassword = _vncPasswordBox.Password.Trim();
        if (string.IsNullOrEmpty(vncPassword) || vncPassword.Length < 6)
        {
            SetStatus("VNC 密码至少 6 位", false);
            return;
        }

        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;
        var port = _vncPortBox.Text.Trim();
        if (!int.TryParse(port, out int portNum) || portNum < 1 || portNum > 65535)
        {
            SetStatus("端口无效", false);
            return;
        }

        _tab3DeployBtn.IsEnabled = false;
        _tab3ScanBtn.IsEnabled = false;
        SetStatus("正在部署 VNC Server...", true);
        AppendTab3("\n━━━━ VNC Server：部署 ━━━━");

        try
        {
            var ssh = Ssh;
            var sftp = Sftp;

            await Task.Run(() =>
            {
                // Step 1: 检测目标架构，选择对应二进制
                var arch = RunCommand(ssh, "uname -m").Trim();
                AppendTab3ThreadSafe($"  目标架构: {arch}");
                var binaryName = arch.Contains("aarch64") || arch.Contains("arm64")
                    ? "x11vnc_aarch64"
                    : "x11vnc_x86_64";

                // Step 2: 从 Resources 读取 x11vnc 二进制并上传
                var resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "x11vnc", binaryName);
                if (!File.Exists(resourcePath))
                    throw new Exception($"x11vnc 二进制文件未找到: {resourcePath}");

                using (var fs = File.OpenRead(resourcePath))
                {
                    var tmpPath = $"/tmp/x11vnc_{Guid.NewGuid():N}";
                    sftp.UploadFile(fs, tmpPath, true);
                    RunCommandSudo(ssh, $"mv {tmpPath} /usr/local/bin/x11vnc && chmod 755 /usr/local/bin/x11vnc", sshUser, sshPass);
                    AppendTab3ThreadSafe("  ✅ x11vnc 二进制已上传并授权");
                }

                // Step 3: 生成加密的 VNC 密码文件（x11vnc -storepasswd；两次输入确认，失败退化为参数形式）
                var escPw = vncPassword.Replace("'", "'\\''");
                var tmpPw = $"/tmp/x11vnc_pw_{Guid.NewGuid():N}";
                RunCommand(ssh,
                    $"printf '{escPw}\\n{escPw}\\n' | /usr/local/bin/x11vnc -storepasswd {tmpPw} >/dev/null 2>&1 " +
                    $"|| /usr/local/bin/x11vnc -storepasswd '{escPw}' {tmpPw} >/dev/null 2>&1");
                var pwOk = RunCommand(ssh, $"test -s {tmpPw} && echo PWOK || echo PWFAIL").Trim().Contains("PWOK");
                if (!pwOk)
                    throw new Exception("VNC 密码文件生成失败（x11vnc -storepasswd 执行异常）");
                RunCommandSudo(ssh, $"mv {tmpPw} /etc/x11vnc.passwd && chmod 600 /etc/x11vnc.passwd", sshUser, sshPass);
                AppendTab3ThreadSafe("  ✅ VNC 密码文件已生成");

                // Step 4: 自动探测 X auth 文件路径
                var authPath = RunCommand(ssh, @"for p in /var/run/lightdm/root/:0 /var/lib/lightdm/.Xauthority /run/lightdm/root/:0 ""$(xauth info 2>/dev/null | grep 'Authority file' | awk '{print $NF}')""; do test -f ""$p"" && echo ""$p"" && break; done").Trim();

                if (string.IsNullOrEmpty(authPath))
                    throw new Exception("无法探测 X authority 文件路径，请确认 LightDM 正在运行");

                AppendTab3ThreadSafe($"  X auth 文件: {authPath}");

                // Step 5: 生成 systemd unit 并上传
                var serviceContent = X11VNC_SERVICE_TEMPLATE
                    .Replace("{AUTH_FILE}", authPath)
                    .Replace("{PORT}", port)
                    .Replace("\r\n", "\n");  // 确保 LF 换行

                var tmpSvc = $"/tmp/x11vnc_service_{Guid.NewGuid():N}";
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(serviceContent)))
                    sftp.UploadFile(ms, tmpSvc, true);
                RunCommandSudo(ssh, $"mv {tmpSvc} /etc/systemd/system/x11vnc.service && chmod 644 /etc/systemd/system/x11vnc.service", sshUser, sshPass);
                AppendTab3ThreadSafe("  ✅ systemd unit 已部署");

                // Step 6: reload + enable + start
                RunCommandSudo(ssh, "systemctl daemon-reload", sshUser, sshPass);
                RunCommandSudo(ssh, "systemctl enable x11vnc 2>&1", sshUser, sshPass);
                RunCommandSudo(ssh, "systemctl start x11vnc 2>&1", sshUser, sshPass);
                AppendTab3ThreadSafe("  ✅ 服务已启动并设为开机自启");

                // Step 7: 验证端口监听
                var portCheck = RunCommand(ssh, $"ss -tlnp | grep ':{port}' || echo NOT_LISTENING").Trim();
                if (portCheck.Contains("NOT_LISTENING"))
                    AppendTab3ThreadSafe($"  ⚠️ 端口 {port} 未检测到监听，请检查 /var/log/x11vnc.log");
                else
                    AppendTab3ThreadSafe($"  ✅ 端口 {port} 已监听");
            });

            AppendTab3("━━━━ VNC Server 部署完成 ━━━━\n");
            AppendTab3($"🔗 现在可在「VNC 远程连接」中输入 IP:{port} 和密码进行连接");
            SetStatus("VNC Server 部署完成", true);
            ScanFeature3(); // 刷新状态
        }
        catch (Exception ex)
        {
            AppendTab3($"❌ 部署失败: {ex.Message}");
            SetStatus($"部署失败: {ex.Message}", false);
            _tab3DeployBtn.IsEnabled = true;
            _tab3ScanBtn.IsEnabled = true;
        }
    }

    private void StartVncServer()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        try
        {
            var output = RunCommandSudo(Ssh, "systemctl start x11vnc 2>&1", sshUser, sshPass);
            AppendTab3($"  systemctl start: {(string.IsNullOrWhiteSpace(output) ? "(无输出)" : output.Trim())}");
            _vncServiceRunning = true;
            UpdateTab3Buttons();
            SetStatus("VNC Server 已启动", true);
        }
        catch (Exception ex)
        {
            AppendTab3($"❌ 启动失败: {ex.Message}");
            SetStatus($"启动失败: {ex.Message}", false);
        }
    }

    private void StopVncServer()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        try
        {
            var output = RunCommandSudo(Ssh, "systemctl stop x11vnc 2>&1", sshUser, sshPass);
            AppendTab3($"  systemctl stop: {(string.IsNullOrWhiteSpace(output) ? "(无输出)" : output.Trim())}");
            _vncServiceRunning = false;
            UpdateTab3Buttons();
            SetStatus("VNC Server 已停止", true);
        }
        catch (Exception ex)
        {
            AppendTab3($"❌ 停止失败: {ex.Message}");
            SetStatus($"停止失败: {ex.Message}", false);
        }
    }

    private async void UninstallFeature3()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (MessageBox.Show("确认卸载 VNC Server 的 3 个文件？\n\n这将停止服务并删除：\n• /usr/local/bin/x11vnc\n• /etc/x11vnc.passwd\n• /etc/systemd/system/x11vnc.service",
            "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        _tab3UninstallBtn.IsEnabled = false;
        _tab3ScanBtn.IsEnabled = false;
        _tab3DeployBtn.IsEnabled = false;
        _tab3StartBtn.IsEnabled = false;
        _tab3StopBtn.IsEnabled = false;
        SetStatus("正在卸载 VNC Server...", true);
        AppendTab3("\n━━━━ VNC Server：卸载 ━━━━");

        try
        {
            var ssh = Ssh;
            int remaining = 0;
            await Task.Run(() =>
            {
                // 先停止服务
                RunCommandSudo(ssh, "systemctl stop x11vnc 2>/dev/null; systemctl disable x11vnc 2>/dev/null", sshUser, sshPass);
                AppendTab3ThreadSafe("  已停止并禁用服务");

                // 执行删除
                RunCommandSudo(ssh,
                    "rm -f /usr/local/bin/x11vnc /etc/x11vnc.passwd /etc/systemd/system/x11vnc.service && systemctl daemon-reload",
                    sshUser, sshPass);

                // 逐文件验证
                var verifyCmds = new[]
                {
                    "test -f /usr/local/bin/x11vnc && echo STILL_EXISTS || echo REMOVED",
                    "test -f /etc/x11vnc.passwd && echo STILL_EXISTS || echo REMOVED",
                    "test -f /etc/systemd/system/x11vnc.service && echo STILL_EXISTS || echo REMOVED",
                };
                foreach (var cmd in verifyCmds)
                {
                    var result = RunCommand(ssh, cmd).Trim();
                    if (result.Contains("STILL_EXISTS")) remaining++;
                    AppendTab3ThreadSafe($"  验证: {result}");
                }
            });

            if (remaining == 0)
            {
                AppendTab3("━━━━ VNC Server 卸载完成 ━━━━\n");
                SetStatus("VNC Server 卸载完成", true);
                _feature3State = FeatureState.NotDeployed;
                _vncServiceRunning = false;
                UpdateTab3Buttons();
                ScanFeature3();
            }
            else
            {
                AppendTab3($"⚠️ 仍有 {remaining} 个文件未删除（sudo 权限可能不足）\n");
                SetStatus($"卸载不完整: {remaining} 个文件残留", false);
                _tab3UninstallBtn.IsEnabled = true;
                _tab3ScanBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            AppendTab3($"❌ 卸载失败: {ex.Message}");
            SetStatus($"卸载失败: {ex.Message}", false);
            _tab3UninstallBtn.IsEnabled = true;
            _tab3ScanBtn.IsEnabled = true;
        }
    }

    // ================== 脚本常量 ==================

    /// <summary>x11vnc systemd unit 模板（运行时替换 {AUTH_FILE} 和 {PORT}）</summary>
    private const string X11VNC_SERVICE_TEMPLATE = @"[Unit]
Description=x11vnc VNC Server for X display :0
After=lightdm.service
Requires=lightdm.service

[Service]
Type=forking
ExecStart=/usr/local/bin/x11vnc -display :0 -auth {AUTH_FILE} -rfbauth /etc/x11vnc.passwd -forever -shared -rfbport {PORT} -o /var/log/x11vnc.log -bg -noxdamage
ExecStop=/usr/bin/killall x11vnc
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
";

    private const string SCHEDULED_REBOOT_SCRIPT_TEMPLATE = @"#!/bin/bash
LOG=""/var/log/scheduled-reboot.log""
USERNAME=""{{DESKTOP_USERNAME}}""
DM_CONF=""/etc/lightdm/lightdm.conf""
echo ""========================================="" >> ""$LOG""
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] 定时重启任务触发"" >> ""$LOG""
sed -i '/^\[Seat:\*\]/d; /^autologin-user=/d; /^autologin-user-timeout=/d' ""$DM_CONF""
tee -a ""$DM_CONF"" > /dev/null << AUTOLOGIN_EOF

[Seat:*]
autologin-user=$USERNAME
autologin-user-timeout=0
AUTOLOGIN_EOF
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] 已写入临时 autologin-user=$USERNAME"" >> ""$LOG""
sync
sleep 3
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] 执行 reboot"" >> ""$LOG""
/sbin/reboot
";

    private const string CLEAR_AUTOLOGIN_SCRIPT = @"#!/bin/bash
DM_CONF=""/etc/lightdm/lightdm.conf""
sleep 10
sudo sed -i '/^\[Seat:\*\]/d; /^autologin-user=/d; /^autologin-user-timeout=/d' ""$DM_CONF""
";

    private const string AUTOSTART_DESKTOP = @"[Desktop Entry]
Type=Application
Name=Clear AutoLogin
Comment=Remove temporary autologin after scheduled reboot
Exec=/usr/local/bin/clear-autologin.sh
NoDisplay=true
";

    private const string CRON_AUTO_REBOOT = @"# KylinOS 定时重启（由 ToolHelper 部署）
# 每月 1 日 00:00 执行（以 root 身份运行，脚本内无需 sudo）
0 0 1 * * root /usr/local/bin/scheduled-reboot.sh
";

    private const string SUDOERS_TEMPLATE = @"# clear-autologin.sh 所需的 sudo 免密权限（由 ToolHelper 部署）
# 仅授权桌面用户免密执行 sed（用于清除 lightdm.conf 中的 autologin 配置）
{{DESKTOP_USERNAME}} ALL=(ALL) NOPASSWD: /usr/bin/sed
";

    private const string CLEAN_LOGS_SCRIPT = @"#!/bin/bash
LOG_FILE=""/var/log/clean-logs.log""
CLEAN_COUNT=0
echo ""========================================="" >> ""$LOG_FILE""
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] ===== 月度清理开始 ====="" >> ""$LOG_FILE""
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] 清理 systemd journal（保留30天）..."" >> ""$LOG_FILE""
journalctl --vacuum-time=30d --quiet >> ""$LOG_FILE"" 2>&1
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] 清理 /var/log（>365天）..."" >> ""$LOG_FILE""
while IFS= read -r -d '' f; do
    rm -f ""$f""
    echo ""  [删除] $f"" >> ""$LOG_FILE""
    CLEAN_COUNT=$((CLEAN_COUNT + 1))
done < <(find /var/log -type f \( -name ""*.log"" -o -name ""*.log.*"" -o -name ""*.gz"" \) -mtime +365 -print0 2>/dev/null)
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] 清理 /tmp（>365天）..."" >> ""$LOG_FILE""
while IFS= read -r -d '' f; do
    rm -f ""$f""
    echo ""  [删除] $f"" >> ""$LOG_FILE""
    CLEAN_COUNT=$((CLEAN_COUNT + 1))
done < <(find /tmp -type f -mtime +365 -print0 2>/dev/null)
find /tmp -type d -empty -mtime +365 -delete 2>/dev/null
for f in /var/log/syslog /var/log/messages /var/log/kern.log /var/log/auth.log; do
    if [ -f ""$f"" ]; then
        SIZE=$(stat -c%s ""$f"" 2>/dev/null || echo 0)
        if [ ""$SIZE"" -gt 524288000 ]; then
            truncate -s 0 ""$f""
            echo ""  [清空] $f ($(($SIZE / 1048576)) MB)"" >> ""$LOG_FILE""
            CLEAN_COUNT=$((CLEAN_COUNT + 1))
        fi
    fi
done
echo ""[$(date '+%Y-%m-%d %H:%M:%S')] ===== 清理完成，共处理 $CLEAN_COUNT 项 ====="" >> ""$LOG_FILE""
";

    private const string CRON_CLEAN_LOGS = @"# KylinOS 日志和临时文件清理（由 ToolHelper 部署）
# 每月 1 日 01:00 执行（在定时重启之后一小时，避免冲突）
0 1 1 * * root /usr/local/bin/clean-logs.sh
";

    // ================== 功能四：PostgreSQL 连接配置 ==================

    private async void ScanFeature4()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        _tab4ScanBtn.IsEnabled = false;
        SetStatus("正在扫描 PostgreSQL 连接配置...", true);
        AppendTab4("\n━━━━ PostgreSQL连接：扫描 ━━━━");

        try
        {
            var ssh = Ssh;
            await Task.Run(() =>
            {
                // 检查目标目录是否存在
                var dirOk = RunCommand(ssh, $"test -d {OpenGaussDataDir} && echo OK || echo MISSING").Trim().Contains("OK");
                Dispatcher.Invoke(() => AppendTab4($"  数据目录: {(dirOk ? "✅ 存在" : "❌ 不存在")} - {OpenGaussDataDir}"));

                int deployed = 0;
                var scanCmds = new (string cmd, int idx)[]
                {
                    ($"test -f {OpenGaussDataDir}/pg_hba.conf && echo OK || echo MISSING", 0),
                    ($"test -f {OpenGaussDataDir}/postgresql.conf && echo OK || echo MISSING", 1),
                };

                foreach (var (cmd, idx) in scanCmds)
                {
                    var output = RunCommand(ssh, cmd).Trim();
                    var ok = output.Contains("OK");
                    Dispatcher.Invoke(() =>
                    {
                        _tab4Items[idx].IsDeployed = ok;
                        _tab4Items[idx].StatusIcon = ok ? "✅" : "❌";
                        _tab4Items[idx].Detail = ok ? "文件存在" : "文件缺失";
                        _tab4Dg.Items.Refresh();
                        AppendTab4($"  {_tab4Items[idx].ItemName} → {(ok ? "✅" : "❌")} {_tab4Items[idx].Detail}");
                    });
                    if (ok) deployed++;
                }

                // 检查 backup_conf 是否存在（判断是否已部署）
                var backupOk = RunCommand(ssh, $"test -d {OpenGaussBackupDir} && echo OK || echo MISSING").Trim().Contains("OK");
                Dispatcher.Invoke(() =>
                {
                    _tab4Items[2].StatusIcon = backupOk ? "📦" : "⬜";
                    _tab4Items[2].Detail = backupOk ? "已部署（备份存在）" : "未部署";
                    _tab4Dg.Items.Refresh();
                    AppendTab4($"  backup_conf/ → {(backupOk ? "📦 已部署" : "⬜ 未部署")}");

                    if (!dirOk) _feature4State = FeatureState.NotDeployed;
                    else if (backupOk) _feature4State = FeatureState.Deployed;
                    else if (deployed == 2) _feature4State = FeatureState.NotDeployed;  // 文件存在但没备份 = 未部署
                    else _feature4State = FeatureState.Partial;
                    UpdateTab4Buttons();
                    SetStatus($"PostgreSQL连接扫描完成: {deployed}/2", deployed > 0);
                });
            });
        }
        catch (Exception ex)
        {
            AppendTab4($"❌ 扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
            _feature4State = FeatureState.Failed;
            UpdateTab4Buttons();
        }
        finally { _tab4ScanBtn.IsEnabled = true; }
    }

    private async void DeployFeature4()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (Sftp == null || !Sftp.IsConnected) { SetStatus("SFTP 未连接", false); return; }

        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        _tab4DeployBtn.IsEnabled = false;
        _tab4ScanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus("正在部署 PostgreSQL 连接配置...", true);
        AppendTab4("\n━━━━ PostgreSQL连接：部署 ━━━━");

        try
        {
            var ssh = Ssh;
            var sftp = Sftp;

            await Task.Run(() =>
            {
                // Step 1: 确保目标目录存在
                RunCommandSudo(ssh, $"mkdir -p {OpenGaussDataDir}", sshUser, sshPass);

                // Step 2: 创建备份目录
                RunCommandSudo(ssh, $"mkdir -p {OpenGaussBackupDir}", sshUser, sshPass);
                Dispatcher.Invoke(() => AppendTab4("  ✅ 备份目录已创建: backup_conf/"));

                // Step 3: 备份原文件并上传新文件
                var files = new[] { "pg_hba.conf", "postgresql.conf" };
                var localDir = OpenGaussConfDir;

                for (int idx = 0; idx < files.Length; idx++)
                {
                    var f = files[idx];
                    var remotePath = $"{OpenGaussDataDir}/{f}";
                    var backupPath = $"{OpenGaussBackupDir}/{f}";

                    // 备份原文件（如果存在且未备份）
                    var exists = RunCommand(ssh, $"test -f {remotePath} && echo OK || echo MISSING").Trim().Contains("OK");
                    var backedUp = RunCommand(ssh, $"test -f {backupPath} && echo OK || echo MISSING").Trim().Contains("OK");
                    if (exists && !backedUp)
                    {
                        RunCommandSudo(ssh, $"cp {remotePath} {backupPath}", sshUser, sshPass);
                        Dispatcher.Invoke(() => AppendTab4($"  📦 {f} → 备份到 backup_conf/"));
                    }

                    // 上传新文件
                    var localFile = Path.Combine(localDir, f);
                    if (!File.Exists(localFile)) throw new Exception($"本地配置文件缺失: {localFile}");

                    var tmpFile = $"/tmp/{f}_{Guid.NewGuid():N}";
                    using (var fs = File.OpenRead(localFile))
                        sftp.UploadFile(fs, tmpFile, true);
                    RunCommandSudo(ssh, $"mv {tmpFile} {remotePath} && chmod 600 {remotePath}", sshUser, sshPass);

                    Dispatcher.Invoke(() =>
                    {
                        _tab4Items[idx].StatusIcon = "✅";
                        _tab4Items[idx].Detail = "已部署";
                        _tab4Dg.Items.Refresh();
                        AppendTab4($"  ✅ {f} 已部署");
                    });
                }

                // 更新 backup_conf 状态
                Dispatcher.Invoke(() =>
                {
                    _tab4Items[2].StatusIcon = "📦";
                    _tab4Items[2].Detail = "已部署";
                    _tab4Dg.Items.Refresh();
                });
            });

            AppendTab4("━━━━ 部署完成 ━━━━\n");
            AppendTab4("⚠️ 配置文件已更新，需重启 openGauss 或执行 gs_ctl reload 使配置生效。点击「重启服务」按钮执行重启。");
            SetStatus("PostgreSQL连接配置部署完成", true);
            _feature4State = FeatureState.Deployed;
            UpdateTab4Buttons();
        }
        catch (Exception ex)
        {
            AppendTab4($"❌ 部署失败: {ex.Message}");
            SetStatus($"部署失败: {ex.Message}", false);
            _tab4DeployBtn.IsEnabled = true;
            _tab4ScanBtn.IsEnabled = true;
        }
        finally { DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private async void UninstallFeature4()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (MessageBox.Show("确认卸载 PostgreSQL 连接配置？\n\n将恢复原始 pg_hba.conf 和 postgresql.conf", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;

        _tab4UninstallBtn.IsEnabled = false;
        _tab4ScanBtn.IsEnabled = false;
        _tab4DeployBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus("正在卸载 PostgreSQL 连接配置...", true);
        AppendTab4("\n━━━━ PostgreSQL连接：卸载 ━━━━");

        try
        {
            var ssh = Ssh;
            int remaining = 0;
            await Task.Run(() =>
            {
                var files = new[] { "pg_hba.conf", "postgresql.conf" };
                foreach (var f in files)
                {
                    var backupPath = $"{OpenGaussBackupDir}/{f}";
                    var remotePath = $"{OpenGaussDataDir}/{f}";
                    var hasBackup = RunCommand(ssh, $"test -f {backupPath} && echo OK || echo MISSING").Trim().Contains("OK");

                    if (hasBackup)
                    {
                        RunCommandSudo(ssh, $"mv {backupPath} {remotePath}", sshUser, sshPass);
                        Dispatcher.Invoke(() => AppendTab4($"  ↩️ {f} → 已恢复为原始配置"));
                    }
                    else
                    {
                        remaining++;
                        Dispatcher.Invoke(() => AppendTab4($"  ⚠️ {f} → 备份文件不存在，无法恢复"));
                    }
                }

                // 清理备份目录
                RunCommandSudo(ssh, $"rmdir {OpenGaussBackupDir} 2>/dev/null", sshUser, sshPass);
                Dispatcher.Invoke(() => AppendTab4("  🗑️ backup_conf/ 目录已清理"));
            });

            if (remaining == 0)
            {
                AppendTab4("━━━━ 卸载完成，配置已恢复 ━━━━\n");
                AppendTab4("⚠️ 配置已恢复为原始版本，需重启 openGauss 或执行 gs_ctl reload 使配置生效。");
                SetStatus("PostgreSQL连接配置已卸载", true);
                _feature4State = FeatureState.NotDeployed;
                UpdateTab4Buttons();
                ScanFeature4();
            }
            else
            {
                AppendTab4($"⚠️ {remaining} 个文件备份缺失，无法完全恢复\n");
                SetStatus($"卸载不完整: {remaining} 个文件备份缺失", false);
                _tab4UninstallBtn.IsEnabled = true;
                _tab4ScanBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            AppendTab4($"❌ 卸载失败: {ex.Message}");
            SetStatus($"卸载失败: {ex.Message}", false);
            _tab4UninstallBtn.IsEnabled = true;
            _tab4ScanBtn.IsEnabled = true;
        }
        finally { DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private void RestartOpenGauss()
    {
        if (Ssh == null || !Ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        var sshUser = UserBox.Text.Trim();
        var sshPass = PassBox.Password;
        AppendTab4("\n━━━━ 重启 openGauss 服务 ━━━━");
        try
        {
            // 尝试 gs_ctl reload（不重启，仅重载配置）
            var output = RunCommandSudo(Ssh, $"su - omm -c 'gs_ctl reload -D {OpenGaussDataDir}' 2>&1", sshUser, sshPass);
            AppendTab4($"  gs_ctl reload: {output.Trim()}");
            if (output.Contains("server signaled") || output.Contains("PID"))
            {
                AppendTab4("  ✅ 配置重载成功（无需重启）");
                SetStatus("openGauss 配置已重载", true);
            }
            else
            {
                // 回退：尝试 systemctl restart
                AppendTab4("  gs_ctl reload 未成功，尝试 systemctl restart...");
                var output2 = RunCommandSudo(Ssh, "systemctl restart opengauss 2>&1 || systemctl restart gaussdb 2>&1 || echo RESTART_FAILED", sshUser, sshPass);
                AppendTab4($"  systemctl: {output2.Trim()}");
                if (output2.Contains("RESTART_FAILED"))
                {
                    AppendTab4("  ⚠️ 自动重启失败，请手动重启 openGauss");
                    SetStatus("重启失败，请手动执行", false);
                }
                else
                {
                    AppendTab4("  ✅ openGauss 已重启");
                    SetStatus("openGauss 已重启", true);
                }
            }
        }
        catch (Exception ex)
        {
            AppendTab4($"❌ 重启失败: {ex.Message}");
            SetStatus($"重启失败: {ex.Message}", false);
        }
    }

    #region Tab6: 漏洞扫描逻辑（kylin-offline-upgrade 本地提权）

    /// <summary>
    /// 补丁目录：从 BaseDirectory 向上逐级查找 plugins/Security patch/
    /// </summary>
    private static string VulPatchDir
    {
        get
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(dir, "plugins", "Security patch");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetFullPath(Path.Combine(dir, ".."));
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "Security patch");
        }
    }

    private static List<string> GetAvailableVulPatches()
    {
        try
        {
            if (!Directory.Exists(VulPatchDir)) return new List<string>();
            return Directory.GetFiles(VulPatchDir, "*.deb")
                           .Select(Path.GetFileName)
                           .Where(f => f != null)
                           .Cast<string>()
                           .OrderByDescending(f =>
                           {
                               var v = ExtractVulVersionFromFileName(f);
                               return v != null ? DpkgVersion.Parse(v) : new DpkgVersion();
                           })
                           .ToList();
        }
        catch { return new List<string>(); }
    }

    private static string? GetBestVulPatch() => GetAvailableVulPatches().FirstOrDefault();

    private static string? ExtractVulVersionFromFileName(string fileName)
    {
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('_');
        if (parts.Length >= 2) return parts[1];
        return null;
    }

    // ================== 扫描 ==================

    private async void DoVulScan()
    {
        _vulScanBtn.IsEnabled = false;
        SetStatus("正在扫描...", true);
        AppendTab6("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendTab6($"[{DateTime.Now:HH:mm:ss}] 开始扫描...");

        try
        {
            var ssh = Ssh;
            if (ssh == null || !ssh.IsConnected) throw new InvalidOperationException("SSH 未连接");
            DisconnectBtn.IsEnabled = false;
            var result = await Task.Run(() => ScanVulInternal(ssh));
            _vulLastResult = result;
            DisplayVulResult(result);
            UpdateTab6Buttons();
            SetStatus(result.IsVulnerable ? "发现漏洞" : "未发现漏洞", !result.IsVulnerable);
        }
        catch (Exception ex)
        {
            AppendTab6($"扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
        }
        finally { _vulScanBtn.IsEnabled = Ssh != null && Ssh.IsConnected; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private VulnerabilityResult ScanVulInternal(SshClient ssh)
    {
        var result = new VulnerabilityResult { ScanTime = DateTime.Now };
        var osRelease = RunCommand(ssh, "cat /etc/os-release");
        var kernel = RunCommand(ssh, "uname -r").Trim();
        var arch = RunCommand(ssh, "arch").Trim();

        result.OsName = ExtractField(osRelease, "NAME");
        result.OsVersion = ExtractField(osRelease, "VERSION");
        result.OsSp = ExtractField(osRelease, "VERSION_US");
        result.KernelVersion = kernel;
        result.Architecture = arch;

        var dpkgOutput = RunCommand(ssh, "dpkg -l kylin-offline-upgrade 2>/dev/null");
        result.CurrentVersion = ParseVulDpkgVersion(dpkgOutput);

        if (result.CurrentVersion == null)
        {
            result.IsVulnerable = false;
            result.Status = dpkgOutput.Contains("no packages found") || dpkgOutput.Contains("未安装")
                ? VulnerabilityStatus.NotInstalled : VulnerabilityStatus.ScanFailed;
            return result;
        }

        var bestPatch = GetBestVulPatch();
        result.PatchFile = bestPatch;
        result.FixedVersion = bestPatch != null ? (ExtractVulVersionFromFileName(bestPatch) ?? "0") : "0";

        var current = DpkgVersion.Parse(result.CurrentVersion);
        var fixed_ = DpkgVersion.Parse(result.FixedVersion);
        result.IsVulnerable = current.CompareTo(fixed_) < 0;
        result.Status = result.IsVulnerable ? VulnerabilityStatus.Vulnerable : VulnerabilityStatus.Fixed;
        return result;
    }

    private void DisplayVulResult(VulnerabilityResult r)
    {
        AppendTab6($"扫描时间: {r.ScanTime:yyyy-MM-dd HH:mm:ss}");
        AppendTab6($"目标系统: {r.OsName} {r.OsVersion} ({r.OsSp})");
        AppendTab6($"内核版本: {r.KernelVersion}  架构: {r.Architecture}");
        AppendTab6("");

        if (r.Status == VulnerabilityStatus.NotInstalled)
        {
            AppendTab6("[结果] 未安装 kylin-offline-upgrade，不受此漏洞影响。");
        }
        else if (r.Status == VulnerabilityStatus.ScanFailed)
        {
            AppendTab6("[结果] 扫描失败，无法获取组件版本信息。");
        }
        else if (r.IsVulnerable)
        {
            AppendTab6("[结果] 发现漏洞!");
            AppendTab6($"  组件: kylin-offline-upgrade");
            AppendTab6($"  当前版本: {r.CurrentVersion}  <-- 存在漏洞");
            AppendTab6($"  修复版本: {r.FixedVersion}  <-- 需升级到此版本");
            AppendTab6($"  补丁文件: {r.PatchFile}");
            AppendTab6("");
            AppendTab6("漏洞描述:");
            AppendTab6("  kylin-offline-upgrade 核心组件存在本地权限提升漏洞，");
            AppendTab6("  普通用户可借此获得 root 权限，完全控制系统。");
            AppendTab6("");
            AppendTab6("修复方法:");
            AppendTab6("  点击 [修复] 按钮，将自动上传并安装安全补丁。");
        }
        else
        {
            AppendTab6("[结果] 未发现漏洞（已修复）");
            AppendTab6($"  组件: kylin-offline-upgrade");
            AppendTab6($"  当前版本: {r.CurrentVersion}");
            AppendTab6($"  修复版本: {r.FixedVersion}");
            AppendTab6("  状态: 已包含安全补丁，无需修复。");
        }
        AppendTab6("");
    }

    // ================== 修复 ==================

    private async void DoVulRepair()
    {
        if (_vulLastResult == null || !_vulLastResult.IsVulnerable) { SetStatus("无需修复", false); return; }
        var ssh = Ssh;
        var sftp = Sftp;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (sftp == null || !sftp.IsConnected) { SetStatus("SFTP 未连接", false); return; }

        var patchFile = _vulLastResult.PatchFile;
        if (string.IsNullOrEmpty(patchFile))
        {
            SetStatus("补丁目录中没有 .deb 文件", false);
            AppendTab6($"\n[修复失败] 补丁目录中没有找到 .deb 文件");
            AppendTab6($"  目录: {VulPatchDir}");
            AppendTab6("请将官方安全补丁 (.deb) 放入 plugins/Security patch/ 目录后重试。");
            return;
        }

        var localPath = Path.Combine(VulPatchDir, patchFile);
        if (!File.Exists(localPath))
        {
            var available = GetAvailableVulPatches();
            SetStatus($"补丁文件缺失: {patchFile}", false);
            AppendTab6($"\n[修复失败] 补丁文件不存在: {localPath}");
            AppendTab6($"  目录中可用文件: {(available.Count > 0 ? string.Join(", ", available) : "无")}");
            AppendTab6("请将官方安全补丁 (.deb) 放入 plugins/Security patch/ 目录后重试。");
            return;
        }

        _vulRepairBtn.IsEnabled = false;
        _vulScanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus("正在修复...", true);
        AppendTab6("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendTab6($"[{DateTime.Now:HH:mm:ss}] 开始修复...");

        try
        {
            var username = UserBox.Text.Trim();
            if (string.IsNullOrEmpty(username)) username = "root";
            var password = PassBox.Password;
            await Task.Run(() => RepairVulInternal(ssh, sftp, patchFile, localPath, username, password));
            AppendTab6("\n[修复完成] 补丁安装成功！");
            AppendTab6("请点击 [验证] 确认修复是否生效。");
            _vulRepairBtn.IsEnabled = false;
            _vulVerifyBtn.IsEnabled = true;
            SetStatus("修复完成", true);
            MessageBox.Show($"漏洞已修复！\n\nkylin-offline-upgrade 已升级到安全版本。\n请点击 [验证] 确认修复效果。",
                "修复成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendTab6($"\n[修复失败] {ex.Message}");
            SetStatus($"修复失败: {ex.Message}", false);
            _vulRepairBtn.IsEnabled = true;
        }
        finally { _vulScanBtn.IsEnabled = Ssh != null; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private void RepairVulInternal(SshClient ssh, SftpClient sftp, string patchFile, string localPath, string username, string password)
    {
        var remotePath = $"/home/{username}/{patchFile}";

        Dispatcher.Invoke(() => AppendTab6($"[步骤1/2] 上传补丁文件..."));
        Dispatcher.Invoke(() => AppendTab6($"  本地: {localPath}"));
        Dispatcher.Invoke(() => AppendTab6($"  远程: {remotePath}"));

        if (sftp == null || !sftp.IsConnected) throw new InvalidOperationException("SFTP 未连接");
        using (var stream = File.OpenRead(localPath))
        {
            sftp.UploadFile(stream, remotePath, true);
        }
        Dispatcher.Invoke(() => AppendTab6("  上传完成。\n"));

        Dispatcher.Invoke(() => AppendTab6($"[步骤2/2] 安装补丁..."));
        string installCmd;
        if (username == "root")
        {
            installCmd = $"cd /home/{username} && dpkg -i {patchFile}";
        }
        else
        {
            var escapedPassword = password.Replace("'", "'\\''");
            installCmd = $"cd /home/{username} && echo '{escapedPassword}' | sudo -S dpkg -i {patchFile}";
        }

        Dispatcher.Invoke(() => AppendTab6($"  $ cd /home/{username} && sudo dpkg -i {patchFile}"));
        var output = RunCommand(ssh, installCmd);
        Dispatcher.Invoke(() => AppendTab6(output));

        if (!output.Contains("Setting up") && !output.Contains("正在设置") && !output.Contains("Unpacking"))
            throw new Exception("安装可能未成功，输出中未找到安装确认关键字。\n请确认用户具有 sudo 权限且密码正确。");

        Dispatcher.Invoke(() => AppendTab6("  安装完成。\n"));
        try { RunCommand(ssh, $"rm -f {remotePath}"); } catch { }
    }

    // ================== 验证 ==================

    private async void DoVulVerify()
    {
        _vulVerifyBtn.IsEnabled = false;
        SetStatus("正在验证...", true);
        AppendTab6("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendTab6($"[{DateTime.Now:HH:mm:ss}] 开始验证修复结果...");

        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        DisconnectBtn.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => ScanVulInternal(ssh));
            _vulLastResult = result;

            if (result.Status == VulnerabilityStatus.Fixed || result.Status == VulnerabilityStatus.NotInstalled)
            {
                AppendTab6("[验证通过] 漏洞已成功修复！");
                AppendTab6($"  当前版本: {result.CurrentVersion ?? "未安装"}");
                AppendTab6($"  修复版本: {result.FixedVersion}");
                _vulRepairBtn.IsEnabled = false;
                SetStatus("验证通过", true);
            }
            else if (result.IsVulnerable)
            {
                AppendTab6("[验证失败] 修复未生效，版本仍为: " + result.CurrentVersion);
                _vulRepairBtn.IsEnabled = true;
                SetStatus("验证失败", false);
            }
            else
            {
                AppendTab6("[验证异常] 无法确定修复状态");
                SetStatus("验证异常", false);
            }
        }
        catch (Exception ex)
        {
            AppendTab6($"验证失败: {ex.Message}");
            SetStatus($"验证失败: {ex.Message}", false);
        }
        finally { _vulVerifyBtn.IsEnabled = true; _vulScanBtn.IsEnabled = Ssh != null; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private static string? ParseVulDpkgVersion(string dpkgOutput)
    {
        if (string.IsNullOrWhiteSpace(dpkgOutput)) return null;
        if (dpkgOutput.Contains("no packages found")) return null;
        foreach (var line in dpkgOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("ii ") || trimmed.StartsWith("hi "))
            {
                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[1] == "kylin-offline-upgrade")
                    return parts[2];
            }
        }
        return null;
    }

    #endregion

    #region Tab7: 系统优化逻辑（服务/进程/定时任务）

    // ================== 选择操作 ==================

    private void OptSelectAll() { foreach (var item in _optItems) item.IsSelected = true; RefreshOptGrid(); }
    private void OptSelectNone() { foreach (var item in _optItems) item.IsSelected = false; RefreshOptGrid(); }
    private void OptInvertSelection() { foreach (var item in _optItems) item.IsSelected = !item.IsSelected; RefreshOptGrid(); }

    private void RefreshOptGrid()
    {
        _optDataGrid.Items.Refresh();
        UpdateTab7Buttons();
    }

    // ================== 扫描 ==================

    private async void DoOptScan()
    {
        _optScanBtn.IsEnabled = false;
        SetStatus("正在扫描...", true);
        AppendTab7("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendTab7($"[{DateTime.Now:HH:mm:ss}] 开始扫描 {_optItems.Count} 项优化项...");

        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); _optScanBtn.IsEnabled = false; return; }
        DisconnectBtn.IsEnabled = false;

        // 获取系统信息
        try
        {
            var osRelease = await Task.Run(() => RunCommand(ssh, "cat /etc/os-release"));
            var osName = ExtractField(osRelease, "PRETTY_NAME");
            Dispatcher.Invoke(() => _optSystemInfoText.Text = $"目标系统: {osName}");
        }
        catch { }

        try
        {
            var username = UserBox.Text.Trim();
            if (string.IsNullOrEmpty(username)) username = "root";
            var password = PassBox.Password;

            await Task.Run(() =>
            {
                foreach (var item in _optItems)
                {
                    try
                    {
                        var output = RunCommand(ssh, item.ScanCmd);
                        item.ScanDetail = DescribeOptScan(item, output);
                        item.Status = EvaluateOptScanResult(item, output);
                        item.IsApplicable = item.Status == "可优化";
                        if (item.Status == "不适用") item.IsSelected = false;
                        Dispatcher.Invoke(() => RefreshOptGrid());
                        Dispatcher.Invoke(() => AppendTab7($"  [{item.Id}] {item.Name} → {item.Status}"));
                    }
                    catch (Exception ex)
                    {
                        item.Status = "扫描失败";
                        item.ScanDetail = ex.Message;
                        Dispatcher.Invoke(() => RefreshOptGrid());
                        Dispatcher.Invoke(() => AppendTab7($"  [{item.Id}] {item.Name} → 扫描失败: {ex.Message}"));
                    }
                }
            });

            UpdateOptSummary();
            var optimizable = _optItems.Count(i => i.Status == "可优化");
            var optimized = _optItems.Count(i => i.Status.StartsWith("已优化"));
            _optOptimizeBtn.IsEnabled = optimizable > 0 && _optItems.Any(i => i.IsSelected && i.Status == "可优化");
            _optVerifyBtn.IsEnabled = true;
            if (optimizable > 0)
                SetStatus($"可优化 — {optimizable} 项待处理", false);    // false=红色
            else if (optimized > 0)
                SetStatus($"已优化 — {optimized} 项已完成", true);        // true=绿色
            else
                SetStatus("无待优化项", true);
        }
        catch (Exception ex)
        {
            AppendTab7($"扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
        }
        finally { _optScanBtn.IsEnabled = Ssh != null && Ssh.IsConnected; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    // ================== 标签行协议解析 ==================
    // 远程命令输出统一为 key=value 标签行：active.<unit> / enabled.<unit> / file.<path> / EXIT_CODE。
    // 逐行解析替代旧版整串 Contains 匹配，杜绝 "inactive" 含子串 "active" 等误判。

    /// <summary>判断 is-enabled 标签值是否为"单元不存在"</summary>
    private static bool IsOptNotFound(string value) =>
        value.Contains("No such file or directory")
        || value.Contains("Failed to get unit file state")
        || value.Contains("not-found");

    /// <summary>单元运行中（is-active = active）</summary>
    private static bool IsOptActiveValue(string value) => value == "active";

    /// <summary>单元开机自启未关（is-enabled = enabled / enabled-runtime）</summary>
    private static bool IsOptEnabledValue(string value) => value is "enabled" or "enabled-runtime";

    /// <summary>解析远程输出中的 key=value 标签行</summary>
    private static Dictionary<string, string> ParseOptLabels(string output)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (key.Length == 0) continue;
            labels[key] = line[(eq + 1)..].Trim();
        }
        return labels;
    }

    /// <summary>提取 EXIT_CODE 标签值（无则返回 null）</summary>
    private static int? ExtractOptExitCode(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("EXIT_CODE=") && int.TryParse(line["EXIT_CODE=".Length..].Trim(), out var code))
                return code;
        }
        return null;
    }

    /// <summary>生成人类可读的扫描详情（逐项原因说明）</summary>
    private static string DescribeOptScan(OptimizationItem item, string output)
    {
        var labels = ParseOptLabels(output);
        var notes = new List<string>();
        foreach (var kv in labels)
        {
            if (kv.Key.StartsWith("active."))
                notes.Add(kv.Value == "active" ? $"{kv.Key["active.".Length..]} 运行中" : $"{kv.Key["active.".Length..]} 未运行");
            else if (kv.Key.StartsWith("enabled."))
            {
                var unit = kv.Key["enabled.".Length..];
                if (kv.Value == "masked") notes.Add($"{unit} 已mask停用");
                else if (IsOptNotFound(kv.Value)) notes.Add($"{unit} 不存在");
                else if (IsOptEnabledValue(kv.Value)) notes.Add($"{unit} 开机自启未关({kv.Value})");
                else notes.Add($"{unit} 开机自启已关({kv.Value})");
            }
            else if (kv.Key.StartsWith("file."))
            {
                var path = kv.Key["file.".Length..];
                var name = Path.GetFileName(path);
                notes.Add(kv.Value switch { "EXECUTABLE" => $"{name} 可执行", "NOT_FOUND" => $"{name} 不存在", _ => $"{name} 不可执行" });
            }
            else if (kv.Key.StartsWith("desktop."))
            {
                if (kv.Value == "ENABLED") notes.Add($"自启动项 {kv.Key["desktop.".Length..]} 未禁用");
                else if (kv.Value == "DISABLED") notes.Add($"自启动项 {kv.Key["desktop.".Length..]} 已禁用");
            }
            else if (kv.Key.StartsWith("dbus."))
            {
                if (kv.Value == "ENABLED") notes.Add($"dbus激活 {kv.Key["dbus.".Length..]} 未禁用");
                else if (kv.Value == "DISABLED") notes.Add($"dbus激活 {kv.Key["dbus.".Length..]} 已禁用");
            }
            else if (kv.Key.StartsWith("proc."))
            {
                notes.Add(kv.Value == "RUNNING" ? $"残留进程 {kv.Key["proc.".Length..]} 运行中" : $"残留进程 {kv.Key["proc.".Length..]} 未运行");
            }
        }
        return notes.Count > 0 ? string.Join("; ", notes) : output.Trim();
    }

    /// <summary>若输出中任一 enabled 标签为 masked，标记该项为 mask 停用</summary>
    private static void MarkOptMasked(OptimizationItem item, string output)
    {
        var labels = ParseOptLabels(output);
        var masked = labels.Any(kv => kv.Key.StartsWith("enabled.") && kv.Value == "masked");
        item.IsMasked = masked;
        if (masked && item.Status == "已优化") item.Status = "已优化(mask)";
    }

    private static string EvaluateOptScanResult(OptimizationItem item, string output)
    {
        var labels = ParseOptLabels(output);
        if (labels.Count == 0) return "扫描失败";

        // chmod 类 — file.<path>=EXECUTABLE / NOT_EXECUTABLE / NOT_FOUND
        if (item.Category == "chmod")
        {
            var fileLabels = labels.Where(kv => kv.Key.StartsWith("file.")).ToList();
            if (fileLabels.Count == 0) return "扫描失败";
            if (fileLabels.Any(kv => kv.Value == "EXECUTABLE")) return "可优化";
            return fileLabels.All(kv => kv.Value == "NOT_FOUND") ? "不适用" : "已优化";
        }

        // autostart 类 — 四维度：可执行位(file.*) + XDG自启动(desktop.*) + dbus激活(dbus.*) + 残留进程(proc.*)
        // 任一项未禁用即"可优化"；全部达标才"已优化"（不做不适用判定：非麒麟桌面全部不存在与已优化同表现，均无需操作）
        if (item.Category == "autostart")
        {
            var fileLabels = labels.Where(kv => kv.Key.StartsWith("file.")).ToList();
            var desktopLabels = labels.Where(kv => kv.Key.StartsWith("desktop.")).ToList();
            var dbusLabels = labels.Where(kv => kv.Key.StartsWith("dbus.")).ToList();
            var procLabels = labels.Where(kv => kv.Key.StartsWith("proc.")).ToList();
            if (fileLabels.Count == 0 && desktopLabels.Count == 0 && dbusLabels.Count == 0 && procLabels.Count == 0) return "扫描失败";
            if (fileLabels.Any(kv => kv.Value == "EXECUTABLE")) return "可优化";
            if (desktopLabels.Any(kv => kv.Value == "ENABLED")) return "可优化";
            if (dbusLabels.Any(kv => kv.Value == "ENABLED")) return "可优化";
            if (procLabels.Any(kv => kv.Value == "RUNNING")) return "可优化";
            return "已优化";
        }

        // systemctl 类 — 双维度判定：运行状态(active.*) + 开机自启(enabled.*)
        // 任一维度未达标（仍在运行 / 开机自启未关）即"可优化"；两者均达标才"已优化"
        var activeLabels = labels.Where(kv => kv.Key.StartsWith("active.")).ToList();
        var enabledLabels = labels.Where(kv => kv.Key.StartsWith("enabled.")).ToList();

        // 全部单元均不存在 → 不适用
        if (enabledLabels.Count > 0 && enabledLabels.All(kv => IsOptNotFound(kv.Value)) && !activeLabels.Any(kv => IsOptActiveValue(kv.Value)))
            return "不适用";

        if (activeLabels.Any(kv => IsOptActiveValue(kv.Value))) return "可优化";     // 仍有服务在运行
        if (enabledLabels.Any(kv => IsOptEnabledValue(kv.Value))) return "可优化";   // 仍开机自启

        return "已优化";
    }

    private void UpdateOptSummary()
    {
        var optimizable = _optItems.Count(i => i.Status == "可优化");
        var optimized = _optItems.Count(i => i.Status.StartsWith("已优化"));
        var na = _optItems.Count(i => i.Status == "不适用");
        _optInfoText.Text = $"优化项数量: {_optItems.Count} 项 (可优化: {optimizable}, 已优化: {optimized}, 不适用: {na})";
    }

    // ================== 优化 ==================

    private async void DoOptOptimize()
    {
        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }

        var selected = _optItems.Where(i => i.IsSelected && i.Status == "可优化" && !string.IsNullOrEmpty(i.OptimizeCmd)).ToList();
        if (selected.Count == 0) { SetStatus("没有选中可优化的项", false); return; }

        _optOptimizeBtn.IsEnabled = false;
        _optScanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus($"正在优化 {selected.Count} 项...", true);
        AppendTab7("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendTab7($"[{DateTime.Now:HH:mm:ss}] 开始优化 {selected.Count} 项...");

        var username = UserBox.Text.Trim();
        if (string.IsNullOrEmpty(username)) username = "root";
        var password = PassBox.Password;

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    Dispatcher.Invoke(() => { item.Status = "优化中"; RefreshOptGrid(); });
                    Dispatcher.Invoke(() => AppendTab7($"  [{item.Id}] {item.Name}..."));
                    Dispatcher.Invoke(() => AppendTab7($"    命令: {item.OptimizeCmd}"));

                    try
                    {
                        var cmd = RunCommandSudo(ssh, item.OptimizeCmd, username, password);
                        if (!string.IsNullOrWhiteSpace(cmd))
                            Dispatcher.Invoke(() => AppendTab7($"    输出: {cmd.Trim()}"));
                        var exitCode = ExtractOptExitCode(cmd);
                        if (exitCode != null && exitCode != 0)
                            Dispatcher.Invoke(() => AppendTab7($"    警告: 停用命令退出码 {exitCode}（部分单元可能不存在，以验证为准）"));
                        // 立即验证（双维度：运行状态 + 开机自启）
                        if (!string.IsNullOrEmpty(item.VerifyCmd))
                        {
                            Thread.Sleep(500); // 等待服务完全停止
                            var verifyOutput = RunCommand(ssh, item.VerifyCmd);
                            item.Status = EvaluateOptVerifyResult(item, verifyOutput);
                            MarkOptMasked(item, verifyOutput);
                            Dispatcher.Invoke(() => AppendTab7($"    验证: {verifyOutput.Trim()}"));
                            Dispatcher.Invoke(() => AppendTab7($"    → {item.Status}"));
                        }
                        else
                        {
                            item.Status = "已优化";
                            Dispatcher.Invoke(() => AppendTab7($"    → {item.Status}"));
                        }
                        item.IsOptimized = item.Status.StartsWith("已优化");
                    }
                    catch (Exception ex)
                    {
                        item.Status = "失败";
                        item.ScanDetail = ex.Message;
                        Dispatcher.Invoke(() => AppendTab7($"    → 失败: {ex.Message}"));
                    }
                    Dispatcher.Invoke(() => RefreshOptGrid());
                }
            });

            UpdateOptSummary();
            SetStatus("优化完成", true);
        }
        catch (Exception ex)
        {
            AppendTab7($"优化失败: {ex.Message}");
            SetStatus($"优化失败: {ex.Message}", false);
        }
        finally { _optOptimizeBtn.IsEnabled = true; _optScanBtn.IsEnabled = Ssh != null; _optVerifyBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private static string EvaluateOptVerifyResult(OptimizationItem item, string output)
    {
        var labels = ParseOptLabels(output);
        if (labels.Count == 0) return "验证异常";

        // chmod 类 — 任一文件仍可执行 → 失败
        if (item.Category == "chmod")
            return labels.Any(kv => kv.Key.StartsWith("file.") && kv.Value == "EXECUTABLE") ? "失败" : "已优化";

        // autostart 类 — 任一维度未禁用（可执行/自启动未禁/dbus未禁/进程仍在）→ 失败
        if (item.Category == "autostart")
            return labels.Any(kv =>
                (kv.Key.StartsWith("file.") && kv.Value == "EXECUTABLE")
                || (kv.Key.StartsWith("desktop.") && kv.Value == "ENABLED")
                || (kv.Key.StartsWith("dbus.") && kv.Value == "ENABLED")
                || (kv.Key.StartsWith("proc.") && kv.Value == "RUNNING")) ? "失败" : "已优化";

        // systemctl 类 — 双维度：仍在运行 或 仍开机自启 → 失败
        bool stillRunning = labels.Any(kv => kv.Key.StartsWith("active.") && IsOptActiveValue(kv.Value));
        bool stillEnabled = labels.Any(kv => kv.Key.StartsWith("enabled.") && IsOptEnabledValue(kv.Value));
        return stillRunning || stillEnabled ? "失败" : "已优化";
    }

    // ================== 验证 ==================

    private async void DoOptVerify()
    {
        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }

        _optVerifyBtn.IsEnabled = false;
        _optScanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus("正在验证...", true);
        AppendTab7("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendTab7($"[{DateTime.Now:HH:mm:ss}] 开始逐项验证...");

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in _optItems)
                {
                    if (item.Status == "不适用" || item.Status == "待扫描") continue;

                    try
                    {
                        // 验证 = 重新扫描：所有项 ScanCmd 与 VerifyCmd 一致，统一走扫描判定（可优化/已优化/不适用）
                        var output = RunCommand(ssh, item.ScanCmd);
                        item.ScanDetail = DescribeOptScan(item, output);
                        item.Status = EvaluateOptScanResult(item, output);
                        item.IsApplicable = item.Status == "可优化";
                        if (item.Status == "不适用") item.IsSelected = false;
                        MarkOptMasked(item, output);
                        Dispatcher.Invoke(() => AppendTab7($"  [{item.Id}] {item.Name} → {item.Status}"));
                    }
                    catch (Exception ex)
                    {
                        item.Status = "验证失败";
                        Dispatcher.Invoke(() => AppendTab7($"  [{item.Id}] {item.Name} → 验证失败: {ex.Message}"));
                    }
                    Dispatcher.Invoke(() => RefreshOptGrid());
                }
            });

            UpdateOptSummary();
            SetStatus("验证完成", true);
        }
        catch (Exception ex)
        {
            AppendTab7($"验证失败: {ex.Message}");
            SetStatus($"验证失败: {ex.Message}", false);
        }
        finally { _optVerifyBtn.IsEnabled = true; _optScanBtn.IsEnabled = Ssh != null; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    // ================== 恢复 ==================

    private async void DoOptRestore()
    {
        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }

        var selected = _optItems.Where(i => i.IsSelected && !string.IsNullOrEmpty(i.RestoreCmd)).ToList();
        if (selected.Count == 0) { SetStatus("没有选中可恢复的项", false); return; }

        _optRestoreBtn.IsEnabled = false;
        _optScanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus($"正在恢复 {selected.Count} 项...", true);
        AppendTab7("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendTab7($"[{DateTime.Now:HH:mm:ss}] 开始恢复 {selected.Count} 项...");

        var username = UserBox.Text.Trim();
        if (string.IsNullOrEmpty(username)) username = "root";
        var password = PassBox.Password;

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    Dispatcher.Invoke(() => { item.Status = "恢复中"; RefreshOptGrid(); });
                    Dispatcher.Invoke(() => AppendTab7($"  [{item.Id}] {item.Name}..."));
                    Dispatcher.Invoke(() => AppendTab7($"    命令: {item.RestoreCmd}"));

                    try
                    {
                        var cmd = RunCommandSudo(ssh, item.RestoreCmd, username, password);
                        if (!string.IsNullOrWhiteSpace(cmd))
                            Dispatcher.Invoke(() => AppendTab7($"    输出: {cmd.Trim()}"));
                        Thread.Sleep(300);
                        var scanOutput = RunCommand(ssh, item.ScanCmd);
                        item.ScanDetail = DescribeOptScan(item, scanOutput);
                        item.Status = EvaluateOptScanResult(item, scanOutput);
                        item.IsApplicable = item.Status == "可优化";
                        item.IsMasked = false;
                        Dispatcher.Invoke(() => AppendTab7($"    恢复后状态: {item.Status}"));
                    }
                    catch (Exception ex)
                    {
                        item.Status = "失败";
                        item.ScanDetail = ex.Message;
                        Dispatcher.Invoke(() => AppendTab7($"    → 失败: {ex.Message}"));
                    }
                    Dispatcher.Invoke(() => RefreshOptGrid());
                }
            });

            UpdateOptSummary();
            SetStatus("恢复完成", true);
        }
        catch (Exception ex)
        {
            AppendTab7($"恢复失败: {ex.Message}");
            SetStatus($"恢复失败: {ex.Message}", false);
        }
        finally { _optRestoreBtn.IsEnabled = Ssh != null; _optScanBtn.IsEnabled = Ssh != null; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    // ================== 状态列颜色转换器 ==================

    private class OptStatusColorConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var text = value?.ToString() ?? "";
            if (text.StartsWith("已优化")) return new SolidColorBrush(Color.FromRgb(0, 150, 0));
            return text switch
            {
                "未扫描" or "待扫描" => Brushes.Gray,
                "可优化" or "优化中" or "恢复中" => new SolidColorBrush(Color.FromRgb(220, 50, 50)),
                "失败" or "扫描失败" or "验证失败" => new SolidColorBrush(Color.FromRgb(200, 80, 80)),
                "不适用" => Brushes.Gray,
                _ => Brushes.Gray
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    #endregion

    #region Tab7: 14 项优化定义与命令构造器
    // 命令协议：扫描/验证输出统一为 key=value 标签行（active.<unit> / enabled.<unit> / file.<path> / EXIT_CODE），
    // 由 ParseOptLabels 逐行解析，杜绝整串 Contains 子串误判（如 "inactive" 含 "active"）。
    // 停用 = disable --now（立即停用）+ mask（永久屏蔽，防系统更新/依赖拉起复活）；恢复 = unmask + enable。
    // 多命令序列用 sh -c '...' 包裹，确保 ; 后续命令同样以 root 执行（sudo -S 只作用于第一个命令）。

    /// <summary>生成 is-active 标签命令（stderr 丢弃，值仅 active/inactive/failed 等）</summary>
    private static string OptAct(string unit) =>
        $"echo \"active.{unit}=$(systemctl is-active {unit} 2>/dev/null)\"";

    /// <summary>生成 is-enabled 标签命令（stderr 合并，值含 not-found 信息）</summary>
    private static string OptEn(string unit) =>
        $"echo \"enabled.{unit}=$(systemctl is-enabled {unit} 2>&1)\"";

    /// <summary>扫描命令：每个单元输出 active/enabled 两个标签</summary>
    private static string OptSysScan(params string[] units) =>
        string.Join("; ", units.SelectMany(u => new[] { OptAct(u), OptEn(u) }));

    /// <summary>停用命令：disable --now 后 mask，并捕获退出码（整体以 root 执行）</summary>
    private static string OptMaskCmd(params string[] units) =>
        $"sh -c 'systemctl disable --now {string.Join(' ', units)} 2>&1; systemctl mask {string.Join(' ', units)} 2>&1; echo EXIT_CODE=$?'";

    /// <summary>恢复命令：unmask 后 enable，并捕获退出码（整体以 root 执行）</summary>
    private static string OptUnmaskCmd(params string[] units) =>
        $"sh -c 'systemctl unmask {string.Join(' ', units)} 2>&1; systemctl enable {string.Join(' ', units)} 2>&1; echo EXIT_CODE=$?'";

    /// <summary>chmod 类扫描命令：file.<path>=EXECUTABLE / NOT_EXECUTABLE / NOT_FOUND</summary>
    private static string OptFileScan(params string[] paths) =>
        string.Join("; ", paths.Select(p =>
            $"if [ -e \"{p}\" ]; then if [ -x \"{p}\" ]; then echo \"file.{p}=EXECUTABLE\"; else echo \"file.{p}=NOT_EXECUTABLE\"; fi; else echo \"file.{p}=NOT_FOUND\"; fi"));

    /// <summary>chmod 类变更命令：mode 为 -x（停用）或 +x（恢复），整体以 root 执行</summary>
    private static string OptChmodCmd(string mode, params string[] paths) =>
        $"sh -c 'chmod {mode} {string.Join(' ', paths.Select(p => $"\"{p}\""))} 2>&1; echo EXIT_CODE=$?'";

    // ================== autostart 类（会话自启动清理）命令构造 ==================
    // 会话残留进程由 XDG 自启动(.desktop) / dbus 激活(.service) / 直接可执行位拉起，不经 systemd，mask 无法阻断。
    // 关闭手段三合一：pkill 结束残留进程 + chmod -x 去可执行位(ELF) + .desktop/.service 改名禁用(脚本类唯一有效手段)。
    // pgrep/pkill 模式用 [x]yyy 正则 + ^锚定行首，避免匹配到扫描命令自身（命令行含相同字符串）。

    /// <summary>XDG 自启动扫描：desktop.<name>=ENABLED / DISABLED / NOT_FOUND（.disabled 后缀为已禁用标记）</summary>
    private static string OptDesktopScan(params string[] names) =>
        string.Join("; ", names.Select(n =>
            $"if [ -e /etc/xdg/autostart/{n}.desktop ]; then echo \"desktop.{n}=ENABLED\"; elif [ -e /etc/xdg/autostart/{n}.desktop.disabled ]; then echo \"desktop.{n}=DISABLED\"; else echo \"desktop.{n}=NOT_FOUND\"; fi"));

    /// <summary>dbus 激活文件扫描：dbus.<name>=ENABLED / DISABLED / NOT_FOUND</summary>
    private static string OptDbusScan(params string[] names) =>
        string.Join("; ", names.Select(n =>
            $"if [ -e /usr/share/dbus-1/system-services/{n}.service ]; then echo \"dbus.{n}=ENABLED\"; elif [ -e /usr/share/dbus-1/system-services/{n}.service.disabled ]; then echo \"dbus.{n}=DISABLED\"; else echo \"dbus.{n}=NOT_FOUND\"; fi"));

    /// <summary>残留进程扫描：proc.<key>=RUNNING / STOPPED（Pattern 为 ^ 锚定正则，如 ^/usr/bin/[u]kui-bluetooth）</summary>
    private static string OptProcScan(params (string Key, string Pattern)[] procs) =>
        string.Join("; ", procs.Select(p =>
            $"echo \"proc.{p.Key}=$(pgrep -f '{p.Pattern}' >/dev/null 2>&1 && echo RUNNING || echo STOPPED)\""));

    /// <summary>autostart 类停用命令：pkill 残留进程 + chmod -x + 禁用 .desktop 与 dbus .service（整体 root 执行）</summary>
    private static string OptAutoDisableCmd(string[] files, string[] desktops, string[] dbus, (string Key, string Pattern)[] procs)
    {
        var parts = new List<string>();
        foreach (var p in procs) parts.Add($"pkill -f \"{p.Pattern}\" 2>/dev/null");
        parts.Add($"chmod -x {string.Join(' ', files.Select(f => $"\"{f}\""))} 2>/dev/null");
        parts.Add($"for d in {string.Join(' ', desktops)}; do [ -e /etc/xdg/autostart/$d.desktop ] && mv /etc/xdg/autostart/$d.desktop /etc/xdg/autostart/$d.desktop.disabled 2>/dev/null || true; done");
        parts.Add($"for s in {string.Join(' ', dbus)}; do [ -e /usr/share/dbus-1/system-services/$s.service ] && mv /usr/share/dbus-1/system-services/$s.service /usr/share/dbus-1/system-services/$s.service.disabled 2>/dev/null || true; done");
        parts.Add("echo EXIT_CODE=$?");
        return $"sh -c '{string.Join("; ", parts)}'";
    }

    /// <summary>autostart 类恢复命令：chmod +x + 还原 .desktop 与 dbus .service（重新登录后进程自动恢复）</summary>
    private static string OptAutoEnableCmd(string[] files, string[] desktops, string[] dbus)
    {
        var parts = new List<string>();
        parts.Add($"chmod +x {string.Join(' ', files.Select(f => $"\"{f}\""))} 2>/dev/null");
        parts.Add($"for d in {string.Join(' ', desktops)}; do [ -e /etc/xdg/autostart/$d.desktop.disabled ] && mv /etc/xdg/autostart/$d.desktop.disabled /etc/xdg/autostart/$d.desktop 2>/dev/null || true; done");
        parts.Add($"for s in {string.Join(' ', dbus)}; do [ -e /usr/share/dbus-1/system-services/$s.service.disabled ] && mv /usr/share/dbus-1/system-services/$s.service.disabled /usr/share/dbus-1/system-services/$s.service 2>/dev/null || true; done");
        parts.Add("echo EXIT_CODE=$?");
        return $"sh -c '{string.Join("; ", parts)}'";
    }

    /// <summary>systemctl 类优化项工厂</summary>
    private static OptimizationItem OptSysItem(int id, string name, string risk, string riskNote, params string[] units) => new()
    {
        Id = id, Name = name, RiskLevel = risk, Category = "systemctl",
        ScanCmd = OptSysScan(units),
        OptimizeCmd = OptMaskCmd(units),
        VerifyCmd = OptSysScan(units),
        RestoreCmd = OptUnmaskCmd(units),
        RiskNote = riskNote
    };

    /// <summary>chmod 类优化项工厂</summary>
    private static OptimizationItem OptChmodItem(int id, string name, string risk, string riskNote, params string[] paths) => new()
    {
        Id = id, Name = name, RiskLevel = risk, Category = "chmod",
        ScanCmd = OptFileScan(paths),
        OptimizeCmd = OptChmodCmd("-x", paths),
        VerifyCmd = OptFileScan(paths),
        RestoreCmd = OptChmodCmd("+x", paths),
        RiskNote = riskNote
    };

    /// <summary>autostart 类（会话自启动清理）优化项工厂：files=去可执行位的 ELF 二进制；
    /// desktops=禁用的 XDG 自启动项名（不含 .desktop 后缀）；dbus=禁用的 dbus 激活服务名（不含 .service 后缀）；
    /// procs=残留进程探测/清理的 ^ 锚定正则（Key 为标签名，Pattern 为 pgrep/pkill 模式）</summary>
    private static OptimizationItem OptAutoItem(int id, string name, string risk, string riskNote,
        string[] files, string[] desktops, string[] dbus, (string Key, string Pattern)[] procs) => new()
    {
        Id = id, Name = name, RiskLevel = risk, Category = "autostart",
        ScanCmd = string.Join("; ", new[] { OptFileScan(files), OptDesktopScan(desktops), OptDbusScan(dbus), OptProcScan(procs) }),
        OptimizeCmd = OptAutoDisableCmd(files, desktops, dbus, procs),
        VerifyCmd = string.Join("; ", new[] { OptFileScan(files), OptDesktopScan(desktops), OptDbusScan(dbus), OptProcScan(procs) }),
        RestoreCmd = OptAutoEnableCmd(files, desktops, dbus),
        RiskNote = riskNote
    };

    private static List<OptimizationItem> GetOptimizationItems() => new()
    {
        OptSysItem(1, "关闭蓝牙、打印机、生物识别全套服务", "低",
            "服务器场景通常不需要蓝牙/打印/生物识别",
            "bluetooth.service", "cups.service", "cups.socket", "cups.path", "cups-browsed.service", "biometric-authentication.service", "ukui-bluetooth.service"),
        OptChmodItem(2, "关闭麒麟管家后台进程", "中",
            "麒麟管家是桌面环境组件，纯服务器场景可关闭",
            "/usr/bin/kylin-os-manager-daemon",
            "/usr/share/kylin-os-manager/kylin-core-dump-monitor/kylin-core-dump-monitor.sh",
            "/usr/lib/kylin-os-manager/bin/kylin-os-manager-session-service"),
        OptSysItem(3, "关闭麒麟管家系统服务", "中",
            "麒麟管家的 systemd 服务单元，与后台进程配套关闭；纯服务器场景建议关闭",
            "kylin-core-dump-monitor.service", "kylin-process-manager-daemon.service", "com.kylin.kysdk.SyncConfig.service", "com.kylin-os-manager.service"),
        OptSysItem(4, "关闭系统激活校验服务", "低",
            "关闭后跳过系统激活校验，不影响日常使用",
            "kylin-activation-check.service"),
        OptSysItem(5, "关闭定时更新服务", "中",
            "关闭后将不再自动下载和安装系统更新，需手动维护",
            "kylin-source-update.service", "kylin-source-update-timer.service", "kylin-source-update-timer.timer", "kylin-system-updater.service", "kylin-offline-upgrade.service", "kylin-unattended-upgrades.service"),
        OptSysItem(6, "关闭安全审计日志服务(auditd)", "低",
            "关闭后 /var/log/audit/ 不再增长，但失去审计追踪能力",
            "auditd.service"),
        OptSysItem(7, "关闭 Samba 服务", "低",
            "不需要 Windows 文件共享的场景可关闭",
            "smbd.service", "nmbd.service"),
        OptSysItem(8, "关闭 pppd-dns 服务", "低",
            "PPP 拨号 DNS 更新服务，不使用拨号网络即可关闭",
            "pppd-dns.service"),
        OptSysItem(9, "关闭局域网自动发现服务(avahi-daemon)", "低",
            "mDNS/Zeroconf 服务发现协议，服务器场景通常不需要",
            "avahi-daemon.service", "avahi-daemon.socket"),
        OptChmodItem(10, "关闭天气服务(kylin-weather)", "低",
            "天气小部件为桌面附加组件，关闭后不再显示天气并释放约 0.8% 内存（进程内存排行第 3）；重启后生效",
            "/usr/bin/kylin-weather"),
        OptSysItem(11, "关闭磁盘、存储冗余监控服务(LVM)", "中",
            "未使用 LVM 逻辑卷管理的系统可关闭；使用 LVM 的系统建议保留",
            "lvm2-monitor.service", "lvm2-lvmpolld.service", "lvm2-lvmpolld.socket"),
        OptSysItem(12, "关闭多账户实时监控服务(accounts-daemon)", "中",
            "mask 后即使手动 systemctl start 也无法启动，纯服务器场景建议关闭",
            "accounts-daemon.service"),
        OptChmodItem(13, "关闭系统全局搜索后台(ukui-search)", "低",
            "UKUI 桌面环境的全局搜索功能，纯命令行服务器不需要",
            "/usr/bin/ukui-search"),
        OptAutoItem(14, "清理会话层自启动残留(蓝牙/激活/更新/打印/管家/搜索)", "中",
            "上述服务虽已 mask，但桌面会话自启动项(XDG autostart / dbus 激活)仍会拉起残留进程；此项禁用自启动入口、去可执行位并结束残留进程，彻底阻断重启复活；恢复后重新登录生效",
            new[]
            {
                "/usr/bin/ukui-bluetooth",
                "/usr/bin/kylin-activation", "/usr/bin/kylin-activation-renewalcheck", "/usr/bin/kylin-activation-prompt", "/usr/sbin/activation-daemon",
                "/usr/bin/OfflineUpgradeNotification", "/usr/bin/kylin-background-upgrade",
                "/usr/bin/kylin-printer-applet", "/usr/bin/kpr-backend",
                "/usr/bin/kylin-process-manager",
                "/usr/bin/ukui-search-service", "/usr/bin/ukui-search-app-data-service", "/usr/bin/ukui-search-service-dir-manager", "/usr/bin/ukui-search-systemdbus",
            },
            new[]
            {
                "ukui-bluetooth",
                "kylin-activation-autostart", "kylin-activation-prompt-autostart", "kylin-activation-check-deactivate", "kylin-activation-volume",
                "print-applet", "kylin-printer-applet",
                "kylin-process-manager",
                "ukui-search", "ukui-search-service", "ukui-search-app-data-service", "ukui-search-service-dir-manager",
                "UpgradeMountFailedNotify", "kylin-background-upgrade", "kylin-updatefinish-notify1", "kylin-updateresult-notify", "kylin-updateresult-notify-2303", "kpct-updatefinish-notify", "kylin-reboot-installnotify", "kylin-stepinstall-notify",
            },
            new[]
            {
                "com.kylin.UpgradeStrategies", "org.kylin.KprBackend", "com.ukui.search.qt.systemdbus",
            },
            new (string, string)[]
            {
                ("bluetooth", "^/usr/bin/[u]kui-bluetooth"),
                ("activation", "^/usr/bin/[k]ylin-activation"),
                ("activationdaemon", "^/usr/sbin/[a]ctivation-daemon"),
                ("upgradestrategies", "^/usr/bin/python3 /usr/share/[k]ylin-system-updater"),
                ("kconf2", "^[k]conf2"),
                ("offlineupgrade", "^/usr/bin/[O]fflineUpgradeNotification"),
                ("backgroundupgrade", "^/usr/bin/[k]ylin-background-upgrade"),
                ("printapplet", "^/usr/bin/python3 /usr/share/[s]ystem-config-printer"),
                ("printerapplet", "^/usr/bin/[k]ylin-printer-applet"),
                ("kprbackend", "^/usr/bin/[k]pr-backend"),
                ("procmanager", "^/usr/bin/[k]ylin-process-manager"),
                ("ukuisearch", "^/usr/bin/[u]kui-search"),
            }),
    };

    #endregion
}

// ================== 整合模型（原 KylinOsScanView / KylinOsOptimizeView）==================

public class VulnerabilityResult
{
    public bool IsVulnerable { get; set; }
    public VulnerabilityStatus Status { get; set; }
    public string? CurrentVersion { get; set; }
    public string? FixedVersion { get; set; }
    public string? PatchFile { get; set; }
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string OsSp { get; set; } = "";
    public string KernelVersion { get; set; } = "";
    public string Architecture { get; set; } = "";
    public DateTime ScanTime { get; set; }
}

public enum VulnerabilityStatus { Unknown, Vulnerable, Fixed, NotInstalled, ScanFailed }

public class OptimizationItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string RiskLevel { get; set; } = "低";
    public string Category { get; set; } = "";
    public string ScanCmd { get; set; } = "";
    public string OptimizeCmd { get; set; } = "";
    public string VerifyCmd { get; set; } = "";
    public string RestoreCmd { get; set; } = "";
    public bool IsSelected { get; set; } = true;
    public string Status { get; set; } = "待扫描";
    public bool IsApplicable { get; set; } = false;
    public bool IsOptimized { get; set; } = false;
    public bool IsMasked { get; set; } = false;
    public string RiskNote { get; set; } = "";
    public string ScanDetail { get; set; } = "";
}

public class DpkgVersion : IComparable<DpkgVersion>
{
    public int Epoch { get; set; }
    public string UpstreamVersion { get; set; } = "";
    public string DebianRevision { get; set; } = "0";

    public static DpkgVersion Parse(string version)
    {
        var v = new DpkgVersion();
        int colonIdx = version.IndexOf(':');
        if (colonIdx >= 0)
        {
            if (int.TryParse(version.Substring(0, colonIdx), out int ep)) v.Epoch = ep;
            version = version.Substring(colonIdx + 1);
        }
        int dashIdx = version.LastIndexOf('-');
        if (dashIdx >= 0)
        {
            v.DebianRevision = version.Substring(dashIdx + 1);
            v.UpstreamVersion = version.Substring(0, dashIdx);
        }
        else
        {
            v.UpstreamVersion = version;
        }
        return v;
    }

    public int CompareTo(DpkgVersion? other)
    {
        if (other == null) return 1;
        if (Epoch != other.Epoch) return Epoch.CompareTo(other.Epoch);
        int cmp = CompareSegments(UpstreamVersion, other.UpstreamVersion);
        if (cmp != 0) return cmp;
        return CompareSegments(DebianRevision, other.DebianRevision);
    }

    private static int CompareSegments(string a, string b)
    {
        var segsA = SplitSegments(a);
        var segsB = SplitSegments(b);
        int maxLen = Math.Max(segsA.Count, segsB.Count);
        for (int i = 0; i < maxLen; i++)
        {
            string sa = i < segsA.Count ? segsA[i] : "0";
            string sb = i < segsB.Count ? segsB[i] : "0";
            bool na = int.TryParse(sa, out int va);
            bool nb = int.TryParse(sb, out int vb);
            if (na && nb) { if (va != vb) return va.CompareTo(vb); }
            else { int sc = string.Compare(sa, sb, StringComparison.Ordinal); if (sc != 0) return sc; }
        }
        return 0;
    }

    private static List<string> SplitSegments(string s)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool? lastIsDigit = null;
        foreach (char c in s)
        {
            bool isDigit = char.IsDigit(c);
            if (lastIsDigit.HasValue && isDigit != lastIsDigit.Value)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            current.Append(c);
            lastIsDigit = isDigit;
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}