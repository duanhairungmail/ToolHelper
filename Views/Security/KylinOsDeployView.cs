using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Renci.SshNet;
using Renci.SshNet.Sftp;
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

        // 顶部按钮行：定时重启 + 日志优化 + VNC Server + 复制结果（同一行，统一样式）
        var topBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
        var tab1Btn = MakeButton("定时重启", () => _tabControl.SelectedIndex = 0, false, PackIconKind.ClockOutline);
        var tab2Btn = MakeButton("日志优化", () => _tabControl.SelectedIndex = 1, false, PackIconKind.DeleteSweep);
        var tab3Btn = MakeButton("VNC Server", () => _tabControl.SelectedIndex = 2, false, PackIconKind.Monitor);
        topBtnRow.Children.Add(tab1Btn);
        topBtnRow.Children.Add(tab2Btn);
        topBtnRow.Children.Add(tab3Btn);
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

        topPanel.Children.Add(_tabControl);

        AppendTab1("点击 [连接SSH] 连接到麒麟系统，然后在对应 Tab 中执行扫描/部署/卸载/验证操作。");
    }

    private StackPanel BuildTab1Content()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };

        // 信息卡片
        _desktopUserBox = MakeBox("用户名", "", 100);
        panel.Children.Add(MakeInfoCard(new[]
        {
            "📅 执行时间：每月 1 日 00:00（cron 以 root 身份触发）",
            "🔐 仅该次重启免密登录桌面，其余所有重启必须输入密码",
            "📁 部署 5 个文件（脚本/cron/sudoers/XDG自启动）"
        },
        "👤 桌面登录用户:", _desktopUserBox));

        // DataGrid
        _tab1Dg = BuildItemGrid(_tab1Items);
        panel.Children.Add(_tab1Dg);

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
        panel.Children.Add(btnRow);

        // 独立日志区域
        _tab1Log = MakeLogBox();
        var scroll1 = new ScrollViewer { Content = _tab1Log, Height = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new Border { Child = scroll1, Margin = new Thickness(0, 4, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(60, 65, 75)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });

        return panel;
    }

    private StackPanel BuildTab2Content()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };

        // 信息卡片
        _desktopUserBox2 = MakeBox("用户名", "", 100);
        panel.Children.Add(MakeInfoCard(new[]
        {
            "📅 执行时间：每月 1 日 01:00（比重启晚 1 小时）",
            "🗑️ 删除 >365 天的 /var/log 日志和 /tmp 临时文件",
            "📋 journal 保留 30 天，超大日志(>500MB) truncate 清空"
        },
        "👤 桌面登录用户:", _desktopUserBox2));

        // DataGrid
        _tab2Dg = BuildItemGrid(_tab2Items);
        panel.Children.Add(_tab2Dg);

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
        panel.Children.Add(btnRow);

        // 独立日志区域
        _tab2Log = MakeLogBox();
        var scroll2 = new ScrollViewer { Content = _tab2Log, Height = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new Border { Child = scroll2, Margin = new Thickness(0, 4, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(60, 65, 75)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });

        return panel;
    }

    private StackPanel BuildTab3Content()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };

        // 信息卡片
        panel.Children.Add(MakeInfoCard(new[]
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
        panel.Children.Add(configRow);

        // DataGrid
        _tab3Dg = BuildItemGrid(_tab3Items);
        panel.Children.Add(_tab3Dg);

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
        panel.Children.Add(btnRow);

        // 独立日志区
        _tab3Log = MakeLogBox();
        var scroll3 = new ScrollViewer { Content = _tab3Log, Height = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new Border { Child = scroll3, Margin = new Thickness(0, 4, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(60, 65, 75)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });

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

    private Border MakeInfoCard(string[] lines, string? inputLabel = null, TextBox? inputBox = null)
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

        return new Border
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
        dg.Columns.Add(new DataGridTextColumn { Header = "远程路径", Binding = new System.Windows.Data.Binding("RemotePath"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });
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
    }

    // ================== 连接状态回调 ==================

    protected override void OnConnected()
    {
        _tab1ScanBtn.IsEnabled = true;
        _tab2ScanBtn.IsEnabled = true;
        _tab3ScanBtn.IsEnabled = true;
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
}
