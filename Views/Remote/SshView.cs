using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Renci.SshNet;

namespace ToolHelper.Views.Remote;

public class SshView : UserControl
{
    private TextBox _hostBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private PasswordBox _passBox = new();
    private TerminalBox _terminalBox = new();
    private TextBox _commandBox = new();
    private Button _connectBtn = new();
    private Button _disconnectBtn = new();
    private TextBlock _statusText = new();
    private SshClient? _sshClient;
    private ShellStream? _shellStream;
    private CancellationTokenSource? _readCts;
    private bool _built;
    private Window? _floatingWindow;
    private Border? _termBorder;

    /// <summary>视图最小高度 = 顶部配置区约 180px + 底部命令输入行约 50px + 终端区约 150px：
    /// 窗口正常缩放范围内视图高度始终等于视口（顶部区贴顶、命令输入行贴底、终端区填满中间），
    /// 仅当窗口缩到比此值还矮时才由宿主滚动条兜底</summary>
    private const double MinViewHeight = 380;

    public SshView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        // 宿主内容区包裹了 ScrollViewer（无限高度约束），终端区会随输出内容无限拉长：
        // 把视图高度钉在宿主视口高度上，终端输出超出部分在 TerminalBox 内部滚动，顶部区/命令输入行固定不动
        ViewportFitHelper.FitToViewport(this, MinViewHeight);
        // 终端框尺寸变化（窗口缩放）时滚到底部，保持最新输出可见
        _terminalBox.SizeChanged += (_, _) => _terminalBox.ScrollToEnd();
    }

    private TextBox MakeBox(string hint, string defaultText = "", int minWidth = 120)
    {
        var tb = new TextBox
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = minWidth,
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

    private Button MakeButton(string text, Action handler, bool primary = false)
    {
        var styleName = primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton";
        var btn = new Button
        {
            Content = text,
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

        // 顶部面板
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        topPanel.Children.Add(new TextBlock
        {
            Text = "SSH 终端",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = titleBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });

        topPanel.Children.Add(new TextBlock
        {
            Text = "通过 SSH 连接到远程服务器，执行命令并查看输出。",
            FontSize = 13,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 连接参数行
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow.Children.Add(MakeLabel("主机:"));
        _hostBox = MakeBox("IP或主机名", "", 200);
        connRow.Children.Add(_hostBox);
        connRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeBox("端口", "22", 80);
        connRow.Children.Add(_portBox);
        connRow.Children.Add(MakeLabel("用户名:"));
        _userBox = MakeBox("用户名", "", 140);
        connRow.Children.Add(_userBox);
        connRow.Children.Add(MakeLabel("密码:"));
        _passBox = MakePasswordBox("密码");
        connRow.Children.Add(_passBox);
        topPanel.Children.Add(connRow);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        _connectBtn = MakeButton("连接", Connect, true);
        _disconnectBtn = MakeButton("断开", Disconnect);
        _disconnectBtn.IsEnabled = false;
        btnRow.Children.Add(_connectBtn);
        btnRow.Children.Add(_disconnectBtn);
        btnRow.Children.Add(MakeButton("清屏", ClearTerminal));
        btnRow.Children.Add(MakeButton("独立窗口", OpenFloatingWindow));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnRow.Children.Add(_statusText);
        topPanel.Children.Add(btnRow);

        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // 底部命令输入行
        var cmdPanel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var sendBtn = new Button
        {
            Content = "发送",
            Margin = new Thickness(8, 0, 0, 0),
            Style = TryFindResource("MaterialDesignRaisedButton") as Style,
            MinWidth = 60
        };
        sendBtn.Click += (s, e) => SendCommand();
        DockPanel.SetDock(sendBtn, Dock.Right);
        cmdPanel.Children.Add(sendBtn);

        _commandBox.FontFamily = new FontFamily("Microsoft YaHei");
        _commandBox.FontSize = 13;
        var cmdStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (cmdStyle != null) _commandBox.Style = cmdStyle;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_commandBox, "输入命令后按 Enter 发送...");
        _commandBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { SendCommand(); e.Handled = true; }
        };
        cmdPanel.Children.Add(_commandBox);

        DockPanel.SetDock(cmdPanel, Dock.Bottom);
        root.Children.Add(cmdPanel);

        // 终端输出区域
        var termBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
        };

        _terminalBox.Margin = new Thickness(0);

        termBorder.Child = _terminalBox;
        _termBorder = termBorder;
        root.Children.Add(termBorder);

        Content = root;
    }

    private void Connect()
    {
        var host = _hostBox.Text.Trim();
        var portText = _portBox.Text.Trim();
        var user = _userBox.Text.Trim();
        var pass = _passBox.Password;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user))
        {
            SetStatus("请输入主机和用户名", false);
            return;
        }
        if (!int.TryParse(portText, out int port)) port = 22;

        try
        {
            Disconnect();
            AppendTerminal($"正在连接到 {user}@{host}:{port}...\r\n");

            var connInfo = new ConnectionInfo(host, port, user,
                new PasswordAuthenticationMethod(user, pass));
            connInfo.Timeout = TimeSpan.FromSeconds(10);

            _sshClient = new SshClient(connInfo);
            _sshClient.Connect();

            _shellStream = _sshClient.CreateShellStream("xterm-256color", 200, 50, 800, 600, 65536);

            _readCts = new CancellationTokenSource();
            var token = _readCts.Token;
            Task.Run(() => ReadShellOutput(token), token);

            Dispatcher.Invoke(() =>
            {
                _connected = true;
                _connectBtn.IsEnabled = false;
                _disconnectBtn.IsEnabled = true;
                _hostBox.IsEnabled = false;
                _portBox.IsEnabled = false;
                _userBox.IsEnabled = false;
                _passBox.IsEnabled = false;
                SetStatus($"已连接到 {host}:{port}", true);
                AppendTerminal($"连接成功！\r\n");
            });
        }
        catch (Exception ex)
        {
            AppendTerminal($"连接失败: {ex.Message}\r\n");
            SetStatus($"连接失败: {ex.Message}", false);
            Disconnect();
        }
    }

    private bool _connected;

    public void SafeDisconnect() { try { Disconnect(); } catch { } }

    private void Disconnect()
    {
        try
        {
            _readCts?.Cancel();
            _shellStream?.Dispose();
            _sshClient?.Disconnect();
            _sshClient?.Dispose();
        }
        catch { }
        finally
        {
            _shellStream = null;
            _sshClient = null;
            _readCts = null;
            _connected = false;
            Dispatcher.Invoke(() =>
            {
                _connectBtn.IsEnabled = true;
                _disconnectBtn.IsEnabled = false;
                _hostBox.IsEnabled = true;
                _portBox.IsEnabled = true;
                _userBox.IsEnabled = true;
                _passBox.IsEnabled = true;
            });
        }
    }

    private async Task ReadShellOutput(CancellationToken token)
    {
        try
        {
            var buffer = new byte[4096];
            while (!token.IsCancellationRequested && _shellStream != null)
            {
                if (_shellStream.DataAvailable)
                {
                    int read = await _shellStream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read > 0)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        Dispatcher.Invoke(() => AppendTerminal(text));
                    }
                }
                else
                {
                    await Task.Delay(100, token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AppendTerminal($"\r\n[读取结束: {ex.Message}]\r\n"));
        }
    }

    private void SendCommand()
    {
        if (!_connected || _shellStream == null)
        {
            SetStatus("未连接，无法发送命令", false);
            return;
        }

        var cmdBox = _floatingCmdBox ?? _commandBox;
        var cmd = cmdBox.Text;
        if (string.IsNullOrEmpty(cmd)) return;

        try
        {
            _shellStream.WriteLine(cmd);
            cmdBox.Text = "";
        }
        catch (Exception ex)
        {
            SetStatus($"发送失败: {ex.Message}", false);
        }
    }

    private void AppendTerminal(string text)
    {
        _terminalBox.Append(text);
        _terminalBox.ScrollToEnd();
        if (_floatingTerminal != null)
        {
            _floatingTerminal.Append(text);
            _floatingTerminal.ScrollToEnd();
        }
    }

    private void ClearTerminal()
    {
        _terminalBox.Clear();
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    // ========== 独立窗口 ==========

    private void OpenFloatingWindow()
    {
        if (_floatingWindow != null)
        {
            _floatingWindow.Activate();
            return;
        }

        var win = new Window
        {
            Title = "SSH 终端",
            Width = 900,
            Height = 600,
            MinWidth = 400,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.CanResize
        };

        var dock = new DockPanel();

        // 底部命令输入
        var cmdPanel = new DockPanel { Margin = new Thickness(8, 8, 8, 8) };
        var sendBtn = new Button
        {
            Content = "发送",
            Margin = new Thickness(8, 0, 0, 0),
            Style = TryFindResource("MaterialDesignRaisedButton") as Style,
            MinWidth = 60
        };
        sendBtn.Click += (s, e) => SendCommand();
        DockPanel.SetDock(sendBtn, Dock.Right);
        cmdPanel.Children.Add(sendBtn);

        var floatCmdBox = new TextBox
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 13,
            Style = TryFindResource("MaterialDesignOutlinedTextBox") as Style
        };
        MaterialDesignThemes.Wpf.HintAssist.SetHint(floatCmdBox, "输入命令后按 Enter 发送...");
        floatCmdBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { SendCommand(); e.Handled = true; }
        };
        // 同步命令框引用，让 SendCommand 使用独立窗口的输入框
        floatCmdBox.GotFocus += (s, e) => { _floatingCmdBox = floatCmdBox; };
        cmdPanel.Children.Add(floatCmdBox);
        DockPanel.SetDock(cmdPanel, Dock.Bottom);
        dock.Children.Add(cmdPanel);

        // 终端显示
        var termBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
        };

        var floatTerm = new TerminalBox
        {
            Margin = new Thickness(0)
        };
        // 同步已有内容
        floatTerm.Append(_terminalBox.GetAllText());
        _floatingTerminal = floatTerm;

        termBorder.Child = floatTerm;
        dock.Children.Add(termBorder);

        win.Content = dock;

        win.Closing += (s, e) =>
        {
            Disconnect();
            _floatingWindow = null;
            _floatingTerminal = null;
            _floatingCmdBox = null;
        };

        _floatingWindow = win;
        _floatingCmdBox = floatCmdBox;
        win.Show();
    }

    private TerminalBox? _floatingTerminal;
    private TextBox? _floatingCmdBox;
}
