using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using ToolHelper.Services;

namespace ToolHelper.Views.Serial;

public class SerialDebugView : UserControl
{
    private SerialPort? _serialPort;
    private DispatcherTimer? _autoSendTimer;
    private readonly StringBuilder _receiveBuffer = new();

    // UI 控件
    private ComboBox _portCombo = new();
    private ComboBox _baudCombo = new();
    private ComboBox _dataBitsCombo = new();
    private ComboBox _stopBitsCombo = new();
    private ComboBox _parityCombo = new();
    private Button _openBtn = new();
    private Button _closeBtn = new();
    private TextBlock _statusText = new();

    private TextBox _sendBox = new();
    private CheckBox _hexSendCb = new();
    private CheckBox _autoSendCb = new();
    private TextBox _autoSendIntervalBox = new();
    private CheckBox _sendNewlineCb = new();

    private CheckBox _hexDisplayCb = new();
    private CheckBox _showTimestampCb = new();
    private CheckBox _autoScrollCb = new();
    private ComboBox _checksumTypeCombo = new();
    private TextBox _checksumStartBox = new();
    private TextBlock _checksumValueText = new();
    private TextBox _msgLogBox = new();

    private bool _built;

    public SerialDebugView()
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

    // ========== UI 构建 ==========

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
            MinWidth = 80,
            BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        // 禁用时保持边框可见
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
            Text = "  串口调试工具", FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        topPanel.Children.Add(titleRow);
        topPanel.Children.Add(new TextBlock
        {
            Text = "支持串口通信调试，可配置波特率、数据位、停止位、校验位，支持文本/十六进制收发。",
            FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // ===== 串口配置行 =====
        var configRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        configRow.Children.Add(MakeLabel("串口号:"));
        _portCombo = new ComboBox { FontSize = 13, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)), BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center };
        var portStyle = new Style(typeof(ComboBox), _portCombo.Style);
        portStyle.Triggers.Add(new Trigger { Property = IsEnabledProperty, Value = false, Setters = { new Setter(OpacityProperty, 1.0) } });
        _portCombo.Style = portStyle;
        configRow.Children.Add(_portCombo);
        configRow.Children.Add(MakeButton("刷新", RefreshPorts, false, PackIconKind.Refresh));

        configRow.Children.Add(MakeLabel("波特率:"));
        _baudCombo = MakeCombo(new[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" }, 3);
        configRow.Children.Add(_baudCombo);

        configRow.Children.Add(MakeLabel("数据位:"));
        _dataBitsCombo = MakeCombo(new[] { "5", "6", "7", "8" }, 3);
        configRow.Children.Add(_dataBitsCombo);

        configRow.Children.Add(MakeLabel("停止位:"));
        _stopBitsCombo = MakeCombo(new[] { "1", "1.5", "2" }, 0);
        configRow.Children.Add(_stopBitsCombo);

        configRow.Children.Add(MakeLabel("校验位:"));
        _parityCombo = MakeCombo(new[] { "无", "奇校验", "偶校验", "标记", "空格" }, 0);
        configRow.Children.Add(_parityCombo);

        topPanel.Children.Add(configRow);

        // ===== 连接按钮行 =====
        var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _openBtn = MakeButton("打开串口", OpenPort, true, PackIconKind.PowerPlug);
        _closeBtn = MakeButton("关闭串口", ClosePort, false, PackIconKind.PowerPlugOff);
        _closeBtn.IsEnabled = false;
        connRow.Children.Add(_openBtn);
        connRow.Children.Add(_closeBtn);

        _statusText.FontSize = 13;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        connRow.Children.Add(_statusText);

        topPanel.Children.Add(connRow);
        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // ===== 消息日志区 (收发统一显示) — 先创建，后添加，保证最后填充剩余空间 =====
        var logPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var logHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        logHeader.Children.Add(MakeLabel("接收区"));
        _hexDisplayCb = new CheckBox { Content = "HEX显示", FontSize = 12, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = true };
        logHeader.Children.Add(_hexDisplayCb);
        _showTimestampCb = new CheckBox { Content = "显示时间戳", FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = true };
        logHeader.Children.Add(_showTimestampCb);
        _autoScrollCb = new CheckBox { Content = "自动滚动", FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = true };
        logHeader.Children.Add(_autoScrollCb);
        logHeader.Children.Add(MakeButton("清空日志", () => _msgLogBox.Clear(), false, PackIconKind.Eraser));
        logHeader.Children.Add(MakeButton("清空发送", () => _sendBox.Clear(), false, PackIconKind.Eraser));
        DockPanel.SetDock(logHeader, Dock.Top);
        logPanel.Children.Add(logHeader);

        _msgLogBox.IsReadOnly = true;
        _msgLogBox.AcceptsReturn = true;
        _msgLogBox.TextWrapping = TextWrapping.NoWrap;
        _msgLogBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _msgLogBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _msgLogBox.FontFamily = new FontFamily("Consolas");
        _msgLogBox.FontSize = 13;
        _msgLogBox.VerticalContentAlignment = VerticalAlignment.Top;
        _msgLogBox.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        _msgLogBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
        var logStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (logStyle != null) _msgLogBox.Style = logStyle;
        logPanel.Children.Add(_msgLogBox);

        // ===== 中间控制栏 (发送模式 + 校验 + 自动发送) =====
        var middleBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        middleBar.Children.Add(MakeLabel("发送区"));
        _hexSendCb = new CheckBox { Content = "HEX发送", FontSize = 12, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = true };
        _hexSendCb.Checked += (s, e) => UpdateChecksum();
        _hexSendCb.Unchecked += (s, e) => UpdateChecksum();
        middleBar.Children.Add(_hexSendCb);
        _sendNewlineCb = new CheckBox { Content = "加回车换行", FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = true };
        middleBar.Children.Add(_sendNewlineCb);
        _autoSendCb = new CheckBox { Content = "定时发送", FontSize = 12, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        _autoSendCb.Checked += (s, e) => StartAutoSend();
        _autoSendCb.Unchecked += (s, e) => StopAutoSend();
        middleBar.Children.Add(_autoSendCb);
        middleBar.Children.Add(MakeLabel("间隔:"));
        _autoSendIntervalBox = new TextBox { Text = "1000", FontSize = 13, Width = 60, Margin = new Thickness(0, 0, 4, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)), BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center };
        middleBar.Children.Add(_autoSendIntervalBox);
        middleBar.Children.Add(MakeLabel("ms"));

        // 校验配置 (带边框)
        var csBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var csInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        csInner.Children.Add(MakeLabel("加校验:"));
        _checksumTypeCombo = MakeCombo(new[] { "None", "ModbusCRC16", "CRC16-CCITT", "CRC32", "LRC", "XOR" }, 1);
        _checksumTypeCombo.SelectionChanged += (s, e) => UpdateChecksum();
        csInner.Children.Add(_checksumTypeCombo);
        csInner.Children.Add(MakeLabel("第"));
        _checksumStartBox = new TextBox { Text = "1", FontSize = 13, Width = 36, Margin = new Thickness(0, 0, 4, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)), BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        _checksumStartBox.TextChanged += (s, e) => UpdateChecksum();
        csInner.Children.Add(_checksumStartBox);
        csInner.Children.Add(MakeLabel("字节 至 末尾"));
        csBorder.Child = csInner;
        middleBar.Children.Add(csBorder);

        // 校验值显示 (蓝色边框)
        var cvBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3), MinWidth = 80,
            VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(40, 40, 50))
        };
        _checksumValueText = new TextBlock { Text = "--", FontSize = 13, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255)), VerticalAlignment = VerticalAlignment.Center };
        cvBorder.Child = _checksumValueText;
        middleBar.Children.Add(cvBorder);


        // ===== 发送输入 + 按钮 (底部，固定两行高度) =====
        var sendInputPanel = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var sendBtn = MakeButton("发送", SendData, true, PackIconKind.Send);
        sendBtn.MinWidth = 80;
        sendBtn.VerticalAlignment = VerticalAlignment.Stretch;
        DockPanel.SetDock(sendBtn, Dock.Right);
        sendInputPanel.Children.Add(sendBtn);

        _sendBox.AcceptsReturn = true;
        _sendBox.TextWrapping = TextWrapping.Wrap;
        _sendBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _sendBox.FontFamily = new FontFamily("Consolas");
        _sendBox.FontSize = 13;
        _sendBox.Height = 88;
        _sendBox.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        _sendBox.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        _sendBox.CaretBrush = Brushes.White;
        _sendBox.TextChanged += (s, e) => UpdateChecksum();
        _sendBox.KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            { SendData(); e.Handled = true; }
        };
        var sendStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (sendStyle != null) _sendBox.Style = sendStyle;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_sendBox, "输入要发送的数据，Ctrl+Enter 快速发送");
        sendInputPanel.Children.Add(_sendBox);

        // ===== 使用 Grid 布局精确控制各区域位置 =====
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(420) }); // 0: 接收区（固定420px）
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 1: 中间控制栏
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 2: 发送输入框

        Grid.SetRow(logPanel, 0);        // 接收区（填满剩余，在上）
        Grid.SetRow(middleBar, 1);         // 中间控制栏（在中间）
        Grid.SetRow(sendInputPanel, 2);    // 发送输入框（88px，在下）
        mainGrid.Children.Add(logPanel);
        mainGrid.Children.Add(middleBar);
        mainGrid.Children.Add(sendInputPanel);

        root.Children.Add(mainGrid);
        Content = root;
    }

    // ========== 串口操作 ==========

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

    private Parity GetParity() => _parityCombo.SelectedIndex switch
    {
        1 => Parity.Odd,
        2 => Parity.Even,
        3 => Parity.Mark,
        4 => Parity.Space,
        _ => Parity.None
    };

    private StopBits GetStopBits() => _stopBitsCombo.SelectedIndex switch
    {
        1 => StopBits.OnePointFive,
        2 => StopBits.Two,
        _ => StopBits.One
    };

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
                DataBits = int.Parse(_dataBitsCombo.SelectedItem?.ToString() ?? "8"),
                StopBits = GetStopBits(),
                Parity = GetParity(),
                ReadTimeout = 500,
                WriteTimeout = 500,
                Encoding = Encoding.UTF8
            };

            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();

            _openBtn.IsEnabled = false;
            _closeBtn.IsEnabled = true;
            _portCombo.IsEnabled = false;
            _baudCombo.IsEnabled = false;
            _dataBitsCombo.IsEnabled = false;
            _stopBitsCombo.IsEnabled = false;
            _parityCombo.IsEnabled = false;

            SetStatus($"已连接 {portName} @ {_serialPort.BaudRate}bps", true);
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
        StopAutoSend();
        _autoSendCb.IsChecked = false;

        if (_serialPort != null)
        {
            var portName = _serialPort.PortName;
            _serialPort.DataReceived -= OnDataReceived;
            try { _serialPort.Close(); } catch { }
            _serialPort.Dispose();
            _serialPort = null;
        }

        _openBtn.IsEnabled = true;
        _closeBtn.IsEnabled = false;
        _portCombo.IsEnabled = true;
        _baudCombo.IsEnabled = true;
        _dataBitsCombo.IsEnabled = true;
        _stopBitsCombo.IsEnabled = true;
        _parityCombo.IsEnabled = true;

        SetStatus("已断开", false);
    }

    // ========== 数据收发 ==========

    private void SendData()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        { SetStatus("请先打开串口", false); return; }

        var text = _sendBox.Text;
        if (string.IsNullOrEmpty(text) && _sendNewlineCb.IsChecked != true) return;

        try
        {
            byte[] data;
            var isHex = _hexSendCb.IsChecked == true;
            if (isHex)
                data = HexToBytes(text);
            else
            {
                var toSend = _sendNewlineCb.IsChecked == true ? text + "\r\n" : text;
                data = Encoding.UTF8.GetBytes(toSend);
            }

            // 校验计算
            var csType = _checksumTypeCombo.SelectedItem?.ToString() ?? "None";
            byte[] checksumBytes = Array.Empty<byte>();
            if (csType != "None" && data.Length > 0)
            {
                int startIdx = 0;
                if (int.TryParse(_checksumStartBox.Text, out var sb) && sb >= 1)
                    startIdx = Math.Min(sb - 1, data.Length - 1);
                var range = new byte[data.Length - startIdx];
                Array.Copy(data, startIdx, range, 0, range.Length);
                checksumBytes = csType switch
                {
                    "ModbusCRC16" => CalcModbusCRC16(range),
                    "CRC16-CCITT" => CalcCRC16CCITT(range),
                    "CRC32" => CalcCRC32(range),
                    "LRC" => new[] { CalcLRC(range) },
                    "XOR" => new[] { CalcXOR(range) },
                    _ => Array.Empty<byte>()
                };
                if (checksumBytes.Length > 0)
                {
                    var csHex = BitConverter.ToString(checksumBytes).Replace("-", " ");
                    _checksumValueText.Text = csHex;
                    var full = new byte[data.Length + checksumBytes.Length];
                    Array.Copy(data, full, data.Length);
                    Array.Copy(checksumBytes, 0, full, data.Length, checksumBytes.Length);
                    data = full;
                }
            }

            var dataHex = BitConverter.ToString(data).Replace("-", " ");
            AppendMsg("发", dataHex, data.Length);

            _serialPort.Write(data, 0, data.Length);
            FileLogger.Write("Serial", $"[TX] {dataHex}");
        }
        catch (Exception ex)
        {
            SetStatus($"发送失败: {ex.Message}", false);
            try { _serialPort?.DiscardOutBuffer(); } catch { }
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort == null || !_serialPort.IsOpen) return;
        try
        {
            var bytesToRead = _serialPort.BytesToRead;
            if (bytesToRead <= 0) return;
            var buffer = new byte[bytesToRead];
            var bytesRead = _serialPort.Read(buffer, 0, bytesToRead);
            var dataHex = BitConverter.ToString(buffer, 0, bytesRead).Replace("-", " ");
            Dispatcher.BeginInvoke(() =>
            {
                var isHex = _hexDisplayCb.IsChecked == true;
                var display = isHex
                    ? dataHex
                    : Encoding.UTF8.GetString(buffer, 0, bytesRead);
                AppendMsg("收", display, bytesRead);
                FileLogger.Write("Serial", $"[RX] {display}");
            });
        }
        catch { }
    }

    // ========== 自动发送 ==========

    private void StartAutoSend()
    {
        if (!int.TryParse(_autoSendIntervalBox.Text.Trim(), out var interval) || interval < 10)
            interval = 1000;

        _autoSendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _autoSendTimer.Tick += (s, e) => SendData();
        _autoSendTimer.Start();
    }

    private void StopAutoSend()
    {
        _autoSendTimer?.Stop();
        _autoSendTimer = null;
    }

    // ========== 辅助方法 ==========

    private void AppendMsg(string direction, string content, int byteCount = 0)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var ts = _showTimestampCb.IsChecked == true ? $"[{DateTime.Now:HH:mm:ss.fff}] " : "";
            var lenInfo = byteCount > 0 ? $" ({byteCount}B)" : "";
            _msgLogBox.AppendText($"{ts}{direction} ▷ {content}{lenInfo}\n");
            if (_autoScrollCb.IsChecked == true)
                _msgLogBox.ScrollToEnd();
        });
    }

    private void UpdateChecksum()
    {
        try
        {
            var csType = _checksumTypeCombo.SelectedItem?.ToString() ?? "None";
            if (csType == "None" || string.IsNullOrWhiteSpace(_sendBox.Text))
            {
                _checksumValueText.Text = "--";
                return;
            }
            byte[] data;
            var isHex = _hexSendCb.IsChecked == true;
            if (isHex)
                data = HexToBytes(_sendBox.Text);
            else
                data = Encoding.UTF8.GetBytes(_sendBox.Text);
            if (data.Length == 0) { _checksumValueText.Text = "--"; return; }

            int startIdx = 0;
            if (int.TryParse(_checksumStartBox.Text, out var sb) && sb >= 1)
                startIdx = Math.Min(sb - 1, data.Length - 1);
            var range = new byte[data.Length - startIdx];
            Array.Copy(data, startIdx, range, 0, range.Length);

            byte[] result = csType switch
            {
                "ModbusCRC16" => CalcModbusCRC16(range),
                "CRC16-CCITT" => CalcCRC16CCITT(range),
                "CRC32" => CalcCRC32(range),
                "LRC" => new[] { CalcLRC(range) },
                "XOR" => new[] { CalcXOR(range) },
                _ => Array.Empty<byte>()
            };
            _checksumValueText.Text = result.Length > 0
                ? BitConverter.ToString(result).Replace("-", " ")
                : "--";
        }
        catch { _checksumValueText.Text = "ERR"; }
    }

    private void SetStatus(string msg, bool connected)
    {
        _statusText.Text = msg;
        _statusText.Foreground = connected ? Brushes.Green : Brushes.Red;
    }

    private static byte[] HexToBytes(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        if (hex.Length % 2 != 0) hex = "0" + hex;
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    // ========== 校验算法 ==========

    private static byte[] CalcModbusCRC16(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
    }

    private static byte[] CalcCRC16CCITT(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return new[] { (byte)(crc >> 8), (byte)(crc & 0xFF) };
    }

    private static byte[] CalcCRC32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        crc = ~crc;
        return BitConverter.GetBytes(crc);
    }

    private static byte CalcLRC(byte[] data)
    {
        byte lrc = 0;
        foreach (var b in data) lrc += b;
        return (byte)(-lrc);
    }

    private static byte CalcXOR(byte[] data)
    {
        byte xor = 0;
        foreach (var b in data) xor ^= b;
        return xor;
    }

    // ========== 公共方法（供 DisposeAllViews 调用） ==========

    public void SafeDisconnect()
    {
        StopAutoSend();
        if (_serialPort != null && _serialPort.IsOpen)
        {
            _serialPort.DataReceived -= OnDataReceived;
            try { _serialPort.Close(); } catch { }
            _serialPort.Dispose();
            _serialPort = null;
        }
    }
}
