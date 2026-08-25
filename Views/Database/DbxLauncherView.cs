using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ToolHelper.Services;
using PackIcon = MaterialDesignThemes.Wpf.PackIcon;
using PackIconKind = MaterialDesignThemes.Wpf.PackIconKind;

namespace ToolHelper.Views.Database;

/// <summary>
/// 数据库外挂连接：DBX 下载/管理 + MySQL/PostgreSQL 填参连接（dbx:// 深链唤起）。
/// 发布不打包插件，运行时从 GitHub latest release 下载 x64-portable.zip 版。
/// </summary>
public class DbxLauncherView : UserControl
{
    private TextBox _logBox = new();
    private TextBlock _statusInfoText = new();
    private Button _launchBtn = new();
    private Button _downloadBtn = new();
    private Button _deleteBtn = new();
    private Button _updateBtn = new();
    private TabControl _tabControl = new();
    private Button _tabManageBtn = new();
    private Button _tabConnectBtn = new();
    private TextBlock _statusText = new();
    private bool _built;

    // 数据库填参连接区
    private ComboBox _dbTypeCombo = new();
    private TextBox _hostBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private PasswordBox _passBox = new();
    private TextBox _connectionNameBox = new();
    private TextBox _dbNameBox = new();

    // 数据库类型：显示名 / dbx type / 默认端口
    private static readonly (string Name, string Type, string Port)[] DbTypes =
    {
        ("MySQL", "mysql", "3306"),
        ("PostgreSQL", "postgres", "5432"),
    };

    public DbxLauncherView()
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
            ViewportFitHelper.FitToViewport(this, 520);
        }
        // 视图被 MainViewModel 缓存复用（GetOrCreateView），每次切入都必须重新检测插件状态：
        // 修复"手动删除插件目录后下载按钮仍禁用"的问题（踩坑记录 #22）
        RefreshPluginState();
    }

    /// <summary>解析 plugins/dbx 目录（不存在也返回路径，供下载目标用）</summary>
    private static string ResolveDbxDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var plugins = Path.Combine(dir, "plugins");
            if (Directory.Exists(plugins)) return Path.Combine(plugins, "dbx");
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "dbx");
    }

    /// <summary>查找 DBX.exe（根目录优先，否则递归按名称匹配，兼容解压后的子目录）</summary>
    private static string? FindDbxExe()
    {
        var dbxDir = ResolveDbxDir();
        if (!Directory.Exists(dbxDir)) return null;
        var exe = Path.Combine(dbxDir, "DBX.exe");
        if (File.Exists(exe)) return exe;
        return Directory.GetFiles(dbxDir, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains("dbx", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>本地版本：优先 version.txt 标记，回退解析含三段版本号的目录名</summary>
    private static string? GetLocalVersion()
    {
        var dir = ResolveDbxDir();
        if (!Directory.Exists(dir)) return null;
        var marker = PluginDownloader.ReadVersionMarker(dir);
        if (!string.IsNullOrWhiteSpace(marker)) return marker;
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var m = Regex.Match(Path.GetFileName(sub), @"(\d+\.\d+\.\d+)");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>重新检测插件安装状态并刷新按钮可用态与信息行（视图缓存复用下的关键修复）</summary>
    private void RefreshPluginState()
    {
        var installed = FindDbxExe() != null;
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
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Database, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  数据库外挂连接", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(titleRow);

        top.Children.Add(new TextBlock
        {
            Text = "通过 DBX 外挂连接数据库（MySQL/postgresql），填参后唤起 DBX 完成连接。首次使用需联网下载插件。",
            FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });

        // ── Tab 切换按钮行（与 KylinOS 运维策略同款：隐藏 Tab 标题，由上方按钮切换）──
        var tabBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _tabManageBtn = MakeButton("插件管理", () => SwitchTab(0), false, PackIconKind.PackageVariant);
        tabBtnRow.Children.Add(_tabManageBtn);
        _tabConnectBtn = MakeButton("外挂连接", () => SwitchTab(1), false, PackIconKind.LinkVariant);
        tabBtnRow.Children.Add(_tabConnectBtn);
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        tabBtnRow.Children.Add(_statusText);
        top.Children.Add(tabBtnRow);

        // ── TabControl（固定高度保证两个 Tab 排版一致）──
        _tabControl = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Height = 230 };
        var tabManage = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tabManage.Content = BuildManageTab();
        _tabControl.Items.Add(tabManage);
        var tabConnect = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tabConnect.Content = BuildConnectTab();
        _tabControl.Items.Add(tabConnect);
        top.Children.Add(_tabControl);
        SwitchTab(0);

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
        AppendLog("视图已就绪，插件状态：" + (_launchBtn.IsEnabled ? "已安装" : "未安装") + "；「外挂连接」Tab 可填参唤起 DBX");
    }

    /// <summary>日志写入：进度类消息替换末行，避免逐 MB 刷屏。</summary>
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
            var text = _logBox.Text.TrimEnd('\r', '\n');
            var idx = text.LastIndexOf('\n');
            var lastLine = idx >= 0 ? text[(idx + 1)..] : text;
            if (lastLine.StartsWith("[") && lastLine.Contains("下载中", StringComparison.Ordinal))
            {
                _logBox.Text = (idx >= 0 ? text[..(idx + 1)] : "") + line + "\n";
                _logBox.ScrollToEnd();
                return;
            }
        }
        _logBox.AppendText(line + "\n");
        _logBox.ScrollToEnd();
    }

    private async void Download() => await DoDownload();

    /// <summary>下载/更新共用流程：下载解压到 plugins/dbx 并写入版本标记</summary>
    private async Task DoDownload()
    {
        _downloadBtn.IsEnabled = false;
        AppendLog("开始下载插件...");
        try
        {
            var progress = new Progress<string>(AppendLog);
            var tag = await PluginDownloader.DownloadAsync(
                "t8y2", "dbx",
                // 精确匹配 zip 资产：仓库同发 .sig 签名文件（名称含 x64-portable.zip 子串），EndsWith 避免误选
                name => name.EndsWith("x64-portable.zip", StringComparison.OrdinalIgnoreCase),
                ResolveDbxDir(),
                progress);
            AppendLog($"下载完成（{tag}），点击「启动外挂」开始使用");
        }
        catch (Exception ex)
        {
            AppendLog($"下载失败: {ex.Message}");
        }
        RefreshPluginState();
    }

    /// <summary>删除插件：确认后递归删除插件目录（进程占用时提示先关闭 DBX）</summary>
    private void DeletePlugin()
    {
        var dir = ResolveDbxDir();
        if (!Directory.Exists(dir))
        {
            AppendLog("插件未安装，无需删除");
            RefreshPluginState();
            return;
        }
        var res = MessageBox.Show(
            "确定删除 DBX 插件吗？\n删除后需重新下载才能使用。",
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
            AppendLog("删除失败：文件被占用，请先关闭 DBX 进程再试");
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
            var latest = await PluginDownloader.GetLatestVersionAsync("t8y2", "dbx");
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

            var dir = ResolveDbxDir();
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                AppendLog("更新失败：文件被占用，请先关闭 DBX 进程再试");
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
        var exe = FindDbxExe();
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
            AppendLog("数据库外挂已启动");
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败: {ex.Message}");
        }
    }

    private void OpenDir()
    {
        var dir = ResolveDbxDir();
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        try { Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true }); }
        catch (Exception ex) { AppendLog($"打开目录失败: {ex.Message}"); }
    }

    // ========== 数据库填参连接区辅助 ==========

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

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
    };

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

    /// <summary>
    /// 确保 dbx:// 协议已注册。portable 版 DBX 未启动过时不会注册协议（踩坑 #23 同款问题），
    /// 用本地 DBX.exe 注册 HKCU 用户级协议（无需管理员权限）。
    /// </summary>
    private static bool EnsureDbxProtocolRegistered()
    {
        using (var cmdKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\dbx\shell\open\command"))
        {
            if (cmdKey?.GetValue(null) is string existing && existing.Contains("dbx", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var exe = FindDbxExe();
        if (string.IsNullOrEmpty(exe)) return false;

        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\dbx"))
        {
            key.SetValue(null, "URL:dbx Protocol");
            key.SetValue("URL Protocol", "");
        }
        using (var cmd = Registry.CurrentUser.CreateSubKey(@"Software\Classes\dbx\shell\open\command"))
        {
            cmd.SetValue(null, $"\"{exe}\" \"%1\"");
        }
        return true;
    }

    // ========== 外挂连接（通过 dbx:// 唤起）==========

    private void LaunchDbxConnection()
    {
        var host = _hostBox.Text.Trim();
        var portText = _portBox.Text.Trim();
        var user = _userBox.Text.Trim();
        var password = _passBox.Password;
        var connectionName = _connectionNameBox.Text.Trim();
        var dbName = _dbNameBox.Text.Trim();
        if (string.IsNullOrEmpty(host)) { AppendLog("外挂连接失败：请输入主机"); return; }
        if (_dbTypeCombo.SelectedIndex < 0 || _dbTypeCombo.SelectedIndex >= DbTypes.Length) return;
        var dbType = DbTypes[_dbTypeCombo.SelectedIndex].Type;

        // 协议未注册时自动注册（portable 版未启动过会弹「获取打开此 dbx 链接的应用」）
        if (!EnsureDbxProtocolRegistered())
        {
            AppendLog("未检测到 dbx:// 协议且未找到本地 DBX，请先点击「下载插件」并启动一次 DBX");
            return;
        }

        var sb = new StringBuilder("dbx://connection/new?type=").Append(dbType);
        if (!string.IsNullOrEmpty(connectionName)) sb.Append("&name=").Append(Uri.EscapeDataString(connectionName));
        sb.Append("&host=").Append(Uri.EscapeDataString(host));
        sb.Append("&port=").Append(Uri.EscapeDataString(portText));
        if (!string.IsNullOrEmpty(user)) sb.Append("&user=").Append(Uri.EscapeDataString(user));
        if (!string.IsNullOrEmpty(password)) sb.Append("&password=").Append(Uri.EscapeDataString(password));
        if (!string.IsNullOrEmpty(dbName)) sb.Append("&database=").Append(Uri.EscapeDataString(dbName));
        sb.Append("&one_time=true");

        var url = sb.ToString();
        AppendLog($"唤起 DBX（{dbType} 连接）: {host}:{portText}");
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _passBox.Clear();
            AppendLog("已唤起 DBX，连接密码已从输入框清除");
            SetStatus("已唤起 DBX", true);
        }
        catch (Exception protocolEx)
        {
            // 协议刚注册时系统可能尚未感知，回退：直接用本地 exe 携带 URL 参数启动
            var exe = FindDbxExe();
            if (!string.IsNullOrEmpty(exe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = exe, Arguments = $"\"{url}\"", UseShellExecute = true });
                    _passBox.Clear();
                    AppendLog("已唤起 DBX，连接密码已从输入框清除");
                    SetStatus("已唤起 DBX", true);
                    return;
                }
                catch (Exception launchEx)
                {
                    AppendLog($"外挂连接失败（{launchEx.GetType().Name}）");
                    SetStatus("外挂连接失败：无法启动 DBX", false);
                    return;
                }
            }
            AppendLog($"外挂连接失败（{protocolEx.GetType().Name}）");
            SetStatus("外挂连接失败：无法唤起 DBX", false);
        }
    }

    /// <summary>Tab1 插件管理：KylinOS 同款信息卡片 + 插件按钮行</summary>
    private StackPanel BuildManageTab()
    {
        var panel = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };

        // 信息卡片（样式与 KylinOS 运维策略的 MakeInfoCard 一致）
        var cardRows = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        stateRow.Children.Add(new TextBlock { Text = "📦 插件状态：", FontSize = 12, FontWeight = FontWeights.SemiBold });
        _statusInfoText.FontSize = 12;
        _statusInfoText.TextWrapping = TextWrapping.Wrap;
        stateRow.Children.Add(_statusInfoText);
        cardRows.Children.Add(stateRow);
        cardRows.Children.Add(new TextBlock { Text = "📁 存放目录：plugins\\dbx\\（按需下载，发布包不预置）", FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap });
        cardRows.Children.Add(new TextBlock { Text = "📜 许可证：Apache-2.0（通用数据库客户端，支持 90+ 数据库）", FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(237, 242, 247)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(189, 206, 223)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 8),
            Child = cardRows
        });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        _launchBtn = MakeButton("启动外挂", Launch, true, PackIconKind.Database);
        btnRow.Children.Add(_launchBtn);
        _downloadBtn = MakeButton("下载插件", Download, false, PackIconKind.CloudDownload);
        btnRow.Children.Add(_downloadBtn);
        _deleteBtn = MakeButton("删除插件", DeletePlugin, false, PackIconKind.DeleteOutline);
        btnRow.Children.Add(_deleteBtn);
        _updateBtn = MakeButton("插件更新", CheckUpdate, false, PackIconKind.Update);
        btnRow.Children.Add(_updateBtn);
        btnRow.Children.Add(MakeButton("打开所在目录", OpenDir, false, PackIconKind.FolderOpen));
        panel.Children.Add(btnRow);

        return panel;
    }

    /// <summary>Tab2 外挂连接：说明卡片 + 填参行 + 外挂连接按钮</summary>
    private StackPanel BuildConnectTab()
    {
        var panel = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };

        // 说明卡片（样式与 KylinOS 运维策略的 MakeInfoCard 一致）
        var cardRows = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        cardRows.Children.Add(new TextBlock { Text = "🔗 点击「外挂连接」通过 dbx:// 深链唤起 DBX 并预填连接信息（ToolHelper 不保存密码、不写日志）", FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap });
        cardRows.Children.Add(new TextBlock { Text = "📌 类型切换自动填充默认端口；连接名称和数据库名填写后预填到 DBX", FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(237, 242, 247)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(189, 206, 223)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 8),
            Child = cardRows
        });

        var connRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow1.Children.Add(MakeLabel("类型:"));
        _dbTypeCombo.FontFamily = new FontFamily("Microsoft YaHei");
        _dbTypeCombo.FontSize = 13;
        _dbTypeCombo.MinWidth = 120;
        _dbTypeCombo.Margin = new Thickness(0, 0, 6, 0);
        var comboStyle = TryFindResource("MaterialDesignOutlinedComboBox") as Style;
        if (comboStyle != null) _dbTypeCombo.Style = comboStyle;
        foreach (var t in DbTypes) _dbTypeCombo.Items.Add(t.Name);
        _dbTypeCombo.SelectionChanged += (s, e) =>
        {
            if (_dbTypeCombo.SelectedIndex >= 0 && _dbTypeCombo.SelectedIndex < DbTypes.Length)
                _portBox.Text = DbTypes[_dbTypeCombo.SelectedIndex].Port;
        };
        connRow1.Children.Add(_dbTypeCombo);
        connRow1.Children.Add(MakeLabel("主机:"));
        _hostBox = MakeBox("IP或主机名", "", 180);
        connRow1.Children.Add(_hostBox);
        connRow1.Children.Add(MakeLabel("端口:"));
        _portBox = MakeBox("端口", "3306", 70);
        connRow1.Children.Add(_portBox);
        connRow1.Children.Add(MakeLabel("用户名:"));
        _userBox = MakeBox("用户名", "", 120);
        connRow1.Children.Add(_userBox);
        panel.Children.Add(connRow1);

        var connRow2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow2.Children.Add(MakeLabel("连接名称:"));
        _connectionNameBox = MakeBox("连接名称（可选）", "", 140);
        connRow2.Children.Add(_connectionNameBox);
        connRow2.Children.Add(MakeLabel("数据库:"));
        _dbNameBox = MakeBox("数据库（可选，外挂连接预填）", "", 140);
        connRow2.Children.Add(_dbNameBox);
        connRow2.Children.Add(MakeLabel("密码:"));
        _passBox = MakePasswordBox("密码（仅本次连接）", 140);
        connRow2.Children.Add(_passBox);
        panel.Children.Add(connRow2);

        var connBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
        connBtnRow.Children.Add(MakeButton("外挂连接", LaunchDbxConnection, true, PackIconKind.OpenInApp));
        panel.Children.Add(connBtnRow);

        _dbTypeCombo.SelectedIndex = 0;  // 默认 MySQL，触发端口联动
        return panel;
    }

    /// <summary>切换 Tab 并同步按钮高亮（当前 Tab 按钮用 Raised 样式）</summary>
    private void SwitchTab(int index)
    {
        _tabControl.SelectedIndex = index;
        var active = TryFindResource("MaterialDesignRaisedButton") as Style;
        var inactive = TryFindResource("MaterialDesignOutlinedButton") as Style;
        _tabManageBtn.Style = index == 0 ? active : inactive;
        _tabConnectBtn.Style = index == 1 ? active : inactive;
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
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
