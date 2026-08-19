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
using MySqlConnector;
using Newtonsoft.Json;

namespace ToolHelper.Views.Database;

public class MySqlView : UserControl
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
    private MySqlConnection? _connection;
    private DataTable? _currentTable;
    private string? _currentTableName;
    private bool _built;
    private bool _loadingDatabases;
    private string? _rightClickColumnName;
    private const double MinViewHeight = 620; // 宿主视口过小时保持的视图最小高度（不足部分由宿主滚动条兜底）

    public MySqlView()
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

        // Top panel
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        // 标题行（图标 + 文字）
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Database, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  MySQL 连接工具",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);
        topPanel.Children.Add(new TextBlock
        {
            Text = "\u8fde\u63a5 MySQL \u6570\u636e\u5e93\uff0c\u6d4f\u89c8\u8868\u7ed3\u6784\uff0c\u6267\u884c SQL \u8bed\u53e5\uff0c\u67e5\u770b\u7ed3\u679c\u5e76\u8fdb\u884c\u6570\u636e\u7f16\u8f91\u3002",
            FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Connection row
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow.Children.Add(MakeLabel("\u4e3b\u673a:"));
        _hostBox = MakeBox("IP\u6216\u4e3b\u673a\u540d", "", 180);
        connRow.Children.Add(_hostBox);
        connRow.Children.Add(MakeLabel("\u7aef\u53e3:"));
        _portBox = MakeBox("\u7aef\u53e3", "3306", 70);
        connRow.Children.Add(_portBox);
        connRow.Children.Add(MakeLabel("\u7528\u6237\u540d:"));
        _userBox = MakeBox("\u7528\u6237\u540d", "", 120);
        connRow.Children.Add(_userBox);
        connRow.Children.Add(MakeLabel("\u5bc6\u7801:"));
        _passBox = MakePasswordBox("\u5bc6\u7801");
        connRow.Children.Add(_passBox);
        topPanel.Children.Add(connRow);

        // Database selector row
        var dbRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        dbRow.Children.Add(MakeLabel("\u6570\u636e\u5e93:"));
        _dbCombo.FontFamily = new FontFamily("Microsoft YaHei");
        _dbCombo.FontSize = 13;
        _dbCombo.MinWidth = 200;
        _dbCombo.Margin = new Thickness(0, 0, 6, 0);
        var comboStyle = TryFindResource("MaterialDesignOutlinedComboBox") as Style;
        if (comboStyle != null) _dbCombo.Style = comboStyle;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_dbCombo, "\u9009\u62e9\u6570\u636e\u5e93");
        _dbCombo.SelectionChanged += async (s, e) =>
        {
            // 防止加载数据库列表时触发切换
            if (_loadingDatabases) return;
            if (_dbCombo.SelectedItem is string db && !string.IsNullOrEmpty(db) && _connection != null
                && _connection.State == ConnectionState.Open)
            {
                try
                {
                    using var cmd = new MySqlCommand($"USE `{db}`", _connection);
                    await cmd.ExecuteNonQueryAsync();
                    await RefreshTablesAsync();
                }
                catch (Exception ex) { SetStatus($"\u5207\u6362\u6570\u636e\u5e93\u5931\u8d25[{db}]: {ex.Message}", false); }
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
        var tableLabel = new TextBlock { Text = "\u6570\u636e\u8868", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(tableLabel, Dock.Top);
        leftPanel.Children.Add(tableLabel);
        var tableBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)), BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        _tableGrid.Children.Clear();
        // 用 StackPanel 包裹 UniformGrid，防止在 ScrollViewer 中拉大间距
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
        var sqlLabel = new TextBlock { Text = "SQL \u8bed\u53e5", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(sqlLabel, Dock.Top);
        sqlPanel.Children.Add(sqlLabel);
        var sqlDock = new DockPanel();
        var execBtn = new Button
        {
            Content = "\u6267\u884c", Margin = new Thickness(8, 0, 0, 0),
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
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_sqlBox, "\u5728\u6b64\u8f93\u5165SQL\u8bed\u53e5\uff0c\u6309F5\u6216Ctrl+Enter\u6267\u884c");
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
        var filterLabel = new TextBlock { Text = "\u5217\u7b5b\u9009\uff08\u8f93\u5165\u5173\u952e\u5b57\u8fc7\u6ee4\uff09", FontSize = 11, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 2) };
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
        var sortAscItem = new MenuItem { Header = "\u5f53\u524d\u5217 \u2191 \u5347\u5e8f\u6392\u5217" };
        sortAscItem.Click += (s2, e2) => SortDataGrid(null, "ASC");
        var sortDescItem = new MenuItem { Header = "\u5f53\u524d\u5217 \u2193 \u964d\u5e8f\u6392\u5217" };
        sortDescItem.Click += (s2, e2) => SortDataGrid(null, "DESC");
        var sortClearItem = new MenuItem { Header = "\u79fb\u9664\u6392\u5e8f" };
        sortClearItem.Click += (s2, e2) => SortDataGrid(null, "CLEAR");
        gridMenu.Items.Add(sortAscItem);
        gridMenu.Items.Add(sortDescItem);
        gridMenu.Items.Add(sortClearItem);
        _resultGrid.ContextMenu = gridMenu;
        // 记录右键点击的列名
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
        { SetStatus("\u8bf7\u8f93\u5165\u4e3b\u673a\u548c\u7528\u6237\u540d", false); return; }
        if (!int.TryParse(portText, out int port)) port = 3306;

        SetStatus("\u6b63\u5728\u6d4b\u8bd5\u8fde\u63a5...", true);
        try
        {
            var connStr = new MySqlConnectionStringBuilder
            {
                Server = host, Port = (uint)port, UserID = user,
                Password = pass, ConnectionTimeout = 5
            }.ConnectionString;

            using var testConn = new MySqlConnection(connStr);
            await testConn.OpenAsync();

            // 获取服务器版本
            using var cmd = new MySqlCommand("SELECT VERSION()", testConn);
            var version = await cmd.ExecuteScalarAsync();

            // 获取数据库数量
            using var cmd2 = new MySqlCommand("SHOW DATABASES", testConn);
            using var reader = await cmd2.ExecuteReaderAsync();
            int dbCount = 0;
            while (reader.Read()) dbCount++;

            SetStatus($"\u8fde\u63a5\u6d4b\u8bd5\u6210\u529f! MySQL {version} \u2014 \u5171 {dbCount} \u4e2a\u6570\u636e\u5e93", true);
        }
        catch (Exception ex)
        {
            SetStatus($"\u8fde\u63a5\u6d4b\u8bd5\u5931\u8d25: {ex.Message}", false);
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
        { SetStatus("\u8bf7\u8f93\u5165\u4e3b\u673a\u548c\u7528\u6237\u540d", false); return; }
        if (!int.TryParse(portText, out int port)) port = 3306;
        try
        {
            Disconnect();
            SetStatus("\u6b63\u5728\u8fde\u63a5...", true);
            _connectBtn.IsEnabled = false;
            var connStr = new MySqlConnectionStringBuilder
            {
                Server = host, Port = (uint)port, UserID = user,
                Password = pass, ConnectionTimeout = 10
            }.ConnectionString;
            _connection = new MySqlConnection(connStr);
            await _connection.OpenAsync();
            _disconnectBtn.IsEnabled = true;
            SetConnFieldsEnabled(false);
            SetStatus($"\u5df2\u8fde\u63a5\u5230 {host}:{port}", true);
            await LoadDatabasesAsync();
        }
        catch (Exception ex)
        { SetStatus($"\u8fde\u63a5\u5931\u8d25: {ex.Message}", false); _connectBtn.IsEnabled = true; Disconnect(); }
    }

    private async Task LoadDatabasesAsync()
    {
        if (_connection == null) return;
        try
        {
            _loadingDatabases = true;
            _dbCombo.Items.Clear();
            using var cmd = new MySqlCommand("SHOW DATABASES", _connection);
            using var reader = await cmd.ExecuteReaderAsync();
            var dbList = new List<string>();
            while (await reader.ReadAsync())
            {
                var db = reader.GetString(0);
                if (db != "information_schema" && db != "performance_schema" && db != "mysql" && db != "sys")
                    dbList.Add(db);
            }
            reader.Close(); // 确保 reader 完全关闭

            foreach (var db in dbList)
                _dbCombo.Items.Add(db);

            if (_dbCombo.Items.Count > 0) _dbCombo.SelectedIndex = 0;
            _loadingDatabases = false;

            // 手动触发第一个数据库的表加载
            if (dbList.Count > 0)
            {
                var firstDb = dbList[0];
                try
                {
                    using var cmd2 = new MySqlCommand($"USE `{firstDb}`", _connection);
                    await cmd2.ExecuteNonQueryAsync();
                    await RefreshTablesAsync();
                }
                catch (Exception ex)
                {
                    SetStatus($"\u5207\u6362\u6570\u636e\u5e93\u5931\u8d25[{firstDb}]: {ex.Message} (\u8fde\u63a5\u72b6\u6001:{_connection.State})", false);
                }
            }
        }
        catch (Exception ex) { _loadingDatabases = false; SetStatus($"\u52a0\u8f7d\u6570\u636e\u5e93\u5217\u8868\u5931\u8d25: {ex.Message}", false); }
    }

    public void SafeDisconnect() { try { Disconnect(); } catch { } }

    private void Disconnect()
    {
        try { _connection?.Close(); _connection?.Dispose(); } catch { }
        finally
        {
            _connection = null;
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
        if (_connection == null || _connection.State != ConnectionState.Open)
        { SetStatus("\u672a\u8fde\u63a5", false); return; }
        try
        {
            SetStatus("\u52a0\u8f7d\u8868\u5217\u8868\u4e2d...", true);
            var tables = new List<string>();
            using var cmd = new MySqlCommand("SHOW TABLES", _connection);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            reader.Close();

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
                    ToolTip = $"\u53cc\u51fb\u6253\u5f00 {table}\uff0c\u53f3\u952e\u67e5\u770b\u66f4\u591a\u9009\u9879",
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(4, 2, 4, 2),
                    MinWidth = 60
                };
                btn.PreviewMouseLeftButtonDown += async (s, e) =>
                {
                    if (e.ClickCount >= 2) { await OpenTableAsync(table); e.Handled = true; }
                };
                var menu = new ContextMenu();
                var openItem = new MenuItem { Header = "\u6253\u5f00\u8868\uff08\u524d100\u884c\uff09" };
                openItem.Click += async (s2, e2) => await OpenTableAsync(table);
                var schemaItem = new MenuItem { Header = "\u67e5\u770b\u8868\u7ed3\u6784" };
                schemaItem.Click += async (s2, e2) => await ShowSchemaAsync(table);
                menu.Items.Add(openItem);
                menu.Items.Add(schemaItem);
                btn.ContextMenu = menu;
                _tableGrid.Children.Add(btn);
            }
            SetStatus($"\u5df2\u52a0\u8f7d {tables.Count} \u5f20\u8868", true);
        }
        catch (Exception ex) { SetStatus($"\u5237\u65b0\u5931\u8d25: {ex.Message}", false); }
    }

    private async Task OpenTableAsync(string tableName)
    {
        _sqlBox.Text = $"SELECT * FROM `{tableName}`;";
        _currentTableName = tableName;
        await ExecuteSqlAsync();
    }

    private void ShowTableSchema()
    {
        if (_currentTableName == null) { SetStatus("\u8bf7\u5148\u6253\u5f00\u4e00\u5f20\u8868", false); return; }
        _ = ShowSchemaAsync(_currentTableName);
    }

    private async Task ShowSchemaAsync(string tableName)
    {
        _sqlBox.Text = $"DESCRIBE `{tableName}`;";
        try { await ExecuteSqlAsync(); }
        catch (Exception ex) { SetStatus($"\u67e5\u770b\u7ed3\u6784\u5931\u8d25: {ex.Message}", false); }
    }

    // ========== SQL ==========

    private async Task ExecuteSqlAsync()
    {
        var sql = _sqlBox.Text.Trim();
        if (string.IsNullOrEmpty(sql)) { SetStatus("\u8bf7\u8f93\u5165 SQL \u8bed\u53e5", false); return; }
        if (_connection == null || _connection.State != ConnectionState.Open)
        { SetStatus("\u672a\u8fde\u63a5", false); return; }
        try
        {
            SetStatus("\u6267\u884c\u4e2d...", true);
            var tableMatch = System.Text.RegularExpressions.Regex.Match(sql, @"(?i)FROM\s+`?(\w+)`?");
            if (tableMatch.Success) _currentTableName = tableMatch.Groups[1].Value;

            using var cmd = new MySqlCommand(sql, _connection);
            cmd.CommandTimeout = 30;
            var dt = new DataTable();
            using var adapter = new MySqlDataAdapter(cmd);
            await Task.Run(() => adapter.Fill(dt));

            if (dt.Columns.Count > 0)
            {
                _currentTable = dt;
                _resultGrid.ItemsSource = dt.DefaultView;
                BuildFilterPanel(dt);
                SetStatus($"\u67e5\u8be2\u6210\u529f\uff0c\u8fd4\u56de {dt.Rows.Count} \u884c", true);
            }
            else
            {
                _resultGrid.ItemsSource = null;
                _filterPanel.Children.Clear();
                _currentTable = null;
                SetStatus("\u6267\u884c\u6210\u529f\uff08\u65e0\u7ed3\u679c\u96c6\uff09", true);
            }
        }
        catch (MySqlException)
        {
            try
            {
                using var cmd2 = new MySqlCommand(sql, _connection);
                cmd2.CommandTimeout = 30;
                int affected = await cmd2.ExecuteNonQueryAsync();
                _resultGrid.ItemsSource = null;
                _filterPanel.Children.Clear();
                SetStatus($"\u6267\u884c\u6210\u529f\uff0c\u5f71\u54cd {affected} \u884c", true);
            }
            catch (Exception ex2) { SetStatus($"\u6267\u884c\u5931\u8d25: {ex2.Message}", false); }
        }
        catch (Exception ex) { SetStatus($"\u6267\u884c\u5931\u8d25: {ex.Message}", false); }
    }

    // ========== Filter ==========

    private void OnDataGridSorting(object? sender, DataGridSortingEventArgs e)
    {
        // 禁用内置排序，改用右键菜单三态排序
        e.Handled = true;
        var view = _currentTable?.DefaultView;
        if (view == null) return;

        var colName = e.Column.Header?.ToString() ?? e.Column.SortMemberPath;
        try
        {
            // 三态循环：null → ASC → DESC → null
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
        catch (Exception ex) { SetStatus($"\u6392\u5e8f\u5931\u8d25: {ex.Message}", false); }
    }

    private void SortDataGrid(string? columnName, string direction)
    {
        var view = _currentTable?.DefaultView;
        if (view == null) return;
        var col = columnName ?? _rightClickColumnName;
        if (string.IsNullOrEmpty(col)) { SetStatus("\u8bf7\u53f3\u952e\u5217\u5934\u9009\u62e9\u6392\u5e8f", false); return; }
        try
        {
            if (direction == "CLEAR")
            {
                view.Sort = "";
                foreach (var c in _resultGrid.Columns) c.SortDirection = null;
                SetStatus("\u5df2\u79fb\u9664\u6392\u5e8f", true);
            }
            else
            {
                view.Sort = $"[{col}] {direction}";
                foreach (var c in _resultGrid.Columns)
                    c.SortDirection = c.Header?.ToString() == col
                        ? (direction == "ASC" ? ListSortDirection.Ascending : ListSortDirection.Descending)
                        : null;
                SetStatus($"\u5df2\u6309 [{col}] {(direction == "ASC" ? "\u5347\u5e8f" : "\u964d\u5e8f")}", true);
            }
        }
        catch (Exception ex) { SetStatus($"\u6392\u5e8f\u5931\u8d25: {ex.Message}", false); }
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
            MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, "\u8f93\u5165\u7b5b\u9009");
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
        catch (Exception ex) { SetStatus($"\u7b5b\u9009\u9519\u8bef: {ex.Message}", false); }
    }

    // ========== CRUD ==========

    private void AddRow()
    {
        if (_currentTable == null) { SetStatus("\u8bf7\u5148\u67e5\u8be2\u4e00\u5f20\u8868", false); return; }
        var row = _currentTable.NewRow();
        _currentTable.Rows.Add(row);
        SetStatus("\u5df2\u65b0\u589e\u4e00\u884c\uff0c\u7f16\u8f91\u540e\u70b9\u51fb[\u4fdd\u5b58\u66f4\u6539]\u5199\u5165\u6570\u636e\u5e93", true);
    }

    private void DeleteSelectedRows()
    {
        if (_currentTable == null || _resultGrid.SelectedItems.Count == 0)
        { SetStatus("\u8bf7\u5148\u9009\u4e2d\u8981\u5220\u9664\u7684\u884c", false); return; }
        var selected = _resultGrid.SelectedItems.Cast<DataRowView>().ToList();
        foreach (var rowView in selected) rowView.Row.Delete();
        SetStatus($"\u5df2\u6807\u8bb0\u5220\u9664 {selected.Count} \u884c\uff0c\u70b9\u51fb[\u4fdd\u5b58\u66f4\u6539]\u5199\u5165\u6570\u636e\u5e93", true);
    }

    private async void SaveChanges()
    {
        if (_currentTable == null || _currentTableName == null || _connection == null)
        { SetStatus("\u65e0\u53ef\u4fdd\u5b58\u7684\u6570\u636e", false); return; }
        try
        {
            var changes = _currentTable.GetChanges();
            if (changes == null || changes.Rows.Count == 0)
            { SetStatus("\u6ca1\u6709\u9700\u8981\u4fdd\u5b58\u7684\u66f4\u6539", true); return; }

            int saved = 0, errors = 0;
            using var transaction = _connection.BeginTransaction();
            try
            {
                foreach (DataRow row in changes.Rows)
                {
                    try
                    {
                        if (row.RowState == DataRowState.Deleted)
                        {
                            var where = BuildWhereClause(row, DataRowVersion.Original);
                            var sql = $"DELETE FROM `{_currentTableName}` WHERE {where}";
                            using var cmd = new MySqlCommand(sql, _connection, transaction);
                            cmd.ExecuteNonQuery(); saved++;
                        }
                        else if (row.RowState == DataRowState.Added)
                        {
                            var cols = new List<string>();
                            var vals = new List<string>();
                            foreach (DataColumn col in _currentTable.Columns)
                            {
                                if (row.IsNull(col)) continue;
                                cols.Add($"`{col.ColumnName}`");
                                vals.Add($"'{EscapeSql(row[col]?.ToString() ?? "")}'");
                            }
                            var sql = $"INSERT INTO `{_currentTableName}` ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})";
                            using var cmd = new MySqlCommand(sql, _connection, transaction);
                            cmd.ExecuteNonQuery(); saved++;
                        }
                        else if (row.RowState == DataRowState.Modified)
                        {
                            var sets = new List<string>();
                            var where = BuildWhereClause(row, DataRowVersion.Original);
                            foreach (DataColumn col in _currentTable.Columns)
                            {
                                var val = row.IsNull(col) ? "NULL" : $"'{EscapeSql(row[col]?.ToString() ?? "")}'";
                                sets.Add($"`{col.ColumnName}` = {val}");
                            }
                            var sql = $"UPDATE `{_currentTableName}` SET {string.Join(", ", sets)} WHERE {where}";
                            using var cmd = new MySqlCommand(sql, _connection, transaction);
                            cmd.ExecuteNonQuery(); saved++;
                        }
                    }
                    catch { errors++; }
                }
                transaction.Commit();
                _currentTable.AcceptChanges();
                SetStatus($"\u4fdd\u5b58\u5b8c\u6210: \u6210\u529f {saved} \u884c" + (errors > 0 ? $", \u5931\u8d25 {errors} \u884c" : ""), errors == 0);
                if (_currentTableName != null) await OpenTableAsync(_currentTableName);
            }
            catch { transaction.Rollback(); throw; }
        }
        catch (Exception ex) { SetStatus($"\u4fdd\u5b58\u5931\u8d25: {ex.Message}", false); }
    }

    private string BuildWhereClause(DataRow row, DataRowVersion version)
    {
        var conditions = new List<string>();
        foreach (DataColumn col in _currentTable!.Columns)
        {
            var val = row[col, version];
            if (val == DBNull.Value || val == null)
                conditions.Add($"`{col.ColumnName}` IS NULL");
            else
                conditions.Add($"`{col.ColumnName}` = '{EscapeSql(val.ToString() ?? "")}'");
        }
        return string.Join(" AND ", conditions);
    }

    private static string EscapeSql(string s) => s.Replace("'", "\\'");

    // ========== Import / Export ==========

    private void ExportCsv()
    {
        if (_currentTable == null || _currentTable.Rows.Count == 0)
        { SetStatus("\u6ca1\u6709\u6570\u636e\u53ef\u5bfc\u51fa", false); return; }
        var dlg = new SaveFileDialog
        {
            Title = "\u5bfc\u51fa CSV", Filter = "CSV \u6587\u4ef6 (*.csv)|*.csv",
            FileName = $"{_currentTableName ?? "export"}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", _currentTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\"")));
            foreach (DataRow row in _currentTable.Rows)
                sb.AppendLine(string.Join(",", row.ItemArray.Select(v => $"\"{(v?.ToString() ?? "").Replace("\"", "\"\"")}\"")));
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            SetStatus($"\u5bfc\u51fa\u6210\u529f: {dlg.FileName}", true);
        }
        catch (Exception ex) { SetStatus($"\u5bfc\u51fa\u5931\u8d25: {ex.Message}", false); }
    }

    private void ExportJson()
    {
        if (_currentTable == null || _currentTable.Rows.Count == 0)
        { SetStatus("\u6ca1\u6709\u6570\u636e\u53ef\u5bfc\u51fa", false); return; }
        var dlg = new SaveFileDialog
        {
            Title = "\u5bfc\u51fa JSON", Filter = "JSON \u6587\u4ef6 (*.json)|*.json",
            FileName = $"{_currentTableName ?? "export"}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var list = new List<Dictionary<string, object?>>();
            foreach (DataRow row in _currentTable.Rows)
            {
                var obj = new Dictionary<string, object?>();
                foreach (DataColumn col in _currentTable.Columns)
                    obj[col.ColumnName] = row.IsNull(col) ? null : row[col];
                list.Add(obj);
            }
            var json = JsonConvert.SerializeObject(list, Formatting.Indented);
            File.WriteAllText(dlg.FileName, json, Encoding.UTF8);
            SetStatus($"\u5bfc\u51fa\u6210\u529f: {dlg.FileName}", true);
        }
        catch (Exception ex) { SetStatus($"\u5bfc\u51fa\u5931\u8d25: {ex.Message}", false); }
    }

    private void ImportCsv()
    {
        var dlg = new OpenFileDialog { Title = "\u5bfc\u5165 CSV", Filter = "CSV \u6587\u4ef6 (*.csv)|*.csv" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
            if (lines.Length < 2) { SetStatus("CSV \u6587\u4ef6\u4e3a\u7a7a\u6216\u683c\u5f0f\u4e0d\u6b63\u786e", false); return; }
            var dt = new DataTable("imported");
            var headers = ParseCsvLine(lines[0]);
            foreach (var h in headers) dt.Columns.Add(h.Trim('"'));
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var vals = ParseCsvLine(lines[i]);
                var row = dt.NewRow();
                for (int j = 0; j < Math.Min(vals.Length, dt.Columns.Count); j++)
                    row[j] = vals[j].Trim('"').Replace("\"\"", "\"");
                dt.Rows.Add(row);
            }
            _currentTable = dt;
            _currentTableName = null;
            _resultGrid.ItemsSource = dt.DefaultView;
            BuildFilterPanel(dt);
            SetStatus($"\u5bfc\u5165\u6210\u529f: {dt.Rows.Count} \u884c", true);
        }
        catch (Exception ex) { SetStatus($"\u5bfc\u5165\u5931\u8d25: {ex.Message}", false); }
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { inQuotes = !inQuotes; current.Append(c); }
            else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    // ========== Backup ==========

    private async void BackupDatabase()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        { SetStatus("\u8bf7\u5148\u8fde\u63a5\u6570\u636e\u5e93", false); return; }
        var db = _connection.Database;
        if (string.IsNullOrEmpty(db)) { SetStatus("\u8bf7\u5148\u9009\u62e9\u6570\u636e\u5e93", false); return; }

        var allTables = new List<string>();
        try
        {
            using var cmd = new MySqlCommand("SHOW TABLES", _connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) allTables.Add(reader.GetString(0));
        }
        catch (Exception ex) { SetStatus($"\u83b7\u53d6\u8868\u5217\u8868\u5931\u8d25: {ex.Message}", false); return; }
        if (allTables.Count == 0) { SetStatus("\u5f53\u524d\u6570\u636e\u5e93\u6ca1\u6709\u8868", false); return; }

        var selectedTables = ShowBackupSelectionDialog(allTables, db);
        if (selectedTables == null || selectedTables.Count == 0) return;

        var dlg = new SaveFileDialog
        {
            Title = "\u9009\u62e9\u5907\u4efd\u6587\u4ef6\u4fdd\u5b58\u8def\u5f84",
            Filter = "SQL \u6587\u4ef6 (*.sql)|*.sql|\u6240\u6709\u6587\u4ef6 (*.*)|*.*",
            FileName = $"{db}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            DefaultExt = ".sql"
        };
        if (dlg.ShowDialog() != true) return;

        SetStatus("\u6b63\u5728\u5907\u4efd...", true);
        try
        {
            var conn = _connection;
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine($"-- MySQL Database Backup");
                sb.AppendLine($"-- Database: {db}");
                sb.AppendLine($"-- Tables: {string.Join(", ", selectedTables)}");
                sb.AppendLine($"-- Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine("SET NAMES utf8mb4;");
                sb.AppendLine("SET FOREIGN_KEY_CHECKS = 0;");
                sb.AppendLine();
                foreach (var table in selectedTables)
                {
                    sb.AppendLine($"-- ----------------------------");
                    sb.AppendLine($"-- Table: {table}");
                    sb.AppendLine($"-- ----------------------------");
                    sb.AppendLine($"DROP TABLE IF EXISTS `{table}`;");
                    using (var cmd = new MySqlCommand($"SHOW CREATE TABLE `{table}`", conn))
                    using (var reader = cmd.ExecuteReader())
                    { if (reader.Read()) sb.AppendLine(reader.GetString(1) + ";"); }
                    sb.AppendLine();
                    using (var cmd = new MySqlCommand($"SELECT * FROM `{table}`", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            var cols = new string[reader.FieldCount];
                            for (int i = 0; i < reader.FieldCount; i++) cols[i] = $"`{reader.GetName(i)}`";
                            var colList = string.Join(", ", cols);
                            while (reader.Read())
                            {
                                var vals = new string[reader.FieldCount];
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    if (reader.IsDBNull(i)) vals[i] = "NULL";
                                    else vals[i] = $"'{(reader.GetValue(i)?.ToString() ?? "").Replace("'", "\\'")}'";
                                }
                                sb.AppendLine($"INSERT INTO `{table}` ({colList}) VALUES ({string.Join(", ", vals)});");
                            }
                        }
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("SET FOREIGN_KEY_CHECKS = 1;");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            });
            SetStatus($"\u5907\u4efd\u6210\u529f: {dlg.FileName}\uff08{selectedTables.Count} \u5f20\u8868\uff09", true);
        }
        catch (Exception ex) { SetStatus($"\u5907\u4efd\u5931\u8d25: {ex.Message}", false); }
    }

    private List<string>? ShowBackupSelectionDialog(List<string> allTables, string dbName)
    {
        var win = new Window
        {
            Title = $"\u9009\u62e9\u5907\u4efd\u7684\u6570\u636e\u8868 - {dbName}",
            Width = 400, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = Window.GetWindow(this)
        };
        var result = new List<string>();
        var panel = new DockPanel { Margin = new Thickness(12) };
        var selectAllCb = new CheckBox { Content = "\u5168\u9009/\u53d6\u6d88\u5168\u9009", FontSize = 13, Margin = new Thickness(0, 0, 0, 8), IsChecked = true };
        DockPanel.SetDock(selectAllCb, Dock.Top);
        panel.Children.Add(selectAllCb);
        var tip = new TextBlock { Text = $"\u5171 {allTables.Count} \u5f20\u8868\uff0c\u52fe\u9009\u9700\u8981\u5907\u4efd\u7684\u8868\uff1a", FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(tip, Dock.Top);
        panel.Children.Add(tip);
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var confirmBtn = new Button { Content = "\u786e\u8ba4\u5907\u4efd", Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource("MaterialDesignRaisedButton") as Style };
        var cancelBtn = new Button { Content = "\u53d6\u6d88", Style = TryFindResource("MaterialDesignOutlinedButton") as Style };
        btnRow.Children.Add(confirmBtn);
        btnRow.Children.Add(cancelBtn);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        panel.Children.Add(btnRow);
        var listBox = new ListBox { FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 13 };
        var checkboxes = new List<CheckBox>();
        foreach (var table in allTables)
        {
            var cb = new CheckBox { Content = table, IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 13 };
            checkboxes.Add(cb);
            listBox.Items.Add(cb);
        }
        panel.Children.Add(listBox);
        selectAllCb.Click += (s, e) => { bool check = selectAllCb.IsChecked == true; foreach (var cb in checkboxes) cb.IsChecked = check; };
        bool confirmed = false;
        confirmBtn.Click += (s, e) =>
        {
            result.AddRange(checkboxes.Where(cb => cb.IsChecked == true).Select(cb => cb.Content?.ToString() ?? ""));
            confirmed = true; win.Close();
        };
        cancelBtn.Click += (s, e) => win.Close();
        win.Content = panel;
        win.ShowDialog();
        return confirmed ? result : null;
    }

    // ========== Restore ==========

    private async void RestoreDatabase()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        { SetStatus("\u8bf7\u5148\u8fde\u63a5\u6570\u636e\u5e93", false); return; }
        var dlg = new OpenFileDialog
        {
            Title = "\u9009\u62e9\u8981\u6062\u590d\u7684\u5907\u4efd\u6587\u4ef6",
            Filter = "SQL \u6587\u4ef6 (*.sql)|*.sql|\u6240\u6709\u6587\u4ef6 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        SetStatus("\u6b63\u5728\u6062\u590d...", true);
        try
        {
            var sqlContent = await File.ReadAllTextAsync(dlg.FileName, Encoding.UTF8);
            int successCount = 0, errorCount = 0;
            var statements = sqlContent.Split(';')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s) && !s.StartsWith("--"));
            foreach (var stmt in statements)
            {
                try { using var cmd = new MySqlCommand(stmt, _connection); cmd.CommandTimeout = 60; cmd.ExecuteNonQuery(); successCount++; }
                catch { errorCount++; }
            }
            await RefreshTablesAsync();
            SetStatus($"\u6062\u590d\u5b8c\u6210: \u6210\u529f {successCount} \u6761, \u5931\u8d25 {errorCount} \u6761", errorCount == 0);
        }
        catch (Exception ex) { SetStatus($"\u6062\u590d\u5931\u8d25: {ex.Message}", false); }
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }
}
