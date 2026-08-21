using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace ToolHelper.Views;

/// <summary>
/// SSH 工具的通用基类，提供 SSH 连接管理、UI 辅助方法、结果输出和剪贴板支持。
/// 子类只需实现 BuildToolContent() 添加特定工具内容。
/// </summary>
public abstract class SshToolBaseView : UserControl
{
    // ===== SSH 客户端 =====
    protected SshClient? Ssh { get; private set; }
    protected SftpClient? Sftp { get; private set; }

    // ===== 通用 UI 控件 =====
    protected TextBox HostBox { get; private set; } = new();
    protected TextBox PortBox { get; private set; } = new();
    protected TextBox UserBox { get; private set; } = new();
    protected PasswordBox PassBox { get; private set; } = new();
    protected Button ConnBtn { get; private set; } = new();
    protected Button DisconnectBtn { get; private set; } = new();
    protected TextBlock ConnStatus { get; private set; } = new();
    protected TextBlock StatusText { get; private set; } = new();
    protected TextBox ResultBox { get; private set; } = new();

    /// <summary>
    /// 子类可覆写为 false 以隐藏基类的共享日志区（如已有独立日志窗口）
    /// </summary>
    protected virtual bool ShowSharedResultBox => true;

    private bool _built;

    protected SshToolBaseView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildBaseUI();
        // 视图高度钉在宿主视口上（踩坑记录 #15 方案）：宿主向视图传递无限高度，
        // 结果区/填充区才能按窗口比例分配高度（窗口缩放时 SizeChanged 自动同步）
        ViewportFitHelper.FitToViewport(this, 560);
    }

    // ================== UI 构建 ==================

    /// <summary>
    /// 子类必须实现：在连接区与结果区之间插入工具特定的内容
    /// </summary>
    /// <param name="root">根 DockPanel，已包含顶部连接区和底部结果区</param>
    /// <param name="topPanel">顶部面板，子类可在其中追加按钮等</param>
    protected abstract void BuildToolContent(DockPanel root, StackPanel topPanel);

    /// <summary>
    /// 子类提供工具的标题图标
    /// </summary>
    protected abstract PackIconKind TitleIcon { get; }

    /// <summary>
    /// 子类提供工具的标题文本
    /// </summary>
    protected abstract string TitleText { get; }

    /// <summary>
    /// 子类提供工具的描述文本
    /// </summary>
    protected abstract string DescriptionText { get; }

    private void BuildBaseUI()
    {
        var root = new DockPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // 顶部面板
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 标题行
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = TitleIcon, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  " + TitleText, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        topPanel.Children.Add(titleRow);
        topPanel.Children.Add(new TextBlock { Text = DescriptionText, FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        // SSH 连接行
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow.Children.Add(MakeLabel("主机:"));
        HostBox = MakeBox("IP地址", "", 160);
        connRow.Children.Add(HostBox);
        connRow.Children.Add(MakeLabel("端口:"));
        PortBox = MakeBox("端口", "22", 60);
        connRow.Children.Add(PortBox);
        connRow.Children.Add(MakeLabel("用户:"));
        UserBox = MakeBox("用户名", "", 80);
        connRow.Children.Add(UserBox);
        connRow.Children.Add(MakeLabel("密码:"));
        PassBox = MakePasswordBox("密码", 120);
        connRow.Children.Add(PassBox);
        topPanel.Children.Add(connRow);

        // 连接按钮行
        var connBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        ConnBtn = MakeButton("连接SSH", Connect, true, PackIconKind.Login);
        connBtnRow.Children.Add(ConnBtn);
        DisconnectBtn = MakeButton("断开", Disconnect, false, PackIconKind.Logout);
        DisconnectBtn.IsEnabled = false;
        connBtnRow.Children.Add(DisconnectBtn);
        ConnStatus.Text = "● 未连接";
        ConnStatus.Foreground = Brushes.Gray;
        ConnStatus.FontSize = 13;
        ConnStatus.VerticalAlignment = VerticalAlignment.Center;
        ConnStatus.Margin = new Thickness(16, 0, 0, 0);
        connBtnRow.Children.Add(ConnStatus);
        topPanel.Children.Add(connBtnRow);

        // 先挂载顶部面板，再让子类在连接区与结果区之间插入内容
        // （子类此时可向 root 追加填充子元素，成为 DockPanel 的最后一个子元素自动填充剩余高度）
        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);
        BuildToolContent(root, topPanel);

        // 结果区（子类可通过 ShowSharedResultBox = false 隐藏；带「操作日志」标签，高度随窗口比例缩放）
        if (ShowSharedResultBox)
        {
            var resultPanel = new DockPanel { MinHeight = 160 };

            var logLabel = new TextBlock
            {
                Text = "操作日志",
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 8, 0, 4)
            };
            DockPanel.SetDock(logLabel, Dock.Top);
            resultPanel.Children.Add(logLabel);

            var resultScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 120
            };
            var resultBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 72)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                MinHeight = 120
            };
            ResultBox.AcceptsReturn = true;
            ResultBox.TextWrapping = TextWrapping.Wrap;
            ResultBox.IsReadOnly = true;
            ResultBox.FontFamily = new FontFamily("Consolas");
            ResultBox.FontSize = 12;
            ResultBox.Background = new SolidColorBrush(Color.FromRgb(40, 44, 52));
            ResultBox.Foreground = new SolidColorBrush(Color.FromRgb(171, 178, 191));
            ResultBox.BorderThickness = new Thickness(0);
            ResultBox.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ResultBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var rbStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
            if (rbStyle != null) ResultBox.Style = rbStyle;
            resultScroll.Content = ResultBox;
            resultBorder.Child = resultScroll;
            resultPanel.Children.Add(resultBorder);
            root.Children.Add(resultPanel);
        }

        Content = root;
    }

    // ================== UI 辅助方法 ==================

    protected TextBox MakeBox(string hint, string def = "", int minWidth = 120)
    {
        var tb = new TextBox { FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 13, Margin = new Thickness(0, 0, 6, 0), MinWidth = minWidth, Text = def };
        var style = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) tb.Style = style;
        HintAssist.SetHint(tb, hint);
        return tb;
    }

    protected PasswordBox MakePasswordBox(string hint, int minWidth = 120)
    {
        var pb = new PasswordBox { FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 13, Margin = new Thickness(0, 0, 6, 0), MinWidth = minWidth };
        var style = TryFindResource("MaterialDesignOutlinedPasswordBox") as Style
                    ?? TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) pb.Style = style;
        HintAssist.SetHint(pb, hint);
        return pb;
    }

    protected TextBlock MakeLabel(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

    protected Button MakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button { Content = sp, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource(primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton") as Style };
        btn.Click += (s, e) => handler();
        return btn;
    }

    protected StackPanel MakeInfoRow(string label, string value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Width = 80 });
        row.Children.Add(new TextBlock { Text = value, FontSize = 12 });
        return row;
    }

    protected void AppendResult(string text)
    {
        ResultBox.AppendText(text + "\n");
        ResultBox.ScrollToEnd();
    }

    protected void SetStatus(string msg, bool success)
    {
        StatusText.Text = msg;
        StatusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    // ================== SSH 连接管理 ==================

    protected virtual void OnConnected() { }
    protected virtual void OnDisconnected() { }

    protected async void Connect()
    {
        var host = HostBox.Text.Trim();
        var portText = PortBox.Text.Trim();
        var user = UserBox.Text.Trim();
        var pass = PassBox.Password;

        if (string.IsNullOrEmpty(host)) { SetStatus("请输入主机地址", false); return; }
        if (!int.TryParse(portText, out int port)) port = 22;
        if (string.IsNullOrEmpty(user)) user = "root";

        ConnBtn.IsEnabled = false;
        SetStatus("正在连接...", true);

        try
        {
            Disconnect();

            await Task.Run(() =>
            {
                var connInfo = new ConnectionInfo(host, port, user, new PasswordAuthenticationMethod(user, pass));
                connInfo.Timeout = TimeSpan.FromSeconds(30);

                var ssh = new SshClient(connInfo);
                ssh.Connect();

                var sftp = new SftpClient(connInfo);
                sftp.Connect();

                Ssh = ssh;
                Sftp = sftp;
            });

            ConnStatus.Text = $"● 已连接 {host}";
            ConnStatus.Foreground = Brushes.Green;
            DisconnectBtn.IsEnabled = true;
            HostBox.IsEnabled = false;
            PortBox.IsEnabled = false;
            UserBox.IsEnabled = false;
            PassBox.IsEnabled = false;
            SetStatus($"已连接到 {user}@{host}:{port}", true);
            OnConnected();
        }
        catch (Exception ex)
        {
            ConnBtn.IsEnabled = true;
            Disconnect();
            SetStatus($"连接失败: {ex.Message}", false);
        }
    }

    public void SafeDisconnect() { try { Disconnect(); } catch { } }

    protected void Disconnect()
    {
        try { Ssh?.Disconnect(); Ssh?.Dispose(); } catch { }
        try { Sftp?.Disconnect(); Sftp?.Dispose(); } catch { }
        Ssh = null;
        Sftp = null;
        ConnStatus.Text = "● 未连接";
        ConnStatus.Foreground = Brushes.Gray;
        ConnBtn.IsEnabled = true;
        DisconnectBtn.IsEnabled = false;
        HostBox.IsEnabled = true;
        PortBox.IsEnabled = true;
        UserBox.IsEnabled = true;
        PassBox.IsEnabled = true;
        OnDisconnected();
    }

    protected string RunCommand(SshClient ssh, string cmd)
    {
        if (ssh == null || !ssh.IsConnected) throw new InvalidOperationException("SSH 未连接");
        var result = ssh.RunCommand(cmd);
        return (result.Result ?? "") + (result.Error ?? "");
    }

    protected string RunCommandSudo(SshClient ssh, string cmd, string username, string password)
    {
        if (username == "root") return RunCommand(ssh, cmd);
        var escapedPassword = password.Replace("'", "'\\''");
        return RunCommand(ssh, $"echo '{escapedPassword}' | sudo -S {cmd}");
    }

    // ================== 剪贴板 ==================

    #region 剪贴板 Win32 API（解决 CLIPBRD_E_CANT_OPEN）
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 2;

    protected static bool SetClipboardTextWin32(string text)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = Encoding.Unicode.GetBytes(text + "\0");
                    var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                    if (hGlobal != IntPtr.Zero)
                    {
                        var ptr = GlobalLock(hGlobal);
                        Marshal.Copy(bytes, 0, ptr, bytes.Length);
                        GlobalUnlock(hGlobal);
                        SetClipboardData(CF_UNICODETEXT, hGlobal);
                    }
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(30);
        }
        return false;
    }
    #endregion

    protected void CopyResult()
    {
        var text = ResultBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            SetStatus("无可复制内容", false);
            return;
        }

        if (SetClipboardTextWin32(text))
        {
            SetStatus("已复制到剪贴板", true);
        }
        else
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                SetStatus("已复制到剪贴板", true);
            }
            catch
            {
                SetStatus("复制失败: 剪贴板被其他程序占用，请稍后重试", false);
            }
        }
    }

    // ================== 通用辅助 ==================

    protected static string ExtractField(string content, string field)
    {
        foreach (var line in content.Split('\n'))
        {
            if (line.StartsWith($"{field}="))
                return line.Substring(field.Length + 1).Trim('"', '\r').Trim();
        }
        return "未知";
    }
}
