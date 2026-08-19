using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using ToolHelper.Services;

namespace ToolHelper.Views.Database;

public class OpenGaussView : UserControl
{
    private TextBox _hostBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private PasswordBox _passBox = new();
    private ComboBox _dbCombo = new();
    private TextBox _sqlBox = new();
    private DataGrid _resultGrid = new();
    private Button _connectBtn = new();
    private Button _disconnectBtn = new();
    private TextBlock _statusText = new();
    private WrapPanel _filterPanel = new();
    private ScrollViewer _tableScroll = new();
    private UniformGrid _tableGrid = new() { Columns = 4 };
    private OpenGaussProxyClient? _proxy;
    private DataTable? _currentTable;
    private string? _currentTableName;
    private bool _built;
    private bool _loadingDatabases;
    private string? _rightClickColumnName;
    private const double MinViewHeight = 620; // 宿主视口过小时保持的视图最小高度（不足部分由宿主滚动条兜底）

    public OpenGaussView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        // 宿主内容区包裹了 ScrollViewer（无限高度约束），主区域的星号行会退化为按内容自然高度：
        // 查询结果行数一多就把 SQL 输入框、操作按钮、表头顶出可视区，故把视图高度钉在宿主视口高度上
        ViewportFitHelper.FitToViewport(this, MinViewHeight);
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

        // 顶部面板
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 标题行（图标 + 文字）
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Database, Width = 28, Height = 28, Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)), VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  openGauss 连接工具",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)), VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);

        topPanel.Children.Add(new TextBlock
        {
            Text = "连接 openGauss 国产数据库（基于 PostgreSQL 协议），浏览表结构，执行 SQL 查询并查看结果。",
            FontSize = 13,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 连接参数行
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow.Children.Add(MakeLabel("主机:"));
        _hostBox = MakeBox("IP或主机名", "", 180);
        connRow.Children.Add(_hostBox);
        connRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeBox("端口", "5432", 70);
        connRow.Children.Add(_portBox);
        connRow.Children.Add(MakeLabel("用户名:"));
        _userBox = MakeBox("用户名", "", 120);
        connRow.Children.Add(_userBox);
        connRow.Children.Add(MakeLabel("密码:"));
        _passBox = MakePasswordBox("密码");
        connRow.Children.Add(_passBox);
        topPanel.Children.Add(connRow);

        // Database selector row
        var dbRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        dbRow.Children.Add(MakeLabel("数据库:"));
        _dbCombo.FontFamily = new FontFamily("Microsoft YaHei");
        _dbCombo.FontSize = 13;
        _dbCombo.MinWidth = 200;
        _dbCombo.Margin = new Thickness(0, 0, 6, 0);
        var comboStyle = TryFindResource("MaterialDesignOutlinedComboBox") as Style;
        if (comboStyle != null) _dbCombo.Style = comboStyle;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_dbCombo, "选择数据库");
        _dbCombo.SelectionChanged += async (s, e) =>
        {
            if (_loadingDatabases) return;
            if (_dbCombo.SelectedItem is string db && !string.IsNullOrEmpty(db) && _proxy != null && _proxy.IsConnected)
            {
                try
                {
                    _proxy.SwitchDatabase(db);
                    await RefreshTablesAsync();
                }
                catch (Exception ex) { SetStatus($"切换数据库失败[{db}]: {ex.Message}", false); }
            }
        };
        dbRow.Children.Add(_dbCombo);
        _connectBtn = MakeButton("连接", Connect, true, PackIconKind.Login);
        _disconnectBtn = MakeButton("断开", Disconnect, false, PackIconKind.Logout);
        _disconnectBtn.IsEnabled = false;
        dbRow.Children.Add(_connectBtn);
        dbRow.Children.Add(_disconnectBtn);
        dbRow.Children.Add(MakeButton("连接测试", TestConnection, false, PackIconKind.Network));
        dbRow.Children.Add(MakeButton("刷新表列表", async () => await RefreshTablesAsync(), false, PackIconKind.Refresh));
        dbRow.Children.Add(MakeButton("备份", BackupDatabase, false, PackIconKind.BackupRestore));
        dbRow.Children.Add(MakeButton("恢复", RestoreDatabase, false, PackIconKind.DatabaseImport));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        dbRow.Children.Add(_statusText);
        topPanel.Children.Add(dbRow);

        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // Main area: left tables + right query
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Left: 4-column table list
        var leftPanel = new DockPanel();
        var tableLabel = new TextBlock { Text = "数据表", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(tableLabel, Dock.Top);
        leftPanel.Children.Add(tableLabel);
        var tableBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)), BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        _tableGrid.Children.Clear();
        var tableWrapper = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        tableWrapper.Children.Add(_tableGrid);
        _tableScroll.Content = tableWrapper;
        _tableScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _tableScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        tableBorder.Child = _tableScroll;
        leftPanel.Children.Add(tableBorder);
        Grid.SetColumn(leftPanel, 0);
        mainGrid.Children.Add(leftPanel);

        var splitter = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        mainGrid.Children.Add(splitter);

        // Right: SQL + results
        var rightPanel = new DockPanel();
        Grid.SetColumn(rightPanel, 2);
        mainGrid.Children.Add(rightPanel);

        // SQL editor
        var sqlPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var sqlLabel = new TextBlock { Text = "SQL 语句", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(sqlLabel, Dock.Top);
        sqlPanel.Children.Add(sqlLabel);
        var sqlDock = new DockPanel();
        var execBtn = new Button
        {
            Content = "执行", Margin = new Thickness(8, 0, 0, 0),
            Style = TryFindResource("MaterialDesignRaisedButton") as Style,
            MinWidth = 60, MinHeight = 80
        };
        execBtn.Click += async (s, e) => await ExecuteSqlAsync();
        DockPanel.SetDock(execBtn, Dock.Right);
        sqlDock.Children.Add(execBtn);
        _sqlBox.AcceptsReturn = true;
        _sqlBox.TextWrapping = TextWrapping.Wrap;
        _sqlBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _sqlBox.MinHeight = 80;
        _sqlBox.VerticalContentAlignment = VerticalAlignment.Top;
        _sqlBox.FontFamily = new FontFamily("Microsoft YaHei");
        _sqlBox.FontSize = 13;
        var sqlStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (sqlStyle != null) _sqlBox.Style = sqlStyle;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_sqlBox, "在此输入SQL语句，按F5或Ctrl+Enter执行");
        _sqlBox.KeyDown += async (s, e) =>
        {
            if (e.Key == Key.F5 || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control))
            { await ExecuteSqlAsync(); e.Handled = true; }
        };
        sqlDock.Children.Add(_sqlBox);
        DockPanel.SetDock(sqlDock, Dock.Top);
        sqlPanel.Children.Add(sqlDock);
        DockPanel.SetDock(sqlPanel, Dock.Top);
        rightPanel.Children.Add(sqlPanel);

        // Operation buttons row
        var opRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        opRow.Children.Add(MakeButton("新增行", AddRow, false, PackIconKind.Plus));
        opRow.Children.Add(MakeButton("删除选中行", DeleteSelectedRows, false, PackIconKind.Delete));
        opRow.Children.Add(MakeButton("保存更改", SaveChanges, true, PackIconKind.ContentSave));
        opRow.Children.Add(MakeButton("导出 CSV", ExportCsv, false, PackIconKind.FileDelimited));
        opRow.Children.Add(MakeButton("导出 JSON", ExportJson, false, PackIconKind.CodeJson));
        opRow.Children.Add(MakeButton("导入 CSV", ImportCsv, false, PackIconKind.FileImport));
        opRow.Children.Add(MakeButton("查看表结构", ShowTableSchema, false, PackIconKind.Table));
        DockPanel.SetDock(opRow, Dock.Top);
        rightPanel.Children.Add(opRow);

        // Filter area
        var filterLabel = new TextBlock { Text = "列筛选（输入关键字过滤）", FontSize = 11, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(filterLabel, Dock.Top);
        rightPanel.Children.Add(filterLabel);
        _filterPanel.Margin = new Thickness(0, 0, 0, 4);
        _filterPanel.MinHeight = 36;
        var filterScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 80 };
        filterScroll.Content = _filterPanel;
        DockPanel.SetDock(filterScroll, Dock.Top);
        rightPanel.Children.Add(filterScroll);

        // DataGrid result
        _resultGrid.AutoGenerateColumns = true;
        _resultGrid.CanUserAddRows = false;
        _resultGrid.CanUserDeleteRows = false;
        _resultGrid.CanUserSortColumns = true;
        _resultGrid.Sorting += OnDataGridSorting;
        _resultGrid.IsReadOnly = false;
        _resultGrid.SelectionMode = DataGridSelectionMode.Extended;
        _resultGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _resultGrid.FontFamily = new FontFamily("Microsoft YaHei");
        _resultGrid.FontSize = 12;
        _resultGrid.GridLinesVisibility = DataGridGridLinesVisibility.All;
        _resultGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));

        // 右键菜单：排序
        var gridMenu = new ContextMenu();
        var sortAscItem = new MenuItem { Header = "当前列 ↑ 升序排列" };
        sortAscItem.Click += (s2, e2) => SortDataGrid(null, "ASC");
        var sortDescItem = new MenuItem { Header = "当前列 ↓ 降序排列" };
        sortDescItem.Click += (s2, e2) => SortDataGrid(null, "DESC");
        var sortClearItem = new MenuItem { Header = "移除排序" };
        sortClearItem.Click += (s2, e2) => SortDataGrid(null, "CLEAR");
        gridMenu.Items.Add(sortAscItem);
        gridMenu.Items.Add(sortDescItem);
        gridMenu.Items.Add(sortClearItem);
        _resultGrid.ContextMenu = gridMenu;
        _resultGrid.PreviewMouseRightButtonDown += (s2, e2) =>
        {
            var dep = e2.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridColumnHeader)
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            if (dep is DataGridColumnHeader header && header.Column != null)
                _rightClickColumnName = header.Column.Header?.ToString();
        };
        rightPanel.Children.Add(_resultGrid);

        root.Children.Add(mainGrid);

        Content = root;
    }

    // ========== Connection Test ==========

    private async void TestConnection()
    {
        var host = _hostBox.Text.Trim();
        var portText = _portBox.Text.Trim();
        var user = _userBox.Text.Trim();
        var pass = _passBox.Password;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user))
        { SetStatus("请输入主机和用户名", false); return; }
        if (!int.TryParse(portText, out int port)) port = 5432;

        SetStatus("正在测试连接...", true);
        try
        {
            using var testProxy = new OpenGaussProxyClient();
            await testProxy.ConnectAsync(host, portText, user, pass);
            var version = await testProxy.GetVersionAsync();
            var dbs = await testProxy.GetDatabasesAsync();

            var verShort = version;
            if (verShort.Length > 60) verShort = verShort[..60] + "...";
            SetStatus($"连接测试成功! {verShort} — 共 {dbs.Count} 个数据库", true);
        }
        catch (Exception ex)
        {
            SetStatus($"连接测试失败: {ex.Message}", false);
        }
    }

    // ========== Connection ==========

    private async void Connect()
    {
        var host = _hostBox.Text.Trim();
        var portText = _portBox.Text.Trim();
        var user = _userBox.Text.Trim();
        var pass = _passBox.Password;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user))
        { SetStatus("请输入主机和用户名", false); return; }
        if (!int.TryParse(portText, out int port)) port = 5432;

        try
        {
            Disconnect();
            SetStatus("正在连接...", true);
            _connectBtn.IsEnabled = false;
            _proxy = new OpenGaussProxyClient();
            await _proxy.ConnectAsync(host, portText, user, pass);
            _disconnectBtn.IsEnabled = true;
            SetConnFieldsEnabled(false);
            SetStatus($"已连接到 {host}:{port}", true);
            await LoadDatabasesAsync();
        }
        catch (Exception ex)
        { SetStatus($"连接失败: {ex.Message}", false); _connectBtn.IsEnabled = true; Disconnect(); }
    }

    private async Task LoadDatabasesAsync()
    {
        if (_proxy == null) return;
        try
        {
            _loadingDatabases = true;
            _dbCombo.Items.Clear();
            var dbList = await _proxy.GetDatabasesAsync();

            foreach (var db in dbList)
                _dbCombo.Items.Add(db);

            // 默认选中 postgres
            var defaultIdx = dbList.IndexOf("firesys_station");
            if (defaultIdx >= 0) _dbCombo.SelectedIndex = defaultIdx;
            else if (_dbCombo.Items.Count > 0) _dbCombo.SelectedIndex = 0;
            _loadingDatabases = false;

            // 切换到选中的数据库并加载表
            if (_dbCombo.SelectedItem is string firstDb && !string.IsNullOrEmpty(firstDb))
            {
                try
                {
                    _proxy.SwitchDatabase(firstDb);
                    await RefreshTablesAsync();
                }
                catch (Exception ex)
                {
                    SetStatus($"切换数据库失败[{firstDb}]: {ex.Message}", false);
                }
            }
        }
        catch (Exception ex) { _loadingDatabases = false; SetStatus($"加载数据库列表失败: {ex.Message}", false); }
    }

    public void SafeDisconnect() { try { Disconnect(); } catch { } }

    private void Disconnect()
    {
        try { _proxy?.StopProxy(); _proxy?.Dispose(); } catch { }
        finally
        {
            _proxy = null;
            _connectBtn.IsEnabled = true;
            _disconnectBtn.IsEnabled = false;
            SetConnFieldsEnabled(true);
            _dbCombo.Items.Clear();
            _tableGrid.Children.Clear();
            _resultGrid.ItemsSource = null;
            _filterPanel.Children.Clear();
            _currentTable = null;
            _currentTableName = null;
        }
    }

    private void SetConnFieldsEnabled(bool enabled)
    {
        _hostBox.IsEnabled = enabled; _portBox.IsEnabled = enabled;
        _userBox.IsEnabled = enabled; _passBox.IsEnabled = enabled;
        _dbCombo.IsEnabled = enabled;
    }

    // ========== Table list ==========

    private async Task RefreshTablesAsync()
    {
        if (_proxy == null || !_proxy.IsConnected)
        { SetStatus("未连接", false); return; }
        try
        {
            SetStatus("加载表列表中...", true);
            var tables = await _proxy.GetTablesAsync();

            _tableGrid.Children.Clear();
            foreach (var table in tables)
            {
                var btn = new Button
                {
                    Content = table,
                    Margin = new Thickness(2),
                    FontSize = 11,
                    FontFamily = new FontFamily("Microsoft YaHei"),
                    Style = TryFindResource("MaterialDesignOutlinedButton") as Style,
                    ToolTip = $"双击打开 {table}，右键查看更多选项",
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(4, 2, 4, 2),
                    MinWidth = 60
                };
                btn.PreviewMouseLeftButtonDown += async (s, e) =>
                {
                    if (e.ClickCount >= 2) { await OpenTableAsync(table); e.Handled = true; }
                };
                var menu = new ContextMenu();
                var openItem = new MenuItem { Header = "打开表" };
                openItem.Click += async (s2, e2) => await OpenTableAsync(table);
                var schemaItem = new MenuItem { Header = "查看表结构" };
                schemaItem.Click += async (s2, e2) => await ShowSchemaAsync(table);
                menu.Items.Add(openItem);
                menu.Items.Add(schemaItem);
                btn.ContextMenu = menu;
                _tableGrid.Children.Add(btn);
            }
            SetStatus($"已加载 {tables.Count} 张表", true);
        }
        catch (Exception ex) { SetStatus($"刷新失败: {ex.Message}", false); }
    }

    private async Task OpenTableAsync(string tableName)
    {
        _sqlBox.Text = $"SELECT * FROM \"{tableName}\";";
        _currentTableName = tableName;
        await ExecuteSqlAsync();
    }

    private void ShowTableSchema()
    {
        if (_currentTableName == null) { SetStatus("请先打开一张表", false); return; }
        _ = ShowSchemaAsync(_currentTableName);
    }

    private async Task ShowSchemaAsync(string tableName)
    {
        _sqlBox.Text = $"SELECT column_name AS \"列名\", data_type AS \"类型\", is_nullable AS \"可空\", column_default AS \"默认值\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{tableName}' ORDER BY ordinal_position;";
        try { await ExecuteSqlAsync(); }
        catch (Exception ex) { SetStatus($"查看结构失败: {ex.Message}", false); }
    }

    // ========== SQL ==========

    private async Task ExecuteSqlAsync()
    {
        var sql = _sqlBox.Text.Trim();
        if (string.IsNullOrEmpty(sql)) { SetStatus("请输入 SQL 语句", false); return; }
        if (_proxy == null || !_proxy.IsConnected)
        { SetStatus("未连接", false); return; }
        try
        {
            SetStatus("执行中...", true);
            var tableMatch = System.Text.RegularExpressions.Regex.Match(sql, @"(?i)FROM\s+""?(\w+)""?");
            if (tableMatch.Success) _currentTableName = tableMatch.Groups[1].Value;

            var dt = await _proxy.ExecuteQueryAsync(sql);

            if (dt.Columns.Count > 0)
            {
                _currentTable = dt;
                _resultGrid.ItemsSource = dt.DefaultView;
                BuildFilterPanel(dt);
                SetStatus($"查询成功，返回 {dt.Rows.Count} 行", true);
            }
            else
            {
                var affected = await _proxy.ExecuteNonQueryAsync(sql);
                _resultGrid.ItemsSource = null;
                _filterPanel.Children.Clear();
                _currentTable = null;
                SetStatus($"执行成功，影响 {affected} 行", true);
            }
        }
        catch (Exception ex) { SetStatus($"执行失败: {ex.Message}", false); }
    }

    // ========== Filter ==========

    private void OnDataGridSorting(object? sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var view = _currentTable?.DefaultView;
        if (view == null) return;

        var colName = e.Column.Header?.ToString() ?? e.Column.SortMemberPath;
        try
        {
            var current = view.Sort;
            if (string.IsNullOrEmpty(current) || !current.StartsWith($"[{colName}]"))
            {
                view.Sort = $"[{colName}] ASC";
                e.Column.SortDirection = ListSortDirection.Ascending;
            }
            else if (current.Contains("ASC"))
            {
                view.Sort = $"[{colName}] DESC";
                e.Column.SortDirection = ListSortDirection.Descending;
            }
            else
            {
                view.Sort = "";
                e.Column.SortDirection = null;
            }
        }
        catch (Exception ex) { SetStatus($"排序失败: {ex.Message}", false); }
    }

    private void SortDataGrid(string? columnName, string direction)
    {
        var view = _currentTable?.DefaultView;
        if (view == null) return;
        var col = columnName ?? _rightClickColumnName;
        if (string.IsNullOrEmpty(col)) { SetStatus("请右键列头选择排序", false); return; }
        try
        {
            if (direction == "CLEAR")
            {
                view.Sort = "";
                foreach (var c in _resultGrid.Columns) c.SortDirection = null;
                SetStatus("已移除排序", true);
            }
            else
            {
                view.Sort = $"[{col}] {direction}";
                foreach (var c in _resultGrid.Columns)
                    c.SortDirection = c.Header?.ToString() == col
                        ? (direction == "ASC" ? ListSortDirection.Ascending : ListSortDirection.Descending)
                        : null;
                SetStatus($"已按 [{col}] {(direction == "ASC" ? "升序" : "降序")}", true);
            }
        }
        catch (Exception ex) { SetStatus($"排序失败: {ex.Message}", false); }
    }

    private Dictionary<string, TextBox> _filterBoxes = new();

    private void BuildFilterPanel(DataTable dt)
    {
        _filterPanel.Children.Clear();
        _filterBoxes.Clear();
        var tbStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        foreach (DataColumn col in dt.Columns)
        {
            var sp = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 6, 0), MinWidth = 100 };
            var lbl = new TextBlock { Text = col.ColumnName, FontSize = 10, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 1) };
            sp.Children.Add(lbl);
            var tb = new TextBox
            {
                MinWidth = 90, FontSize = 11, FontFamily = new FontFamily("Microsoft YaHei"),
                Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2)
            };
            if (tbStyle != null) tb.Style = tbStyle;
            MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, "输入筛选");
            tb.TextChanged += (s, e) => ApplyFilter();
            sp.Children.Add(tb);
            _filterPanel.Children.Add(sp);
            _filterBoxes[col.ColumnName] = tb;
        }
    }

    private void ApplyFilter()
    {
        if (_currentTable == null) return;
        var view = _currentTable.DefaultView;
        var conditions = new List<string>();
        foreach (var kv in _filterBoxes)
        {
            var val = kv.Value.Text.Trim();
            if (!string.IsNullOrEmpty(val))
            {
                var escaped = val.Replace("'", "''").Replace("[", "[[").Replace("]", "]]");
                conditions.Add($"[{kv.Key}] LIKE '%{escaped}%'");
            }
        }
        try { view.RowFilter = conditions.Count > 0 ? string.Join(" AND ", conditions) : ""; }
        catch (Exception ex) { SetStatus($"筛选错误: {ex.Message}", false); }
    }

    // ========== CRUD ==========

    private void AddRow()
    {
        if (_currentTable == null) { SetStatus("请先查询一张表", false); return; }
        var row = _currentTable.NewRow();
        _currentTable.Rows.Add(row);
        SetStatus("已新增一行，编辑后点击[保存更改]写入数据库", true);
    }

    private void DeleteSelectedRows()
    {
        if (_resultGrid.SelectedItems.Count == 0) { SetStatus("请先选中要删除的行", false); return; }
        var selected = new List<DataRowView>();
        foreach (var item in _resultGrid.SelectedItems)
            if (item is DataRowView drv) selected.Add(drv);
        foreach (var drv in selected) drv.Row.Delete();
        SetStatus($"已标记删除 {selected.Count} 行，点击[保存更改]写入数据库", true);
    }

    private async void SaveChanges()
    {
        if (_currentTable == null || _currentTableName == null || _proxy == null)
        { SetStatus("无可保存的数据", false); return; }
        var changes = _currentTable.GetChanges();
        if (changes == null || changes.Rows.Count == 0)
        { SetStatus("没有需要保存的更改", false); return; }

        try
        {
            SetStatus("保存中...", true);
            int saved = 0, errors = 0;
            foreach (DataRow row in changes.Rows)
            {
                try
                {
                    if (row.RowState == DataRowState.Deleted)
                    {
                        var origVals = row.ItemArray;
                        var pkCols = _currentTable.PrimaryKey;
                        var where = pkCols.Length > 0
                            ? string.Join(" AND ", pkCols.Select(pk => $"\"{pk.ColumnName}\" = {FormatPgValue(origVals[pk.Ordinal])}"))
                            : BuildWhereFromOriginal(row);
                        await _proxy!.ExecuteNonQueryAsync($"DELETE FROM \"{_currentTableName}\" WHERE {where}");
                    }
                    else if (row.RowState == DataRowState.Added)
                    {
                        var cols = string.Join(", ", _currentTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));
                        var vals = string.Join(", ", _currentTable.Columns.Cast<DataColumn>().Select(c => FormatPgValue(row[c])));
                        await _proxy!.ExecuteNonQueryAsync($"INSERT INTO \"{_currentTableName}\" ({cols}) VALUES ({vals})");
                    }
                    else if (row.RowState == DataRowState.Modified)
                    {
                        var pkCols = _currentTable.PrimaryKey;
                        var where = pkCols.Length > 0
                            ? string.Join(" AND ", pkCols.Select(pk => $"\"{pk.ColumnName}\" = {FormatPgValue(row[pk.ColumnName])}"))
                            : BuildWhereFromOriginal(row);
                        var sets = string.Join(", ", _currentTable.Columns.Cast<DataColumn>()
                            .Where(c => !pkCols.Contains(c))
                            .Select(c => $"\"{c.ColumnName}\" = {FormatPgValue(row[c])}"));
                        if (!string.IsNullOrEmpty(sets))
                        {
                            await _proxy!.ExecuteNonQueryAsync($"UPDATE \"{_currentTableName}\" SET {sets} WHERE {where}");
                        }
                    }
                    saved++;
                }
                catch { errors++; }
            }
            _currentTable.AcceptChanges();
            SetStatus($"保存完成: 成功 {saved} 行" + (errors > 0 ? $", 失败 {errors} 行" : ""), errors == 0);
            if (_currentTableName != null) await OpenTableAsync(_currentTableName);
        }
        catch { throw; }
    }

    private string FormatPgValue(object? val)
    {
        if (val == null || val == DBNull.Value) return "NULL";
        if (val is int or long or short or byte or decimal or float or double) return val.ToString()!;
        if (val is bool b) return b ? "true" : "false";
        return $"'{val.ToString()!.Replace("'", "''")}'";
    }

    private string BuildWhereFromOriginal(DataRow row)
    {
        var parts = new List<string>();
        foreach (DataColumn col in _currentTable!.Columns)
        {
            var origVal = row[col, DataRowVersion.Original];
            parts.Add($"\"{col.ColumnName}\" = {FormatPgValue(origVal)}");
            if (parts.Count >= 3) break;
        }
        return string.Join(" AND ", parts);
    }

    // ========== Import / Export ==========

    private void ExportCsv()
    {
        if (_currentTable == null) { SetStatus("无数据可导出", false); return; }
        var dlg = new SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = $"{_currentTableName ?? "export"}.csv" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", _currentTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\"")));
            foreach (DataRow row in _currentTable.Rows)
            {
                sb.AppendLine(string.Join(",", row.ItemArray.Select(v => v == null || v == DBNull.Value ? "" : $"\"{v.ToString()!.Replace("\"", "\"\"")}\"")));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            SetStatus($"导出成功: {dlg.FileName}", true);
        }
        catch (Exception ex) { SetStatus($"导出失败: {ex.Message}", false); }
    }

    private void ExportJson()
    {
        if (_currentTable == null) { SetStatus("无数据可导出", false); return; }
        var dlg = new SaveFileDialog { Filter = "JSON 文件 (*.json)|*.json", FileName = $"{_currentTableName ?? "export"}.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var list = new List<Dictionary<string, object?>>();
            foreach (DataRow row in _currentTable.Rows)
            {
                var dict = new Dictionary<string, object?>();
                foreach (DataColumn col in _currentTable.Columns)
                    dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                list.Add(dict);
            }
            File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(list, Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
            SetStatus($"导出成功: {dlg.FileName}", true);
        }
        catch (Exception ex) { SetStatus($"导出失败: {ex.Message}", false); }
    }

    private void ImportCsv()
    {
        if (_currentTable == null || _currentTableName == null) { SetStatus("请先打开一张表", false); return; }
        var dlg = new OpenFileDialog { Filter = "CSV 文件 (*.csv)|*.csv" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
            if (lines.Length < 2) { SetStatus("CSV 文件为空", false); return; }
            var headers = ParseCsvLine(lines[0]);
            int imported = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var vals = ParseCsvLine(lines[i]);
                if (vals.Length == 0) continue;
                var row = _currentTable.NewRow();
                for (int j = 0; j < Math.Min(headers.Length, vals.Length); j++)
                {
                    var colName = headers[j].Trim('"');
                    if (_currentTable.Columns.Contains(colName))
                        row[colName] = string.IsNullOrEmpty(vals[j]) ? DBNull.Value : (object)vals[j];
                }
                _currentTable.Rows.Add(row);
                imported++;
            }
            SetStatus($"已导入 {imported} 行，点击[保存更改]写入数据库", true);
        }
        catch (Exception ex) { SetStatus($"导入失败: {ex.Message}", false); }
    }

    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    // ========== 备份 ==========

    private async void BackupDatabase()
    {
        if (_proxy == null || !_proxy.IsConnected)
        { SetStatus("请先连接数据库", false); return; }

        var db = _proxy.CurrentDatabase;
        if (string.IsNullOrEmpty(db))
        { SetStatus("请先选择数据库", false); return; }

        // 获取所有表
        var allTables = new List<string>();
        try
        {
            allTables = await _proxy!.GetTablesAsync();
        }
        catch (Exception ex) { SetStatus($"获取表列表失败: {ex.Message}", false); return; }

        if (allTables.Count == 0) { SetStatus("当前数据库没有表", false); return; }

        var selectedTables = ShowBackupSelectionDialog(allTables, db);
        if (selectedTables == null || selectedTables.Count == 0) return;

        var dlg = new SaveFileDialog
        {
            Title = "选择备份文件保存路径",
            Filter = "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*",
            FileName = $"{db}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            DefaultExt = ".sql"
        };
        if (dlg.ShowDialog() != true) return;

        SetStatus("正在备份...", true);
        try
        {
            await Task.Run(async () =>
            {
                var sb = new StringBuilder();
                sb.AppendLine($"-- openGauss Database Backup");
                sb.AppendLine($"-- Database: {db}");
                sb.AppendLine($"-- Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"-- Tables: {string.Join(", ", selectedTables)}");
                sb.AppendLine();
                sb.AppendLine("SET client_encoding = 'UTF8';");
                sb.AppendLine();

                foreach (var table in selectedTables)
                {
                    sb.AppendLine($"-- ----------------------------");
                    sb.AppendLine($"-- Table: {table}");
                    sb.AppendLine($"-- ----------------------------");
                    sb.AppendLine($"DROP TABLE IF EXISTS \"{table}\" CASCADE;");

                    var columns = new List<string>();
                    var colDt = await _proxy!.ExecuteQueryAsync($@"
                        SELECT column_name, data_type, is_nullable, column_default,
                               character_maximum_length, numeric_precision
                        FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = '{table}'
                        ORDER BY ordinal_position");
                    foreach (DataRow colRow in colDt.Rows)
                    {
                        var colName = colRow[0]?.ToString() ?? "";
                        var dataType = colRow[1]?.ToString() ?? "";
                        var nullable = colRow[2]?.ToString() == "YES" ? "" : " NOT NULL";
                        var defVal = string.IsNullOrEmpty(colRow[3]?.ToString()) ? "" : $" DEFAULT {colRow[3]}";
                        var maxLen = colRow[4]?.ToString();
                        var pgType = dataType.ToUpper() switch
                        {
                            "INTEGER" => "INTEGER",
                            "BIGINT" => "BIGINT",
                            "SMALLINT" => "SMALLINT",
                            "BOOLEAN" => "BOOLEAN",
                            "TEXT" => "TEXT",
                            "DATE" => "DATE",
                            "TIMESTAMP WITHOUT TIME ZONE" => "TIMESTAMP",
                            "TIMESTAMP WITH TIME ZONE" => "TIMESTAMPTZ",
                            "CHARACTER VARYING" => $"VARCHAR({(string.IsNullOrEmpty(maxLen) ? "255" : maxLen)})",
                            _ => dataType
                        };
                        columns.Add($"    \"{colName}\" {pgType}{nullable}{defVal}");
                    }

                    if (columns.Count > 0)
                    {
                        sb.AppendLine($"CREATE TABLE \"{table}\" (");
                        sb.AppendLine(string.Join(",\n", columns));
                        sb.AppendLine(");");
                        sb.AppendLine();
                    }

                    var dataDt = await _proxy.ExecuteQueryAsync($"SELECT * FROM \"{table}\"");
                    if (dataDt.Rows.Count > 0)
                    {
                        var colNames = new List<string>();
                        foreach (DataColumn dc in dataDt.Columns)
                            colNames.Add($"\"{dc.ColumnName}\"");
                        var colList = string.Join(", ", colNames);
                        foreach (DataRow dataRow in dataDt.Rows)
                        {
                            var vals = new List<string>();
                            foreach (DataColumn dc in dataDt.Columns)
                            {
                                var v = dataRow[dc];
                                if (v == null || v == DBNull.Value) vals.Add("NULL");
                                else vals.Add($"'{v.ToString()?.Replace("'", "''")}'");
                            }
                            sb.AppendLine($"INSERT INTO \"{table}\" ({colList}) VALUES ({string.Join(", ", vals)});");
                        }
                    }
                    sb.AppendLine();
                }

                await File.WriteAllTextAsync(dlg.FileName, sb.ToString(), Encoding.UTF8);
            });

            SetStatus($"备份成功: {dlg.FileName}", true);
        }
        catch (Exception ex) { SetStatus($"备份失败: {ex.Message}", false); }
    }

    private List<string>? ShowBackupSelectionDialog(List<string> allTables, string dbName)
    {
        var win = new Window
        {
            Title = $"选择备份表 - {dbName}",
            Width = 400, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "请选择需要备份的数据表:", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

        var selectAllCb = new CheckBox { Content = "全选/取消全选", IsChecked = true, Margin = new Thickness(0, 0, 0, 8), FontSize = 13 };
        panel.Children.Add(selectAllCb);

        var listBox = new ListBox { Height = 350, FontSize = 12, FontFamily = new FontFamily("Microsoft YaHei") };
        var checkBoxes = new List<CheckBox>();
        foreach (var table in allTables)
        {
            var cb = new CheckBox { Content = table, IsChecked = true, Margin = new Thickness(4, 2, 4, 2) };
            listBox.Items.Add(cb);
            checkBoxes.Add(cb);
        }
        panel.Children.Add(listBox);

        selectAllCb.Checked += (s, e) => { foreach (var cb in checkBoxes) cb.IsChecked = true; };
        selectAllCb.Unchecked += (s, e) => { foreach (var cb in checkBoxes) cb.IsChecked = false; };

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        var okBtn = new Button { Content = "确认备份", Width = 120, Margin = new Thickness(0, 0, 16, 0), Style = TryFindResource("MaterialDesignRaisedButton") as Style };
        okBtn.Click += (s, e) => { win.DialogResult = true; win.Close(); };
        var cancelBtn = new Button { Content = "取消", Width = 120, Style = TryFindResource("MaterialDesignOutlinedButton") as Style };
        cancelBtn.Click += (s, e) => { win.DialogResult = false; win.Close(); };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        win.Content = panel;
        if (win.ShowDialog() != true) return null;

        return checkBoxes.Where(cb => cb.IsChecked == true).Select(cb => cb.Content?.ToString()!).Where(n => !string.IsNullOrEmpty(n)).ToList();
    }

    // ========== 恢复 ==========

    private async void RestoreDatabase()
    {
        if (_proxy == null || !_proxy.IsConnected)
        { SetStatus("请先连接数据库", false); return; }

        var dlg = new OpenFileDialog
        {
            Title = "选择要恢复的备份文件",
            Filter = "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        SetStatus("正在恢复...", true);
        try
        {
            var sqlContent = await File.ReadAllTextAsync(dlg.FileName, Encoding.UTF8);
            int successCount = 0, errorCount = 0;
            var statements = sqlContent.Split(';')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s) && !s.StartsWith("--"));

            foreach (var stmt in statements)
            {
                try { await _proxy!.ExecuteNonQueryAsync(stmt); successCount++; }
                catch { errorCount++; }
            }
            await RefreshTablesAsync();
            SetStatus($"恢复完成: 成功 {successCount} 条, 失败 {errorCount} 条", errorCount == 0);
        }
        catch (Exception ex) { SetStatus($"恢复失败: {ex.Message}", false); }
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }
}
