using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolHelper.Services;
using PackIcon = MaterialDesignThemes.Wpf.PackIcon;
using PackIconKind = MaterialDesignThemes.Wpf.PackIconKind;

namespace ToolHelper.Views.Remote;

/// <summary>
/// SSH 外挂（electerm）启动器：按需下载/删除/更新便携版，之后点击启动。
/// 发布不打包插件，运行时从 GitHub latest release 下载 win-x64-portable 版。
/// </summary>
public class ElectermLauncherView : UserControl
{
    private TextBox _logBox = new();
    private TextBlock _statusInfoText = new();
    private Button _launchBtn = new();
    private Button _downloadBtn = new();
    private Button _deleteBtn = new();
    private Button _updateBtn = new();
    private bool _built;

    public ElectermLauncherView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_built)
        {
            _built = true;
            BuildUI();
            // 视图高度钉在宿主视口上（踩坑记录 #15 同款方案）：宿主向视图传递无限高度，
            // 星号行/填充区无法按比例分配；钉住高度后日志区才能随窗口大小自动伸缩
            ViewportFitHelper.FitToViewport(this, 480);
        }
        // 视图被 MainViewModel 缓存复用（GetOrCreateView），每次切入都必须重新检测插件状态：
        // 修复"手动删除插件目录后下载按钮仍禁用"的问题（踩坑记录 #22）
        RefreshPluginState();
    }

    /// <summary>解析 plugins/electerm 目录（不存在也返回路径，供下载目标用）</summary>
    private static string ResolveElectermDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var plugins = Path.Combine(dir, "plugins");
            if (Directory.Exists(plugins)) return Path.Combine(plugins, "electerm");
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "electerm");
    }

    /// <summary>查找 electerm.exe（根目录优先，否则递归按名称匹配，兼容解压后的子目录）</summary>
    private static string? FindElectermExe()
    {
        var electermDir = ResolveElectermDir();
        if (!Directory.Exists(electermDir)) return null;
        var exe = Path.Combine(electermDir, "electerm.exe");
        if (File.Exists(exe)) return exe;
        return Directory.GetFiles(electermDir, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains("electerm", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>本地版本：优先 version.txt 标记，回退解析解压目录名（electerm-x.y.z-win-x64-portable）</summary>
    private static string? GetLocalVersion()
    {
        var dir = ResolveElectermDir();
        if (!Directory.Exists(dir)) return null;
        var marker = PluginDownloader.ReadVersionMarker(dir);
        if (!string.IsNullOrWhiteSpace(marker)) return marker;
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var m = Regex.Match(Path.GetFileName(sub), @"electerm-(\d+\.\d+\.\d+)-");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>重新检测插件安装状态并刷新按钮可用态与信息行（视图缓存复用下的关键修复）</summary>
    private void RefreshPluginState()
    {
        var installed = FindElectermExe() != null;
        var ver = GetLocalVersion();
        _launchBtn.IsEnabled = installed;
        _downloadBtn.IsEnabled = !installed;
        _statusInfoText.Text = installed ? $"已安装（v{ver ?? "未知"}）" : "未安装（需联网下载）";
    }

    private void BuildUI()
    {
        var root = new DockPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // ── 上部固定内容（标题/描述/信息行/按钮行） ──
        var top = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Terminal, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  SSH 外挂", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(titleRow);

        top.Children.Add(new TextBlock
        {
            Text = "点击启动 electerm（终端 + SSH + SFTP + 串口 + RDP/VNC 客户端，MIT 开源）。首次使用需联网下载插件。",
            FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });

        var info = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        _statusInfoText.FontSize = 12;
        _statusInfoText.TextWrapping = TextWrapping.Wrap;
        info.Children.Add(MakeInfoRow("插件状态:", _statusInfoText));
        info.Children.Add(MakeInfoRow("存放目录:", "plugins\\electerm\\"));
        info.Children.Add(MakeInfoRow("许可证:", "MIT"));
        top.Children.Add(info);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        _launchBtn = MakeButton("启动外挂", Launch, true, PackIconKind.Terminal);
        btnRow.Children.Add(_launchBtn);
        _downloadBtn = MakeButton("下载插件", Download, false, PackIconKind.CloudDownload);
        btnRow.Children.Add(_downloadBtn);
        _deleteBtn = MakeButton("删除插件", DeletePlugin, false, PackIconKind.DeleteOutline);
        btnRow.Children.Add(_deleteBtn);
        _updateBtn = MakeButton("插件更新", CheckUpdate, false, PackIconKind.Update);
        btnRow.Children.Add(_updateBtn);
        btnRow.Children.Add(MakeButton("打开所在目录", OpenDir, false, PackIconKind.FolderOpen));
        top.Children.Add(btnRow);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // ── 操作日志区（最后一个子元素填充剩余高度，随窗口自动伸缩；样式与「漏洞检测与系统优化」一致） ──
        var logPanel = new DockPanel();
        var logLabel = new TextBlock
        {
            Text = "操作日志",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Margin = new Thickness(0, 12, 0, 4)
        };
        DockPanel.SetDock(logLabel, Dock.Top);
        logPanel.Children.Add(logLabel);
        _logBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
            Foreground = new SolidColorBrush(Color.FromRgb(171, 178, 191)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 72)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 120
        };
        logPanel.Children.Add(_logBox);
        root.Children.Add(logPanel);

        Content = root;
        RefreshPluginState();
        AppendLog("视图已就绪，插件状态：" + (_launchBtn.IsEnabled ? "已安装" : "未安装"));
    }

    /// <summary>日志写入：带 [HH:mm:ss] 时间戳；进度类消息替换末行避免刷屏</summary>
    private void AppendLog(string msg)
    {
        if (!_logBox.Dispatcher.CheckAccess())
        {
            _logBox.Dispatcher.Invoke(() => AppendLog(msg));
            return;
        }
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        if (msg.StartsWith("下载中", StringComparison.Ordinal))
        {
            var idx = _logBox.Text.LastIndexOf('\n');
            if (idx >= 0)
            {
                var lastLine = _logBox.Text[(idx + 1)..];
                if (lastLine.StartsWith("[") && lastLine.Contains("下载中"))
                {
                    _logBox.Text = _logBox.Text[..(idx + 1)] + line;
                    _logBox.ScrollToEnd();
                    return;
                }
            }
        }
        _logBox.AppendText(line + "\n");
        _logBox.ScrollToEnd();
    }

    private async void Download() => await DoDownload();

    /// <summary>下载/更新共用流程：下载解压到 plugins/electerm 并写入版本标记</summary>
    private async Task DoDownload()
    {
        _downloadBtn.IsEnabled = false;
        AppendLog("开始下载插件...");
        try
        {
            var progress = new Progress<string>(AppendLog);
            var tag = await PluginDownloader.DownloadAsync(
                "electerm", "electerm",
                name => name.Contains("win-x64-portable", StringComparison.OrdinalIgnoreCase),
                ResolveElectermDir(),
                progress);
            AppendLog($"下载完成（{tag}），点击「启动外挂」开始使用");
        }
        catch (Exception ex)
        {
            AppendLog($"下载失败: {ex.Message}");
        }
        RefreshPluginState();
    }

    /// <summary>删除插件：确认后递归删除插件目录（进程占用时提示先关闭 electerm）</summary>
    private void DeletePlugin()
    {
        var dir = ResolveElectermDir();
        if (!Directory.Exists(dir))
        {
            AppendLog("插件未安装，无需删除");
            RefreshPluginState();
            return;
        }
        var res = MessageBox.Show(
            "确定删除 electerm 插件吗？\n删除后需重新下载才能使用。",
            "删除插件", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (res != MessageBoxResult.OK) { AppendLog("已取消删除"); return; }
        AppendLog("正在删除插件...");
        try
        {
            Directory.Delete(dir, true);
            AppendLog("插件已删除");
        }
        catch (IOException)
        {
            AppendLog("删除失败：文件被占用，请先关闭 electerm 进程再试");
        }
        catch (Exception ex)
        {
            AppendLog($"删除失败: {ex.Message}");
        }
        RefreshPluginState();
    }

    /// <summary>插件更新：联网检测最新版本，用户确认后先删旧目录再下载</summary>
    private async void CheckUpdate()
    {
        _updateBtn.IsEnabled = false;
        AppendLog("正在检查最新版本...");
        try
        {
            var latest = await PluginDownloader.GetLatestVersionAsync("electerm", "electerm");
            if (string.IsNullOrWhiteSpace(latest))
            {
                AppendLog("获取最新版本失败（GitHub API 无响应或限流）");
                return;
            }
            var local = GetLocalVersion();
            AppendLog($"最新版本: {latest}，本地版本: {local ?? "未知"}");

            if (local == null)
            {
                var ask = MessageBox.Show($"本地版本未知/未安装，是否下载最新版 {latest}？",
                    "插件更新", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (ask != MessageBoxResult.OK) { AppendLog("已取消"); return; }
                await DoDownload();
                return;
            }

            if (!PluginDownloader.IsNewer(latest, local))
            {
                AppendLog($"已是最新版本（{latest}）");
                return;
            }

            var res = MessageBox.Show($"检测到新版本 {latest}（当前 {local}），是否更新？",
                "插件更新", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (res != MessageBoxResult.OK) { AppendLog("已取消更新"); return; }

            var dir = ResolveElectermDir();
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                AppendLog("更新失败：文件被占用，请先关闭 electerm 进程再试");
                return;
            }
            AppendLog($"开始更新到 {latest} ...");
            await DoDownload();
        }
        catch (Exception ex)
        {
            AppendLog($"版本检测失败: {ex.Message}");
        }
        finally
        {
            _updateBtn.IsEnabled = true;
        }
    }

    private void Launch()
    {
        var exe = FindElectermExe();
        if (string.IsNullOrEmpty(exe))
        {
            AppendLog("插件未安装，请先点击「下载插件」");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            });
            AppendLog("SSH 外挂已启动");
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败: {ex.Message}");
        }
    }

    private void OpenDir()
    {
        var dir = ResolveElectermDir();
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        try { Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true }); }
        catch (Exception ex) { AppendLog($"打开目录失败: {ex.Message}"); }
    }

    private StackPanel MakeInfoRow(string label, TextBlock value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Width = 100 });
        row.Children.Add(value);
        return row;
    }

    private StackPanel MakeInfoRow(string label, string value)
    {
        return MakeInfoRow(label, new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap });
    }

    private Button MakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button
        {
            Content = sp,
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource(primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton") as Style
        };
        btn.Click += (s, e) => handler();
        return btn;
    }
}
