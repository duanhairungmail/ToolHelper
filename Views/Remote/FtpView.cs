using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using ToolHelper.Services;

namespace ToolHelper.Views.Remote;

/// <summary>
/// 文件列表数据模型（本地和远程共用）
/// </summary>
internal class SftpFileItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public PackIconKind IconKind { get; set; }
    public Brush IconColor { get; set; } = Brushes.Gray;
    public string Size { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Modified { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Group { get; set; } = "";
    public string Permissions { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsParent { get; set; }
}

public class FtpView : UserControl
{
    // 连接参数
    private TextBox _hostBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private PasswordBox _passBox = new();
    private Button _connectBtn = new();
    private Button _disconnectBtn = new();
    private TextBlock _statusText = new();
    private TextBlock _fileCountText = new();
    private TextBox _logBox = new();
    private SftpClient? _sftpClient;
    private bool _built;
    private StackPanel? _logPanel;

    // 本地面板
    private TextBox _localPathBox = new();
    private ListView _localListView = new();
    private TextBlock _localCountText = new();
    private string _localCurrentPath = "";
    private Border? _localListBorder;

    // 远程面板
    private TextBox _remotePathBox = new();
    private ListView _remoteListView = new();
    private TextBlock _remoteCountText = new();
    private string _remoteCurrentPath = "/";
    private Border? _remoteListBorder;

    public FtpView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();

        // 加载本地桌面路径
        _localCurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _localPathBox.Text = _localCurrentPath;
        RefreshLocalAsync();

        // 布局完成后计算可用高度
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ConstrainLayout();
            var win = Window.GetWindow(this);
            if (win != null)
                win.SizeChanged += (s, args) => ConstrainLayout();
        }));
    }

    private void ConstrainLayout()
    {
        if (_localListBorder == null || _remoteListBorder == null) return;
        try
        {
            var win = Window.GetWindow(this);
            if (win == null) return;

            var localTop = _localListBorder.TransformToAncestor(win).Transform(new Point(0, 0)).Y;
            var remoteTop = _remoteListBorder.TransformToAncestor(win).Transform(new Point(0, 0)).Y;
            var logHeight = _logPanel?.ActualHeight ?? 180;
            var listTop = Math.Max(localTop, remoteTop);

            var available = win.ActualHeight - listTop - logHeight - 20;
            if (available > 150)
            {
                _localListBorder.MaxHeight = available;
                _remoteListBorder.MaxHeight = available;
            }
        }
        catch { }
    }

    #region UI 工厂方法

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
        HintAssist.SetHint(tb, hint);
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
        HintAssist.SetHint(pb, hint);
        return pb;
    }

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
    };

    private Button MakeButton(string text, PackIconKind iconKind, Action handler, bool primary = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = iconKind, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
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

    private PackIcon MakeIcon(PackIconKind kind, double size = 20, Brush? foreground = null) => new()
    {
        Kind = kind, Width = size, Height = size,
        Foreground = foreground ?? Brushes.Gray,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static GridView CreateGridView()
    {
        var gridView = new GridView();

        // 名称列（含图标）
        var nameCol = new GridViewColumn { Header = "名称", Width = 220 };
        var nameTemplate = new DataTemplate();
        var nameFactory = new FrameworkElementFactory(typeof(StackPanel));
        nameFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var iconFactory = new FrameworkElementFactory(typeof(PackIcon));
        iconFactory.SetBinding(PackIcon.KindProperty, new Binding("IconKind"));
        iconFactory.SetValue(PackIcon.ForegroundProperty, new Binding("IconColor"));
        iconFactory.SetValue(FrameworkElement.WidthProperty, 18.0);
        iconFactory.SetValue(FrameworkElement.HeightProperty, 18.0);
        iconFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        iconFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        nameFactory.AppendChild(iconFactory);

        var nameTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        nameTextFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        nameTextFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
        nameTextFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        nameFactory.AppendChild(nameTextFactory);

        nameTemplate.VisualTree = nameFactory;
        nameCol.CellTemplate = nameTemplate;
        gridView.Columns.Add(nameCol);

        gridView.Columns.Add(new GridViewColumn { Header = "大小", Width = 90, DisplayMemberBinding = new Binding("Size") });
        gridView.Columns.Add(new GridViewColumn { Header = "修改时间", Width = 150, DisplayMemberBinding = new Binding("Modified") });
        gridView.Columns.Add(new GridViewColumn { Header = "权限", Width = 100, DisplayMemberBinding = new Binding("Permissions") });

        return gridView;
    }

    #endregion

    #region 构建 UI

    private void BuildUI()
    {
        var root = new DockPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // ===== 顶部区域 =====
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 标题行
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(MakeIcon(PackIconKind.ServerNetwork, 28, titleBrush));
        titleRow.Children.Add(new TextBlock
        {
            Text = "  SFTP 文件管理", FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);

        topPanel.Children.Add(new TextBlock
        {
            Text = "通过 SFTP 协议连接远程服务器，左侧浏览本地文件，右侧浏览远程文件，支持上传和下载。",
            FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 连接参数行
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow.Children.Add(MakeIcon(PackIconKind.Lan, 18, Brushes.Gray));
        connRow.Children.Add(MakeLabel("主机:"));
        _hostBox = MakeBox("IP 或主机名", "", 200);
        connRow.Children.Add(_hostBox);
        connRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeBox("端口", "22", 60);
        connRow.Children.Add(_portBox);
        connRow.Children.Add(MakeLabel("用户名:"));
        _userBox = MakeBox("用户名", "", 120);
        connRow.Children.Add(_userBox);
        connRow.Children.Add(MakeLabel("密码:"));
        _passBox = MakePasswordBox("密码", 120);
        connRow.Children.Add(_passBox);
        topPanel.Children.Add(connRow);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        _connectBtn = MakeButton("连接", PackIconKind.Login, () => ConnectAsync(), true);
        _disconnectBtn = MakeButton("断开", PackIconKind.Logout, () => DisconnectAsync());
        _disconnectBtn.IsEnabled = false;
        btnRow.Children.Add(_connectBtn);
        btnRow.Children.Add(_disconnectBtn);
        btnRow.Children.Add(MakeButton("上传", PackIconKind.Upload, () => UploadAsync()));
        btnRow.Children.Add(MakeButton("下载", PackIconKind.Download, () => DownloadAsync()));
        btnRow.Children.Add(MakeButton("删除", PackIconKind.Delete, () => DeleteAsync()));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnRow.Children.Add(_statusText);
        topPanel.Children.Add(btnRow);

        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // ===== 底部日志区域 =====
        var logBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            CornerRadius = new CornerRadius(4),
            Height = 130
        };

        _logBox.AcceptsReturn = true;
        _logBox.TextWrapping = TextWrapping.Wrap;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.IsReadOnly = true;
        _logBox.FontFamily = new FontFamily("Consolas");
        _logBox.FontSize = 12;
        _logBox.Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        _logBox.Background = Brushes.Transparent;
        _logBox.BorderThickness = new Thickness(0);
        _logBox.VerticalContentAlignment = VerticalAlignment.Top;
        _logBox.Padding = new Thickness(6, 4, 6, 4);
        logBorder.Child = _logBox;

        var logHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        logHeader.Children.Add(MakeIcon(PackIconKind.ConsoleLine, 16, Brushes.Gray));
        logHeader.Children.Add(new TextBlock { Text = "  操作日志", FontSize = 12, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
        logHeader.Children.Add(new TextBlock { Text = "  ", VerticalAlignment = VerticalAlignment.Center });
        _fileCountText.FontSize = 12;
        _fileCountText.Opacity = 0.5;
        _fileCountText.VerticalAlignment = VerticalAlignment.Center;
        logHeader.Children.Add(_fileCountText);

        var logPanel = new StackPanel();
        logPanel.Children.Add(logHeader);
        logPanel.Children.Add(logBorder);
        _logPanel = logPanel;
        DockPanel.SetDock(logPanel, Dock.Bottom);
        root.Children.Add(logPanel);

        // ===== 中间：左右双面板 =====
        var splitGrid = new Grid();
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧：本地文件面板
        var localPanel = new DockPanel { Margin = new Thickness(0, 8, 4, 0) };
        var localHeader = BuildPanelHeader("本地文件", PackIconKind.Harddisk,
            new SolidColorBrush(Color.FromRgb(33, 150, 243)), ref _localPathBox,
            () => RefreshLocalAsync(), () => LocalGoUp());
        DockPanel.SetDock(localHeader, Dock.Top);
        localPanel.Children.Add(localHeader);

        var localListBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        _localListView.View = CreateGridView();
        _localListView.FontFamily = new FontFamily("Microsoft YaHei");
        _localListView.FontSize = 13;
        _localListView.SelectionMode = SelectionMode.Single;
        _localListView.MouseDoubleClick += (s, e) => OnLocalDoubleClick();
        localListBorder.Child = _localListView;
        _localListBorder = localListBorder;
        localPanel.Children.Add(localListBorder);

        Grid.SetColumn(localPanel, 0);
        splitGrid.Children.Add(localPanel);

        // GridSplitter
        var splitter = new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            Margin = new Thickness(2, 8, 2, 0),
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetColumn(splitter, 1);
        splitGrid.Children.Add(splitter);

        // 右侧：远程文件面板
        var remotePanel = new DockPanel { Margin = new Thickness(4, 8, 0, 0) };
        var remoteHeader = BuildPanelHeader("远程文件", PackIconKind.ServerNetwork,
            new SolidColorBrush(Color.FromRgb(76, 175, 80)), ref _remotePathBox,
            () => RefreshRemoteAsync(), () => RemoteGoUp());
        DockPanel.SetDock(remoteHeader, Dock.Top);
        remotePanel.Children.Add(remoteHeader);

        var remoteListBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        _remoteListView.View = CreateGridView();
        _remoteListView.FontFamily = new FontFamily("Microsoft YaHei");
        _remoteListView.FontSize = 13;
        _remoteListView.SelectionMode = SelectionMode.Single;
        _remoteListView.MouseDoubleClick += (s, e) => OnRemoteDoubleClick();
        remoteListBorder.Child = _remoteListView;
        _remoteListBorder = remoteListBorder;
        remotePanel.Children.Add(remoteListBorder);

        Grid.SetColumn(remotePanel, 2);
        splitGrid.Children.Add(remotePanel);

        root.Children.Add(splitGrid);
        Content = root;
    }

    /// <summary>
    /// 构建面板顶部：标题 + 路径框 + 刷新/上级按钮
    /// </summary>
    private StackPanel BuildPanelHeader(string title, PackIconKind icon, Brush color,
        ref TextBox pathBox, Action refreshAction, Action goUpAction)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        // 标题行
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(MakeIcon(icon, 20, color));
        titleRow.Children.Add(new TextBlock
        {
            Text = $"  {title}", FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = color, VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);

        // 路径行
        var pathRow = new DockPanel();
        var goUpBtn = new Button
        {
            Content = new PackIcon { Kind = PackIconKind.ArrowUpBold, VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(0, 0, 4, 0),
            Style = TryFindResource("MaterialDesignOutlinedButton") as Style,
            ToolTip = "上级目录"
        };
        goUpBtn.Click += (s, e) => goUpAction();
        DockPanel.SetDock(goUpBtn, Dock.Left);
        pathRow.Children.Add(goUpBtn);

        var refreshBtn = new Button
        {
            Content = new PackIcon { Kind = PackIconKind.Refresh, VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(4, 0, 0, 0),
            Style = TryFindResource("MaterialDesignOutlinedButton") as Style,
            ToolTip = "刷新"
        };
        refreshBtn.Click += (s, e) => refreshAction();
        DockPanel.SetDock(refreshBtn, Dock.Right);
        pathRow.Children.Add(refreshBtn);

        var goBtn = new Button
        {
            Content = new PackIcon { Kind = PackIconKind.ArrowRight, VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(4, 0, 0, 0),
            Style = TryFindResource("MaterialDesignRaisedButton") as Style,
            MinWidth = 36,
            ToolTip = "前往"
        };
        goBtn.Click += (s, e) => { refreshAction(); };
        DockPanel.SetDock(goBtn, Dock.Right);
        pathRow.Children.Add(goBtn);

        pathBox = MakeBox("路径", "", 200);
        pathBox.Margin = new Thickness(0);
        pathBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { refreshAction(); e.Handled = true; } };
        pathRow.Children.Add(pathBox);

        header.Children.Add(pathRow);
        return header;
    }

    #endregion

    #region 本地文件浏览

    private void RefreshLocalAsync()
    {
        var path = _localPathBox.Text.Trim();
        if (string.IsNullOrEmpty(path)) return;
        _localCurrentPath = path;
        RefreshLocalInternal();
    }

    private void RefreshLocalInternal()
    {
        try
        {
            if (!Directory.Exists(_localCurrentPath))
            {
                Log($"本地路径不存在: {_localCurrentPath}");
                return;
            }

            var dirs = Directory.GetDirectories(_localCurrentPath);
            var files = Directory.GetFiles(_localCurrentPath);

            var items = new List<SftpFileItem>();

            // 父目录
            var parentDir = Directory.GetParent(_localCurrentPath);
            if (parentDir != null)
            {
                items.Add(new SftpFileItem
                {
                    Name = "..", FullPath = parentDir.FullName,
                    Size = "", Modified = "", Permissions = "",
                    IsDirectory = true, IsParent = true,
                    IconKind = PackIconKind.ArrowUpBoldCircle,
                    IconColor = new SolidColorBrush(Color.FromRgb(100, 149, 237))
                });
            }

            foreach (var d in dirs.OrderBy(d => Path.GetFileName(d)))
            {
                try
                {
                    var di = new DirectoryInfo(d);
                    items.Add(new SftpFileItem
                    {
                        Name = di.Name, FullPath = di.FullName,
                        Size = "", Modified = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        Permissions = GetLocalPermissions(di),
                        IsDirectory = true,
                        IconKind = PackIconKind.Folder,
                        IconColor = new SolidColorBrush(Color.FromRgb(255, 193, 7))
                    });
                }
                catch { }
            }

            foreach (var f in files.OrderBy(f => Path.GetFileName(f)))
            {
                try
                {
                    var fi = new FileInfo(f);
                    items.Add(CreateLocalFileItem(fi));
                }
                catch { }
            }

            _localListView.ItemsSource = items;
            _localCountText.Text = $"本地: {dirs.Length + files.Length} 项";
        }
        catch (Exception ex)
        {
            Log($"浏览本地目录失败: {ex.Message}");
        }
    }

    private SftpFileItem CreateLocalFileItem(FileInfo fi)
    {
        var item = new SftpFileItem
        {
            Name = fi.Name, FullPath = fi.FullName,
            Size = FormatSize(fi.Length), SizeBytes = fi.Length,
            Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            Permissions = fi.IsReadOnly ? "-r--r--r--" : "-rw-r--r--",
            IsDirectory = false
        };

        var ext = fi.Extension.ToLowerInvariant();
        (item.IconKind, item.IconColor) = ext switch
        {
            ".txt" or ".log" or ".md" or ".csv" => (PackIconKind.FileDocument, new SolidColorBrush(Color.FromRgb(66, 133, 244))),
            ".zip" or ".tar" or ".gz" or ".rar" or ".7z" => (PackIconKind.ZipBox, new SolidColorBrush(Color.FromRgb(255, 87, 34))),
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" => (PackIconKind.FileImage, new SolidColorBrush(Color.FromRgb(76, 175, 80))),
            ".mp4" or ".avi" or ".mkv" or ".mov" => (PackIconKind.FileVideo, new SolidColorBrush(Color.FromRgb(156, 39, 176))),
            ".mp3" or ".wav" or ".flac" or ".ogg" => (PackIconKind.FileMusic, new SolidColorBrush(Color.FromRgb(233, 30, 99))),
            ".sh" or ".py" or ".js" or ".cs" or ".java" or ".go" => (PackIconKind.FileCode, new SolidColorBrush(Color.FromRgb(0, 150, 136))),
            ".json" or ".xml" or ".yaml" or ".yml" or ".ini" or ".conf" => (PackIconKind.FileCog, new SolidColorBrush(Color.FromRgb(121, 85, 72))),
            ".pdf" => (PackIconKind.FilePdfBox, new SolidColorBrush(Color.FromRgb(244, 67, 54))),
            ".doc" or ".docx" => (PackIconKind.FileWord, new SolidColorBrush(Color.FromRgb(33, 150, 243))),
            ".xls" or ".xlsx" => (PackIconKind.FileExcel, new SolidColorBrush(Color.FromRgb(76, 175, 80))),
            ".exe" or ".msi" => (PackIconKind.Application, new SolidColorBrush(Color.FromRgb(96, 125, 139))),
            _ => (PackIconKind.File, new SolidColorBrush(Color.FromRgb(158, 158, 158)))
        };
        return item;
    }

    private static string GetLocalPermissions(DirectoryInfo di)
    {
        return di.Attributes.HasFlag(FileAttributes.ReadOnly) ? "dr--r--r--" : "drwxr-xr-x";
    }

    private void LocalGoUp()
    {
        var parent = Directory.GetParent(_localCurrentPath);
        if (parent == null) return;
        _localCurrentPath = parent.FullName;
        _localPathBox.Text = _localCurrentPath;
        RefreshLocalInternal();
    }

    private void OnLocalDoubleClick()
    {
        if (_localListView.SelectedItem is not SftpFileItem selected) return;
        if (selected.IsParent) { LocalGoUp(); return; }
        if (!selected.IsDirectory) return;

        _localCurrentPath = selected.FullPath;
        _localPathBox.Text = _localCurrentPath;
        RefreshLocalInternal();
    }

    #endregion

    #region SFTP 连接

    private async void ConnectAsync()
    {
        var host = _hostBox.Text.Trim();
        var portText = _portBox.Text.Trim();
        var user = _userBox.Text.Trim();
        var pass = _passBox.Password;

        if (string.IsNullOrEmpty(host)) { SetStatus("请输入主机地址", false); return; }
        if (string.IsNullOrEmpty(user)) { SetStatus("请输入用户名", false); return; }
        if (!int.TryParse(portText, out int port)) port = 22;

        try
        {
            await DisconnectInternalAsync();
            var connInfo = new ConnectionInfo(host, port, user,
                new PasswordAuthenticationMethod(user, pass)) { Timeout = TimeSpan.FromSeconds(10) };

            _sftpClient = new SftpClient(connInfo);
            await Task.Run(() => _sftpClient.Connect());

            _connectBtn.IsEnabled = false;
            _disconnectBtn.IsEnabled = true;
            SetConnFieldsEnabled(false);
            SetStatus($"已连接到 {host}:{port}", true);
            Log($"已连接到 {host}:{port}");

            _remoteCurrentPath = _sftpClient.WorkingDirectory ?? "/";
            _remotePathBox.Text = _remoteCurrentPath;
            await RefreshRemoteInternalAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"连接失败: {ex.Message}", false);
            Log($"连接失败: {ex.Message}");
            await DisconnectInternalAsync();
        }
    }

    public void SafeDisconnect()
    {
        try { _sftpClient?.Disconnect(); _sftpClient?.Dispose(); _sftpClient = null; } catch { }
    }

    private async void DisconnectAsync() => await DisconnectInternalAsync();

    private async Task DisconnectInternalAsync()
    {
        try
        {
            if (_sftpClient != null && _sftpClient.IsConnected)
                await Task.Run(() => _sftpClient.Disconnect());
            _sftpClient?.Dispose();
        }
        catch { }
        finally
        {
            _sftpClient = null;
            _connectBtn.IsEnabled = true;
            _disconnectBtn.IsEnabled = false;
            SetConnFieldsEnabled(true);
            _remoteListView.ItemsSource = null;
            _fileCountText.Text = "";
        }
    }

    #endregion

    #region 远程目录浏览

    private async void RefreshRemoteAsync() => await RefreshRemoteInternalAsync();

    private async Task RefreshRemoteInternalAsync()
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;
        var path = _remotePathBox.Text.Trim();
        if (!string.IsNullOrEmpty(path)) _remoteCurrentPath = path;

        try
        {
            Log($"正在打开远程目录 {_remoteCurrentPath}...");
            var items = await Task.Run(() => _sftpClient.ListDirectory(_remoteCurrentPath).ToList());

            var sorted = items
                .Where(i => i.Name != "." && i.Name != "..")
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Name)
                .ToList();

            var fileItems = new List<SftpFileItem>
            {
                new()
                {
                    Name = "..", FullPath = "",
                    Size = "", Modified = "", Owner = "", Group = "",
                    Permissions = "", IsDirectory = true, IsParent = true,
                    IconKind = PackIconKind.ArrowUpBoldCircle,
                    IconColor = new SolidColorBrush(Color.FromRgb(100, 149, 237))
                }
            };

            foreach (var item in sorted)
                fileItems.Add(CreateRemoteFileItem(item));

            _remoteListView.ItemsSource = fileItems;
            _fileCountText.Text = $"远程: {sorted.Count} 项";
            Log($"远程目录已加载 ({sorted.Count} 项)");
        }
        catch (Exception ex)
        {
            Log($"刷新远程目录失败: {ex.Message}");
            SetStatus($"刷新失败: {ex.Message}", false);
        }
    }

    private SftpFileItem CreateRemoteFileItem(ISftpFile item)
    {
        var remotePath = _remoteCurrentPath.TrimEnd('/') + "/" + item.Name;
        var fi = new SftpFileItem
        {
            Name = item.Name,
            FullPath = remotePath,
            Size = item.IsDirectory ? "" : FormatSize(item.Length),
            SizeBytes = item.IsDirectory ? 0 : item.Length,
            Modified = item.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            Owner = item.UserId.ToString(),
            Group = item.GroupId.ToString(),
            Permissions = FormatPermissions(item),
            IsDirectory = item.IsDirectory
        };

        if (item.IsDirectory)
        {
            fi.IconKind = PackIconKind.Folder;
            fi.IconColor = new SolidColorBrush(Color.FromRgb(255, 193, 7));
        }
        else if (item.IsSymbolicLink)
        {
            fi.IconKind = PackIconKind.Link;
            fi.IconColor = new SolidColorBrush(Color.FromRgb(0, 188, 212));
        }
        else
        {
            var ext = Path.GetExtension(item.Name).ToLowerInvariant();
            (fi.IconKind, fi.IconColor) = ext switch
            {
                ".txt" or ".log" or ".md" or ".csv" => (PackIconKind.FileDocument, new SolidColorBrush(Color.FromRgb(66, 133, 244))),
                ".zip" or ".tar" or ".gz" or ".rar" or ".7z" or ".bz2" => (PackIconKind.ZipBox, new SolidColorBrush(Color.FromRgb(255, 87, 34))),
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" => (PackIconKind.FileImage, new SolidColorBrush(Color.FromRgb(76, 175, 80))),
                ".mp4" or ".avi" or ".mkv" or ".mov" => (PackIconKind.FileVideo, new SolidColorBrush(Color.FromRgb(156, 39, 176))),
                ".mp3" or ".wav" or ".flac" or ".ogg" => (PackIconKind.FileMusic, new SolidColorBrush(Color.FromRgb(233, 30, 99))),
                ".sh" or ".py" or ".js" or ".cs" or ".java" or ".go" => (PackIconKind.FileCode, new SolidColorBrush(Color.FromRgb(0, 150, 136))),
                ".json" or ".xml" or ".yaml" or ".yml" or ".ini" or ".conf" => (PackIconKind.FileCog, new SolidColorBrush(Color.FromRgb(121, 85, 72))),
                ".pdf" => (PackIconKind.FilePdfBox, new SolidColorBrush(Color.FromRgb(244, 67, 54))),
                ".doc" or ".docx" => (PackIconKind.FileWord, new SolidColorBrush(Color.FromRgb(33, 150, 243))),
                ".xls" or ".xlsx" => (PackIconKind.FileExcel, new SolidColorBrush(Color.FromRgb(76, 175, 80))),
                _ => (PackIconKind.File, new SolidColorBrush(Color.FromRgb(158, 158, 158)))
            };
        }
        return fi;
    }

    private static string FormatPermissions(ISftpFile item)
    {
        try
        {
            var a = item.Attributes;
            if (a == null) return "";
            var p = new char[10];
            p[0] = item.IsDirectory ? 'd' : (item.IsSymbolicLink ? 'l' : '-');
            p[1] = a.OwnerCanRead ? 'r' : '-';
            p[2] = a.OwnerCanWrite ? 'w' : '-';
            p[3] = a.OwnerCanExecute ? 'x' : '-';
            p[4] = a.GroupCanRead ? 'r' : '-';
            p[5] = a.GroupCanWrite ? 'w' : '-';
            p[6] = a.GroupCanExecute ? 'x' : '-';
            p[7] = a.OthersCanRead ? 'r' : '-';
            p[8] = a.OthersCanWrite ? 'w' : '-';
            p[9] = a.OthersCanExecute ? 'x' : '-';
            return new string(p);
        }
        catch { return ""; }
    }

    private void RemoteGoUp()
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;
        var parts = _remoteCurrentPath.TrimEnd('/').Split('/');
        _remoteCurrentPath = parts.Length <= 1 ? "/" : string.Join("/", parts[..^1]);
        if (_remoteCurrentPath == "") _remoteCurrentPath = "/";
        _remotePathBox.Text = _remoteCurrentPath;
        _ = RefreshRemoteInternalAsync();
    }

    private void OnRemoteDoubleClick()
    {
        if (_remoteListView.SelectedItem is not SftpFileItem selected) return;
        if (selected.IsParent) { RemoteGoUp(); return; }
        if (!selected.IsDirectory) return;

        _remoteCurrentPath = _remoteCurrentPath.TrimEnd('/') + "/" + selected.Name;
        _remotePathBox.Text = _remoteCurrentPath;
        _ = RefreshRemoteInternalAsync();
    }

    #endregion

    #region 上传 / 下载 / 删除

    private async void UploadAsync()
    {
        if (_localListView.SelectedItem is not SftpFileItem selected || selected.IsParent)
        {
            SetStatus("请在左侧选择要上传的文件", false);
            return;
        }
        if (selected.IsDirectory) { SetStatus("不支持上传目录", false); return; }
        if (_sftpClient == null || !_sftpClient.IsConnected) { SetStatus("未连接", false); return; }

        try
        {
            var localPath = selected.FullPath;
            var fileName = selected.Name;
            var remotePath = _remoteCurrentPath.TrimEnd('/') + "/" + fileName;
            var sizeMb = selected.SizeBytes / (1024.0 * 1024);
            SetStatus($"上传中: {fileName}...", true);
            Log($"Uploading \"{localPath}\" to \"{remotePath}\" ({sizeMb:F2} MB)");

            await Task.Run(() =>
            {
                using var stream = File.OpenRead(localPath);
                _sftpClient.UploadFile(stream, remotePath, true);
            });

            SetStatus($"上传完成: {fileName}", true);
            Log($"上传完成!");
            await RefreshRemoteInternalAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"上传失败: {ex.Message}", false);
            Log($"上传失败: {ex.Message}");
        }
    }

    private async void DownloadAsync()
    {
        if (_remoteListView.SelectedItem is not SftpFileItem selected || selected.IsParent)
        {
            SetStatus("请在右侧选择要下载的文件", false);
            return;
        }
        if (selected.IsDirectory) { SetStatus("不支持下载目录", false); return; }
        if (_sftpClient == null || !_sftpClient.IsConnected) { SetStatus("未连接", false); return; }

        // 下载到左侧当前目录
        var localDir = _localCurrentPath;
        if (string.IsNullOrEmpty(localDir) || !Directory.Exists(localDir))
            localDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var localPath = Path.Combine(localDir, selected.Name);

        try
        {
            var remotePath = selected.FullPath;
            var sizeMb = selected.SizeBytes / (1024.0 * 1024);
            SetStatus($"下载中: {selected.Name}...", true);
            Log($"Downloading \"{remotePath}\" to \"{localPath}\" ({sizeMb:F2} MB)");

            await Task.Run(() =>
            {
                using var stream = File.Create(localPath);
                _sftpClient.DownloadFile(remotePath, stream);
            });

            SetStatus($"下载完成: {selected.Name} -> {localPath}", true);
            Log($"下载完成!");
            RefreshLocalInternal();
        }
        catch (Exception ex)
        {
            SetStatus($"下载失败: {ex.Message}", false);
            Log($"下载失败: {ex.Message}");
        }
    }

    private void DeleteAsync()
    {
        // 优先检查右侧远程选中
        if (_remoteListView.SelectedItem is SftpFileItem remoteItem && !remoteItem.IsParent)
            DeleteRemoteItemAsync(remoteItem);
        // 其次检查左侧本地选中
        else if (_localListView.SelectedItem is SftpFileItem localItem && !localItem.IsParent)
            DeleteLocalItem(localItem);
        else
            SetStatus("请选择要删除的文件或目录", false);
    }

    private async void DeleteRemoteItemAsync(SftpFileItem selected)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) { SetStatus("未连接", false); return; }

        var remotePath = selected.FullPath;
        try
        {
            await Task.Run(() =>
            {
                if (selected.IsDirectory) _sftpClient.DeleteDirectory(remotePath);
                else _sftpClient.DeleteFile(remotePath);
            });

            SetStatus($"已删除远程: {selected.Name}", true);
            Log($"已删除远程: {remotePath}");
            await RefreshRemoteInternalAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"删除失败: {ex.Message}", false);
            Log($"删除远程失败: {ex.Message}");
        }
    }

    private void DeleteLocalItem(SftpFileItem selected)
    {
        var localPath = selected.FullPath;
        try
        {
            if (selected.IsDirectory)
                Directory.Delete(localPath, true);
            else
                File.Delete(localPath);

            SetStatus($"已删除本地: {selected.Name}", true);
            Log($"已删除本地: {localPath}");
            RefreshLocalInternal();
        }
        catch (Exception ex)
        {
            SetStatus($"删除失败: {ex.Message}", false);
            Log($"删除本地失败: {ex.Message}");
        }
    }

    #endregion

    #region 辅助方法

    private void SetConnFieldsEnabled(bool enabled)
    {
        _hostBox.IsEnabled = enabled;
        _portBox.IsEnabled = enabled;
        _userBox.IsEnabled = enabled;
        _passBox.IsEnabled = enabled;
    }

    private void Log(string msg)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        _logBox.ScrollToEnd();
        FileLogger.Write("Sftp", msg);
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    #endregion
}

