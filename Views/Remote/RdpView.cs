using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace ToolHelper.Views.Remote;

public class RdpView : UserControl
{
    private TextBox _hostBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private Button _connectBtn = new();
    private Button _disconnectBtn = new();
    private TextBlock _statusText = new();
    private TextBox _logBox = new();
    private bool _built;
    private bool _connected;
    private Process? _rdpProcess;
    private string _rdpFilePath = "";

    /// <summary>视图最小高度 = 顶部配置区实际高度（标题+描述+参数行+按钮行+日志标签约 220px）：
    /// 窗口正常缩放范围内视图高度始终等于视口（顶部区贴顶固定、日志区填满剩余），
    /// 仅当窗口缩到比顶部区还矮时才由宿主滚动条兜底</summary>
    private const double MinViewHeight = 220;

    public RdpView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        // 宿主内容区包裹了 ScrollViewer（无限高度约束），日志框会随日志内容无限拉长：
        // 日志一多，最新连接信息就被顶出可视区且内部滚动条不出现，故把视图高度钉在宿主视口高度上，日志在框内滚动
        ViewportFitHelper.FitToViewport(this, MinViewHeight);
    }

    private TextBox MakeSingleLineBox(string hint, string defaultText = "")
    {
        var tb = new TextBox
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 120,
            Text = defaultText
        };
        var style = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) tb.Style = style;
        HintAssist.SetHint(tb, hint);
        return tb;
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private Button MakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var styleName = primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton";
        var btn = new Button
        {
            Content = sp,
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource(styleName) as Style
        };
        btn.Click += (s, e) => handler();
        return btn;
    }

    private void BuildUI()
    {
        var root = new DockPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // 顶部区域
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 标题
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.MicrosoftWindows, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  Windows 远程桌面",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);

        topPanel.Children.Add(new TextBlock
        {
            Text = "通过 RDP 协议连接到 Windows 远程桌面。请确认目标机器已启用远程桌面（默认端口 3389）。",
            FontSize = 13,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 连接参数行
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        connRow.Children.Add(MakeLabel("主机:"));
        _hostBox = MakeSingleLineBox("IP 或主机名", "");
        _hostBox.MinWidth = 200;
        connRow.Children.Add(_hostBox);

        connRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeSingleLineBox("端口", "3389");
        _portBox.MinWidth = 80;
        connRow.Children.Add(_portBox);

        connRow.Children.Add(MakeLabel("用户名:"));
        _userBox = MakeSingleLineBox("Windows 用户名（可选）");
        _userBox.MinWidth = 160;
        connRow.Children.Add(_userBox);

        topPanel.Children.Add(connRow);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        _connectBtn = MakeButton("连接", Connect, true, PackIconKind.Login);
        _disconnectBtn = MakeButton("断开", Disconnect, false, PackIconKind.Logout);
        _disconnectBtn.IsEnabled = false;
        btnRow.Children.Add(_connectBtn);
        btnRow.Children.Add(_disconnectBtn);
        btnRow.Children.Add(MakeButton("日志清理", () => { _logBox.Clear(); }, false, PackIconKind.Eraser));

        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnRow.Children.Add(_statusText);

        topPanel.Children.Add(btnRow);

        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        var mainPanel = new DockPanel();

        // 日志区
        var logLabel = new TextBlock { Text = "连接日志", FontSize = 12, Margin = new Thickness(0, 8, 0, 4) };
        DockPanel.SetDock(logLabel, Dock.Top);
        mainPanel.Children.Add(logLabel);

        _logBox.AcceptsReturn = true;
        _logBox.TextWrapping = TextWrapping.Wrap;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.IsReadOnly = true;
        _logBox.FontFamily = new FontFamily("Microsoft YaHei");
        _logBox.FontSize = 12;
        _logBox.MinHeight = 80;
        _logBox.VerticalContentAlignment = VerticalAlignment.Top;
        // 日志框尺寸变化（窗口最大化/恢复/拖动）时重新滚到底部：TextBox 尺寸变化会保持旧滚动位置，
        // 否则窗口放大后最新日志不在视野、缩小后最新日志被推出视野
        _logBox.SizeChanged += (_, _) => _logBox.ScrollToEnd();
        var logStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (logStyle != null) _logBox.Style = logStyle;
        mainPanel.Children.Add(_logBox);

        root.Children.Add(mainPanel);
        Content = root;
    }

    private void Connect()
    {
        var host = _hostBox.Text.Trim();
        var portText = _portBox.Text.Trim();
        var username = _userBox.Text.Trim();

        if (string.IsNullOrEmpty(host))
        {
            SetStatus("请输入主机地址", false);
            return;
        }

        if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
        {
            SetStatus("端口无效，请输入 1-65535 的数字", false);
            return;
        }

        // 断开已有连接
        Disconnect();

        _connectBtn.IsEnabled = false;
        AppendLog($"正在准备连接 {host}:{port}...");

        try
        {
            // 生成 .rdp 文件
            var rdpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(rdpDir))
                Directory.CreateDirectory(rdpDir);

            _rdpFilePath = Path.Combine(rdpDir, $"rdp_{host}_{port}.rdp");
            var rdpContent = new StringBuilder();
            rdpContent.AppendLine($"full address:s:{host}:{port}");
            rdpContent.AppendLine($"screen mode id:i:2");           // 全屏
            rdpContent.AppendLine($"use multimon:i:0");
            rdpContent.AppendLine($"desktopwidth:i:1920");
            rdpContent.AppendLine($"desktopheight:i:1080");
            rdpContent.AppendLine($"session bpp:i:32");
            rdpContent.AppendLine($"winposstr:s:0,1,0,0,1920,1080");
            rdpContent.AppendLine($"compression:i:1");
            rdpContent.AppendLine($"keyboardhook:i:2");            // 全屏时应用快捷键
            rdpContent.AppendLine($"audiocapturemode:i:0");
            rdpContent.AppendLine($"videoplaybackmode:i:1");
            rdpContent.AppendLine($"networkautodetect:i:1");
            rdpContent.AppendLine($"bandwidthautodetect:i:1");
            rdpContent.AppendLine($"displayconnectionbar:i:1");
            rdpContent.AppendLine($"enableworkspacereconnect:i:0");
            rdpContent.AppendLine($"disable wallpaper:i:0");
            rdpContent.AppendLine($"allow font smoothing:i:1");
            rdpContent.AppendLine($"allow desktop composition:i:1");
            rdpContent.AppendLine($"disable full window drag:i:1");
            rdpContent.AppendLine($"disable menu anims:i:1");
            rdpContent.AppendLine($"disable cursor setting:i:0");
            rdpContent.AppendLine($"disable themes:i:0");
            rdpContent.AppendLine($"redirectclipboard:i:1");        // 剪贴板共享
            rdpContent.AppendLine($"redirectprinters:i:0");
            rdpContent.AppendLine($"redirectcomports:i:0");
            rdpContent.AppendLine($"redirectsmartcards:i:0");
            rdpContent.AppendLine($"drivestoredirect:s:");
            rdpContent.AppendLine($"audiomode:i:0");
            rdpContent.AppendLine($"prompt for credentials:i:1");   // 连接时弹出凭据窗口
            rdpContent.AppendLine($"negotiate security layer:i:1");
            rdpContent.AppendLine($"remoteapplicationmode:i:0");
            rdpContent.AppendLine($"alternate shell:s:");
            rdpContent.AppendLine($"shell working directory:s:");
            rdpContent.AppendLine($"gatewayhostname:s:");
            rdpContent.AppendLine($"gatewayusagemethod:i:4");
            rdpContent.AppendLine($"gatewaycredentialssource:i:4");
            rdpContent.AppendLine($"gatewayprofileusagemethod:i:0");

            if (!string.IsNullOrEmpty(username))
            {
                rdpContent.AppendLine($"username:s:{username}");
                AppendLog($"已设置用户名: {username}");
            }
            else
            {
                AppendLog("未设置用户名（连接时将弹出凭据窗口）");
            }

            File.WriteAllText(_rdpFilePath, rdpContent.ToString(), Encoding.UTF8);
            AppendLog($"已生成 RDP 配置文件: {_rdpFilePath}");

            // 启动 mstsc.exe
            AppendLog("正在启动 Windows 远程桌面客户端 (mstsc.exe)...");
            SetStatus("正在启动远程桌面...", true);

            var psi = new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"\"{_rdpFilePath}\"",
                UseShellExecute = true
            };

            _rdpProcess = Process.Start(psi);
            if (_rdpProcess == null)
            {
                throw new Exception("无法启动 mstsc.exe");
            }

            _connected = true;
            _disconnectBtn.IsEnabled = true;
            SetStatus($"已连接到 {host}:{port}", true);
            AppendLog($"远程桌面客户端已启动，PID: {_rdpProcess.Id}");
            AppendLog($"连接目标: {host}:{port}");

            // 后台监听进程退出
            Task.Run(async () =>
            {
                try
                {
                    await Task.Run(() => _rdpProcess.WaitForExit());
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (_connected)
                        {
                            _connected = false;
                            _connectBtn.IsEnabled = true;
                            _disconnectBtn.IsEnabled = false;
                            SetStatus("远程桌面已断开", false);
                            AppendLog("远程桌面客户端已关闭");
                        }
                    });
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            _connectBtn.IsEnabled = true;
            SetStatus($"连接失败: {ex.Message}", false);
            AppendLog($"连接失败: {ex.Message}");
            AppendLog("---");
            AppendLog("排查建议:");
            AppendLog("1. 确认目标机器已启用远程桌面（系统属性 → 远程 → 允许远程连接）");
            AppendLog("2. 确认 IP 地址和端口正确（RDP 默认端口 3389）");
            AppendLog("3. 确认防火墙未阻止 3389 端口");
            AppendLog("4. 确认目标机器在同一个局域网或网络可达");
            AppendLog("5. 确认 mstsc.exe 存在于系统 PATH 中");
        }
    }

    public void SafeDisconnect() { try { Disconnect(); } catch { } }

    private void Disconnect()
    {
        try
        {
            if (_rdpProcess != null && !_rdpProcess.HasExited)
            {
                _rdpProcess.Kill();
                AppendLog("已强制关闭远程桌面客户端");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"断开连接时出错: {ex.Message}");
        }
        finally
        {
            _connected = false;
            _connectBtn.IsEnabled = true;
            _disconnectBtn.IsEnabled = false;
            _rdpProcess = null;

            // 清理临时 .rdp 文件
            try
            {
                if (!string.IsNullOrEmpty(_rdpFilePath) && File.Exists(_rdpFilePath))
                    File.Delete(_rdpFilePath);
            }
            catch { }
        }
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    private void AppendLog(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        _logBox.AppendText(line + "\n");
        // caret 移到末尾并滚动到底：CaretIndex 赋值会触发视图跟随，比单独 ScrollToEnd 更可靠
        _logBox.CaretIndex = _logBox.Text.Length;
        _logBox.ScrollToEnd();
        // 文本追加后布局尚未完成时 ScrollToEnd 可能停在旧位置，
        // Background 优先级（布局之后）再滚一次，确保始终显示最新日志
        Dispatcher.BeginInvoke(() => _logBox.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

        // 同时写入日志文件
        try
        {
            var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "rdp_log.txt");
            File.AppendAllText(logFile, line + "\n", Encoding.UTF8);
        }
        catch { }
    }
}
