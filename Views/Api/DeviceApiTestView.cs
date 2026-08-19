using System.Net.Http;
using System.Net.Http.Headers;
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

public class DeviceApiTestView : UserControl
{
    // AES256(CBC) 常量 —— 来自接口设计说明书
    private const string AES_KEY = "32DGoR8HdfIiw1judwJHY&^%1_aFSSJw";
    private const string AES_IV  = "32DGoR8HdfIiw1ju";

    private TextBox _ipBox = new();
    private TextBox _portBox = new();
    private TextBox _userBox = new();
    private PasswordBox _passBox = new();
    private TextBox _tokenBox = new();
    private TextBox _logBox = new();
    private DataGrid _deviceGrid = new();
    private DataGrid _mqttGrid = new();
    private DockPanel _gridPanel = new();   // 设备列表展示面板
    private DockPanel _mqttPanel = new();   // MQTT 主题展示面板（与设备面板共用展示区，二选一显示）
    private bool _mqttActive;               // 当前展示的是否为 MQTT 主题列表
    private TextBlock _statusText = new();
    private Button _loginBtn = new();
    private Button _getDeviceBtn = new();
    private bool _built;
    private readonly HttpClient _http = new();
    private string? _token;

    // 布局：默认窗口下设备列表固定显示 4 条数据（多余数据内部滚动查看）、日志区固定高度；窗口放大后设备列表按剩余空间自适应
    private RowDefinition _resultRow = new();
    private const double DefaultWindowHeight = 768; // 与 MainWindow 默认窗口高度一致
    private const int VisibleDataRows = 4;          // 默认窗口下可见数据条数
    private const double GridRowHeight = 32;        // 单条数据行高（三列均为单行短文本）
    private const double GridHeaderHeight = 44;     // 表头高度（显式固定，保证高度推导确定）
    private const double GridChromeHeight = 26;     // “设备列表”标签 20 + 边框 2 + 面板下边距 4
    /// <summary>设备列表区固定高度 = 表头 + 4 条数据 + 外框修饰</summary>
    private static double ResultFixedHeight => GridHeaderHeight + VisibleDataRows * GridRowHeight + GridChromeHeight;
    private const int MqttVisibleRows = 3;            // MQTT 主题区默认可见条数（3 条，节省纵向空间）
    /// <summary>MQTT主题区固定高度 = 表头 + 3 条数据 + 外框修饰</summary>
    private static double MqttFixedHeight => GridHeaderHeight + MqttVisibleRows * GridRowHeight + GridChromeHeight;
    private const double LogFixedHeight = 200;      // 日志区固定高度（含“响应日志”标签）

    public DeviceApiTestView()
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

    /// <summary>默认窗口（≤768 高）下设备列表固定显示 4 条数据；窗口放大后按剩余空间自适应（日志区始终固定高度）</summary>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResultRowHeight();

    private void UpdateResultRowHeight()
    {
        var win = Window.GetWindow(this);
        if (win == null) return;
        var extra = win.ActualHeight - DefaultWindowHeight;
        // 列表区基础高度随当前展示的面板切换（设备列表 4 条 / MQTT 主题 3 条）
        var baseHeight = _mqttActive ? MqttFixedHeight : ResultFixedHeight;
        _resultRow.Height = new GridLength(extra > 0 ? baseHeight + extra : baseHeight);
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

        // 顶部面板
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 标题行（图标 + 文字）
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.CellphoneLink, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  获取设备ID",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);

        topPanel.Children.Add(new TextBlock
        {
            Text = "获取极早期火灾探测系统中的设备名称、通信地址和设备ID。用户名和密码自动使用 AES256(CBC) 加密传输。",
            FontSize = 13,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 连接参数行
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        connRow.Children.Add(MakeLabel("IP 地址:"));
        _ipBox = MakeBox("如 192.168.1.1", "", 180);
        connRow.Children.Add(_ipBox);
        connRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeBox("端口号", "8088", 80);
        connRow.Children.Add(_portBox);
        topPanel.Children.Add(connRow);

        // 用户名 + 密码行
        var authRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        authRow.Children.Add(MakeLabel("用户名:"));
        _userBox = MakeBox("用户名（明文，自动加密）", "", 180);
        authRow.Children.Add(_userBox);
        authRow.Children.Add(MakeLabel("密码:"));
        _passBox = MakePasswordBox("密码（明文，自动加密）", 180);
        authRow.Children.Add(_passBox);
        authRow.Children.Add(MakeButton("查看加密值", ShowEncryptedValues, false, PackIconKind.KeyVariant));
        topPanel.Children.Add(authRow);

        // Token 行
        var tokenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        tokenRow.Children.Add(MakeLabel("Token:"));
        _tokenBox = MakeBox("登录后自动填入", "", 500);
        _tokenBox.IsReadOnly = true;
        tokenRow.Children.Add(_tokenBox);
        topPanel.Children.Add(tokenRow);

        // 操作按钮
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _loginBtn = MakeButton("登录", Login, true, PackIconKind.Login);
        btnRow.Children.Add(_loginBtn);
        _getDeviceBtn = MakeButton("获取设备ID", GetDeviceIds, false, PackIconKind.CellphoneInformation);
        btnRow.Children.Add(_getDeviceBtn);
        // MQTT 主题：与设备列表共用展示区，点击后切换显示 MQTT 主题表格
        btnRow.Children.Add(MakeButton("MQTT主题", GetMqttTopics, false, PackIconKind.Antenna));
        // 清空结果：同时清空设备列表与 MQTT 主题列表，并切回设备列表视图
        btnRow.Children.Add(MakeButton("清空结果", () => { _deviceGrid.ItemsSource = null; _mqttGrid.ItemsSource = null; ShowDevicePanel(); _logBox.Clear(); _tokenBox.Text = ""; _token = null; SetStatus("", true); }, false, PackIconKind.Eraser));
        // 导出：导出当前展示的列表（设备列表 或 MQTT主题）
        btnRow.Children.Add(MakeButton("导出", ExportCurrent, false, PackIconKind.FileExcel));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnRow.Children.Add(_statusText);
        topPanel.Children.Add(btnRow);

        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // 设备列表表格
        var gridLabel = new TextBlock
        {
            Text = "设备列表",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(gridLabel, Dock.Top);

        _deviceGrid.AutoGenerateColumns = false;
        _deviceGrid.IsReadOnly = true;
        _deviceGrid.CanUserAddRows = false;
        _deviceGrid.CanUserDeleteRows = false;
        _deviceGrid.SelectionMode = DataGridSelectionMode.Extended;
        _deviceGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        _deviceGrid.FontFamily = new FontFamily("Microsoft YaHei");
        _deviceGrid.FontSize = 13;
        _deviceGrid.RowHeight = GridRowHeight;             // 固定单条数据行高
        _deviceGrid.ColumnHeaderHeight = GridHeaderHeight; // 固定表头高度，与高度推导保持一致
        _deviceGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));

        _deviceGrid.Columns.Add(new DataGridTextColumn { Header = "设备名称", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _deviceGrid.Columns.Add(new DataGridTextColumn { Header = "通信地址", Binding = new System.Windows.Data.Binding("Address"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _deviceGrid.Columns.Add(new DataGridTextColumn { Header = "设备ID", Binding = new System.Windows.Data.Binding("Id"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });

        // DataGrid 使用内置滚动条（表头固定 + 行虚拟化），高度完全由外层 Grid 行比例约束，数据增多时区域高度不变、内部滚动查看
        var gridBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 0),
            Child = _deviceGrid
        };

        _gridPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        _gridPanel.Children.Add(gridLabel);
        _gridPanel.Children.Add(gridBorder);

        // ===== MQTT 主题列表（与设备列表共用展示区，点击「MQTT主题」按钮时切换显示，独立 3 列）=====
        var mqttLabel = new TextBlock
        {
            Text = "MQTT主题",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(mqttLabel, Dock.Top);

        _mqttGrid.AutoGenerateColumns = false;
        _mqttGrid.IsReadOnly = true;
        _mqttGrid.CanUserAddRows = false;
        _mqttGrid.CanUserDeleteRows = false;
        _mqttGrid.SelectionMode = DataGridSelectionMode.Extended;
        _mqttGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        _mqttGrid.FontFamily = new FontFamily("Microsoft YaHei");
        _mqttGrid.FontSize = 13;
        _mqttGrid.RowHeight = GridRowHeight;
        _mqttGrid.ColumnHeaderHeight = GridHeaderHeight;
        _mqttGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));

        _mqttGrid.Columns.Add(new DataGridTextColumn { Header = "网关名称", Binding = new System.Windows.Data.Binding("GateName"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _mqttGrid.Columns.Add(new DataGridTextColumn { Header = "订阅主题", Binding = new System.Windows.Data.Binding("MqttDownTopic"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });
        _mqttGrid.Columns.Add(new DataGridTextColumn { Header = "发布主题", Binding = new System.Windows.Data.Binding("MqttUpTopic"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), CanUserResize = false });

        var mqttBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Child = _mqttGrid
        };

        _mqttPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        _mqttPanel.Children.Add(mqttLabel);
        _mqttPanel.Children.Add(mqttBorder);
        // 默认展示设备列表，MQTT 主题面板隐藏；点击「获取设备ID」/「MQTT主题」按钮时切换
        _mqttPanel.Visibility = Visibility.Collapsed;

        // 日志输出区（高度由外层 Grid 等比分配，与表格区等高）
        var logPanel = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var logLabel = new TextBlock
        {
            Text = "响应日志",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(logLabel, Dock.Top);
        logPanel.Children.Add(logLabel);

        _logBox.AcceptsReturn = true;
        _logBox.TextWrapping = TextWrapping.Wrap;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.IsReadOnly = true;
        _logBox.FontFamily = new FontFamily("Microsoft YaHei");
        _logBox.FontSize = 12;
        _logBox.VerticalContentAlignment = VerticalAlignment.Top;
        var logStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (logStyle != null) _logBox.Style = logStyle;
        logPanel.Children.Add(_logBox);

        // 两行 Grid：列表展示区（设备列表 / MQTT主题 二选一显示，默认固定高度、窗口放大后自适应） + 日志区（固定高度）
        var contentGrid = new Grid();
        _resultRow = new RowDefinition { Height = new GridLength(ResultFixedHeight) }; // 默认容纳 4 条数据
        contentGrid.RowDefinitions.Add(_resultRow);
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LogFixedHeight) }); // 日志区固定高度
        Grid.SetRow(_gridPanel, 0);
        contentGrid.Children.Add(_gridPanel);
        Grid.SetRow(_mqttPanel, 0);
        contentGrid.Children.Add(_mqttPanel);
        Grid.SetRow(logPanel, 1);
        contentGrid.Children.Add(logPanel);
        root.Children.Add(contentGrid);

        Content = root;
    }

    // ========== AES 加密（与接口说明书一致）==========

    private static string AesEncryptBase64(string plainText)
    {
        var key = Encoding.UTF8.GetBytes(AES_KEY);
        var iv  = Encoding.UTF8.GetBytes(AES_IV);
        var data = Encoding.UTF8.GetBytes(plainText);

        // 密钥 256 bits（32 字节），取前 32 字节
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

        var encUser = AesEncryptBase64(user);
        var encPass = AesEncryptBase64(pass);

        SetStatus("正在登录...", true);
        try
        {
            var loginUrl = $"{baseUrl}/sys/login?userName={Uri.EscapeDataString(encUser)}&password={Uri.EscapeDataString(encPass)}";
            var resp = await _http.PostAsync(loginUrl, null);
            var json = await resp.Content.ReadAsStringAsync();

            AppendLog($"[POST /sys/login] HTTP {(int)resp.StatusCode}");
            AppendLog($"加密用户名: {encUser}");
            AppendLog($"加密密码: {encPass}");

            if (resp.IsSuccessStatusCode)
            {
                var obj = JObject.Parse(json);
                // 尝试从常见字段获取 token
                var token = obj["data"]?["token"]?.ToString()
                         ?? obj["token"]?.ToString()
                         ?? obj["data"]?.ToString();

                if (!string.IsNullOrEmpty(token))
                {
                    _token = token;
                    _tokenBox.Text = token;
                    SetStatus("登录成功，Token 已获取", true);
                }
                else
                {
                    SetStatus("登录返回成功但未解析到 Token，请查看日志", false);
                }
            }
            else
            {
                SetStatus($"登录失败: HTTP {(int)resp.StatusCode}", false);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            SetStatus($"登录异常: {ex.Message}", false);
        }
    }

    // ========== 获取设备ID ==========

    private async void GetDeviceIds()
    {
        var baseUrl = GetBaseUrl();
        var token = _token;
        if (string.IsNullOrEmpty(baseUrl)) { SetStatus("请输入 IP 和端口", false); return; }
        if (string.IsNullOrEmpty(token)) { SetStatus("请先登录获取 Token", false); return; }

        ShowDevicePanel();
        SetStatus("正在获取设备列表...", true);
        try
        {
            var url = $"{baseUrl}/device/deviceinfo/list?deviceName=&Address=&pageSize=100&page=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            // 多种方式传递 Token，兼容不同服务端配置
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("token", token);

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            AppendLog($"[GET /device/deviceinfo/list] HTTP {(int)resp.StatusCode}");

            if (resp.IsSuccessStatusCode)
            {
                var obj = JObject.Parse(json);
                var data = obj["data"];
                // 兼容多种返回格式：data直接是数组 / data.list / data.records
                JToken? items = null;
                if (data is JArray)
                    items = data;
                else if (data is JObject dObj)
                    items = dObj["list"] ?? dObj["records"] ?? dObj["rows"] ?? data;

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
                else
                {
                    AppendLog(json);
                    SetStatus("返回格式异常，请查看日志", false);
                }
            }
            else
            {
                AppendLog(json);
                SetStatus($"请求失败: HTTP {(int)resp.StatusCode}", false);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            SetStatus($"请求异常: {ex.Message}", false);
        }
    }

    // ========== 获取 MQTT 主题（独立列表）==========

    private async void GetMqttTopics()
    {
        var baseUrl = GetBaseUrl();
        var token = _token;
        if (string.IsNullOrEmpty(baseUrl)) { SetStatus("请输入 IP 和端口", false); return; }
        if (string.IsNullOrEmpty(token)) { SetStatus("请先登录获取 Token", false); return; }

        ShowMqttPanel();
        SetStatus("正在获取 MQTT 主题...", true);
        try
        {
            var url = $"{baseUrl}/device/deviceinfo/list?deviceName=&comAddress=&pageSize=100&page=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            // 多种方式传递 Token，兼容不同服务端配置
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("token", token);

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            AppendLog($"[GET /device/deviceinfo/list] HTTP {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode)
            {
                AppendLog(json);
                SetStatus($"请求失败: HTTP {(int)resp.StatusCode}", false);
                return;
            }

            var obj = JObject.Parse(json);
            var data = obj["data"];
            // 兼容多种返回格式：data直接是数组 / data.list / data.records / data.rows
            JToken? items = null;
            if (data is JArray)
                items = data;
            else if (data is JObject dObj)
                items = dObj["list"] ?? dObj["records"] ?? dObj["rows"] ?? data;

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
            else
            {
                AppendLog(json);
                SetStatus("返回格式异常，请查看日志", false);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            SetStatus($"请求异常: {ex.Message}", false);
        }
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

    // ========== 展示区切换（设备列表 / MQTT主题 二选一）==========

    private void ShowDevicePanel()
    {
        _mqttActive = false;
        _gridPanel.Visibility = Visibility.Visible;
        _mqttPanel.Visibility = Visibility.Collapsed;
        UpdateResultRowHeight();
    }

    private void ShowMqttPanel()
    {
        _mqttActive = true;
        _gridPanel.Visibility = Visibility.Collapsed;
        _mqttPanel.Visibility = Visibility.Visible;
        UpdateResultRowHeight();
    }

    /// <summary>导出当前展示的列表：MQTT 主题视图导出 MQTT 列表，否则导出设备列表</summary>
    private void ExportCurrent()
    {
        if (_mqttActive) ExportMqttToExcel();
        else ExportToExcel();
    }

    // ========== 辅助方法 ==========

    private string GetBaseUrl()
    {
        var ip = _ipBox.Text.Trim();
        var port = _portBox.Text.Trim();
        if (string.IsNullOrEmpty(ip)) return "";
        if (string.IsNullOrEmpty(port)) port = "8088";
        return $"http://{ip}:{port}";
    }

    private void ShowEncryptedValues()
    {
        var user = _userBox.Text.Trim();
        var pass = _passBox.Password;
        if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass))
        {
            SetStatus("请输入用户名或密码", false);
            return;
        }

        AppendLog("===== AES256(CBC) 加密结果 =====");
        if (!string.IsNullOrEmpty(user))
            AppendLog($"用户名 [{user}] → {AesEncryptBase64(user)}");
        if (!string.IsNullOrEmpty(pass))
            AppendLog($"密码 [{pass}] → {AesEncryptBase64(pass)}");
        AppendLog($"密钥 = {AES_KEY}");
        AppendLog($"偏移量 = {AES_IV}");
        AppendLog("================================");
        SetStatus("加密值已输出到日志", true);
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
        // 通信地址、设备ID 列额外加宽约2个字符
        if (_deviceGrid.Columns.Count > 0)
            _deviceGrid.Columns[0].Width = new DataGridLength(
                _deviceGrid.Columns[0].ActualWidth + 50, DataGridLengthUnitType.Pixel);
        if (_deviceGrid.Columns.Count > 1)
            _deviceGrid.Columns[1].Width = new DataGridLength(
                _deviceGrid.Columns[1].ActualWidth + 26, DataGridLengthUnitType.Pixel);
        if (_deviceGrid.Columns.Count > 2)
            _deviceGrid.Columns[2].Width = new DataGridLength(
                _deviceGrid.Columns[2].ActualWidth + 26, DataGridLengthUnitType.Pixel);
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

    private void AppendLog(string text)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
        _logBox.ScrollToEnd();
        FileLogger.Write("DeviceApi", text);
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
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
            // 表头
            sheet.Cells[1, 1].Value = "设备名称";
            sheet.Cells[1, 2].Value = "通信地址";
            sheet.Cells[1, 3].Value = "设备ID";
            // 表头样式
            using (var headerRange = sheet.Cells[1, 1, 1, 3])
            {
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
                headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }
            // 数据行
            for (int i = 0; i < items.Count; i++)
            {
                sheet.Cells[i + 2, 1].Value = items[i].Name;
                sheet.Cells[i + 2, 2].Value = items[i].Address;
                sheet.Cells[i + 2, 3].Value = items[i].Id;
            }
            // 自动列宽
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

    // ========== 导出 MQTT 主题（独立导出）==========

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
            // 表头
            sheet.Cells[1, 1].Value = "网关名称";
            sheet.Cells[1, 2].Value = "订阅主题";
            sheet.Cells[1, 3].Value = "发布主题";
            // 表头样式
            using (var headerRange = sheet.Cells[1, 1, 1, 3])
            {
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
                headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }
            // 数据行
            for (int i = 0; i < items.Count; i++)
            {
                sheet.Cells[i + 2, 1].Value = items[i].GateName;
                sheet.Cells[i + 2, 2].Value = items[i].MqttDownTopic;
                sheet.Cells[i + 2, 3].Value = items[i].MqttUpTopic;
            }
            // 自动列宽
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
}

public class DeviceItem
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Id { get; set; } = "";
}

public class MqttTopicItem
{
    public string GateName { get; set; } = "";       // 名称（所属网关名称）
    public string MqttDownTopic { get; set; } = "";  // 订阅主题
    public string MqttUpTopic { get; set; } = "";    // 发布主题
}
