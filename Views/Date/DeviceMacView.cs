using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Word = DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Win32;
using Newtonsoft.Json;
using OfficeOpenXml;
using PackIcon = MaterialDesignThemes.Wpf.PackIcon;
using PackIconKind = MaterialDesignThemes.Wpf.PackIconKind;

namespace ToolHelper.Views.Date;

public class DeviceMacView : UserControl
{
    // ===== Win32 API P/Invoke =====
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SendARP(uint destIp, uint srcIp, byte[] macAddr, ref int macLen);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MIB_IPNETROW
    {
        public int dwIndex;
        public int dwPhysAddrLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] bPhysAddr;
        public int dwAddr;
        public int dwType;
    }

    private static readonly int IpNetRowSize = 24; // 实际 4+4+8+4+4 = 24 字节

    private static uint IPToUInt(string ip)
    {
        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static readonly Dictionary<uint, string> ArpErrors = new()
    {
        [31]   = "网络适配器异常，请检查网卡驱动",
        [53]   = "目标 IP 不在本地子网内，无法通过 ARP 获取 MAC 地址",
        [87]   = "内部错误：IP 地址解析异常",
        [110]  = "网络连接未就绪，请检查网线或 WiFi 是否已连接",
        [1168] = "目标设备无响应 — 可能已关机、未联网或被防火墙拦截",
        [1231] = "无法到达目标网络，请检查路由配置"
    };

    // ===== 列定义 =====
    private static readonly List<(string Name, bool Required, Func<DeviceRecord, object?> Getter)> AllColumns = new()
    {
        ("序号",      true,  r => r.Id),
        ("站名",      false, r => r.StationName),
        ("IP地址",    false, r => r.IPAddress),
        ("设备名",    false, r => r.DeviceName),
        ("设备型号",  false, r => r.DeviceModel),
        ("设备序列号", false, r => r.SerialNumber),
        ("设备MAC地址", false, r => r.MacAddress),
        ("安装位置",  false, r => r.Location),
    };

    // ===== Data =====
    private ObservableCollection<DeviceRecord> _records = new();
    private DataGrid _grid = null!;
    private TextBlock _statusText = null!;
    private PackIcon _statusIcon = null!;
    private TextBlock _countText = null!;
    private Border _statusBarBorder = null!;
    private bool _saved;
    private bool _built;
    private bool _accessAvailable;
    private Button _driverActionBtn = null!;

    /// <summary>
    /// 从 BaseDirectory 向上查找项目根目录下的目标子目录
    /// </summary>
    private static string FindProjectDir(string subDir)
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, subDir);
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        // 兜底：创建在 BaseDirectory 下
        var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, subDir);
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static string DataDir => FindProjectDir("data");
    private static readonly string DbPath = Path.Combine(DataDir, "DeviceMac.accdb");
    private static readonly string JsonPath = Path.Combine(DataDir, "DeviceMac.json");
    private static string PluginsDir => FindProjectDir("plugins");
    private static readonly string[] AceDllPaths = {
        @"C:\Program Files\Common Files\microsoft shared\OFFICE16\ACEOLEDB.DLL",
        @"C:\Program Files (x86)\Common Files\microsoft shared\OFFICE16\ACEOLEDB.DLL",
        @"C:\Program Files\Common Files\microsoft shared\OFFICE15\ACEOLEDB.DLL",
        @"C:\Program Files (x86)\Common Files\microsoft shared\OFFICE15\ACEOLEDB.DLL",
    };

    public DeviceMacView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) { Loaded -= OnLoaded; return; }
        _built = true;
        BuildUI();
        DetectDriverAndLoadData();
        Loaded -= OnLoaded;
    }

    // ========== UI ==========

    private void BuildUI()
    {
        var root = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // ── Title ──
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Network, Width = 26, Height = 26, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = "  获取设备MAC地址", FontSize = 19, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(titleRow);

        root.Children.Add(new TextBlock
        {
            Text = "输入 IP 地址，通过 ARP 获取同子网设备 MAC 地址，支持 Access/JSON 存储与 docx/xlsx 导出",
            FontSize = 12, Opacity = 0.55, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap
        });

        // ── 标题分隔线 ──
        root.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        // ── Toolbar ──
        root.Children.Add(BuildToolbar());

        // ── DataGrid ──
        root.Children.Add(BuildGrid());

        // ── Status bar ──
        root.Children.Add(BuildStatusBar());

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0) };
    }

    private StackPanel BuildToolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var raised = TryFindResource("MaterialDesignRaisedButton") as System.Windows.Style;
        var outlined = TryFindResource("MaterialDesignOutlinedButton") as System.Windows.Style;
        var flat = TryFindResource("MaterialDesignFlatButton") as System.Windows.Style;

        // 数据操作组
        bar.Children.Add(MakeBtn("新增行", AddRow, raised, PackIconKind.Plus));
        bar.Children.Add(MakeBtn("保存", SaveData, outlined, PackIconKind.ContentSave));

        // 分隔符
        bar.Children.Add(new Border
        {
            Width = 1, Margin = new Thickness(12, 4, 12, 4),
            Background = new SolidColorBrush(Color.FromRgb(200, 200, 200))
        });

        // 导出组（带边框）
        bar.Children.Add(MakeBtn("导出 xlsx", ExportXlsx, outlined, PackIconKind.FileExcelOutline));
        bar.Children.Add(MakeBtn("导出 docx", ExportDocx, outlined, PackIconKind.FileWordOutline));

        // 分隔符
        bar.Children.Add(new Border
        {
            Width = 1, Margin = new Thickness(12, 4, 12, 4),
            Background = new SolidColorBrush(Color.FromRgb(200, 200, 200))
        });

        // 通信组
        bar.Children.Add(MakeBtn("全部通信", CommunicateAll, outlined, PackIconKind.LanConnect));
        return bar;
    }

    private Button MakeBtn(string text, Action handler, System.Windows.Style? style, PackIconKind icon)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = icon, Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button { Content = sp, Margin = new Thickness(0, 0, 6, 0), Style = style };
        btn.Click += (s, e) => handler();
        return btn;
    }

    private StackPanel BuildStatusBar()
    {
        var statusBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };

        _statusBarBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 14, 5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(1),
        };

        var inner = new StackPanel { Orientation = Orientation.Horizontal };

        _statusIcon = new PackIcon
        {
            Kind = PackIconKind.InformationOutline,
            Width = 16, Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
        };
        _statusText = new TextBlock
        {
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        _driverActionBtn = new Button
        {
            FontSize = 11, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2),
            Visibility = Visibility.Collapsed, Cursor = System.Windows.Input.Cursors.Hand,
            Style = TryFindResource("MaterialDesignOutlinedButton") as System.Windows.Style
        };
        _driverActionBtn.Click += OnDriverActionClick;
        _countText = new TextBlock
        {
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130))
        };

        inner.Children.Add(_statusIcon);
        inner.Children.Add(_statusText);
        inner.Children.Add(_driverActionBtn);
        inner.Children.Add(_countText);
        _statusBarBorder.Child = inner;
        statusBar.Children.Add(_statusBarBorder);
        return statusBar;
    }

    private Border BuildGrid()
    {
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            MinHeight = 350,
            Margin = new Thickness(0),
            ItemsSource = _records,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeaderWidth = 0,
            FontSize = 13,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            CanUserReorderColumns = false,
        };

        // 覆盖默认蓝色选中背景为灰色
        _grid.Resources.Add(SystemColors.HighlightBrushKey, new SolidColorBrush(Color.FromRgb(224, 224, 224)));
        _grid.Resources.Add(SystemColors.InactiveSelectionHighlightBrushKey, new SolidColorBrush(Color.FromRgb(224, 224, 224)));
    
        _grid.Columns.Add(new DataGridTextColumn { Header = "序号", Binding = new Binding("Id") { Mode = BindingMode.OneWay }, Width = 70, IsReadOnly = true });
        _grid.Columns.Add(new DataGridTextColumn { Header = "站名", Binding = new Binding("StationName") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 155 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "IP地址", Binding = new Binding("IPAddress") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 155 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "设备名", Binding = new Binding("DeviceName") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 155 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "设备型号", Binding = new Binding("DeviceModel") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 155 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "设备序列号", Binding = new Binding("SerialNumber") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 155 });
    
        // MAC 地址列（带颜色状态）
        var macCol = new DataGridTemplateColumn { Header = "设备MAC地址", Width = 155, CanUserSort = false };
        var macTemplate = new DataTemplate(typeof(DeviceRecord));
        var macFef = new FrameworkElementFactory(typeof(TextBlock));
        macFef.SetBinding(TextBlock.TextProperty, new Binding("MacAddress") { Mode = BindingMode.OneWay });
        macFef.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        macFef.SetValue(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0));
        macFef.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
        macFef.SetValue(TextBlock.FontSizeProperty, 12.0);
        macTemplate.VisualTree = macFef;
        macCol.CellTemplate = macTemplate;
        macCol.CellStyle = CreateMacCellStyle();
        _grid.Columns.Add(macCol);
    
        _grid.Columns.Add(new DataGridTextColumn { Header = "安装位置", Binding = new Binding("Location") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 120 });
    
        // 行内“通信”按钮列（MaterialDesign 样式）
        var commCol = new DataGridTemplateColumn { Header = "操作", Width = 96, CanUserSort = false };
        var template = new DataTemplate(typeof(DeviceRecord));
        var fef = new FrameworkElementFactory(typeof(Button));
        fef.SetValue(Button.MarginProperty, new Thickness(4, 2, 4, 2));
        fef.SetValue(Button.CursorProperty, System.Windows.Input.Cursors.Hand);
        fef.SetValue(Button.FontSizeProperty, 12.0);
        fef.SetValue(FrameworkElement.StyleProperty, TryFindResource("MaterialDesignOutlinedButton") as System.Windows.Style);
    
        // 按钮内容：图标 + 文字
        var btnSp = new FrameworkElementFactory(typeof(StackPanel));
        btnSp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var btnIcon = new FrameworkElementFactory(typeof(PackIcon));
        btnIcon.SetValue(PackIcon.KindProperty, PackIconKind.Ethernet);
        btnIcon.SetValue(FrameworkElement.WidthProperty, 14.0);
        btnIcon.SetValue(FrameworkElement.HeightProperty, 14.0);
        btnIcon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        btnIcon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 0));
        var btnTxt = new FrameworkElementFactory(typeof(TextBlock));
        btnTxt.SetValue(TextBlock.TextProperty, "通信");
        btnTxt.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        btnSp.AppendChild(btnIcon);
        btnSp.AppendChild(btnTxt);
        // 注意：ContentControl 模板必须用 AppendChild 设置内容，
        // SetValue(ContentProperty, factory) 会导致工厂对象的 ToString 被当作文本渲染
        fef.AppendChild(btnSp);
    
        fef.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnCommButtonClick));
        template.VisualTree = fef;
        commCol.CellTemplate = template;
        _grid.Columns.Add(commCol);
    
        // Context menu
        var ctx = new ContextMenu();
        ctx.Items.Add(MakeMenuItem("🔗  通信(获取MAC)", CommunicateSelected));
        ctx.Items.Add(new Separator());
        ctx.Items.Add(MakeMenuItem("📋  复制MAC地址", CopyMac));
        ctx.Items.Add(MakeMenuItem("📄  复制整行", CopyRow));
        ctx.Items.Add(new Separator());
        ctx.Items.Add(MakeMenuItem("🗑  删除此行", DeleteRow));
        _grid.ContextMenu = ctx;
    
        // 包裹在圆角 Border 中
        var gridBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4),
            ClipToBounds = true,
            Child = _grid
        };
        return gridBorder;
    }
    
    /// <summary>
    /// 根据 MAC 地址状态动态设置前景色
    /// </summary>
    private System.Windows.Style CreateMacCellStyle()
    {
        var style = new System.Windows.Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        // 默认绿色（成功状态）
        style.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new SolidColorBrush(Color.FromRgb(46, 125, 50))));
        style.Setters.Add(new Setter(DataGridCell.FontWeightProperty, FontWeights.Normal));
    
        // 通过 DataTrigger 实现颜色区分
        // 通信中...
        var triggerBusy = new DataTrigger
        {
            Binding = new Binding("MacAddress"),
            Value = "通信中..."
        };
        triggerBusy.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new SolidColorBrush(Color.FromRgb(25, 118, 210))));
        triggerBusy.Setters.Add(new Setter(DataGridCell.FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(triggerBusy);
    
        // 获取失败 / 超时 / 错误
        var triggerFail = new DataTrigger
        {
            Binding = new Binding("MacAddress"),
            Value = "获取失败"
        };
        triggerFail.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.Red));
        style.Triggers.Add(triggerFail);
    
        var triggerTimeout = new DataTrigger
        {
            Binding = new Binding("MacAddress"),
            Value = "超时"
        };
        triggerTimeout.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 152, 0))));
        style.Triggers.Add(triggerTimeout);
    
        var triggerError = new DataTrigger
        {
            Binding = new Binding("MacAddress"),
            Value = "错误"
        };
        triggerError.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.Red));
        style.Triggers.Add(triggerError);
    
        // 空字符串 → 灰色（未获取）
        var triggerEmpty = new DataTrigger { Binding = new Binding("MacAddress"), Value = "" };
        triggerEmpty.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new SolidColorBrush(Color.FromRgb(160, 160, 160))));
        style.Triggers.Add(triggerEmpty);

        return style;
    }

    private void OnCommButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DeviceRecord rec)
        {
            _ = DoCommunicate(rec);
        }
    }

    private MenuItem MakeMenuItem(string text, Action handler)
    {
        var item = new MenuItem { Header = text };
        item.Click += (s, e) => handler();
        return item;
    }

    // ========== 操作 ==========

    private void AddRow()
    {
        int nextId = _records.Count > 0 ? _records.Max(r => r.Id) + 1 : 1;
        _records.Add(new DeviceRecord { Id = nextId });
        _grid.SelectedIndex = _records.Count - 1;
        _saved = false;
        SetStatus($"已添加第 {nextId} 行", true);
        UpdateCount();
    }

    private DeviceRecord? SelectedRecord => _grid.SelectedItem as DeviceRecord;

    private async void CommunicateSelected()
    {
        var rec = SelectedRecord;
        if (rec == null) { SetStatus("请先选择一行", false); return; }
        await DoCommunicate(rec);
    }

    private async void CommunicateAll()
    {
        var withIp = _records.Where(r => !string.IsNullOrWhiteSpace(r.IPAddress)).ToList();
        if (withIp.Count == 0) { SetStatus("没有含 IP 地址的行", false); return; }

        SetStatus($"正在批量通信 {withIp.Count} 个设备...", true);
        int ok = 0, fail = 0;
        foreach (var rec in withIp)
        {
            bool success = await DoCommunicate(rec);
            if (success) ok++; else fail++;
        }
        SetStatus($"批量通信完成：成功 {ok}，失败 {fail}", true);
    }

    private async Task<bool> DoCommunicate(DeviceRecord rec)
    {
        var ip = rec.IPAddress?.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            SetStatus("请先输入 IP 地址", false);
            return false;
        }
        if (!IPAddress.TryParse(ip, out _))
        {
            SetStatus($"IP 地址格式不正确: {ip}", false);
            return false;
        }

        rec.MacAddress = "通信中...";
        SetStatus($"正在获取 {ip} 的 MAC 地址...", true);

        try
        {
            var result = await Task.Run(() => ResolveMac(ip)).WaitAsync(TimeSpan.FromSeconds(5));
            if (result != null)
            {
                rec.MacAddress = result;
                rec.UpdateTime = DateTime.Now;
                _saved = false;
                SetStatus($"获取成功: {ip} → {result}", true);
                UpdateCount();
                return true;
            }
            else
            {
                rec.MacAddress = "获取失败";
                _saved = false;
                SetStatus($"{ip}: ARP 请求无响应", false);
                UpdateCount();
                return false;
            }
        }
        catch (TimeoutException)
        {
            rec.MacAddress = "超时";
            _saved = false;
            SetStatus($"{ip}: 请求超时（3秒）", false);
            UpdateCount();
            return false;
        }
        catch (Exception ex)
        {
            rec.MacAddress = "错误";
            _saved = false;
            SetStatus($"{ip}: {ex.Message}", false);
            UpdateCount();
            return false;
        }
    }

    private static string? ResolveMac(string ip)
    {
        uint destIp = IPToUInt(ip);
        var errors = new List<string>();

        // 第一阶段：先 ping 一次，预热 ARP 缓存
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = ping.Send(ip, 1500);
            if (reply != null && reply.Status == IPStatus.Success)
                Thread.Sleep(300); // ping 成功后等 ARP 缓存填充
        }
        catch { /* ping 失败不影响后续 ARP */ }

        // 第二阶段：SendARP srcIp=0（系统默认接口）
        var result = TrySendARP(destIp, 0, errors);
        if (result != null) return result;

        // 第三阶段：遍历所有本地接口 IP 逐一尝试
        foreach (var localIp in GetLocalIPv4Addresses())
        {
            try
            {
                uint srcIp = IPToUInt(localIp);
                result = TrySendARP(destIp, srcIp, errors);
                if (result != null) return result;
            }
            catch { }
        }

        // 第四阶段：从系统 ARP 缓存表读取（GetIpNetTable）
        result = ReadMacFromArpTable(destIp);
        if (result != null) return result;

        // 第五阶段：调用系统 arp -a 命令解析输出（最可靠兜底）
        result = ReadMacFromArpCommand(ip);
        if (result != null) return result;

        // 所有方法都失败
        var errDetail = errors.Count > 0 ? $"\n详细: {string.Join("; ", errors.Distinct())}" : "";
        throw new Exception($"所有网络接口均无法获取 MAC 地址{errDetail}");
    }

    private static string? TrySendARP(uint destIp, uint srcIp, List<string> errors)
    {
        byte[] mac = new byte[6];
        int macLen = 6;
        uint ret = SendARP(destIp, srcIp, mac, ref macLen);
        if (ret == 0 && mac.Any(b => b != 0))
            return string.Join(":", mac.Select(b => b.ToString("X2")));
        // 记录错误码用于诊断（将网络字节序 uint 转回可读 IP）
        string srcStr;
        if (srcIp == 0)
        {
            srcStr = "default";
        }
        else
        {
            var rawBytes = BitConverter.GetBytes(srcIp); // 网络字节序
            srcStr = $"{rawBytes[0]}.{rawBytes[1]}.{rawBytes[2]}.{rawBytes[3]}";
        }
        if (ArpErrors.TryGetValue(ret, out var msg))
            errors.Add($"[{srcStr}] {msg} (code={ret})");
        else
            errors.Add($"[{srcStr}] ARP code={ret}");
        return null;
    }

    /// <summary>
    /// 从系统 ARP 缓存表读取 MAC 地址（GetIpNetTable）
    /// </summary>
    private static string? ReadMacFromArpTable(uint destIp)
    {
        int size = 0;
        GetIpNetTable(IntPtr.Zero, ref size, false); // 获取所需大小
        if (size == 0) return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint ret = GetIpNetTable(buffer, ref size, false);
            if (ret != 0) return null;

            int count = Marshal.ReadInt32(buffer);
            for (int i = 0; i < count; i++)
            {
                int offset = 4 + i * IpNetRowSize;
                int index    = Marshal.ReadInt32(buffer, offset);
                int physLen  = Marshal.ReadInt32(buffer, offset + 4);
                int addr     = Marshal.ReadInt32(buffer, offset + 16);
                int entryType = Marshal.ReadInt32(buffer, offset + 20);

                // 匹配 IP，物理地址长度>=6，类型非无效(1)，MAC不全零
                if ((uint)addr == destIp && physLen >= 6 && entryType != 1)
                {
                    var macBytes = new byte[6];
                    Marshal.Copy(IntPtr.Add(buffer, offset + 8), macBytes, 0, 6);
                    if (macBytes.Any(b => b != 0))
                        return string.Join(":", macBytes.Select(b => b.ToString("X2")));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return null;
    }

    /// <summary>
    /// 调用系统 arp -a 命令并解析输出获取 MAC 地址（最可靠的兜底方案）
    /// </summary>
    private static string? ReadMacFromArpCommand(string ip)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-a {ip}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            // 解析输出，匹配格式: 192.168.1.3     00-e0-b4-69-6d-c1     dynamic
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(ip))
                {
                    // 匹配 MAC 地址格式: xx-xx-xx-xx-xx-xx
                    var macMatch = System.Text.RegularExpressions.Regex.Match(
                        line, @"([0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}");
                    if (macMatch.Success)
                    {
                        var mac = macMatch.Value.Replace("-", ":").ToUpperInvariant();
                        if (mac != "00:00:00:00:00:00")
                            return mac;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static List<string> GetLocalIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .ToList();
    }

    private void CopyMac()
    {
        var rec = SelectedRecord;
        if (rec == null || string.IsNullOrEmpty(rec.MacAddress)
            || rec.MacAddress.Contains("...") || rec.MacAddress.Contains("失败")
            || rec.MacAddress.Contains("超时") || rec.MacAddress.Contains("错误"))
        {
            SetStatus("无可复制的 MAC 地址", false); return;
        }
        Clipboard.SetText(rec.MacAddress);
        SetStatus($"已复制: {rec.MacAddress}", true);
    }

    private void CopyRow()
    {
        var rec = SelectedRecord;
        if (rec == null) return;
        var text = $"序号:{rec.Id}\t站名:{rec.StationName}\tIP:{rec.IPAddress}\t设备名:{rec.DeviceName}\t型号:{rec.DeviceModel}\t序列号:{rec.SerialNumber}\tMAC:{rec.MacAddress}\t位置:{rec.Location}";
        Clipboard.SetText(text);
        SetStatus("已复制整行数据", true);
    }

    private void DeleteRow()
    {
        var rec = SelectedRecord;
        if (rec == null) return;
        if (MessageBox.Show($"确定删除第 {rec.Id} 行？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            _records.Remove(rec);
            RenumberIds();
            _saved = false;
            SetStatus("已删除", true);
            UpdateCount();
        }
    }

    private void RenumberIds()
    {
        for (int i = 0; i < _records.Count; i++)
            _records[i].Id = i + 1;
        _grid.Items.Refresh();
    }

    // ========== 数据库/持久化 ==========

    private string GetConnString() => $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={DbPath};";

    /// <summary>
    /// 三级检测 ACE OLEDB 驱动是否可用
    /// </summary>
    private bool CheckAccessProviderAvailable()
    {
        // 第一级：直接尝试实例化 OleDbConnection（最可靠）
        try
        {
            var _ = OleDbFactory.Instance;
            using var conn = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=");
            // 不实际打开，只测试提供者是否注册
            return true;
        }
        catch { }

        // 第二级：检查 ACEOLEDB.DLL 是否存在于已知路径
        foreach (var path in AceDllPaths)
            if (File.Exists(path)) return true;

        // 第三级：OleDbEnumerator（兜底）
        try
        {
            using var reader = OleDbEnumerator.GetRootEnumerator();
            while (reader.Read())
            {
                var desc = reader["SOURCES_DESCRIPTION"]?.ToString() ?? "";
                if (desc.Contains("ACE", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 启动时检测驱动并加载数据
    /// </summary>
    private void DetectDriverAndLoadData()
    {
        _accessAvailable = CheckAccessProviderAvailable();

        if (_accessAvailable)
        {
            SetStatus("检测到 Access 数据库驱动", true);
            ShowDriverButton("验证驱动", true);
            // 自动初始化数据库并加载
            EnsureDatabase();
            LoadData();
        }
        else
        {
            SetStatus("未检测到 Access 数据库驱动", false);
            ShowDriverButton("安装驱动", false);
            // 不自动回退 JSON，等待用户操作
        }
    }

    private void ShowDriverButton(string text, bool isVerify)
    {
        _driverActionBtn.Content = text;
        _driverActionBtn.Tag = isVerify; // true=验证, false=安装
        _driverActionBtn.Visibility = Visibility.Visible;
    }

    private void HideDriverButton()
    {
        _driverActionBtn.Visibility = Visibility.Collapsed;
    }

    private void OnDriverActionClick(object sender, RoutedEventArgs e)
    {
        if (_driverActionBtn.Tag is bool isVerify && isVerify)
            VerifyDriver();
        else
            InstallDriver();
    }

    /// <summary>
    /// 验证驱动：尝试创建/连接数据库
    /// </summary>
    private void VerifyDriver()
    {
        try
        {
            EnsureDatabase();
            if (_accessAvailable && File.Exists(DbPath))
            {
                using var conn = new OleDbConnection(GetConnString());
                conn.Open();
                SetStatus("驱动验证成功，Access 数据库连接正常", true);
                HideDriverButton();
                LoadData();
            }
            else
            {
                SetStatus("驱动验证失败，无法连接数据库", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"驱动验证失败: {ex.Message}，已回退到 JSON 模式", false);
            _accessAvailable = false;
            LoadData(); // 回退 JSON
        }
    }

    /// <summary>
    /// 安装驱动：从 plugins 目录执行安装包
    /// </summary>
    private void InstallDriver()
    {
        // 查找 plugins 目录下的安装包
        string? installerPath = null;
        if (Directory.Exists(PluginsDir))
        {
            installerPath = Directory.GetFiles(PluginsDir, "accessdatabaseengine*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        if (installerPath != null)
        {
            try
            {
                SetStatus("正在安装 Access 数据库驱动...", true);
                HideDriverButton();

                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/quiet /norestart",
                    UseShellExecute = true,
                    Verb = "runas" // 需要管理员权限
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();

                // 安装完成后重新检测
                _accessAvailable = CheckAccessProviderAvailable();
                if (_accessAvailable)
                {
                    SetStatus("驱动安装成功！", true);
                    ShowDriverButton("验证驱动", true);
                    EnsureDatabase();
                    LoadData();
                }
                else
                {
                    SetStatus("驱动安装完成，但未检测到驱动，已回退到 JSON 模式", false);
                    _accessAvailable = false;
                    LoadData();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"驱动安装失败: {ex.Message}，已回退到 JSON 模式", false);
                _accessAvailable = false;
                LoadData();
            }
        }
        else
        {
            // plugins 目录下没有安装包
            var result = MessageBox.Show(
                "未在 plugins 目录找到安装包，是否去官网下载？\n\n" +
                "点击“是”跳转到官网下载\n点击“否”使用 JSON 存储模式",
                "未找到安装包", MessageBoxButton.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.microsoft.com/en-sg/download/details.aspx?id=54920",
                    UseShellExecute = true
                });
            }
            else
            {
                // 回退到 JSON 模式
                _accessAvailable = false;
                SetStatus("已回退到 JSON 存储模式", false);
                HideDriverButton();
                LoadData();
            }
        }
    }

    private void EnsureDatabase()
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);

        // 如果已有 .accdb 文件，检查是否可用
        if (File.Exists(DbPath))
        {
            try
            {
                using var testConn = new OleDbConnection(GetConnString());
                testConn.Open();
                _accessAvailable = true;
                return;
            }
            catch { _accessAvailable = false; }
        }

        if (!_accessAvailable) return;

        // 尝试用 ADOX 创建 .accdb 文件
        try
        {
            dynamic? catalog = null;
            try
            {
                var catalogType = Type.GetTypeFromProgID("ADOX.Catalog");
                if (catalogType == null)
                    throw new Exception("未找到 ADOX 组件");
                catalog = Activator.CreateInstance(catalogType);
                catalog!.Create($"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={DbPath};");
            }
            finally
            {
                if (catalog != null) Marshal.ReleaseComObject(catalog);
            }

            // 建表
            using var conn = new OleDbConnection(GetConnString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE DeviceMac (
                Id COUNTER PRIMARY KEY,
                StationName TEXT(100),
                IPAddress TEXT(50),
                DeviceName TEXT(100),
                DeviceModel TEXT(100),
                SerialNumber TEXT(100),
                MacAddress TEXT(50),
                Location TEXT(200),
                UpdateTime DATETIME
            )";
            cmd.ExecuteNonQuery();
            _accessAvailable = true;
        }
        catch (Exception)
        {
            _accessAvailable = false;
        }
    }

    private void LoadData()
    {
        if (_accessAvailable)
            LoadFromAccess();
        else
            LoadFromJson();

        AddDefaultRow();
        UpdateCount();
    }

    private void LoadFromAccess()
    {
        try
        {
            if (!File.Exists(DbPath)) return;
            using var conn = new OleDbConnection(GetConnString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM DeviceMac ORDER BY Id";
            using var reader = cmd.ExecuteReader();
            var list = new List<DeviceRecord>();
            if (reader != null)
            {
                while (reader.Read())
                {
                    list.Add(new DeviceRecord
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        StationName = reader["StationName"]?.ToString() ?? "",
                        IPAddress = reader["IPAddress"]?.ToString() ?? "",
                        DeviceName = reader["DeviceName"]?.ToString() ?? "",
                        DeviceModel = reader["DeviceModel"]?.ToString() ?? "",
                        SerialNumber = reader["SerialNumber"]?.ToString() ?? "",
                        MacAddress = reader["MacAddress"]?.ToString() ?? "",
                        Location = reader["Location"]?.ToString() ?? "",
                        UpdateTime = reader["UpdateTime"] as DateTime?
                    });
                }
            }
            if (list.Count > 0)
            {
                _records = new ObservableCollection<DeviceRecord>(list);
                _grid.ItemsSource = _records;
                _saved = true;
            }
        }
        catch (Exception ex) { SetStatus($"加载数据失败: {ex.Message}", false); }
    }

    private void LoadFromJson()
    {
        try
        {
            if (!File.Exists(JsonPath)) return;
            var json = File.ReadAllText(JsonPath);
            var list = JsonConvert.DeserializeObject<List<DeviceRecord>>(json);
            if (list != null && list.Count > 0)
            {
                _records = new ObservableCollection<DeviceRecord>(list);
                _grid.ItemsSource = _records;
                _saved = true;
            }
        }
        catch (Exception ex) { SetStatus($"加载数据失败: {ex.Message}", false); }
    }

    private void AddDefaultRow()
    {
        if (_records.Count == 0)
            _records.Add(new DeviceRecord { Id = 1 });
    }

    private void SaveData()
    {
        if (_accessAvailable)
            SaveToAccess();
        else
            SaveToJson();
    }

    private void SaveToAccess()
    {
        try
        {
            if (!File.Exists(DbPath))
            {
                SetStatus("数据库文件不存在，无法保存", false); return;
            }

            using var conn = new OleDbConnection(GetConnString());
            conn.Open();

            using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM DeviceMac";
                delCmd.ExecuteNonQuery();
            }

            // 重置自增计数器：COUNTER 列在 DELETE 后不会归零，
            // 不重置会导致每次保存后序号持续累加
            try
            {
                using var resetCmd = conn.CreateCommand();
                resetCmd.CommandText = "ALTER TABLE DeviceMac ALTER COLUMN Id COUNTER(1,1)";
                resetCmd.ExecuteNonQuery();
            }
            catch { /* 重置失败不影响保存本身，仅序号会继续累加 */ }

            foreach (var rec in _records)
            {
                using var insCmd = conn.CreateCommand();
                insCmd.CommandText = @"INSERT INTO DeviceMac
                    (StationName, IPAddress, DeviceName, DeviceModel, SerialNumber, MacAddress, Location, UpdateTime)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)";
                insCmd.Parameters.Add("@p1", OleDbType.VarWChar, 100).Value = (object?)rec.StationName ?? DBNull.Value;
                insCmd.Parameters.Add("@p2", OleDbType.VarWChar, 50).Value = (object?)rec.IPAddress ?? DBNull.Value;
                insCmd.Parameters.Add("@p3", OleDbType.VarWChar, 100).Value = (object?)rec.DeviceName ?? DBNull.Value;
                insCmd.Parameters.Add("@p4", OleDbType.VarWChar, 100).Value = (object?)rec.DeviceModel ?? DBNull.Value;
                insCmd.Parameters.Add("@p5", OleDbType.VarWChar, 100).Value = (object?)rec.SerialNumber ?? DBNull.Value;
                insCmd.Parameters.Add("@p6", OleDbType.VarWChar, 50).Value = (object?)rec.MacAddress ?? DBNull.Value;
                insCmd.Parameters.Add("@p7", OleDbType.VarWChar, 200).Value = (object?)rec.Location ?? DBNull.Value;
                insCmd.Parameters.Add("@p8", OleDbType.Date).Value = (object?)rec.UpdateTime ?? DBNull.Value;
                insCmd.ExecuteNonQuery();
            }

            // 重新加载以获取数据库自增 Id
            using var readCmd = conn.CreateCommand();
            readCmd.CommandText = "SELECT * FROM DeviceMac ORDER BY Id";
            using var reader = readCmd.ExecuteReader();
            var newList = new List<DeviceRecord>();
            if (reader != null)
            {
                while (reader.Read())
                {
                    newList.Add(new DeviceRecord
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        StationName = reader["StationName"]?.ToString() ?? "",
                        IPAddress = reader["IPAddress"]?.ToString() ?? "",
                        DeviceName = reader["DeviceName"]?.ToString() ?? "",
                        DeviceModel = reader["DeviceModel"]?.ToString() ?? "",
                        SerialNumber = reader["SerialNumber"]?.ToString() ?? "",
                        MacAddress = reader["MacAddress"]?.ToString() ?? "",
                        Location = reader["Location"]?.ToString() ?? "",
                        UpdateTime = reader["UpdateTime"] as DateTime?
                    });
                }
            }
            _records = new ObservableCollection<DeviceRecord>(newList);
            _grid.ItemsSource = _records;

            _saved = true;
            SetStatus($"保存成功 (Access): {DateTime.Now:yyyy-MM-dd HH:mm:ss}，共 {newList.Count} 条", true);
            UpdateCount();
        }
        catch (Exception ex) { SetStatus($"保存失败: {ex.Message}", false); }
    }

    private void SaveToJson()
    {
        try
        {
            if (!Directory.Exists(DataDir))
                Directory.CreateDirectory(DataDir);

            for (int i = 0; i < _records.Count; i++)
                _records[i].Id = i + 1;

            var json = JsonConvert.SerializeObject(_records.ToList(), Formatting.Indented);
            File.WriteAllText(JsonPath, json);
            _saved = true;
            SetStatus($"保存成功 (JSON): {DateTime.Now:yyyy-MM-dd HH:mm:ss}，共 {_records.Count} 条", true);
            UpdateCount();
        }
        catch (Exception ex) { SetStatus($"保存失败: {ex.Message}", false); }
    }

    // ========== 导出前校验 ==========

    private bool CheckCanExport()
    {
        if (!_records.Any())
        {
            SetStatus("无数据可导出", false);
            return false;
        }
        if (!_saved)
        {
            var result = MessageBox.Show("数据尚未保存，是否先保存再导出？", "提示", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                SaveData();
                if (!_saved) return false; // 保存失败
            }
            else return false;
        }
        return true;
    }

    // ========== 导出 xlsx ==========

    private void ExportXlsx()
    {
        if (!CheckCanExport()) return;

        var selectedCols = ShowExportDialog();
        if (selectedCols == null) return;

        ExcelPackage.License.SetNonCommercialPersonal("ToolHelper");
        var dlg = new SaveFileDialog
        {
            Filter = "Excel 文件 (*.xlsx)|*.xlsx",
            FileName = $"设备MAC地址_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("设备MAC地址");

            // Header
            for (int c = 0; c < selectedCols.Count; c++)
                sheet.Cells[1, c + 1].Value = selectedCols[c].Name;

            using (var hdr = sheet.Cells[1, 1, 1, selectedCols.Count])
            {
                hdr.Style.Font.Bold = true;
                hdr.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                hdr.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
                hdr.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            // Data
            for (int r = 0; r < _records.Count; r++)
            {
                var rec = _records[r];
                int col = 1;
                foreach (var c in selectedCols)
                    sheet.Cells[r + 2, col++].Value = c.Getter(rec)?.ToString() ?? "";
            }

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new FileInfo(dlg.FileName));
            SetStatus($"导出成功: {dlg.FileName}", true);
        }
        catch (Exception ex) { SetStatus($"导出失败: {ex.Message}", false); }
    }

    // ========== 导出 docx ==========

    private void ExportDocx()
    {
        if (!CheckCanExport()) return;

        var selectedCols = ShowExportDialog();
        if (selectedCols == null) return;

        var dlg = new SaveFileDialog
        {
            Filter = "Word 文档 (*.docx)|*.docx",
            FileName = $"设备MAC地址_{DateTime.Now:yyyyMMdd_HHmmss}.docx",
            DefaultExt = ".docx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var doc = WordprocessingDocument.Create(dlg.FileName, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Word.Document();
            var body = mainPart.Document.AppendChild(new Word.Body());

            var table = new Word.Table();
            var tblProp = new Word.TableProperties(
                new Word.TableBorders(
                    new Word.TopBorder { Val = Word.BorderValues.Single, Size = 4 },
                    new Word.BottomBorder { Val = Word.BorderValues.Single, Size = 4 },
                    new Word.LeftBorder { Val = Word.BorderValues.Single, Size = 4 },
                    new Word.RightBorder { Val = Word.BorderValues.Single, Size = 4 },
                    new Word.InsideHorizontalBorder { Val = Word.BorderValues.Single, Size = 4 },
                    new Word.InsideVerticalBorder { Val = Word.BorderValues.Single, Size = 4 }
                ),
                new Word.TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = true, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true }
            );
            table.AppendChild(tblProp);

            var headerRow = new Word.TableRow();
            foreach (var col in selectedCols)
            {
                var cell = new Word.TableCell();
                cell.AppendChild(new Word.TableCellProperties(
                    new Word.Shading { Val = Word.ShadingPatternValues.Clear, Fill = "4472C4" }
                ));
                var para = cell.AppendChild(new Word.Paragraph());
                var run = para.AppendChild(new Word.Run(new Word.Text(col.Name)));
                run.AppendChild(new Word.RunProperties(new Word.Bold(), new Word.Color { Val = "FFFFFF" }));
                headerRow.AppendChild(cell);
            }
            table.AppendChild(headerRow);

            foreach (var rec in _records)
            {
                var dataRow = new Word.TableRow();
                foreach (var col in selectedCols)
                {
                    var cell = new Word.TableCell();
                    cell.AppendChild(new Word.Paragraph(new Word.Run(new Word.Text(col.Getter(rec)?.ToString() ?? ""))));
                    dataRow.AppendChild(cell);
                }
                table.AppendChild(dataRow);
            }

            body.AppendChild(table);
            mainPart.Document.Save();
            SetStatus($"导出成功: {dlg.FileName}", true);
        }
        catch (Exception ex) { SetStatus($"导出失败: {ex.Message}", false); }
    }

    // ========== 导出列选择弹窗（带排序编号） ==========

    private List<(string Name, bool Required, Func<DeviceRecord, object?> Getter)>? ShowExportDialog()
    {
        var result = new List<(string Name, bool Required, Func<DeviceRecord, object?> Getter)>();
        bool confirmed = false;

        var win = new Window
        {
            Title = "选择导出列（可调整顺序）", Width = 420, Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "勾选需要导出的列，序号列必选。勾选后按顺序编号，可用上移/下移调整：",
            FontSize = 13, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap
        });

        var listBox = new ListBox { MinHeight = 260, Margin = new Thickness(0, 0, 0, 8) };

        // 每个 item: (IsChecked, Index, ColumnDef)
        var items = AllColumns.Select(c => new ExportColumnItem
        {
            IsChecked = c.Required,
            IsRequired = c.Required,
            Name = c.Name,
            Getter = c.Getter
        }).ToList();

        void RefreshList()
        {
            listBox.Items.Clear();
            int num = 1;
            foreach (var item in items)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                var cb = new System.Windows.Controls.CheckBox
                {
                    IsChecked = item.IsChecked,
                    IsEnabled = !item.IsRequired,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                cb.Checked += (_, _) => { item.IsChecked = true; RefreshList(); };
                cb.Unchecked += (_, _) => { item.IsChecked = false; RefreshList(); };
                sp.Children.Add(cb);

                var label = item.IsChecked
                    ? $"[{num}] {item.Name}" + (item.IsRequired ? "（必选）" : "")
                    : item.Name + (item.IsRequired ? "（必选）" : "");
                num += item.IsChecked ? 1 : 0;
                sp.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

                var li = new ListBoxItem { Content = sp, Tag = item };
                listBox.Items.Add(li);
            }
        }

        RefreshList();
        panel.Children.Add(listBox);

        // 上移/下移按钮
        var movePanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        var moveUpBtn = new Button { Content = "↑ 上移", Width = 80, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource("MaterialDesignOutlinedButton") as System.Windows.Style };
        var moveDownBtn = new Button { Content = "↓ 下移", Width = 80, Style = TryFindResource("MaterialDesignOutlinedButton") as System.Windows.Style };

        moveUpBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem li && li.Tag is ExportColumnItem item)
            {
                int idx = items.IndexOf(item);
                if (idx > 0) { items.RemoveAt(idx); items.Insert(idx - 1, item); RefreshList(); listBox.SelectedIndex = idx - 1; }
            }
        };
        moveDownBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem li && li.Tag is ExportColumnItem item)
            {
                int idx = items.IndexOf(item);
                if (idx < items.Count - 1) { items.RemoveAt(idx); items.Insert(idx + 1, item); RefreshList(); listBox.SelectedIndex = idx + 1; }
            }
        };

        movePanel.Children.Add(moveUpBtn);
        movePanel.Children.Add(moveDownBtn);
        panel.Children.Add(movePanel);

        // 确定/取消
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var okBtn = new Button { Content = "确定导出", Width = 90, Margin = new Thickness(0, 0, 12, 0), Style = TryFindResource("MaterialDesignRaisedButton") as System.Windows.Style };
        var cancelBtn = new Button { Content = "取消", Width = 70 };

        okBtn.Click += (_, _) =>
        {
            confirmed = true;
            foreach (var item in items.Where(i => i.IsChecked))
                result.Add((item.Name, item.IsRequired, item.Getter));
            win.Close();
        };
        cancelBtn.Click += (_, _) => win.Close();

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        win.Content = panel;
        win.Owner = Application.Current.MainWindow;
        win.ShowDialog();

        return confirmed ? result : null;
    }

    // ========== Helpers ==========

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        if (success)
        {
            _statusIcon.Kind = PackIconKind.CheckCircleOutline;
            _statusIcon.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            _statusBarBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(165, 214, 167));
            _statusBarBorder.Background = new SolidColorBrush(Color.FromRgb(241, 248, 241));
        }
        else
        {
            _statusIcon.Kind = PackIconKind.AlertCircleOutline;
            _statusIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));
            _statusBarBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 154, 154));
            _statusBarBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 243));
        }
    }

    private void UpdateCount()
    {
        var saveIcon = _saved ? "✓" : "●";
        var saveColor = _saved ? "已保存" : "未保存";
        _countText.Text = $"共 {_records.Count} 条  |  {saveIcon} {saveColor}";
        _countText.Foreground = _saved
            ? new SolidColorBrush(Color.FromRgb(130, 130, 130))
            : new SolidColorBrush(Color.FromRgb(255, 152, 0));
    }
}

// ========== 导出列辅助类 ==========

internal class ExportColumnItem
{
    public bool IsChecked { get; set; }
    public bool IsRequired { get; set; }
    public string Name { get; set; } = "";
    public Func<DeviceRecord, object?> Getter { get; set; } = _ => null;
}

// ========== Data Model ==========

public class DeviceRecord : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private int _id;
    private string _stationName = "";
    private string _ipAddress = "";
    private string _deviceName = "";
    private string _deviceModel = "";
    private string _serialNumber = "";
    private string _macAddress = "";
    private string _location = "";
    private DateTime? _updateTime;

    public int Id { get => _id; set { _id = value; Notify(nameof(Id)); } }
    public string StationName { get => _stationName; set { _stationName = value; Notify(nameof(StationName)); } }
    public string IPAddress { get => _ipAddress; set { _ipAddress = value; Notify(nameof(IPAddress)); } }
    public string DeviceName { get => _deviceName; set { _deviceName = value; Notify(nameof(DeviceName)); } }
    public string DeviceModel { get => _deviceModel; set { _deviceModel = value; Notify(nameof(DeviceModel)); } }
    public string SerialNumber { get => _serialNumber; set { _serialNumber = value; Notify(nameof(SerialNumber)); } }
    public string MacAddress { get => _macAddress; set { _macAddress = value; Notify(nameof(MacAddress)); } }
    public string Location { get => _location; set { _location = value; Notify(nameof(Location)); } }
    public DateTime? UpdateTime { get => _updateTime; set { _updateTime = value; Notify(nameof(UpdateTime)); } }
}
