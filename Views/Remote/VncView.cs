using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RemoteViewing.Vnc;
using RemoteViewing.WPF;
using MaterialDesignThemes.Wpf;

namespace ToolHelper.Views.Remote;

public class VncView : UserControl
{
    private TextBox _hostBox = new();
    private TextBox _portBox = new();
    private PasswordBox _passwordBox = new();
    private VncControl? _vncControl; // 每次连接时创建新实例
    private Button _connectBtn = new();
    private Button _disconnectBtn = new();
    private TextBlock _statusText = new();
    private TextBox _logBox = new();
    private bool _built;
    private bool _connected;
    private bool _isDisconnecting;
    private Window? _floatingWindow;

    /// <summary>视图最小高度 = 顶部配置区实际高度（标题+描述+参数行+按钮行+日志标签约 220px）：
    /// 窗口正常缩放范围内视图高度始终等于视口（顶部区贴顶固定、日志区填满剩余），
    /// 仅当窗口缩到比顶部区还矮时才由宿主滚动条兜底</summary>
    private const double MinViewHeight = 220;

    public VncView()
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
        MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);
        return tb;
    }

    private PasswordBox MakePasswordBox(string hint, int minWidth = 120)
    {
        var pb = new PasswordBox
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = minWidth
        };
        var style = TryFindResource("MaterialDesignOutlinedPasswordBox") as Style
                    ?? TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) pb.Style = style;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(pb, hint);
        return pb;
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
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Monitor, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  VNC 远程连接",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);

        topPanel.Children.Add(new TextBlock
        {
            Text = "连接到 VNC 服务器，远程查看和控制桌面。请在目标机器上启动 VNC Server 后连接。",
            FontSize = 13,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 连接参数行
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        connRow.Children.Add(MakeLabel("主机:"));
        _hostBox = MakeSingleLineBox("IP或主机名", "");
        _hostBox.MinWidth = 200;
        connRow.Children.Add(_hostBox);

        connRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeSingleLineBox("端口", "5900");
        _portBox.MinWidth = 80;
        connRow.Children.Add(_portBox);

        connRow.Children.Add(MakeLabel("密码:"));
        _passwordBox = MakePasswordBox("VNC 密码（可选）");
        _passwordBox.MinWidth = 160;
        connRow.Children.Add(_passwordBox);

        topPanel.Children.Add(connRow);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        _connectBtn = MakeButton("连接", Connect, true, PackIconKind.Login);
        _disconnectBtn = MakeButton("断开", Disconnect, false, PackIconKind.Logout);
        _disconnectBtn.IsEnabled = false;
        btnRow.Children.Add(_connectBtn);
        btnRow.Children.Add(_disconnectBtn);
        btnRow.Children.Add(MakeButton("全屏", ToggleFullscreen, false, PackIconKind.Fullscreen));
        btnRow.Children.Add(MakeButton("发送 Ctrl+Alt+Del", SendCtrlAltDel, false, PackIconKind.Keyboard));
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

    private async void Connect()
    {
        var host = _hostBox.Text.Trim();
        var portText = _portBox.Text.Trim();
        var password = _passwordBox.Password;

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

        // 禁用按钮，防止重复点击
        _connectBtn.IsEnabled = false;
        AppendLog($"正在连接 {host}:{port}...");

        try
        {
            Disconnect();
            CloseFloatingWindowInternal();

            var options = new VncClientConnectOptions();
            if (!string.IsNullOrEmpty(password))
            {
                options.Password = password.ToCharArray();
                AppendLog("已设置密码");
            }
            else
            {
                AppendLog("未设置密码（无认证模式）");
            }

            AppendLog("正在建立 TCP 连接...");
            SetStatus("正在连接，请稍候...", true);

            // 创建新的 VncControl 和独立窗口
            // 重要：VncControl 继承自 FrameworkElement，UIElement.Focusable 默认为 false，
            // 必须显式设置 Focusable=true 才能接收键盘事件（包括 Shift 修饰键）
            var vncCtrl = new VncControl
            {
                SizeMode = VncControlSizeMode.Zoom,
                AllowClipboardSharingFromServer = true,
                AllowClipboardSharingToServer = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = true           // 允许获取键盘焦点
            };

            var win = new Window
            {
                Title = $"VNC 远程桌面 - {host}:{port}",
                Width = 1024,
                Height = 768,
                MinWidth = 400,
                MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Focusable = false          // 窗口本身不抢焦点
            };
            var border = new Border { Background = Brushes.Black, Child = vncCtrl, Focusable = false };
            win.Content = border;

            // 禁用 VNC 窗口的输入法（IME）：中文输入法会把 Shift+字母等按键当作
            // ImeProcessed 事件拦截，导致按键无法转发到远程服务器（表现为无法输入大写字母）
            InputMethod.SetIsInputMethodEnabled(vncCtrl, false);
            InputMethod.SetIsInputMethodEnabled(border, false);
            InputMethod.SetIsInputMethodEnabled(win, false);

            // 键盘焦点保障：VncControl 必须持有键盘焦点才能接收 KeyDown/KeyUp 事件，
            // 否则 Shift 等修饰键不会被转发到远程服务器（表现为无法输入大写字母）
            void EnsureVncFocus()
            {
                if (!vncCtrl.IsKeyboardFocused)
                {
                    vncCtrl.Focus();
                    Keyboard.Focus(vncCtrl);
                }
            }
            // 窗口渲染完成后首次聚焦（比 Loaded 更可靠）
            win.ContentRendered += (_, _) => EnsureVncFocus();
            // 窗口重新激活时恢复焦点（从其他窗口切回时）
            win.Activated += (_, _) => EnsureVncFocus();
            // 点击 VNC 画面时确保焦点（VncControl.OnMouseDown 内部也会调 Focus()，此处双保险）
            border.PreviewMouseDown += (_, _) => EnsureVncFocus();

            // 诊断日志：记录所有键盘事件以便排查 Shift 等修饰键转发问题
            win.PreviewKeyDown += (_, e) =>
            {
                try
                {
                    var logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "vnc_log.txt");
                    System.IO.File.AppendAllText(logFile,
                        $"[{DateTime.Now:HH:mm:ss}] KeyDown: Key={e.Key}, SystemKey={e.SystemKey}, " +
                        $"IsFocused={vncCtrl.IsKeyboardFocused}, Modifiers={Keyboard.Modifiers}\n", Encoding.UTF8);
                }
                catch { }
            };
            win.Closing += (s, e) =>
            {
                // 直接关闭 VNC 客户端，不调用 Disconnect()（避免循环关闭窗口）
                _floatingWindow = null;
                _vncControl = null;
                if (_connected)
                {
                    _isDisconnecting = true;
                    try
                    {
                        vncCtrl.Client?.Close();
                        AppendLog("已断开连接");
                    }
                    catch { }
                    _connected = false;
                    _connectBtn.IsEnabled = true;
                    _disconnectBtn.IsEnabled = false;
                    _hostBox.IsEnabled = true;
                    _portBox.IsEnabled = true;
                    _passwordBox.IsEnabled = true;
                    _isDisconnecting = false;
                }
            };

            _vncControl = vncCtrl;
            _floatingWindow = win;

            // 在后台线程执行连接
            await Task.Run(async () =>
            {
                var tcp = new TcpClient();
                tcp.ReceiveTimeout = 30000;
                tcp.SendTimeout = 30000;
                await tcp.ConnectAsync(host, port);
                AppendLogThreadSafe($"TCP 连接成功: {host}:{port}");

                var networkStream = tcp.GetStream();
                // 包装器日志仅写入文件用于诊断，不显示在 UI
                var wrapper = new Rfb37StreamWrapper(networkStream, (msg) =>
                {
                    try
                    {
                        var logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "vnc_log.txt");
                        System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n", Encoding.UTF8);
                    }
                    catch { }
                });
                vncCtrl.Client.Connect(wrapper, options);
            });

            _connected = true;
            _disconnectBtn.IsEnabled = true;
            _hostBox.IsEnabled = false;
            _portBox.IsEnabled = false;
            _passwordBox.IsEnabled = false;
            SetStatus($"已连接到 {host}:{port}", true);
            AppendLog($"连接成功: {host}:{port}");

            // 显示独立窗口
            Dispatcher.Invoke(() =>
            {
                win.Show();
                win.Activate();
            });
        }
        catch (Exception ex)
        {
            _connectBtn.IsEnabled = true;
            // 连接失败时关闭已打开的独立窗口
            CloseFloatingWindowInternal();
            _vncControl = null;
            SetStatus($"连接失败: {ex.Message}", false);
            AppendLog($"连接失败!");
            AppendLog($"异常类型: {ex.GetType().Name}");
            AppendLog($"错误信息: {ex.Message}");
            if (ex.InnerException != null)
            {
                AppendLog($"内部异常: {ex.InnerException.GetType().Name}");
                AppendLog($"内部错误: {ex.InnerException.Message}");
            }
            AppendLog("---");
            AppendLog("排查建议:");
            AppendLog("1. 确认目标机器已启动 VNC Server");
            AppendLog("2. 确认 IP 地址和端口正确（VNC 默认端口 5900，显示器 :0 对应 5900，:1 对应 5901）");
            AppendLog("3. 确认防火墙未阻止该端口");
            AppendLog("4. 如果 VNC Server 使用加密认证，本工具可能不支持，请尝试无密码连接");
            AppendLog("5. 确认 VNC Server 支持的协议版本（RFB 3.3/3.7/3.8）");
        }
    }

    public void SafeDisconnect() { try { Disconnect(); } catch { } }

    private void Disconnect()
    {
        if (_isDisconnecting) return;
        _isDisconnecting = true;
        try
        {
            if (_vncControl?.Client != null && _connected)
            {
                _vncControl.Client.Close();
                AppendLog("已断开连接");
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
            _hostBox.IsEnabled = true;
            _portBox.IsEnabled = true;
            _passwordBox.IsEnabled = true;

            CloseFloatingWindowInternal();
            _vncControl = null;
            _isDisconnecting = false;
        }
    }

    /// <summary>关闭独立窗口并将 VncControl 放回原始位置（不触发 Disconnect）</summary>
    private void CloseFloatingWindowInternal()
    {
        if (_floatingWindow != null)
        {
            var win = _floatingWindow;
            _floatingWindow = null; // 先置空，防止 Closing 事件中再次触发
            win.Close();
        }
    }

    private void ToggleFullscreen()
    {
        if (_vncControl == null) return;
        if (_vncControl.SizeMode == VncControlSizeMode.Zoom)
            _vncControl.SizeMode = VncControlSizeMode.Stretch;
        else
            _vncControl.SizeMode = VncControlSizeMode.Zoom;
        SetStatus($"缩放模式: {_vncControl.SizeMode}", true);
    }

    private void SendCtrlAltDel()
    {
        if (!_connected || _vncControl?.Client == null)
        {
            SetStatus("未连接，无法发送按键", false);
            return;
        }

        try
        {
            var client = _vncControl.Client;
            // Ctrl+Alt+Del key codes (keysym values)
            client.SendKeyEvent(0xFFE3, true);  // Ctrl down
            client.SendKeyEvent(0xFFE9, true);  // Alt down
            client.SendKeyEvent(0xFFFF, true);  // Delete down
            client.SendKeyEvent(0xFFFF, false); // Delete up
            client.SendKeyEvent(0xFFE9, false); // Alt up
            client.SendKeyEvent(0xFFE3, false); // Ctrl up
            SetStatus("已发送 Ctrl+Alt+Del", true);
        }
        catch (Exception ex)
        {
            SetStatus($"发送失败: {ex.Message}", false);
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

        // 同时写入日志文件便于诊断
        try
        {
            var logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "vnc_log.txt");
            System.IO.File.AppendAllText(logFile, line + "\n", Encoding.UTF8);
        }
        catch { }
    }

    private void AppendLogThreadSafe(string text)
    {
        Dispatcher.BeginInvoke(() => AppendLog(text));
    }

}

/// <summary>
/// RFB 3.7 协议包装器：用状态机拦截 Lemutec.RemoteViewing 库的协议协商，
/// 强制 RFB 3.7 + None 认证，解决与 RFB 3.7 VNC Server 的兼容性问题。
/// </summary>
class Rfb37StreamWrapper : Stream
{
    private enum Phase
    {
        ReadServerVersion,     // 读取服务器版本 (12字节)
        WriteClientVersion,    // 写入客户端版本 (12字节)
        ReadSecurityCount,     // 读取安全类型数量 (1字节)
        ReadSecurityTypes,     // 读取安全类型列表 (N字节)
        WriteSecuritySelect,   // 写入安全类型选择 (1字节)
        ReadSecurityResult,    // 读取/注入 SecurityResult (4字节)
        Connected              // 透传所有数据
    }

    private readonly Stream _inner;
    private readonly Action<string> _log;
    private Phase _phase = Phase.ReadServerVersion;
    private bool _serverIs37;
    private int _connectedReadCount;  // Connected阶段读取计数
    private int _connectedWriteCount; // Connected阶段写入计数

    // 内部缓冲区：处理分片/合并读取
    private byte[] _internalBuf = Array.Empty<byte>();
    private int _internalPos; // 缓冲区已消费位置

    // 安全类型协商
    private int _pendingTypeBytes;
    private byte[] _securityTypes = Array.Empty<byte>();
    private byte[] _partialTypes = Array.Empty<byte>(); // 已收集的部分类型字节

    // SecurityResult 注入跟踪
    private int _securityResultBytes;

    // 服务器版本累积读取
    private byte[] _versionBuf = new byte[12];
    private int _versionPos;

    public Rfb37StreamWrapper(Stream inner, Action<string> log)
    {
        _inner = inner;
        _log = log;
    }

    /// <summary>将多余数据放入内部缓冲区，供下次 Read 时优先消费</summary>
    private void BufferExtra(byte[] buffer, int offset, int startPos, int totalRead)
    {
        if (totalRead > startPos)
        {
            int extraLen = totalRead - startPos;
            _internalBuf = new byte[extraLen];
            Array.Copy(buffer, offset + startPos, _internalBuf, 0, extraLen);
            _internalPos = 0;
        }
    }

    /// <summary>优先从内部缓冲区读取，返回实际读取字节数；缓冲区为空时返回0</summary>
    private int TryReadFromBuffer(byte[] buffer, int offset, int count)
    {
        if (_internalBuf.Length == 0 || _internalPos >= _internalBuf.Length)
            return 0;
        int available = _internalBuf.Length - _internalPos;
        int toCopy = Math.Min(count, available);
        Array.Copy(_internalBuf, _internalPos, buffer, offset, toCopy);
        _internalPos += toCopy;
        if (_internalPos >= _internalBuf.Length)
            _internalBuf = Array.Empty<byte>(); // 缓冲区已消费完毕
        return toCopy;
    }

    private void InterceptWrite(byte[] buffer, int offset, int count)
    {
        if (_phase == Phase.WriteClientVersion && count == 12)
        {
            var version = Encoding.ASCII.GetString(buffer, offset, count);
            _log($"W 客户端版本: {version.Trim()}");
            // 始终发送 RFB 3.7 给服务器（兼容服务器 3.7）
            if (!version.Contains("003.007"))
            {
                Encoding.ASCII.GetBytes("RFB 003.007\n").CopyTo(buffer, offset);
                _log("W 降级为 RFB 3.7");
            }
            _phase = Phase.ReadSecurityCount;
        }
        else if (_phase == Phase.WriteSecuritySelect && count == 1)
        {
            int type = buffer[offset];
            _log($"W 库选择安全类型: {type}");
            // 强制 None 认证
            if (type != 1 && Array.IndexOf(_securityTypes, (byte)1) >= 0)
            {
                _log($"W 拦截! 改为 None(1)（库原选{type}）");
                buffer[offset] = 1;
                type = 1;
            }
            // RFB 3.7 + None: 服务器不发送 SecurityResult
            // 库内部行为待确认，先尝试跳过 SecurityResult
            _log($"W RFB 3.7 None认证 → 跳过 SecurityResult，进入透传");
            _phase = Phase.Connected;
        }
    }

    private int InterceptRead(byte[] buffer, int offset, int n)
    {
        if (n <= 0) return n;

        switch (_phase)
        {
            case Phase.ReadServerVersion:
            {
                // 累积读取服务器版本（处理分片）
                int toCopy = Math.Min(n, 12 - _versionPos);
                Array.Copy(buffer, offset, _versionBuf, _versionPos, toCopy);
                _versionPos += toCopy;

                if (_versionPos < 12)
                {
                    // 版本信息未读完，继续等待
                    _log($"R 服务器版本片段: 已收{_versionPos}/12字节");
                    return n;
                }

                // 版本信息完整
                var version = Encoding.ASCII.GetString(_versionBuf, 0, 12);
                _log($"R 服务器版本: {version.Trim()}");
                _serverIs37 = version.Contains("003.007");
                _phase = Phase.WriteClientVersion;

                // 如果本次读取超过了12字节（理论上不太可能），缓存多余数据
                if (n > toCopy)
                    BufferExtra(buffer, offset, toCopy, n);

                // 将完整版本写入调用方缓冲区
                Array.Copy(_versionBuf, 0, buffer, offset, 12);
                return n;
            }

            case Phase.ReadSecurityCount:
            {
                int numTypes = buffer[offset];
                _log($"R 安全类型数量: {numTypes}");
                _pendingTypeBytes = numTypes;
                _partialTypes = Array.Empty<byte>();

                // 本次读取中可能包含部分或全部类型字节
                int extraBytes = n - 1; // count 字节之后的额外数据
                if (extraBytes > 0)
                {
                    int typesInThisRead = Math.Min(extraBytes, numTypes);
                    _partialTypes = new byte[typesInThisRead];
                    Array.Copy(buffer, offset + 1, _partialTypes, 0, typesInThisRead);
                    _log($"R 本次读取中包含{typesInThisRead}个类型字节");

                    // 缓存超出 count+types 范围的多余数据
                    if (n > 1 + typesInThisRead)
                        BufferExtra(buffer, offset, 1 + typesInThisRead, n);
                }

                if (_partialTypes.Length >= numTypes)
                {
                    // 所有类型字节已收集
                    _securityTypes = _partialTypes;
                    _log($"R 安全类型: [{string.Join(",", _securityTypes)}]");
                    _phase = Phase.WriteSecuritySelect;
                }
                else
                {
                    // 还有剩余类型字节待读取
                    _pendingTypeBytes = numTypes - _partialTypes.Length;
                    _phase = Phase.ReadSecurityTypes;
                }
                return n;
            }

            case Phase.ReadSecurityTypes:
            {
                int needed = _pendingTypeBytes;
                int collected = Math.Min(n, needed);
                var newTypes = new byte[_partialTypes.Length + collected];
                Array.Copy(_partialTypes, 0, newTypes, 0, _partialTypes.Length);
                Array.Copy(buffer, offset, newTypes, _partialTypes.Length, collected);
                _partialTypes = newTypes;
                _pendingTypeBytes -= collected;
                _log($"R 收集类型字节: {collected}个, 剩余{_pendingTypeBytes}");

                // 缓存多余数据
                if (n > collected)
                    BufferExtra(buffer, offset, collected, n);

                if (_pendingTypeBytes <= 0)
                {
                    _securityTypes = _partialTypes;
                    _log($"R 安全类型: [{string.Join(",", _securityTypes)}]");
                    _phase = Phase.WriteSecuritySelect;
                }
                return n;
            }

            case Phase.ReadSecurityResult:
            {
                _log($"R 注入 SecurityResult=0 (请求{n}字节, 已注入{_securityResultBytes}字节)");
                // 将所有返回的字节清零（SecurityResult 可能分多次读取）
                for (int i = 0; i < n; i++)
                    buffer[offset + i] = 0;
                _securityResultBytes += n;
                if (_securityResultBytes >= 4)
                    _phase = Phase.Connected;
                return n;
            }

            case Phase.Connected:
            {
                _connectedReadCount++;
                if (_connectedReadCount <= 3)
                    _log($"R[Connected#{_connectedReadCount}] 读取{n}字节");
                return n;
            }

            default:
                _log($"R 未知阶段 {_phase}，进入透传模式");
                _phase = Phase.Connected;
                return n;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        InterceptWrite(buffer, offset, count);
        if (_phase == Phase.Connected)
        {
            _connectedWriteCount++;
            if (_connectedWriteCount <= 3)
                _log($"W[Connected#{_connectedWriteCount}] 写入{count}字节");
            LogClientMessages(buffer, offset, count);
        }
        _inner.Write(buffer, offset, count);
    }

    /// <summary>
    /// 记录 Connected 阶段客户端发送的 RFB 消息，重点记录 KeyEvent（消息类型 4）：
    /// 格式为 [type:1][down:1][pad:2][keysym:4 大端]，共 8 字节。
    /// 用于诊断 Shift 等修饰键是否真正发送到服务器。
    /// </summary>
    private void LogClientMessages(byte[] buffer, int offset, int count)
    {
        int pos = 0;
        while (pos < count)
        {
            byte msgType = buffer[offset + pos];
            if (msgType == 4 && count - pos >= 8) // KeyEvent
            {
                bool down = buffer[offset + pos + 1] != 0;
                int keysym = (buffer[offset + pos + 4] << 24) | (buffer[offset + pos + 5] << 16)
                           | (buffer[offset + pos + 6] << 8) | buffer[offset + pos + 7];
                _log($"W KeyEvent: keysym=0x{keysym:X} ({keysym}), down={down}");
                pos += 8;
            }
            else if (msgType == 5 && count - pos >= 6) // PointerEvent
            {
                pos += 6;
            }
            else if (msgType == 3 && count - pos >= 10) // FramebufferUpdateRequest
            {
                pos += 10;
            }
            else
            {
                break; // 未知/不完整消息，停止解析
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // 优先从内部缓冲区读取
        int buffered = TryReadFromBuffer(buffer, offset, count);
        if (buffered > 0)
            return InterceptRead(buffer, offset, buffered);
        var n = _inner.Read(buffer, offset, count);
        return InterceptRead(buffer, offset, n);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        InterceptWrite(buffer, offset, count);
        if (_phase == Phase.Connected)
        {
            _connectedWriteCount++;
            if (_connectedWriteCount <= 3)
                _log($"W[Connected#{_connectedWriteCount}] 异步写入{count}字节");
            LogClientMessages(buffer, offset, count);
        }
        await _inner.WriteAsync(buffer, offset, count, ct);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        // 优先从内部缓冲区读取
        int buffered = TryReadFromBuffer(buffer, offset, count);
        if (buffered > 0)
            return InterceptRead(buffer, offset, buffered);
        var n = await _inner.ReadAsync(buffer, offset, count, ct);
        return InterceptRead(buffer, offset, n);
    }

    // Stream 基本实现
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void WriteByte(byte value) { var b = new[] { value }; Write(b, 0, 1); }
    public override int ReadByte() { var b = new byte[1]; return Read(b, 0, 1) > 0 ? b[0] : -1; }
    public override bool CanTimeout => _inner.CanTimeout;
    public override int ReadTimeout { get => _inner.ReadTimeout; set => _inner.ReadTimeout = value; }
    public override int WriteTimeout { get => _inner.WriteTimeout; set => _inner.WriteTimeout = value; }
    protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
}
