using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PackIcon = MaterialDesignThemes.Wpf.PackIcon;
using PackIconKind = MaterialDesignThemes.Wpf.PackIconKind;
using Newtonsoft.Json.Linq;
using OfficeOpenXml;
using ToolHelper.Services;

namespace ToolHelper.Views.Api;

/// <summary>
/// 极早期接口验证：整合「接口验证」与「获取设备ID」两个工具，共享登录与连接参数。
/// 四个功能：获取设备ID / MQTT主题 / 开始验证（16 接口批量）/ 自动检测（cron 定时），
/// TabControl 承载验证结果 / 设备列表 / MQTT主题 三个表格（极早期接口验证方案）。
/// </summary>
public class EarlyWarningApiView : UserControl
{
    private const string AES_KEY = "32DGoR8HdfIiw1judwJHY&^%1_aFSSJw";
    private const string AES_IV  = "32DGoR8HdfIiw1ju";

    private TextBox _ipBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private PasswordBox _passBox = new();
    private TextBox _logBox = new();
    private DataGrid _resultGrid = new();
    private DataGrid _deviceGrid = new();
    private DataGrid _mqttGrid = new();
    private TabControl _tabControl = new();
    private bool _built;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private string? _token;

    // 按钮引用（用于状态管理）
    private Button _loginBtn = new();
    private Button _encryptBtn = new();
    private Button _getDeviceBtn = new();
    private Button _mqttBtn = new();
    private Button _validateBtn = new();
    private Button _autoDetectBtn = new();
    private Button _clearBtn = new();
    private Button _exportBtn = new();
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

    public EarlyWarningApiView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        // 视图高度钉在宿主视口上（踩坑 #15 方案），表格区星号行填充、日志区固定高度
        ViewportFitHelper.FitToViewport(this, 620);
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
        var btn = new Button
        {
            Content = sp,
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource(primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton") as Style
        };
        btn.Click += (s, e) => handler();
        return btn;
    }

    private void BuildUI()
    {
        var root = new DockPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 标题行
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Api, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  极早期接口验证", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(titleRow);

        top.Children.Add(new TextBlock
        {
            Text = "登录后获取设备ID、MQTT主题，批量验证 16 个接口连通性，支持 cron 定时自动检测。",
            FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
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
        _userBox = MakeBox("用户名（明文，自动加密）", "", 150);
        connRow.Children.Add(_userBox);
        connRow.Children.Add(MakeLabel("密码:"));
        _passBox = MakePasswordBox("密码（明文，自动加密）", 150);
        connRow.Children.Add(_passBox);
        top.Children.Add(connRow);

        // 按钮行1：登录 / 查看加密值
        var authBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _loginBtn = MakeButton("登录", Login, true, PackIconKind.Login);
        authBtnRow.Children.Add(_loginBtn);
        _encryptBtn = MakeButton("查看加密值", ShowEncryptedValues, false, PackIconKind.KeyVariant);
        authBtnRow.Children.Add(_encryptBtn);
        top.Children.Add(authBtnRow);

        // 按钮行2：四功能（开始验证在前）+ 清空/导出（状态与统计均写入操作日志，不在按钮行展示）
        var funcBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _validateBtn = MakeButton("开始验证", ValidateAllApis, false, PackIconKind.CheckAll);
        funcBtnRow.Children.Add(_validateBtn);
        _getDeviceBtn = MakeButton("获取设备ID", GetDeviceIds, false, PackIconKind.CellphoneInformation);
        funcBtnRow.Children.Add(_getDeviceBtn);
        _mqttBtn = MakeButton("MQTT主题", GetMqttTopics, false, PackIconKind.Antenna);
        funcBtnRow.Children.Add(_mqttBtn);
        _autoDetectBtn = MakeButton("自动检测", ToggleAutoDetect, false, PackIconKind.Radar);
        funcBtnRow.Children.Add(_autoDetectBtn);
        _clearBtn = MakeButton("清空结果", () => { _logBox.Clear(); SetStatus("日志已清空", true); }, false, PackIconKind.Eraser);
        funcBtnRow.Children.Add(_clearBtn);
        _exportBtn = MakeButton("导出", ExportCurrent, false, PackIconKind.FileExcel);
        funcBtnRow.Children.Add(_exportBtn);
        top.Children.Add(funcBtnRow);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // ── TabControl：验证结果 / 设备列表 / MQTT主题 ──
        _tabControl = new TabControl { Margin = new Thickness(0, 0, 0, 4) };
        _tabControl.Items.Add(new TabItem { Header = "验证结果", Content = BuildResultTab() });
        _tabControl.Items.Add(new TabItem { Header = "设备列表", Content = BuildDeviceTab() });
        _tabControl.Items.Add(new TabItem { Header = "MQTT主题", Content = BuildMqttTab() });

        // ── 日志区 ──
        var logPanel = new DockPanel();
        var logLabel = new TextBlock
        {
            Text = "操作日志", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)), Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(logLabel, Dock.Top);
        logPanel.Children.Add(logLabel);
        _logBox.AcceptsReturn = true;
        _logBox.TextWrapping = TextWrapping.Wrap;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.IsReadOnly = true;
        _logBox.FontFamily = new FontFamily("Consolas");
        _logBox.FontSize = 12;
        _logBox.Background = new SolidColorBrush(Color.FromRgb(40, 44, 52));
        _logBox.Foreground = new SolidColorBrush(Color.FromRgb(171, 178, 191));
        _logBox.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 72));
        _logBox.VerticalContentAlignment = VerticalAlignment.Top;
        logPanel.Children.Add(_logBox);

        // 两行 Grid：表格区（星号行填充） + 日志区（固定高度）
        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, System.Windows.GridUnitType.Star) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
        Grid.SetRow(_tabControl, 0);
        contentGrid.Children.Add(_tabControl);
        Grid.SetRow(logPanel, 1);
        contentGrid.Children.Add(logPanel);
        root.Children.Add(contentGrid);

        Content = root;
        UpdateButtonStates();
    }

    /// <summary>验证结果 Tab：ApiResult 7 列 + 彩色结果触发器 + 双击行高展开</summary>
    private Border BuildResultTab()
    {
        _resultGrid.AutoGenerateColumns = false;
        _resultGrid.IsReadOnly = true;
        _resultGrid.CanUserAddRows = false;
        _resultGrid.CanUserDeleteRows = false;
        _resultGrid.FontFamily = new FontFamily("Microsoft YaHei");
        _resultGrid.FontSize = 13;
        _resultGrid.RowHeight = 50;
        _resultGrid.ColumnHeaderHeight = 44;
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

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 0),
            Child = _resultGrid
        };
    }

    /// <summary>设备列表 Tab：设备名称/通信地址/设备ID</summary>
    private Border BuildDeviceTab()
    {
        _deviceGrid.AutoGenerateColumns = false;
        _deviceGrid.IsReadOnly = true;
        _deviceGrid.CanUserAddRows = false;
        _deviceGrid.CanUserDeleteRows = false;
        _deviceGrid.SelectionMode = DataGridSelectionMode.Extended;
        _deviceGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        _deviceGrid.FontFamily = new FontFamily("Microsoft YaHei");
        _deviceGrid.FontSize = 13;
        _deviceGrid.RowHeight = 32;
        _deviceGrid.ColumnHeaderHeight = 44;
        _deviceGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));

        _deviceGrid.Columns.Add(new DataGridTextColumn { Header = "设备名称", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _deviceGrid.Columns.Add(new DataGridTextColumn { Header = "通信地址", Binding = new System.Windows.Data.Binding("Address"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _deviceGrid.Columns.Add(new DataGridTextColumn { Header = "设备ID", Binding = new System.Windows.Data.Binding("Id"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 0),
            Child = _deviceGrid
        };
    }

    /// <summary>MQTT主题 Tab：网关名称/订阅主题/发布主题</summary>
    private Border BuildMqttTab()
    {
        _mqttGrid.AutoGenerateColumns = false;
        _mqttGrid.IsReadOnly = true;
        _mqttGrid.CanUserAddRows = false;
        _mqttGrid.CanUserDeleteRows = false;
        _mqttGrid.SelectionMode = DataGridSelectionMode.Extended;
        _mqttGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        _mqttGrid.FontFamily = new FontFamily("Microsoft YaHei");
        _mqttGrid.FontSize = 13;
        _mqttGrid.RowHeight = 32;
        _mqttGrid.ColumnHeaderHeight = 44;
        _mqttGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));

        _mqttGrid.Columns.Add(new DataGridTextColumn { Header = "网关名称", Binding = new System.Windows.Data.Binding("GateName"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _mqttGrid.Columns.Add(new DataGridTextColumn { Header = "订阅主题", Binding = new System.Windows.Data.Binding("MqttDownTopic"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _mqttGrid.Columns.Add(new DataGridTextColumn { Header = "发布主题", Binding = new System.Windows.Data.Binding("MqttUpTopic"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 0),
            Child = _mqttGrid
        };
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
                    AppendLog($"登录成功: POST /sys/login    状态码: {code}    Token 已获取");
                    SetStatus("登录成功，Token 已获取", true);
                }
                else
                {
                    _token = null;
                    AppendLog($"[登录异常] 未获取到令牌: {Truncate(json)}");
                    SetStatus("登录成功但未获取到 Token", false);
                }
            }
            else
            {
                _token = null;
                AppendLog($"[登录失败] POST /sys/login    状态码: {code}    {Truncate(json)}");
                SetStatus($"登录失败: HTTP {code}", false);
            }
        }
        catch (Exception ex)
        {
            _token = null;
            AppendLog($"[登录异常] {ex.Message}");
            SetStatus($"登录异常: {ex.Message}", false);
        }
        UpdateButtonStates();
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

    // ========== 获取设备ID ==========

    private async void GetDeviceIds()
    {
        var baseUrl = GetBaseUrl();
        var token = _token;
        if (string.IsNullOrEmpty(baseUrl)) { SetStatus("请输入 IP 和端口", false); return; }
        if (string.IsNullOrEmpty(token)) { SetStatus("请先登录获取 Token", false); return; }

        _tabControl.SelectedIndex = 1;  // 切到设备列表 Tab
        SetStatus("正在获取设备列表...", true);
        try
        {
            var url = $"{baseUrl}/device/deviceinfo/list?deviceName=&Address=&pageSize=100&page=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddTokenHeaders(req, token);
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            AppendLog($"[GET /device/deviceinfo/list] HTTP {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode) { AppendLog(json); SetStatus($"请求失败: HTTP {(int)resp.StatusCode}", false); return; }

            var items = ParseDataArray(JObject.Parse(json)["data"]);
            if (items is JArray arr)
            {
                var devices = new List<DeviceItem>();
                for (int i = 0; i < arr.Count; i++)
                {
                    var dev = arr[i];
                    devices.Add(new DeviceItem
                    {
                        Name = dev["deviceName"]?.ToString() ?? "无",
                        Address = dev["comAddress"]?.ToString() ?? "无",
                        Id = dev["id"]?.ToString() ?? "无"
                    });
                }
                _deviceGrid.ItemsSource = devices;
                AutoSizeDeviceColumns();
                AppendLog($"获取成功，共 {devices.Count} 台设备");
                SetStatus($"获取成功，共 {devices.Count} 台设备", true);
            }
            else { AppendLog(json); SetStatus("返回格式异常，请查看日志", false); }
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); SetStatus($"请求异常: {ex.Message}", false); }
    }

    // ========== 获取 MQTT 主题 ==========

    private async void GetMqttTopics()
    {
        var baseUrl = GetBaseUrl();
        var token = _token;
        if (string.IsNullOrEmpty(baseUrl)) { SetStatus("请输入 IP 和端口", false); return; }
        if (string.IsNullOrEmpty(token)) { SetStatus("请先登录获取 Token", false); return; }

        _tabControl.SelectedIndex = 2;  // 切到 MQTT主题 Tab
        SetStatus("正在获取 MQTT 主题...", true);
        try
        {
            var url = $"{baseUrl}/device/deviceinfo/list?deviceName=&Address=&pageSize=100&page=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddTokenHeaders(req, token);
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            AppendLog($"[GET /device/deviceinfo/list] HTTP {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode) { AppendLog(json); SetStatus($"请求失败: HTTP {(int)resp.StatusCode}", false); return; }

            var items = ParseDataArray(JObject.Parse(json)["data"]);
            if (items is JArray arr)
            {
                var topics = new List<MqttTopicItem>();
                for (int i = 0; i < arr.Count; i++)
                {
                    var dev = arr[i];
                    topics.Add(new MqttTopicItem
                    {
                        GateName = GetField(dev, "gateName"),
                        MqttDownTopic = GetField(dev, "mqttDownTopic", "mqttDown", "downTopic"),
                        MqttUpTopic = GetField(dev, "mqttUpTopic", "mqttUp", "upTopic")
                    });
                }
                _mqttGrid.ItemsSource = topics;
                AutoSizeMqttColumns();
                AppendLog($"获取成功，共 {topics.Count} 台设备的 MQTT 主题");
                SetStatus($"获取成功，共 {topics.Count} 台设备", true);
            }
            else { AppendLog(json); SetStatus("返回格式异常，请查看日志", false); }
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); SetStatus($"请求异常: {ex.Message}", false); }
    }

    /// <summary>兼容多种返回格式：data 直接是数组 / data.list / data.records / data.rows</summary>
    private static JToken? ParseDataArray(JToken? data)
    {
        if (data is JArray) return data;
        if (data is JObject dObj) return dObj["list"] ?? dObj["records"] ?? dObj["rows"] ?? data;
        return null;
    }

    /// <summary>按候选 key 顺序取值，均不存在或为空时返回"无"（防御式解析，兼容后端字段名与文档不一致）</summary>
    private static string GetField(JToken dev, params string[] keys)
    {
        foreach (var k in keys)
        {
            var v = dev[k]?.ToString();
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return "无";
    }

    // ========== 开始验证 ==========

    private async void ValidateAllApis()
    {
        var baseUrl = GetBaseUrl();
        if (string.IsNullOrEmpty(baseUrl)) { SetStatus("请输入 IP 和端口", false); return; }
        var token = _token;
        if (string.IsNullOrEmpty(token)) { SetStatus("请先点击登录获取 Token", false); return; }

        var selectedApis = ShowApiSelectDialog();
        if (selectedApis == null) { SetStatus("已取消验证", false); return; }

        _tabControl.SelectedIndex = 0;  // 切到验证结果 Tab
        SetStatus("正在验证...", true);
        _validating = true;
        UpdateButtonStates();
        var results = new List<ApiResult>();
        var sw = new Stopwatch();

        // 先获取设备列表，获取一个可用的设备ID
        var deviceId = await GetFirstDeviceId(baseUrl, token);

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
                var req = new HttpRequestMessage(method == "GET" ? HttpMethod.Get : HttpMethod.Post, $"{baseUrl}{path}");
                AddTokenHeaders(req, token);
                if (method != "GET") req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                resp = await _http.SendAsync(req);
                sw.Stop();

                var code = (int)resp.StatusCode;
                var json = await resp.Content.ReadAsStringAsync();
                var msg = ExtractMessage(json, code);

                // 200~499 均表示接口可达（4xx说明接口存在但参数问题，5xx可能是服务端异常）
                var result = code < 500 ? "正常" : "异常";
                if (result == "正常") successCount++;

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

    /// <summary>获取一个可用设备ID（失败回退 "1"）</summary>
    private async Task<string> GetFirstDeviceId(string baseUrl, string token)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/device/deviceinfo/list?deviceName=&Address=&pageSize=1&page=1");
            AddTokenHeaders(req, token);
            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                var items = ParseDataArray(obj["data"]);
                if (items is JArray arr && arr.Count > 0)
                    return arr[0]?["id"]?.ToString() ?? "1";
            }
        }
        catch { }
        return "1";
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
            if (cb.IsChecked == true && cb.Content is string name)
                selected.Add(name);
        return selected;
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
    /// 未登录：四功能按钮（获取设备ID/MQTT主题/开始验证/自动检测）禁用，其余启用；
    /// 已登录/空闲：全部启用；
    /// 自动检测运行中：登录/四功能/清空禁用，查看加密值/导出保持启用；
    /// 手动验证进行中：登录/四功能/清空禁用。
    /// </summary>
    private void UpdateButtonStates()
    {
        bool loggedIn = !string.IsNullOrEmpty(_token);
        if (_autoDetectRunning)
        {
            _loginBtn.IsEnabled = false;
            _encryptBtn.IsEnabled = true;
            _getDeviceBtn.IsEnabled = false;
            _mqttBtn.IsEnabled = false;
            _validateBtn.IsEnabled = false;
            _autoDetectBtn.IsEnabled = true; // 运行中点击即停止
            _clearBtn.IsEnabled = false;
            _exportBtn.IsEnabled = true;
        }
        else if (_validating)
        {
            _loginBtn.IsEnabled = false;
            _encryptBtn.IsEnabled = true;
            _getDeviceBtn.IsEnabled = false;
            _mqttBtn.IsEnabled = false;
            _validateBtn.IsEnabled = false;
            _autoDetectBtn.IsEnabled = false;
            _clearBtn.IsEnabled = false;
            _exportBtn.IsEnabled = true;
        }
        else
        {
            _loginBtn.IsEnabled = true;
            _encryptBtn.IsEnabled = true;
            _getDeviceBtn.IsEnabled = loggedIn;
            _mqttBtn.IsEnabled = loggedIn;
            _validateBtn.IsEnabled = loggedIn;
            _autoDetectBtn.IsEnabled = loggedIn;
            _clearBtn.IsEnabled = true;
            _exportBtn.IsEnabled = true;
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
        _tabControl.SelectedIndex = 0;  // 结果展示在验证结果 Tab
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

        var deviceId = await GetFirstDeviceId(baseUrl, token);

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
        foreach (var part in field.Split(','))
            if (MatchSingle(part.Trim(), value)) return true;
        return false;
    }

    private static bool MatchSingle(string part, int value)
    {
        if (part == "*" || part == "?") return true;
        try
        {
            if (part.Contains('/'))
            {
                var split = part.Split('/');
                int start = split[0] == "*" ? 0 : int.Parse(split[0]);
                int step = int.Parse(split[1]);
                if (step <= 0) return false;
                return value >= start && (value - start) % step == 0;
            }
            if (part.Contains('-'))
            {
                var split = part.Split('-');
                return value >= int.Parse(split[0]) && value <= int.Parse(split[1]);
            }
            return int.TryParse(part, out int exact) && exact == value;
        }
        catch { return false; }
    }

    // ========== 表格列宽自适应 ==========

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
                var measured = MeasureText(sel(item)) + padding;
                if (measured > maxW) maxW = measured;
            }
            if (c == 3) maxW = Math.Min(maxW, 280);       // 路径列限制最大宽度
            if (c == 1) maxW += 16;                         // 接口名称加宽约一个字符
            _resultGrid.Columns[c].Width = new DataGridLength(maxW, DataGridLengthUnitType.Pixel);
        }
    }

    private void AutoSizeDeviceColumns()
    {
        var items = _deviceGrid.ItemsSource as IList<DeviceItem>;
        if (items == null || items.Count == 0) return;
        var dpi = VisualTreeHelper.GetDpi(_deviceGrid).PixelsPerDip;
        var typeface = new Typeface("Microsoft YaHei");

        double MeasureText(string text)
        {
            var ft = new FormattedText(text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 13, Brushes.Black, dpi);
            return ft.Width;
        }

        var dataSelectors = new Func<DeviceItem, string>[]
        {
            r => r.Name,
            r => r.Address,
            r => r.Id
        };

        for (int c = 0; c < _deviceGrid.Columns.Count && c < dataSelectors.Length; c++)
        {
            double maxW = MeasureText(_deviceGrid.Columns[c].Header?.ToString() ?? "") + 24;
            var sel = dataSelectors[c];
            foreach (var item in items)
            {
                var w = MeasureText(sel(item)) + 24;
                if (w > maxW) maxW = w;
            }
            _deviceGrid.Columns[c].Width = new DataGridLength(maxW, DataGridLengthUnitType.Pixel);
        }
        if (_deviceGrid.Columns.Count > 0)
            _deviceGrid.Columns[0].Width = new DataGridLength(_deviceGrid.Columns[0].ActualWidth + 50, DataGridLengthUnitType.Pixel);
        if (_deviceGrid.Columns.Count > 1)
            _deviceGrid.Columns[1].Width = new DataGridLength(_deviceGrid.Columns[1].ActualWidth + 26, DataGridLengthUnitType.Pixel);
        if (_deviceGrid.Columns.Count > 2)
            _deviceGrid.Columns[2].Width = new DataGridLength(_deviceGrid.Columns[2].ActualWidth + 26, DataGridLengthUnitType.Pixel);
    }

    private void AutoSizeMqttColumns()
    {
        var items = _mqttGrid.ItemsSource as IList<MqttTopicItem>;
        if (items == null || items.Count == 0) return;
        var dpi = VisualTreeHelper.GetDpi(_mqttGrid).PixelsPerDip;
        var typeface = new Typeface("Microsoft YaHei");

        double MeasureText(string text)
        {
            var ft = new FormattedText(text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 13, Brushes.Black, dpi);
            return ft.Width;
        }

        var dataSelectors = new Func<MqttTopicItem, string>[]
        {
            r => r.GateName,
            r => r.MqttDownTopic,
            r => r.MqttUpTopic
        };

        for (int c = 0; c < _mqttGrid.Columns.Count && c < dataSelectors.Length; c++)
        {
            double maxW = MeasureText(_mqttGrid.Columns[c].Header?.ToString() ?? "") + 24;
            var sel = dataSelectors[c];
            foreach (var item in items)
            {
                var w = MeasureText(sel(item)) + 24;
                if (w > maxW) maxW = w;
            }
            _mqttGrid.Columns[c].Width = new DataGridLength(maxW, DataGridLengthUnitType.Pixel);
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

    // ========== 导出 ==========

    /// <summary>按当前 Tab 导出：设备列表 / MQTT主题（验证结果暂不支持导出）</summary>
    private void ExportCurrent()
    {
        if (_tabControl.SelectedIndex == 1) ExportToExcel();
        else if (_tabControl.SelectedIndex == 2) ExportMqttToExcel();
        else SetStatus("验证结果暂不支持导出", false);
    }

    private void ExportToExcel()
    {
        var items = _deviceGrid.ItemsSource as IList<DeviceItem>;
        if (items == null || items.Count == 0)
        {
            SetStatus("无数据可导出", false);
            return;
        }

        ExcelPackage.License.SetNonCommercialPersonal("ToolHelper");
        var dlg = new SaveFileDialog
        {
            Filter = "Excel 文件 (*.xlsx)|*.xlsx",
            FileName = $"设备列表_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("设备列表");
            sheet.Cells[1, 1].Value = "设备名称";
            sheet.Cells[1, 2].Value = "通信地址";
            sheet.Cells[1, 3].Value = "设备ID";
            using (var headerRange = sheet.Cells[1, 1, 1, 3])
            {
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
                headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }
            for (int i = 0; i < items.Count; i++)
            {
                sheet.Cells[i + 2, 1].Value = items[i].Name;
                sheet.Cells[i + 2, 2].Value = items[i].Address;
                sheet.Cells[i + 2, 3].Value = items[i].Id;
            }
            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new System.IO.FileInfo(dlg.FileName));
            SetStatus($"导出成功: {dlg.FileName}", true);
            AppendLog($"导出成功: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"导出失败: {ex.Message}", false);
            AppendLog($"[导出失败] {ex.Message}");
        }
    }

    private void ExportMqttToExcel()
    {
        var items = _mqttGrid.ItemsSource as IList<MqttTopicItem>;
        if (items == null || items.Count == 0)
        {
            SetStatus("无 MQTT 主题数据可导出", false);
            return;
        }

        ExcelPackage.License.SetNonCommercialPersonal("ToolHelper");
        var dlg = new SaveFileDialog
        {
            Filter = "Excel 文件 (*.xlsx)|*.xlsx",
            FileName = $"MQTT主题_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("MQTT主题");
            sheet.Cells[1, 1].Value = "网关名称";
            sheet.Cells[1, 2].Value = "订阅主题";
            sheet.Cells[1, 3].Value = "发布主题";
            using (var headerRange = sheet.Cells[1, 1, 1, 3])
            {
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
                headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }
            for (int i = 0; i < items.Count; i++)
            {
                sheet.Cells[i + 2, 1].Value = items[i].GateName;
                sheet.Cells[i + 2, 2].Value = items[i].MqttDownTopic;
                sheet.Cells[i + 2, 3].Value = items[i].MqttUpTopic;
            }
            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new System.IO.FileInfo(dlg.FileName));
            SetStatus($"导出成功: {dlg.FileName}", true);
            AppendLog($"导出成功: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"导出失败: {ex.Message}", false);
            AppendLog($"[导出失败] {ex.Message}");
        }
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
        // 统计结果写入操作日志（不再在按钮行展示）
        AppendLog($"验证统计: 正常 {success}    异常 {fail}    总计 {success + fail}");
    }

    private void AppendLog(string text)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
        _logBox.ScrollToEnd();
        FileLogger.Write("EarlyWarningApi", text);
    }

    private void SetStatus(string msg, bool success)
    {
        // 状态提示统一写入操作日志（界面不再展示状态栏）
        AppendLog(success ? msg : $"⚠ {msg}");
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

public class DeviceItem
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Id { get; set; } = "";
}

public class MqttTopicItem
{
    public string GateName { get; set; } = "";
    public string MqttDownTopic { get; set; } = "";
    public string MqttUpTopic { get; set; } = "";
}
