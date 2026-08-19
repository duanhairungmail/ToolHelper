using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using PackIcon = MaterialDesignThemes.Wpf.PackIcon;
using PackIconKind = MaterialDesignThemes.Wpf.PackIconKind;
using Newtonsoft.Json.Linq;
using ToolHelper.Services;

namespace ToolHelper.Views.Api;

public class ApiValidationView : UserControl
{
    private const string AES_KEY = "32DGoR8HdfIiw1judwJHY&^%1_aFSSJw";
    private const string AES_IV  = "32DGoR8HdfIiw1ju";

    private TextBox _ipBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private PasswordBox _passBox = new();
    private DataGrid _resultGrid = new();
    private TextBox _logBox = new();
    private TextBlock _statusText = new();
    private TextBlock _summaryText = new();
    private bool _built;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private string? _token;
    private TextBox _tokenBox = new();

    // 布局：默认窗口下验证结果区固定显示 4 条数据（多余数据内部滚动查看）、日志区固定高度；窗口放大后验证结果区按剩余空间自适应
    private RowDefinition _resultRow = new();
    private const double DefaultWindowHeight = 768; // 与 MainWindow 默认窗口高度一致
    private const int VisibleDataRows = 4;          // 默认窗口下可见数据条数
    private const double GridRowHeight = 50;        // 单条数据行高（路径列换行需两行文字）
    private const double GridHeaderHeight = 44;     // 表头高度（显式固定，保证高度推导确定）
    private const double GridChromeHeight = 44;     // “验证结果”标签 20 + 边框 2 + 水平滚动条 18 + 面板下边距 4
    /// <summary>验证结果区固定高度 = 表头 + 4 条数据 + 外框修饰</summary>
    private static double ResultFixedHeight => GridHeaderHeight + VisibleDataRows * GridRowHeight + GridChromeHeight;
    private const double LogFixedHeight = 200;      // 日志区固定高度

    // 按钮引用（用于状态管理）
    private Button _loginBtn = new();
    private Button _validateBtn = new();
    private Button _clearBtn = new();
    private Button _encryptBtn = new();
    private Button _autoDetectBtn = new();
    private bool _validating; // 手动验证进行中

    // 自动检测相关
    private CancellationTokenSource? _autoDetectCts;
    private string _autoDetectCron = "0 */5 * * * ?"; // 默认每5分钟
    private HashSet<string> _autoDetectApis = new();
    private bool _autoDetectRunning;
    private int _autoDetectCycleCount;
    private readonly List<(DateTime Time, int Total, int Ok)> _autoDetectHistory = new(); // 最近10次历史

    private readonly List<(string Name, string Method, string Path)> _allApis = new()
    {
        ("设备列表", "GET", "/device/deviceinfo/list?deviceName=&Address=&pageSize=20&page=1"),
        ("设备状态统计", "GET", "/api/getIndexDeviceSummaryList"),
        ("接点状态查询", "GET", "/index/getIOLastStatusData"),
        ("即时数据", "GET", "/index/getDeviceLastData?device={deviceId}"),
        ("历史数据", "GET", "/index/getDeviceHistoryDataByIdList?device={deviceId}&paramName=smokeline&begin=&end="),
        ("感温电缆终端", "GET", "/device/deviceinfo/getCableInfo?device={deviceId}"),
        ("电表即时数据", "GET", "/index/getMeterLastData"),
        ("电表历史数据(交流)", "GET", "/index/getDeviceHistoryDataByIdList?deviceType=ac&pageSize=20&page=1&begin=&end=&sort=dataTime&direction=desc"),
        ("电表历史数据(直流)", "GET", "/index/getDeviceHistoryDataByIdList?deviceType=dc&pageSize=20&page=1&begin=&end=&sort=dataTime&direction=desc"),
        ("即时告警", "GET", "/index/getDeviceAlarmList"),
        ("设备历史告警", "GET", "/index/getAlramHistoryList?device={deviceId}&begin=&end=&pageSize=20&page=1"),
        ("电表历史告警(交流)", "GET", "/index/getMeterAlramHistoryList?deviceType=ac&pageSize=20&page=1&begin=&end=&sort=dataTime&direction=desc"),
        ("电表历史告警(直流)", "GET", "/index/getMeterAlramHistoryList?deviceType=dc&pageSize=20&page=1&begin=&end=&sort=dataTime&direction=desc"),
        ("设备复位", "POST", "/index/resetAlarm"),
        ("设备参数设置", "POST", "/index/writeDeviceData"),
        ("电表复位", "POST", "/index/resetMeterAlarm"),
    };

    public ApiValidationView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        var win = Window.GetWindow(this);
        if (win != null)
        {
            win.SizeChanged += OnWindowSizeChanged;
            UpdateResultRowHeight();
        }
    }

    /// <summary>默认窗口（≤768 高）下验证结果区固定显示 4 条数据；窗口放大后按剩余空间自适应（日志区始终固定高度）</summary>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResultRowHeight();

    private void UpdateResultRowHeight()
    {
        var win = Window.GetWindow(this);
        if (win == null) return;
        var extra = win.ActualHeight - DefaultWindowHeight;
        _resultRow.Height = new GridLength(extra > 0 ? ResultFixedHeight + extra : ResultFixedHeight);
    }

    private TextBox MakeBox(string hint, string defaultText = "", int minWidth = 150)
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

    private PasswordBox MakePasswordBox(string hint, int minWidth = 150)
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

        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 标题行（图标 + 文字）
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Api, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  接口验证",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);

        topPanel.Children.Add(new TextBlock
        {
            Text = "验证火灾探测系统所有接口的连通性，自动登录并逐一访问接口，统计正常/异常数量。",
            FontSize = 13,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 连接参数
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow.Children.Add(MakeLabel("IP 地址:"));
        _ipBox = MakeBox("如 192.168.1.1", "", 180);
        connRow.Children.Add(_ipBox);
        connRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeBox("端口号", "8088", 80);
        connRow.Children.Add(_portBox);
        connRow.Children.Add(MakeLabel("用户名:"));
        _userBox = MakeBox("用户名", "", 150);
        connRow.Children.Add(_userBox);
        connRow.Children.Add(MakeLabel("密码:"));
        _passBox = MakePasswordBox("密码", 150);
        connRow.Children.Add(_passBox);
        topPanel.Children.Add(connRow);

        // Token 行
        var tokenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        tokenRow.Children.Add(MakeLabel("Token:"));
        _tokenBox = MakeBox("登录后自动填入", "", 500);
        _tokenBox.IsReadOnly = true;
        tokenRow.Children.Add(_tokenBox);
        topPanel.Children.Add(tokenRow);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _loginBtn = MakeButton("登录", Login, true, PackIconKind.Login);
        btnRow.Children.Add(_loginBtn);
        _validateBtn = MakeButton("开始验证", ValidateAllApis, false, PackIconKind.CheckAll);
        btnRow.Children.Add(_validateBtn);
        _clearBtn = MakeButton("清空结果", () => { _resultGrid.ItemsSource = null; _logBox.Clear(); _summaryText.Text = ""; _token = null; _tokenBox.Text = ""; SetStatus("", true); UpdateButtonStates(); }, false, PackIconKind.Eraser);
        btnRow.Children.Add(_clearBtn);
        _encryptBtn = MakeButton("查看加密值", ShowEncryptedValues, false, PackIconKind.KeyVariant);
        btnRow.Children.Add(_encryptBtn);
        _autoDetectBtn = MakeButton("自动检测", ToggleAutoDetect, false, PackIconKind.Radar);
        btnRow.Children.Add(_autoDetectBtn);
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnRow.Children.Add(_statusText);
        _summaryText.VerticalAlignment = VerticalAlignment.Center;
        _summaryText.Margin = new Thickness(16, 0, 0, 0);
        _summaryText.FontSize = 14;
        btnRow.Children.Add(_summaryText);
        topPanel.Children.Add(btnRow);

        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // 结果表格
        var gridLabel = new TextBlock { Text = "验证结果", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(gridLabel, Dock.Top);

        _resultGrid.AutoGenerateColumns = false;
        _resultGrid.IsReadOnly = true;
        _resultGrid.CanUserAddRows = false;
        _resultGrid.CanUserDeleteRows = false;
        _resultGrid.FontFamily = new FontFamily("Microsoft YaHei");
        _resultGrid.FontSize = 13;
        _resultGrid.RowHeight = GridRowHeight;             // 固定单条数据行高
        _resultGrid.ColumnHeaderHeight = GridHeaderHeight; // 固定表头高度，与高度推导保持一致
        _resultGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));

        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "序号", Binding = new System.Windows.Data.Binding("Index"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "接口名称", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "方法", Binding = new System.Windows.Data.Binding("Method"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        var pathCol = new DataGridTextColumn { Header = "路径", Binding = new System.Windows.Data.Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false };
        var pathStyle = new Style(typeof(TextBlock));
        pathStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        pathCol.ElementStyle = pathStyle;
        _resultGrid.Columns.Add(pathCol);
        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "状态码", Binding = new System.Windows.Data.Binding("StatusCode"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "耗时(ms)", Binding = new System.Windows.Data.Binding("Elapsed"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        var resultCol = new DataGridTextColumn { Header = "结果", Binding = new System.Windows.Data.Binding("Result"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false };
        var resultStyle = new Style(typeof(TextBlock));
        var okTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("Result"), Value = "正常" };
        okTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Green));
        var failTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("Result"), Value = "异常" };
        failTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Red));
        resultStyle.Triggers.Add(okTrigger);
        resultStyle.Triggers.Add(failTrigger);
        resultCol.ElementStyle = resultStyle;
        _resultGrid.Columns.Add(resultCol);

        // 双击行展开/折叠行高
        _resultGrid.PreviewMouseDoubleClick += (s, e) =>
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
                dep = VisualTreeHelper.GetParent(dep);
            if (dep is DataGridRow row)
            {
                row.Height = row.Height == double.NaN ? 120 : double.NaN;
                e.Handled = true;
            }
        };

        // DataGrid 使用内置滚动条（表头固定 + 行虚拟化），高度完全由外层 Grid 行比例约束，数据增多时区域高度不变、内部滚动查看
        var gridBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 0),
            Child = _resultGrid
        };

        var gridPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        gridPanel.Children.Add(gridLabel);
        gridPanel.Children.Add(gridBorder);

        // 日志区（高度由外层 Grid 等比分配，与表格区等高）
        _logBox.AcceptsReturn = true;
        _logBox.TextWrapping = TextWrapping.Wrap;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.IsReadOnly = true;
        _logBox.FontFamily = new FontFamily("Microsoft YaHei");
        _logBox.FontSize = 12;
        _logBox.VerticalContentAlignment = VerticalAlignment.Top;
        _logBox.Margin = new Thickness(0, 4, 0, 0);
        var logStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (logStyle != null) _logBox.Style = logStyle;

        // 两行 Grid：默认窗口下验证结果区固定显示 4 条数据、日志区固定高度；窗口放大后验证结果区按剩余空间自适应（日志区始终固定）
        var contentGrid = new Grid();
        _resultRow = new RowDefinition { Height = new GridLength(ResultFixedHeight) }; // 默认容纳 4 条数据
        contentGrid.RowDefinitions.Add(_resultRow);
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LogFixedHeight) }); // 日志区固定高度
        Grid.SetRow(gridPanel, 0);
        contentGrid.Children.Add(gridPanel);
        Grid.SetRow(_logBox, 1);
        contentGrid.Children.Add(_logBox);
        root.Children.Add(contentGrid);

        Content = root;
        UpdateButtonStates();
    }

    // ========== AES 加密 ==========

    private static string AesEncryptBase64(string plainText)
    {
        var key = Encoding.UTF8.GetBytes(AES_KEY);
        var iv  = Encoding.UTF8.GetBytes(AES_IV);
        var data = Encoding.UTF8.GetBytes(plainText);
        if (key.Length > 32) key = key[..32];

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
        return Convert.ToBase64String(encrypted);
    }

    // ========== 接口验证 ==========

    // ========== 登录 ==========

    private async void Login()
    {
        var baseUrl = GetBaseUrl();
        if (string.IsNullOrEmpty(baseUrl)) { SetStatus("请输入 IP 和端口", false); return; }

        var user = _userBox.Text.Trim();
        var pass = _passBox.Password;
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        { SetStatus("请输入用户名和密码", false); return; }

        SetStatus("正在登录...", true);
        try
        {
            var encUser = AesEncryptBase64(user);
            var encPass = AesEncryptBase64(pass);
            var loginUrl = $"{baseUrl}/sys/login?userName={Uri.EscapeDataString(encUser)}&password={Uri.EscapeDataString(encPass)}";
            var resp = await _http.PostAsync(loginUrl, null);
            var json = await resp.Content.ReadAsStringAsync();
            var code = (int)resp.StatusCode;

            if (resp.IsSuccessStatusCode)
            {
                var obj = JObject.Parse(json);
                var token = obj["data"]?["token"]?.ToString() ?? obj["token"]?.ToString() ?? obj["data"]?.ToString();
                if (!string.IsNullOrEmpty(token))
                {
                    _token = token;
                    _tokenBox.Text = token;
                    AppendLog($"登录成功: POST /sys/login    状态码: {code}    Token 已填入");
                    SetStatus("登录成功，Token 已获取", true);
                    UpdateButtonStates();
                }
                else
                {
                    _token = null;
                    _tokenBox.Text = "";
                    AppendLog($"[登录异常] 未获取到令牌: {Truncate(json)}");
                    SetStatus("登录成功但未获取到 Token", false);
                    UpdateButtonStates();
                }
            }
            else
            {
                _token = null;
                _tokenBox.Text = "";
                AppendLog($"[登录失败] POST /sys/login    状态码: {code}    {Truncate(json)}");
                SetStatus($"登录失败: HTTP {code}", false);
                UpdateButtonStates();
            }
        }
        catch (Exception ex)
        {
            _token = null;
            _tokenBox.Text = "";
            AppendLog($"[登录异常] {ex.Message}");
            SetStatus($"登录异常: {ex.Message}", false);
            UpdateButtonStates();
        }
    }

    private static string Truncate(string text)
    {
        var clean = (text ?? "").Replace("\n", " ").Replace("\r", "");
        return clean.Length > 100 ? clean[..100] : clean;
    }

    /// <summary>统一向请求头添加 Token（Authorization: Bearer + token 两个头，token 为空则跳过）</summary>
    private static void AddTokenHeaders(HttpRequestMessage req, string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        req.Headers.TryAddWithoutValidation("token", token);
    }

    private async void ValidateAllApis()
    {
        var baseUrl = GetBaseUrl();
        if (string.IsNullOrEmpty(baseUrl)) { SetStatus("请输入 IP 和端口", false); return; }

        // 使用登录后获取的 Token；未登录则提示先登录
        var token = _token;
        if (string.IsNullOrEmpty(token))
        { SetStatus("请先点击登录获取 Token", false); return; }

        var selectedApis = ShowApiSelectDialog();
        if (selectedApis == null) { SetStatus("已取消验证", false); return; }

        SetStatus("正在验证...", true);
        _validating = true;
        UpdateButtonStates();
        _summaryText.Text = "";
        var results = new List<ApiResult>();
        var sw = new Stopwatch();

        // 先获取设备列表，获取一个可用的设备ID
        string? deviceId = null;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/device/deviceinfo/list?deviceName=&Address=&pageSize=1&page=1");
            AddTokenHeaders(req, token);
            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                var data = obj["data"];
                JToken? items = null;
                if (data is JArray)
                    items = data;
                else if (data is JObject dObj)
                    items = dObj["list"] ?? dObj["records"] ?? dObj["rows"] ?? data;
                if (items is JArray arr && arr.Count > 0)
                    deviceId = arr[0]?["id"]?.ToString();
            }
        }
        catch { }

        if (string.IsNullOrEmpty(deviceId)) deviceId = "1"; // fallback

        // 定义所有待验证接口（均为用户勾选项）
        var apis = _allApis.Select(a => (a.Name, a.Method, a.Path.Replace("{deviceId}", deviceId)))
            .Where(a => selectedApis.Contains(a.Name)).ToList();

        int successCount = 0;
        int failCount = 0;

        foreach (var (name, method, path) in apis)
        {
            sw.Restart();
            try
            {
                HttpResponseMessage resp;
                if (method == "GET")
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}");
                    AddTokenHeaders(req, token);
                    resp = await _http.SendAsync(req);
                }
                else
                {
                    // POST 接口使用空 body 测试连通性（不会实际执行操作）
                    var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{path}");
                    AddTokenHeaders(req, token);
                    req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    resp = await _http.SendAsync(req);
                }
                sw.Stop();

                var code = (int)resp.StatusCode;
                var json = await resp.Content.ReadAsStringAsync();
                var msg = ExtractMessage(json, code);

                // 200~499 均表示接口可达（4xx说明接口存在但参数问题，5xx可能是服务端异常）
                var result = code < 500 ? "正常" : "异常";
                if (result == "正常") successCount++;

                // 日志只显示异常接口信息
                if (result == "异常")
                {
                    failCount++;
                    AppendLog($"[异常] 接口: {name}    地址: {method} {path}    状态码: {code}");
                }

                results.Add(new ApiResult
                {
                    Index = results.Count + 1,
                    Name = name,
                    Method = method,
                    Path = path,
                    StatusCode = code,
                    Elapsed = sw.ElapsedMilliseconds,
                    Result = result,
                    Message = msg
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                failCount++;
                AppendLog($"[异常] {name}: HTTP {ex.Message}");
                results.Add(new ApiResult
                {
                    Index = results.Count + 1,
                    Name = name,
                    Method = method,
                    Path = path,
                    StatusCode = 0,
                    Elapsed = sw.ElapsedMilliseconds,
                    Result = "异常",
                    Message = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message
                });
            }

            // 实时更新表格
            _resultGrid.ItemsSource = null;
            _resultGrid.ItemsSource = results;
        }

        AutoSizeGridColumns();

        SetSummary(successCount, failCount);
        SetStatus($"验证完成: 共 {results.Count} 个接口", true);
        AppendLog($"验证完成: 正常 {successCount} 个, 异常 {failCount} 个, 使用设备ID: {deviceId}");
        _validating = false;
        UpdateButtonStates();
    }

    // ========== 查看加密值 ==========

    private void ShowEncryptedValues()
    {
        var u = _userBox.Text.Trim();
        var p = _passBox.Password;
        AppendLog("===== AES256(CBC) 加密结果 =====");
        if (!string.IsNullOrEmpty(u)) AppendLog($"用户名 [{u}] → {AesEncryptBase64(u)}");
        if (!string.IsNullOrEmpty(p)) AppendLog($"密码 [{p}] → {AesEncryptBase64(p)}");
        AppendLog("================================");
        SetStatus("加密值已输出到日志", true);
    }

    // ========== 按钮状态管理 ==========

    /// <summary>
    /// 按钮状态规则：
    /// 未登录：开始验证/自动检测禁用，其余启用；
    /// 已登录/空闲：全部启用；
    /// 自动检测运行中：登录/开始验证/清空结果禁用，查看加密值保持启用（方便调试）；
    /// 手动验证进行中：登录/开始验证/清空结果/自动检测禁用。
    /// </summary>
    private void UpdateButtonStates()
    {
        bool loggedIn = !string.IsNullOrEmpty(_token);
        if (_autoDetectRunning)
        {
            _loginBtn.IsEnabled = false;
            _validateBtn.IsEnabled = false;
            _clearBtn.IsEnabled = false;
            _encryptBtn.IsEnabled = true;
            _autoDetectBtn.IsEnabled = true; // 运行中点击即停止
        }
        else if (_validating)
        {
            _loginBtn.IsEnabled = false;
            _validateBtn.IsEnabled = false;
            _clearBtn.IsEnabled = false;
            _encryptBtn.IsEnabled = true;
            _autoDetectBtn.IsEnabled = false;
        }
        else
        {
            _loginBtn.IsEnabled = true;
            _validateBtn.IsEnabled = loggedIn;
            _clearBtn.IsEnabled = true;
            _encryptBtn.IsEnabled = true;
            _autoDetectBtn.IsEnabled = loggedIn;
        }
    }

    private void SetAutoDetectBtnText(string text, PackIconKind icon)
    {
        if (_autoDetectBtn.Content is StackPanel sp && sp.Children.Count >= 2)
        {
            if (sp.Children[0] is PackIcon ic) ic.Kind = icon;
            if (sp.Children[1] is TextBlock tb) tb.Text = text;
        }
    }

    // ========== 自动检测 ==========

    private void ToggleAutoDetect()
    {
        if (_autoDetectRunning)
        {
            _autoDetectCts?.Cancel(); // 直接停止，不弹确认框
            return;
        }

        if (string.IsNullOrEmpty(_token))
        { SetStatus("请先点击登录获取 Token", false); return; }

        var cfg = ShowAutoDetectDialog();
        if (cfg == null) return;
        _autoDetectApis = cfg.Value.Apis;
        _autoDetectCron = cfg.Value.Cron;
        StartAutoDetect();
    }

    /// <summary>自动检测配置对话框：选择检测接口 + Cron 表达式，取消返回 null</summary>
    private (HashSet<string> Apis, string Cron)? ShowAutoDetectDialog()
    {
        var dlg = new Window
        {
            Title = "自动检测配置",
            Width = 440,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "选择检测接口:", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        var cbPanel = new StackPanel();
        var cbs = new List<CheckBox>();
        foreach (var api in _allApis)
        {
            var cb = new CheckBox
            {
                Content = api.Name,
                IsChecked = _autoDetectApis.Count == 0 || _autoDetectApis.Contains(api.Name),
                FontFamily = new FontFamily("Microsoft YaHei"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            cbs.Add(cb);
            cbPanel.Children.Add(cb);
        }
        var scroll = new ScrollViewer { Content = cbPanel, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(scroll);

        panel.Children.Add(new TextBlock { Text = "Cron 表达式（秒 分 时 日 月 周）:", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
        var cronBox = MakeBox("如 0 */5 * * * ?（每5分钟）", _autoDetectCron, 300);
        panel.Children.Add(cronBox);

        // 常用模板
        var templates = new (string Label, string Cron)[]
        {
            ("每分钟", "0 * * * * ?"),
            ("每5分钟", "0 */5 * * * ?"),
            ("每30分钟", "0 */30 * * * ?"),
            ("每小时", "0 0 * * * ?"),
            ("每6小时", "0 0 */6 * * ?"),
            ("每天", "0 0 0 * * ?"),
        };
        panel.Children.Add(new TextBlock { Text = "常用模板:", FontSize = 12, Margin = new Thickness(0, 8, 0, 4) });
        var tplRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, cron) in templates)
        {
            var b = new Button
            {
                Content = label,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Style = TryFindResource("MaterialDesignOutlinedButton") as Style
            };
            b.Click += (s, e) => cronBox.Text = cron;
            tplRow.Children.Add(b);
        }
        panel.Children.Add(tplRow);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        var okBtn = new Button { Content = "确定", Width = 80, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource("MaterialDesignRaisedButton") as Style };
        okBtn.Click += (s, e) => { dlg.DialogResult = true; dlg.Close(); };
        var cancelBtn = new Button { Content = "取消", Width = 80, Style = TryFindResource("MaterialDesignOutlinedButton") as Style };
        cancelBtn.Click += (s, e) => { dlg.DialogResult = false; dlg.Close(); };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);
        dlg.Content = panel;

        if (dlg.ShowDialog() != true) return null;

        var selected = new HashSet<string>();
        foreach (var cb in cbs)
            if (cb.IsChecked == true && cb.Content is string name) selected.Add(name);
        if (selected.Count == 0)
        {
            MessageBox.Show("请至少选择一个检测接口", "自动检测配置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        var cronText = cronBox.Text.Trim();
        if (string.IsNullOrEmpty(cronText))
        {
            MessageBox.Show("请输入 Cron 表达式", "自动检测配置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return (selected, cronText);
    }

    /// <summary>自动检测主循环：按 cron 表达式定时执行检测，直到被取消</summary>
    private async void StartAutoDetect()
    {
        _autoDetectCts = new CancellationTokenSource();
        _autoDetectRunning = true;
        _autoDetectCycleCount = 0;
        _autoDetectHistory.Clear();
        var ct = _autoDetectCts.Token;
        SetAutoDetectBtnText("停止检测", PackIconKind.Stop);
        UpdateButtonStates();
        AppendLog($"[自动检测] 已启动: Cron={_autoDetectCron}, 接口数={_autoDetectApis.Count}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // cron 解析放后台线程，避免无效表达式逐秒搜索卡住 UI
                var nextTime = await Task.Run(() => GetNextCronTime(_autoDetectCron, DateTime.Now), ct);
                if (nextTime == null)
                {
                    AppendLog("[自动检测] Cron 表达式无效，已停止");
                    break;
                }

                var delay = nextTime.Value - DateTime.Now;
                if (delay > TimeSpan.Zero)
                {
                    SetStatus($"自动检测运行中，下次: {nextTime:HH:mm:ss}", true);
                    await Task.Delay(delay, ct);
                }
                if (ct.IsCancellationRequested) break;

                _autoDetectCycleCount++;
                AppendLog($"[自动检测] 第 {_autoDetectCycleCount} 轮开始执行 ({DateTime.Now:HH:mm:ss})");
                var (total, ok) = await RunAutoDetectCycle(ct);
                _autoDetectHistory.Add((DateTime.Now, total, ok));
                if (_autoDetectHistory.Count > 10) _autoDetectHistory.RemoveAt(0);
                SetStatus($"自动检测运行中，第 {_autoDetectCycleCount} 轮完成 ({ok}/{total}) | 历史成功率: {CalcSuccessRate():F1}%", true);
            }
        }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendLog($"[自动检测异常] {ex.Message}");
        }
        finally
        {
            _autoDetectRunning = false;
            SetAutoDetectBtnText("自动检测", PackIconKind.Radar);
            UpdateButtonStates();
            SetStatus($"自动检测已停止，共执行 {_autoDetectCycleCount} 次", true);
            AppendLog($"[自动检测] 已停止，共执行 {_autoDetectCycleCount} 次");
        }
    }

    /// <summary>执行一轮自动检测：携带 Token 访问选中的接口，更新结果表格，返回 (总数, 成功数)</summary>
    private async Task<(int Total, int Ok)> RunAutoDetectCycle(CancellationToken ct)
    {
        var baseUrl = GetBaseUrl();
        var token = _token;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
        {
            AppendLog("[自动检测] 缺少目标地址或 Token，跳过本轮");
            return (0, 0);
        }

        // 获取一个可用的 deviceId
        string? deviceId = null;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/device/deviceinfo/list?deviceName=&Address=&pageSize=1&page=1");
            AddTokenHeaders(req, token);
            var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                var data = obj["data"];
                JToken? items = null;
                if (data is JArray)
                    items = data;
                else if (data is JObject dObj)
                    items = dObj["list"] ?? dObj["records"] ?? dObj["rows"] ?? data;
                if (items is JArray arr && arr.Count > 0)
                    deviceId = arr[0]?["id"]?.ToString();
            }
        }
        catch { }
        if (string.IsNullOrEmpty(deviceId)) deviceId = "1";

        var apis = _allApis
            .Where(a => _autoDetectApis.Contains(a.Name))
            .Select(a => (a.Name, a.Method, a.Path.Replace("{deviceId}", deviceId)))
            .ToList();

        var results = new List<ApiResult>();
        int ok = 0;
        foreach (var (name, method, path) in apis)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                var req = new HttpRequestMessage(method == "GET" ? HttpMethod.Get : HttpMethod.Post, $"{baseUrl}{path}");
                AddTokenHeaders(req, token);
                if (method != "GET") req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                var resp = await _http.SendAsync(req, ct);
                sw.Stop();
                var code = (int)resp.StatusCode;
                var json = await resp.Content.ReadAsStringAsync();
                var result = code < 500 ? "正常" : "异常";
                if (result == "正常") ok++;
                else AppendLog($"[自动检测][异常] {name}    {method} {path}    状态码: {code}");
                results.Add(new ApiResult { Index = results.Count + 1, Name = name, Method = method, Path = path, StatusCode = code, Elapsed = sw.ElapsedMilliseconds, Result = result, Message = ExtractMessage(json, code) });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                sw.Stop();
                AppendLog($"[自动检测][异常] {name}: {ex.Message}");
                results.Add(new ApiResult { Index = results.Count + 1, Name = name, Method = method, Path = path, StatusCode = 0, Elapsed = sw.ElapsedMilliseconds, Result = "异常", Message = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message });
            }
        }

        _resultGrid.ItemsSource = null;
        _resultGrid.ItemsSource = results;
        AutoSizeGridColumns();
        SetSummary(ok, results.Count - ok);
        AppendLog($"[自动检测] 第 {_autoDetectCycleCount} 轮完成: 正常 {ok}, 异常 {results.Count - ok}");
        return (results.Count, ok);
    }

    /// <summary>最近 10 次自动检测的总体成功率</summary>
    private double CalcSuccessRate()
    {
        int total = 0, okSum = 0;
        foreach (var (_, t, o) in _autoDetectHistory) { total += t; okSum += o; }
        return total == 0 ? 100 : okSum * 100.0 / total;
    }

    // ========== Cron 解析（6 字段：秒 分 时 日 月 周） ==========

    /// <summary>计算 cron 表达式的下一次执行时间，支持 5/6/7 字段格式，无效返回 null</summary>
    private static DateTime? GetNextCronTime(string cron, DateTime from)
    {
        var parts = cron.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || parts.Length > 7) return null;

        // 补齐/截取到 6 字段（秒 分 时 日 月 周）
        string[] fields;
        if (parts.Length == 5)
            fields = new[] { "0", parts[0], parts[1], parts[2], parts[3], parts[4] };
        else if (parts.Length == 7)
            fields = parts[..6]; // 忽略年份字段
        else
            fields = parts;

        var candidate = from.AddSeconds(1); // 从下一秒开始
        var maxIterations = 366 * 24 * 60 * 60; // 最多搜索一年

        for (int i = 0; i < maxIterations; i++)
        {
            if (MatchField(fields[0], candidate.Second) &&
                MatchField(fields[1], candidate.Minute) &&
                MatchField(fields[2], candidate.Hour) &&
                MatchField(fields[3], candidate.Day) &&
                MatchField(fields[4], candidate.Month) &&
                MatchField(fields[5], (int)candidate.DayOfWeek == 0 ? 7 : (int)candidate.DayOfWeek))
            {
                return candidate;
            }
            candidate = candidate.AddSeconds(1);
        }
        return null;
    }

    private static bool MatchField(string field, int value)
    {
        if (field == "*" || field == "?") return true;
        // 逗号分隔：1,15,30
        foreach (var part in field.Split(','))
            if (MatchSingle(part.Trim(), value)) return true;
        return false;
    }

    private static bool MatchSingle(string part, int value)
    {
        if (part == "*" || part == "?") return true;
        try
        {
            // 步长：*/5 或 0/5
            if (part.Contains('/'))
            {
                var split = part.Split('/');
                int start = split[0] == "*" ? 0 : int.Parse(split[0]);
                int step = int.Parse(split[1]);
                if (step <= 0) return false;
                return value >= start && (value - start) % step == 0;
            }
            // 范围：1-5
            if (part.Contains('-'))
            {
                var split = part.Split('-');
                return value >= int.Parse(split[0]) && value <= int.Parse(split[1]);
            }
            // 精确匹配
            return int.TryParse(part, out int exact) && exact == value;
        }
        catch { return false; }
    }

    private void AutoSizeGridColumns()
    {
        var items = _resultGrid.ItemsSource as IList<ApiResult>;
        if (items == null || items.Count == 0) return;
        var dpi = VisualTreeHelper.GetDpi(_resultGrid).PixelsPerDip;
        var typeface = new Typeface("Microsoft YaHei");
        const double padding = 32;

        double MeasureText(string text)
        {
            var ft = new FormattedText(text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 13, Brushes.Black, dpi);
            return ft.Width;
        }

        var dataSelectors = new Func<ApiResult, string>[]
        {
            r => r.Index.ToString(),
            r => r.Name,
            r => r.Method,
            r => r.Path,
            r => r.StatusCode.ToString(),
            r => r.Elapsed.ToString(),
            r => r.Result
        };

        for (int c = 0; c < _resultGrid.Columns.Count && c < dataSelectors.Length; c++)
        {
            double maxW = MeasureText(_resultGrid.Columns[c].Header?.ToString() ?? "") + padding;
            var sel = dataSelectors[c];
            foreach (var item in items)
            {
                var w = sel(item);
                var measured = MeasureText(w) + padding;
                if (measured > maxW) maxW = measured;
            }
            // “路径”列限制最大宽度，避免挤压其他列
            if (c == 3) maxW = Math.Min(maxW, 280);       // 路径
            if (c == 1) maxW += 16;                         // 接口名称加宽约一个字符
            _resultGrid.Columns[c].Width = new DataGridLength(maxW, DataGridLengthUnitType.Pixel);
        }
    }

    private static string ExtractMessage(string json, int statusCode)
    {
        try
        {
            var obj = JObject.Parse(json);
            var msg = obj["msg"]?.ToString() ?? obj["message"]?.ToString();
            if (!string.IsNullOrEmpty(msg)) return msg;
            var data = obj["data"];
            if (data is JArray arr) return $"返回 {arr.Count} 条记录";
            if (data is JObject dObj)
            {
                var list = dObj["list"] ?? dObj["records"] ?? dObj["rows"];
                if (list is JArray la) return $"返回 {la.Count} 条记录";
            }
        }
        catch { }
        var clean = json.Replace("\n", " ").Replace("\r", "");
        return clean.Length > 80 ? clean[..80] : clean;
    }

    private HashSet<string>? ShowApiSelectDialog()
    {
        var dlg = new Window
        {
            Title = "选择要验证的接口",
            Width = 380,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "选择要验证的接口（已登录，将携带 Token 访问）:", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        var cbPanel = new StackPanel();
        var cbs = new List<CheckBox>();
        foreach (var api in _allApis)
        {
            var cb = new CheckBox { Content = api.Name, IsChecked = true, FontFamily = new FontFamily("Microsoft YaHei"), Margin = new Thickness(0, 2, 0, 0) };
            cbs.Add(cb);
            cbPanel.Children.Add(cb);
        }
        var scroll = new ScrollViewer { Content = cbPanel, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(scroll);
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        var okBtn = new Button { Content = "确定", Width = 80, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource("MaterialDesignRaisedButton") as Style };
        okBtn.Click += (s, e) => { dlg.DialogResult = true; dlg.Close(); };
        var cancelBtn = new Button { Content = "取消", Width = 80, Style = TryFindResource("MaterialDesignOutlinedButton") as Style };
        cancelBtn.Click += (s, e) => { dlg.DialogResult = false; dlg.Close(); };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);
        dlg.Content = panel;
        if (dlg.ShowDialog() != true) return null;
        var selected = new HashSet<string>();
        foreach (var cb in cbs)
        {
            if (cb.IsChecked == true && cb.Content is string name)
                selected.Add(name);
        }
        return selected;
    }

    // ========== 辅助 ==========

    private string GetBaseUrl()
    {
        var ip = _ipBox.Text.Trim();
        var port = _portBox.Text.Trim();
        if (string.IsNullOrEmpty(ip)) return "";
        if (string.IsNullOrEmpty(port)) port = "8088";
        return $"http://{ip}:{port}";
    }

    private void SetSummary(int success, int fail)
    {
        _summaryText.Text = $"正常: {success}    异常: {fail}    总计: {success + fail}";
        _summaryText.Foreground = fail == 0 ? Brushes.Green : Brushes.Red;
    }

    private void AppendLog(string text)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
        _logBox.ScrollToEnd();
        FileLogger.Write("ApiValidation", text);
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }
}

public class ApiResult
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public long Elapsed { get; set; }
    public string Result { get; set; } = "";
    public string Message { get; set; } = "";
}
