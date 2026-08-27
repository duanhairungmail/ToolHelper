using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;
using MaterialDesignThemes.Wpf;
using ToolHelper.Services;

namespace ToolHelper.Views.Other;

/// <summary>Node-RED 便携运行时管理及 WebView2 内嵌编辑器。</summary>
public sealed class NodeRedLauncherView : UserControl
{
    private const int DefaultPort = 1880;
    private const string RepoOwner = "duanhairungmail";
    private const string RepoName = "ToolHelper_nodered";
    private readonly NodeRedProcessManager _manager = new();
    private readonly WebView2 _webView = new();
    private readonly TextBox _logBox = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBox _portBox = new() { Text = DefaultPort.ToString(), Width = 70 };
    private Button _downloadButton = new();
    private Button _startButton = new();
    private Button _stopButton = new();
    private Button _updateButton = new();
    private Button _deleteButton = new();
    private bool _built;
    private int _actualPort = DefaultPort;

    public NodeRedLauncherView()
    {
        Loaded += OnLoaded;
        Unloaded += (_, _) => { };
        _manager.OutputReceived += line => Dispatcher.BeginInvoke(() => AppendLog(line));
        _manager.ProcessExited += code => Dispatcher.BeginInvoke(() =>
        {
            SetStatus($"已停止（退出码 {code}）", false);
            RefreshState();
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_built)
        {
            _built = true;
            BuildUi();
            ViewportFitHelper.FitToViewport(this, 620);
        }
        RefreshState();
    }

    private static string ResolveDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var plugins = Path.Combine(dir, "plugins");
            if (Directory.Exists(plugins)) return Path.Combine(plugins, "nodered");
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "nodered");
    }

    private static string? FindFile(string root, string relative)
    {
        var exact = Path.Combine(root, relative);
        if (File.Exists(exact)) return exact;
        return null;
    }

    private bool IsInstalled => FindFile(ResolveDir(), Path.Combine("node", "node.exe")) != null
        && FindFile(ResolveDir(), Path.Combine("node_modules", "node-red", "red.js")) != null;

    private void RefreshState()
    {
        var installed = IsInstalled;
        _downloadButton.IsEnabled = !installed;
        _startButton.IsEnabled = installed && !_manager.IsRunning;
        _stopButton.IsEnabled = _manager.IsRunning;
        _updateButton.IsEnabled = installed && !_manager.IsRunning;
        _deleteButton.IsEnabled = installed && !_manager.IsRunning;
        if (!installed) SetStatus("未安装（点击下载便携包）", false);
        else if (!_manager.IsRunning) SetStatus("已安装，未启动", false);
        else SetStatus($"运行中：http://localhost:{_actualPort}", true);
    }

    private void BuildUi()
    {
        var root = new DockPanel();
        var top = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        title.Children.Add(new PackIcon { Kind = PackIconKind.Sitemap, Width = 28, Height = 28, Foreground = Brushes.DarkBlue });
        title.Children.Add(new TextBlock { Text = "  Node-RED 可视化编排", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.DarkBlue });
        top.Children.Add(title);
        top.Children.Add(new TextBlock { Text = "拖拽编排串口、Modbus、HTTP 等流程；运行时按需下载，无需预装 Node.js。", Opacity = .65, Margin = new Thickness(0, 0, 0, 8) });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        _downloadButton = MakeButton("下载", Download, PackIconKind.CloudDownload);
        _startButton = MakeButton("启动", Start, PackIconKind.Play);
        _stopButton = MakeButton("停止", Stop, PackIconKind.Stop);
        _updateButton = MakeButton("更新", CheckUpdate, PackIconKind.Update);
        _deleteButton = MakeButton("删除", Delete, PackIconKind.DeleteOutline);
        foreach (var button in new[] { _downloadButton, _startButton, _stopButton, _updateButton, _deleteButton }) controls.Children.Add(button);
        controls.Children.Add(new TextBlock { Text = "端口", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
        controls.Children.Add(_portBox);
        controls.Children.Add(_statusText);
        _statusText.Margin = new Thickness(12, 0, 0, 0);
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(controls);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        _webView.Visibility = Visibility.Visible;
        _webView.MinHeight = 360;
        root.Children.Add(_webView);
        var label = new TextBlock { Text = "操作日志", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 3) };
        DockPanel.SetDock(label, Dock.Bottom);
        root.Children.Add(label);
        _logBox.IsReadOnly = true;
        _logBox.AcceptsReturn = true;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.MinHeight = 110;
        _logBox.FontFamily = new FontFamily("Consolas");
        DockPanel.SetDock(_logBox, Dock.Bottom);
        root.Children.Add(_logBox);
        Content = root;
    }

    private Button MakeButton(string text, Action action, PackIconKind icon)
    {
        var button = new Button { Margin = new Thickness(0, 0, 6, 0), Style = TryFindResource("MaterialDesignOutlinedButton") as Style };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new PackIcon { Kind = icon, Width = 17, Height = 17, Margin = new Thickness(0, 0, 4, 0) });
        panel.Children.Add(new TextBlock { Text = text });
        button.Content = panel;
        button.Click += (_, _) => action();
        return button;
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logBox.AppendText(line + Environment.NewLine);
        _logBox.ScrollToEnd();
        FileLogger.Write("NodeRed", message);
    }

    private async void Download()
    {
        _downloadButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(AppendLog);
            var tag = await PluginDownloader.DownloadAsync(RepoOwner, RepoName,
                name => name.Contains("nodered-portable", StringComparison.OrdinalIgnoreCase), ResolveDir(), progress);
            AppendLog($"Node-RED {tag} 下载完成");
        }
        catch (Exception ex) { AppendLog($"下载失败：{ex.Message}"); }
        RefreshState();
    }

    private async void Start()
    {
        if (!int.TryParse(_portBox.Text, out var requested) || requested is < 1 or > 65535)
        {
            AppendLog("端口必须是 1-65535 的整数");
            return;
        }
        var port = FindFreePort(requested);
        if (port == null) { AppendLog("未找到可用端口"); return; }
        var dir = ResolveDir();
        var node = FindFile(dir, Path.Combine("node", "node.exe"))!;
        var red = FindFile(dir, Path.Combine("node_modules", "node-red", "red.js"))!;
        _actualPort = port.Value;
        _portBox.Text = _actualPort.ToString();
        if (_actualPort != requested) AppendLog($"端口 {requested} 已占用，自动使用 {_actualPort}");
        if (!_manager.Start(node, red, Path.Combine(dir, "data"), _actualPort)) { AppendLog("Node-RED 启动失败"); return; }
        AppendLog($"Node-RED 已启动，端口 {_actualPort}");
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Navigate($"http://localhost:{_actualPort}");
        }
        catch (Exception ex) { AppendLog($"WebView2 初始化失败：{ex.Message}"); }
        RefreshState();
    }

    private void Stop() { _manager.Stop(); _webView.Source = null; AppendLog("Node-RED 已停止"); RefreshState(); }

    private async void CheckUpdate()
    {
        try
        {
            var latest = await PluginDownloader.GetLatestVersionAsync(RepoOwner, RepoName);
            var local = PluginDownloader.ReadVersionMarker(ResolveDir());
            if (string.IsNullOrWhiteSpace(local) || PluginDownloader.IsNewer(latest, local))
            {
                if (MessageBox.Show($"检测到 Node-RED 新版本 {latest}，是否更新？", "Node-RED 更新", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                DeleteDirectory();
                Download();
            }
            else AppendLog($"已是最新版本（{local}）");
        }
        catch (Exception ex) { AppendLog($"检查更新失败：{ex.Message}"); }
    }

    private void Delete()
    {
        if (MessageBox.Show("确定删除 Node-RED 运行时吗？", "删除确认", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
        { DeleteDirectory(); RefreshState(); }
    }

    private void DeleteDirectory()
    {
        try { Directory.Delete(ResolveDir(), true); AppendLog("Node-RED 运行时已删除"); }
        catch (Exception ex) { AppendLog($"删除失败：{ex.Message}"); }
    }

    private static int? FindFreePort(int start)
    {
        for (var port = start; port <= 65535; port++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException) { }
        }
        return null;
    }

    private void SetStatus(string text, bool success) { _statusText.Text = text; _statusText.Foreground = success ? Brushes.Green : Brushes.Gray; }

    public void SafeDisconnect() => _manager.Stop();
}
