using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using ToolHelper.Services;
using DataGridTextColumn = System.Windows.Controls.DataGridTextColumn;

namespace ToolHelper.Views.Serial;

/// <summary>
/// 极早期 Modbus 调试：申弘 / 南瑞怡和双协议 Modbus RTU。
/// 自动扫描站端下辖设备，多设备轮询、一键读取全量数据并解析为中文。
/// </summary>
public class EarlyWarningModbusView : UserControl
{
    // ===== 常量 =====
    private const int TimeoutMs = 500;              // 响应超时
    private const int MaxRetries = 2;               // 超时重发次数上限
    private const int DefaultPollIntervalMs = 2000; // 轮询间隔

    // ===== 请求-响应状态机 =====
    private enum PendingCmd { None, ScanDeviceList, ScanCommStatus, ShenHongReadAll, NrReadInput, NrReadHolding, NrReset }

    // ===== 串口与定时器 =====
    private SerialPort? _serialPort;
    private DispatcherTimer? _pollTimer;     // 轮询（2000ms）
    private DispatcherTimer? _timeoutTimer;  // 超时检查（100ms）
    private readonly object _rxLock = new();
    private readonly List<byte> _rxBuffer = new();   // 接收缓冲（跨包拼帧）
    private ModbusProtocolKind _protocol = ModbusProtocolKind.ShenHong;

    private PendingCmd _pendingCmd = PendingCmd.None;
    private byte _pendingAddr;
    private byte[] _pendingTx = Array.Empty<byte>();
    private DateTime _lastTxTime;
    private int _retryCount;
    private bool _awaitingReply;

    // ===== 多设备状态 =====
    private readonly Dictionary<byte, DeviceData> _devices = new();
    private readonly List<byte> _deviceQueue = new();
    private readonly ObservableCollection<DeviceData> _deviceItems = new();
    private int _pollIndex;
    private byte? _currentDeviceAddr;
    private bool _polling;

    // ===== UI 控件 =====
    private bool _built;
    private ComboBox _protocolCombo = new();
    private ComboBox _portCombo = new();
    private ComboBox _baudCombo = new();
    private TextBox _devAddrBox = new();
    private Button _openBtn = new(), _closeBtn = new();
    private Button _scanBtn = new(), _readBtn = new(), _pollBtn = new(), _resetBtn = new();
    private TextBlock _statusText = new();
    private DataGrid _deviceListView = new();
    private TextBlock _detailText = new();
    private TextBox _rawLogBox = new();

    public EarlyWarningModbusView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        RefreshPorts();
    }

    // ================== UI 构建 ==================

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text, FontSize = 12, Margin = new Thickness(0, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private ComboBox MakeCombo(string[] items, int defaultIndex = 0)
    {
        var cb = new ComboBox
        {
            FontSize = 13,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 90,
            BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var disabledStyle = new Style(typeof(ComboBox), cb.Style);
        disabledStyle.Triggers.Add(new Trigger { Property = IsEnabledProperty, Value = false, Setters = { new Setter(OpacityProperty, 1.0) } });
        cb.Style = disabledStyle;
        foreach (var item in items) cb.Items.Add(item);
        if (items.Length > 0) cb.SelectedIndex = Math.Min(defaultIndex, items.Length - 1);
        return cb;
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

        // 标题
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.SerialPort, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  极早期Modbus调试", FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);
        topPanel.Children.Add(new TextBlock
        {
            Text = "支持申弘/南瑞怡和双协议 Modbus RTU，自动扫描下辖设备，一键读取全量数据并解析为中文。",
            FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // ===== 配置行：协议 / 串口号 / 波特率 / 设备地址 =====
        var configRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        configRow.Children.Add(MakeLabel("协议:"));
        _protocolCombo = MakeCombo(new[] { "申弘版", "南瑞怡和版" }, 0);
        _protocolCombo.SelectionChanged += (s, e) =>
        {
            _protocol = _protocolCombo.SelectedIndex == 0 ? ModbusProtocolKind.ShenHong : ModbusProtocolKind.NanRuiYiHe;
            UpdateButtons();
        };
        configRow.Children.Add(_protocolCombo);

        configRow.Children.Add(MakeLabel("串口号:"));
        _portCombo = new ComboBox { FontSize = 13, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)), BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center };
        var portStyle = new Style(typeof(ComboBox), _portCombo.Style);
        portStyle.Triggers.Add(new Trigger { Property = IsEnabledProperty, Value = false, Setters = { new Setter(OpacityProperty, 1.0) } });
        _portCombo.Style = portStyle;
        configRow.Children.Add(_portCombo);
        configRow.Children.Add(MakeButton("刷新", RefreshPorts, false, PackIconKind.Refresh));

        configRow.Children.Add(MakeLabel("波特率:"));
        _baudCombo = MakeCombo(new[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" }, 3);
        configRow.Children.Add(_baudCombo);

        configRow.Children.Add(MakeLabel("设备地址:"));
        _devAddrBox = new TextBox { Text = "1", FontSize = 13, Width = 60, Margin = new Thickness(0, 0, 4, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)), BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center };
        configRow.Children.Add(_devAddrBox);

        topPanel.Children.Add(configRow);

        // ===== 连接 + 操作行 =====
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _openBtn = MakeButton("打开串口", OpenPort, true, PackIconKind.PowerPlug);
        _closeBtn = MakeButton("关闭串口", ClosePort, false, PackIconKind.PowerPlugOff);
        _closeBtn.IsEnabled = false;
        connRow.Children.Add(_openBtn);
        connRow.Children.Add(_closeBtn);

        _scanBtn = MakeButton("扫描设备", ScanDevices, false, PackIconKind.SearchWeb);
        connRow.Children.Add(_scanBtn);
        _readBtn = MakeButton("读取当前设备", ReadCurrentDevice, false, PackIconKind.CellphoneInformation);
        connRow.Children.Add(_readBtn);
        _pollBtn = MakeButton("开始轮询", TogglePoll, false, PackIconKind.PlayCircle);
        connRow.Children.Add(_pollBtn);
        _resetBtn = MakeButton("复位(南瑞怡和)", ResetDevice, false, PackIconKind.Restart);
        _resetBtn.Visibility = Visibility.Collapsed;
        connRow.Children.Add(_resetBtn);

        _statusText.FontSize = 13;
        _statusText.Margin = new Thickness(12, 0, 0, 0);
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        connRow.Children.Add(_statusText);

        topPanel.Children.Add(connRow);
        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // ===== 设备列表区（固定高度）=====
        var listPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var listLabel = new TextBlock { Text = "设备列表", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(listLabel, Dock.Top);
        listPanel.Children.Add(listLabel);

        // DataGrid 设备列表：列宽按内容自适应（表头与内容均完整显示），列顺序锁定
        _deviceListView = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            FontSize = 13,
            FontFamily = new FontFamily("Microsoft YaHei"),
            RowHeight = 32,
            ColumnHeaderHeight = 44, // 低于此值 MaterialDesign 表头文字会被裁切
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            ItemsSource = _deviceItems
        };
        _deviceListView.Columns.Add(new DataGridTextColumn { Header = "地址", Binding = new Binding("Address"), Width = DataGridLength.Auto, CanUserResize = false });
        _deviceListView.Columns.Add(new DataGridTextColumn { Header = "序列号", Binding = new Binding("SerialNumber"), Width = DataGridLength.Auto, CanUserResize = false });
        _deviceListView.Columns.Add(new DataGridTextColumn { Header = "报警级别", Binding = new Binding("AlarmName"), Width = DataGridLength.Auto, CanUserResize = false });
        _deviceListView.Columns.Add(new DataGridTextColumn { Header = "故障", Binding = new Binding("FaultText"), Width = DataGridLength.Auto, CanUserResize = false });
        _deviceListView.Columns.Add(new DataGridTextColumn { Header = "离子值", Binding = new Binding("IonValue"), Width = DataGridLength.Auto, CanUserResize = false });
        _deviceListView.Columns.Add(new DataGridTextColumn { Header = "在线", Binding = new Binding("OnlineMark"), Width = DataGridLength.Auto, CanUserResize = false });
        _deviceListView.Columns.Add(new DataGridTextColumn { Header = "更新", Binding = new Binding("UpdateText"), Width = DataGridLength.Auto, CanUserResize = false });
        _deviceListView.SelectionChanged += OnDeviceSelected;
        listPanel.Children.Add(_deviceListView);

        // ===== 解析详情区（固定高度，内部滚动）=====
        var detailPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var detailLabel = new TextBlock { Text = "解析结果（当前选中设备详情）", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(detailLabel, Dock.Top);
        detailPanel.Children.Add(detailLabel);

        _detailText = new TextBlock
        {
            Text = "（未选中设备）",
            FontSize = 12,
            FontFamily = new FontFamily("Microsoft YaHei"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Top
        };
        var detailBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = _detailText, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        detailPanel.Children.Add(detailBorder);

        // ===== 原始报文区（固定高度）=====
        var rawPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
        var rawLabel = new TextBlock { Text = "原始报文", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(rawLabel, Dock.Top);
        rawPanel.Children.Add(rawLabel);

        _rawLogBox.AcceptsReturn = true;
        _rawLogBox.IsReadOnly = true;
        _rawLogBox.TextWrapping = TextWrapping.NoWrap;
        _rawLogBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _rawLogBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _rawLogBox.FontFamily = new FontFamily("Consolas");
        _rawLogBox.FontSize = 12;
        _rawLogBox.VerticalContentAlignment = VerticalAlignment.Top;
        _rawLogBox.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        _rawLogBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
        var rawStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (rawStyle != null) _rawLogBox.Style = rawStyle;
        rawPanel.Children.Add(_rawLogBox);

        // ===== 三行固定高度布局（设备列表 180 / 详情 180 / 原始报文 150）=====
        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        Grid.SetRow(listPanel, 0);
        Grid.SetRow(detailPanel, 1);
        Grid.SetRow(rawPanel, 2);
        contentGrid.Children.Add(listPanel);
        contentGrid.Children.Add(detailPanel);
        contentGrid.Children.Add(rawPanel);
        root.Children.Add(contentGrid);

        Content = root;

        // 定时器（仅构建一次，按需启停）
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DefaultPollIntervalMs) };
        _pollTimer.Tick += OnPollTick;
        _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timeoutTimer.Tick += OnTimeoutTick;

        UpdateButtons();
    }

    // ================== 串口生命周期 ==================

    private void RefreshPorts()
    {
        var currentSelection = _portCombo.SelectedItem?.ToString();
        _portCombo.Items.Clear();
        var ports = SerialPort.GetPortNames();
        foreach (var port in ports) _portCombo.Items.Add(port);
        if (_portCombo.Items.Count > 0)
        {
            if (currentSelection != null && _portCombo.Items.Contains(currentSelection))
                _portCombo.SelectedItem = currentSelection;
            else
                _portCombo.SelectedIndex = 0;
        }
    }

    private void OpenPort()
    {
        try
        {
            var portName = _portCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(portName)) { SetStatus("请选择串口号", false); return; }

            _serialPort = new SerialPort
            {
                PortName = portName,
                BaudRate = int.Parse(_baudCombo.SelectedItem?.ToString() ?? "9600"),
                DataBits = 8,
                StopBits = StopBits.One,
                Parity = Parity.None,
                ReadTimeout = 500,
                WriteTimeout = 500,
                Encoding = Encoding.ASCII
            };
            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();
            _rxBuffer.Clear();
            _timeoutTimer?.Start();
            UpdateButtons();
            SetStatus($"已连接 {portName} @ {_serialPort.BaudRate}bps 8N1", true);
        }
        catch (Exception ex)
        {
            SetStatus($"打开失败: {ex.Message}", false);
            _serialPort?.Dispose();
            _serialPort = null;
        }
    }

    private void ClosePort()
    {
        StopPolling();
        _timeoutTimer?.Stop();
        _awaitingReply = false;
        _pendingCmd = PendingCmd.None;
        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
            try { _serialPort.Close(); } catch { }
            _serialPort.Dispose();
            _serialPort = null;
        }
        UpdateButtons();
        SetStatus("已断开", false);
    }

    /// <summary>供 MainViewModel.DisposeAllViews 调用</summary>
    public void SafeDisconnect()
    {
        try { _pollTimer?.Stop(); _timeoutTimer?.Stop(); } catch { }
        _polling = false;
        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
            try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
            _serialPort.Dispose();
            _serialPort = null;
        }
    }

    private void UpdateButtons()
    {
        var portOpen = _serialPort != null && _serialPort.IsOpen;
        _openBtn.IsEnabled = !portOpen;
        _closeBtn.IsEnabled = portOpen;
        _portCombo.IsEnabled = !portOpen;
        _baudCombo.IsEnabled = !portOpen;
        _protocolCombo.IsEnabled = !portOpen;
        _scanBtn.IsEnabled = portOpen;
        _readBtn.IsEnabled = portOpen;
        _pollBtn.IsEnabled = portOpen;
        _resetBtn.Visibility = _protocol == ModbusProtocolKind.NanRuiYiHe ? Visibility.Visible : Visibility.Collapsed;
        _resetBtn.IsEnabled = portOpen;
    }

    // ================== 操作入口 ==================

    /// <summary>扫描站端下辖设备清单（南瑞怡和版追加通讯状态）</summary>
    private void ScanDevices()
    {
        if (_serialPort == null || !_serialPort.IsOpen) { SetStatus("请先打开串口", false); return; }
        if (_awaitingReply) { SetStatus("等待上一条响应中...", false); return; }
        SetStatus("正在扫描下辖设备...", true);
        AppendRaw("―― 扫描设备 ――");
        SendFrame(ModbusParser.BuildFrame(ModbusProtocols.BuildDeviceListRead()), 0, PendingCmd.ScanDeviceList);
    }

    /// <summary>手动读取设备地址框指定的设备</summary>
    private void ReadCurrentDevice()
    {
        if (_serialPort == null || !_serialPort.IsOpen) { SetStatus("请先打开串口", false); return; }
        if (_awaitingReply) { SetStatus("等待上一条响应中...", false); return; }
        if (!byte.TryParse(_devAddrBox.Text.Trim(), out var addr) || addr < 1)
        {
            SetStatus("设备地址无效（1-255）", false);
            return;
        }
        if (!_devices.TryGetValue(addr, out var dev))
        {
            dev = new DeviceData { Address = addr };
            _devices[addr] = dev;
        }
        BeginDeviceRead(addr, selectDevice: true);
    }

    /// <summary>按协议组帧并发送单设备读取请求（申弘单帧；南瑞怡和先 0x04 后 0x03）</summary>
    private void BeginDeviceRead(byte addr, bool selectDevice)
    {
        if (selectDevice)
        {
            _currentDeviceAddr = addr;
            _deviceListView.SelectedItem = _deviceItems.FirstOrDefault(d => d.Address == addr);
            UpdateDetail();
        }
        if (_protocol == ModbusProtocolKind.ShenHong)
        {
            SendFrame(ModbusParser.BuildFrame(ModbusProtocols.BuildShenHongReadAll(addr)), addr, PendingCmd.ShenHongReadAll);
        }
        else
        {
            SendFrame(ModbusParser.BuildFrame(ModbusProtocols.BuildNrReadInput(addr)), addr, PendingCmd.NrReadInput);
        }
    }

    /// <summary>南瑞怡和版复位（0x05 线圈 0x07，二次确认）</summary>
    private void ResetDevice()
    {
        if (_serialPort == null || !_serialPort.IsOpen) { SetStatus("请先打开串口", false); return; }
        if (_protocol != ModbusProtocolKind.NanRuiYiHe) return;
        if (_awaitingReply) { SetStatus("等待上一条响应中...", false); return; }
        if (!byte.TryParse(_devAddrBox.Text.Trim(), out var addr) || addr < 1)
        {
            SetStatus("设备地址无效（1-255）", false);
            return;
        }
        if (MessageBox.Show($"确认向设备 {addr} 发送复位命令？\n\n这将复位设备报警状态。",
                "确认复位", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        AppendRaw($"―― 复位设备 {addr} ――");
        SendFrame(ModbusParser.BuildFrame(ModbusProtocols.BuildNrReset(addr)), addr, PendingCmd.NrReset);
    }

    // ================== 轮询 ==================

    private void TogglePoll()
    {
        if (_polling) StopPolling();
        else StartPolling();
    }

    private void StartPolling()
    {
        if (_serialPort == null || !_serialPort.IsOpen) { SetStatus("请先打开串口", false); return; }
        if (_deviceQueue.Count == 0) { SetStatus("请先扫描设备", false); return; }
        _polling = true;
        _pollIndex = 0;
        _pollTimer?.Start();
        SetPollBtnText("停止轮询", PackIconKind.Stop);
        AppendRaw("―― 开始轮询 ――");
        SetStatus($"轮询已启动: {_deviceQueue.Count} 台设备", true);
    }

    private void StopPolling()
    {
        if (!_polling) return;
        _polling = false;
        _pollTimer?.Stop();
        SetPollBtnText("开始轮询", PackIconKind.PlayCircle);
        AppendRaw("―― 停止轮询 ――");
        SetStatus("轮询已停止", true);
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_serialPort == null || !_serialPort.IsOpen) return;
        if (_awaitingReply) return; // 由超时定时器负责推进
        if (_deviceQueue.Count == 0) { SetStatus("请先扫描设备", false); return; }

        var addr = _deviceQueue[_pollIndex];
        _pollIndex = (_pollIndex + 1) % _deviceQueue.Count;
        BeginDeviceRead(addr, selectDevice: false);
    }

    // ================== 发送与超时 ==================

    private void SendFrame(byte[] frame, byte addr, PendingCmd cmd)
    {
        try
        {
            _serialPort!.Write(frame, 0, frame.Length);
            _pendingTx = frame;
            _pendingCmd = cmd;
            _pendingAddr = addr;
            _retryCount = 0;
            _awaitingReply = true;
            _lastTxTime = DateTime.Now;
            AppendRaw($"发→ {ModbusParser.ToHexString(frame)}");
            FileLogger.Write("Modbus", $"[TX] {ModbusParser.ToHexString(frame)}");
        }
        catch (Exception ex)
        {
            _awaitingReply = false;
            _pendingCmd = PendingCmd.None;
            SetStatus($"发送失败: {ex.Message}", false);
        }
    }

    /// <summary>超时检查：500ms 无响应则重发，重试 2 次后标记设备离线</summary>
    private void OnTimeoutTick(object? sender, EventArgs e)
    {
        if (!_awaitingReply) return;
        if ((DateTime.Now - _lastTxTime).TotalMilliseconds < TimeoutMs) return;

        if (_retryCount < MaxRetries)
        {
            _retryCount++;
            AppendRaw($"  ⏱ 超时无响应，第 {_retryCount} 次重发");
            try
            {
                _serialPort!.Write(_pendingTx, 0, _pendingTx.Length);
                _lastTxTime = DateTime.Now;
            }
            catch { }
            return;
        }

        var cmd = _pendingCmd;
        var addr = _pendingAddr;
        _pendingCmd = PendingCmd.None;
        _awaitingReply = false;

        if (cmd == PendingCmd.ScanDeviceList || cmd == PendingCmd.ScanCommStatus)
        {
            AppendRaw("  ⚠️ 站端无响应，扫描失败");
            SetStatus("扫描超时: 站端无响应", false);
            return;
        }

        if (_devices.TryGetValue(addr, out var dev))
        {
            dev.Online = false;
            dev.LastUpdate = DateTime.Now;
            AppendRaw($"  ⚠️ 设备 {addr} 重试 {MaxRetries} 次无响应，标记离线");
            RefreshDeviceList();
            if (_currentDeviceAddr == addr) UpdateDetail();
            SetStatus(_polling ? $"轮询中: 设备 {addr} 离线" : $"设备 {addr} 无响应", false);
        }
    }

    // ================== 接收与解析 ==================

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort == null || !_serialPort.IsOpen) return;
        try
        {
            var bytesToRead = _serialPort.BytesToRead;
            if (bytesToRead <= 0) return;
            var buffer = new byte[bytesToRead];
            var bytesRead = _serialPort.Read(buffer, 0, bytesToRead);
            lock (_rxLock)
            {
                for (int i = 0; i < bytesRead; i++) _rxBuffer.Add(buffer[i]);
            }
            Dispatcher.BeginInvoke(ProcessRxBuffer);
        }
        catch { }
    }

    /// <summary>从接收缓冲循环提取完整帧并处理（UI 线程）</summary>
    private void ProcessRxBuffer()
    {
        while (true)
        {
            byte[]? frame;
            lock (_rxLock)
            {
                if (_rxBuffer.Count == 0) return;
                var arr = _rxBuffer.ToArray();
                if (!ModbusParser.TryExtractFrame(arr, arr.Length, out frame, out var consumed))
                {
                    // 数据不足等待更多字节；缓冲异常膨胀时丢弃首字节防卡死
                    if (_rxBuffer.Count > 300) _rxBuffer.RemoveAt(0);
                    return;
                }
                _rxBuffer.RemoveRange(0, consumed);
            }
            HandleFrame(frame!);
        }
    }

    private void HandleFrame(byte[] frame)
    {
        AppendRaw($"收← {ModbusParser.ToHexString(frame)}");
        FileLogger.Write("Modbus", $"[RX] {ModbusParser.ToHexString(frame)}");

        if (!ModbusParser.VerifyCrc(frame))
        {
            AppendRaw("  ⚠️ [CRC错误]，等待超时重发");
            return;
        }

        var addr = frame[0];
        var fc = frame[1];

        // 异常帧（功能码高位 0x80）
        if ((fc & 0x80) != 0)
        {
            var excCode = frame[2];
            AppendRaw($"  异常响应: 地址{addr} 功能码0x{fc:X2} 异常码0x{excCode:X2}");
            if (_pendingCmd != PendingCmd.None && _pendingAddr == addr)
            {
                _pendingCmd = PendingCmd.None;
                _awaitingReply = false;
                SetStatus($"设备 {addr} 返回异常码 0x{excCode:X2}", false);
            }
            return;
        }

        switch (_pendingCmd)
        {
            case PendingCmd.ScanDeviceList when fc == 0x03:
                HandleDeviceListResponse(frame);
                break;
            case PendingCmd.ScanCommStatus when fc == 0x03:
                HandleCommStatusResponse(frame);
                break;
            case PendingCmd.ShenHongReadAll when fc == 0x03:
                HandleShenHongResponse(frame);
                break;
            case PendingCmd.NrReadInput when fc == 0x04:
                HandleNrInputResponse(frame);
                break;
            case PendingCmd.NrReadHolding when fc == 0x03:
                HandleNrHoldingResponse(frame);
                break;
            case PendingCmd.NrReset when fc == 0x05:
                HandleNrResetResponse(frame);
                break;
            default:
                // 功能码与等待请求不符，忽略等待超时
                break;
        }
    }

    private void HandleDeviceListResponse(byte[] frame)
    {
        var data = frame.AsSpan(3, frame[2]);
        var list = ModbusProtocols.ParseDeviceList(data);

        _devices.Clear();
        _deviceQueue.Clear();
        foreach (var addr in list)
        {
            if (_deviceQueue.Count >= 16) break;
            _deviceQueue.Add(addr);
            _devices[addr] = new DeviceData { Address = addr };
        }
        RefreshDeviceList();
        AppendRaw($"  扫描到 {_deviceQueue.Count} 台下辖设备: [{string.Join(", ", _deviceQueue)}]");

        // 保持选中设备有效
        if (_currentDeviceAddr is byte cur && !_deviceQueue.Contains(cur))
            _currentDeviceAddr = null;
        if (_currentDeviceAddr == null && _deviceQueue.Count > 0)
        {
            _currentDeviceAddr = _deviceQueue[0];
            _deviceListView.SelectedItem = _deviceItems.FirstOrDefault(d => d.Address == _currentDeviceAddr);
        }
        UpdateDetail();

        if (_protocol == ModbusProtocolKind.NanRuiYiHe && _deviceQueue.Count > 0)
        {
            // 南瑞怡和版追加读取各设备通讯状态
            SendFrame(ModbusParser.BuildFrame(ModbusProtocols.BuildNrCommStatusRead()), 0, PendingCmd.ScanCommStatus);
        }
        else
        {
            _pendingCmd = PendingCmd.None;
            _awaitingReply = false;
            SetStatus($"扫描完成: {_deviceQueue.Count} 台设备", true);
        }
    }

    private void HandleCommStatusResponse(byte[] frame)
    {
        var data = frame.AsSpan(3, frame[2]);
        for (int i = 0; i < _deviceQueue.Count && i < data.Length; i++)
        {
            var addr = _deviceQueue[i];
            if (_devices.TryGetValue(addr, out var dev))
                dev.CommStatus = (ushort)(data[i] == 0x01 ? 1 : 0);
        }
        _pendingCmd = PendingCmd.None;
        _awaitingReply = false;
        RefreshDeviceList();
        UpdateDetail();
        SetStatus($"扫描完成: {_deviceQueue.Count} 台设备", true);
    }

    private void HandleShenHongResponse(byte[] frame)
    {
        var addr = frame[0];
        var dev = EnsureDevice(addr);
        ModbusParser.ParseShenHongAll(frame, dev);
        CompleteDeviceRead(addr);
    }

    private void HandleNrInputResponse(byte[] frame)
    {
        var addr = frame[0];
        var dev = EnsureDevice(addr);
        ModbusParser.ParseNrInput(frame, dev);
        // 南瑞怡和两帧序列：0x04 实时完成后继续读 0x03 参数
        SendFrame(ModbusParser.BuildFrame(ModbusProtocols.BuildNrReadHolding(addr)), addr, PendingCmd.NrReadHolding);
    }

    private void HandleNrHoldingResponse(byte[] frame)
    {
        var addr = frame[0];
        var dev = EnsureDevice(addr);
        ModbusParser.ParseNrHolding(frame, dev);
        CompleteDeviceRead(addr);
    }

    private void HandleNrResetResponse(byte[] frame)
    {
        var addr = frame[0];
        AppendRaw($"  复位命令回显: {ModbusParser.ToHexString(frame.AsSpan(0, 6))}，设备 {addr} 复位成功");
        _pendingCmd = PendingCmd.None;
        _awaitingReply = false;
        SetStatus($"设备 {addr} 复位成功", true);
    }

    private DeviceData EnsureDevice(byte addr)
    {
        if (!_devices.TryGetValue(addr, out var dev))
        {
            dev = new DeviceData { Address = addr };
            _devices[addr] = dev;
        }
        return dev;
    }

    private void CompleteDeviceRead(byte addr)
    {
        _pendingCmd = PendingCmd.None;
        _awaitingReply = false;
        RefreshDeviceList();
        if (_currentDeviceAddr == addr) UpdateDetail();
        SetStatus(_polling ? $"轮询中: 设备 {addr} 更新完成" : $"设备 {addr} 读取完成", true);
    }

    // ================== UI 刷新辅助 ==================

    private void RefreshDeviceList()
    {
        _deviceItems.Clear();
        foreach (var addr in _deviceQueue)
            if (_devices.TryGetValue(addr, out var dev))
                _deviceItems.Add(dev);
        AutoSizeModbusColumns();
        // 保持选中设备
        _deviceListView.SelectedItem = _deviceItems.FirstOrDefault(d => d.Address == _currentDeviceAddr);
    }

    /// <summary>按表头与全部数据行内容测量列宽（表头与内容均完整显示，超出区域时水平滚动）</summary>
    private void AutoSizeModbusColumns()
    {
        if (_deviceListView.Columns.Count == 0) return;
        var dpi = VisualTreeHelper.GetDpi(_deviceListView).PixelsPerDip;
        var typeface = new Typeface("Microsoft YaHei");

        double MeasureText(string text)
        {
            var ft = new FormattedText(text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 13, Brushes.Black, dpi);
            return ft.Width;
        }

        var selectors = new Func<DeviceData, string>[]
        {
            d => d.Address.ToString(),
            d => d.SerialNumber,
            d => d.AlarmName,
            d => d.FaultText,
            d => d.IonValue.ToString(),
            d => d.OnlineMark,
            d => d.UpdateText
        };

        for (int c = 0; c < _deviceListView.Columns.Count && c < selectors.Length; c++)
        {
            double maxW = MeasureText(_deviceListView.Columns[c].Header?.ToString() ?? "") + 24;
            var sel = selectors[c];
            foreach (var item in _deviceItems)
            {
                var w = MeasureText(sel(item)) + 24;
                if (w > maxW) maxW = w;
            }
            _deviceListView.Columns[c].Width = new DataGridLength(maxW, DataGridLengthUnitType.Pixel);
        }
    }

    private void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_deviceListView.SelectedItem is DeviceData dev)
        {
            _currentDeviceAddr = dev.Address;
            UpdateDetail();
        }
    }

    private void UpdateDetail()
    {
        if (_currentDeviceAddr is not byte addr || !_devices.TryGetValue(addr, out var dev))
        {
            _detailText.Text = "（未选中设备）";
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"设备地址: {dev.Address}");
        sb.AppendLine($"序列号: {(string.IsNullOrEmpty(dev.SerialNumber) ? "-" : dev.SerialNumber)}");
        sb.AppendLine($"报警级别: {dev.AlarmName}");
        sb.AppendLine($"故障: {dev.FaultText}");
        sb.AppendLine($"热释离子实时值: {dev.IonValue}");
        sb.AppendLine($"工作状态: {dev.WorkStatus}    通讯状态: {dev.CommStatus}    在线: {(dev.Online ? "是" : "否")}    最后更新: {dev.UpdateText}");
        sb.AppendLine("―― 阈值 ――");
        var names = ModbusProtocols.Thresholds;
        for (int i = 0; i < dev.Thresholds.Length && i < names.Length; i++)
            sb.AppendLine($"{names[i].Name}: {dev.Thresholds[i]}");
        _detailText.Text = sb.ToString();
    }

    private void AppendRaw(string text)
    {
        _rawLogBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}\n");
        _rawLogBox.ScrollToEnd();
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    private void SetPollBtnText(string text, PackIconKind icon)
    {
        if (_pollBtn.Content is StackPanel sp && sp.Children.Count >= 2)
        {
            if (sp.Children[0] is PackIcon ic) ic.Kind = icon;
            if (sp.Children[1] is TextBlock tb) tb.Text = text;
        }
    }
}
