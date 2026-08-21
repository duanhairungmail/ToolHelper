using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using DataGridTextColumn = System.Windows.Controls.DataGridTextColumn;

namespace ToolHelper.Views.Other;

/// <summary>
/// 群 Ping — 批量检测多主机的连通性、延迟、丢包，支持 CIDR 网段扫描与 CSV/Excel 导出
/// 原生实现（System.Net.NetworkInformation.Ping），无第三方依赖
/// </summary>
public class GroupPingView : UserControl
{
    private List<string> _targets = new();
    private TextBox _cidrBox = new();
    private TextBox _concurrencyBox = new();
    private TextBox _countBox = new();
    private TextBox _timeoutBox = new();
    private DataGrid _resultGrid = new();
    private TextBlock _summaryText = new();
    private TextBlock _statusText = new();
    private Button _startBtn = new(), _stopBtn = new();
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<PingResult> _results = new();
    private bool _built;

    public GroupPingView()
    {
        Loaded += (s, e) => { if (!_built) { _built = true; BuildUI(); } };
    }

    // ========== UI 构建 ==========

    private void BuildUI()
    {
        var root = new StackPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // 标题
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.LanConnect, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  群 Ping", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(titleRow);
        root.Children.Add(new TextBlock { Text = "批量检测多主机的连通性、延迟、丢包，支持 CIDR 网段扫描与 CSV/Excel 导出", FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        // 网段行
        var cidrRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        cidrRow.Children.Add(MakeLabel("网段:"));
        _cidrBox = MakeBox("192.168.1.0/24 或 192.168.1.1-254", "", 200);
        cidrRow.Children.Add(_cidrBox);
        cidrRow.Children.Add(MakeButton("扫描网段", ScanCidr, false, PackIconKind.Magnify));
        cidrRow.Children.Add(MakeButton("导入文件", ImportFile, false, PackIconKind.FileUpload));
        root.Children.Add(cidrRow);

        // 参数行
        var paramRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        paramRow.Children.Add(MakeLabel("并发数:"));
        _concurrencyBox = MakeBox("并发数", "50", 60);
        paramRow.Children.Add(_concurrencyBox);
        paramRow.Children.Add(MakeLabel("每目标次数:"));
        _countBox = MakeBox("次数", "4", 60);
        paramRow.Children.Add(_countBox);
        paramRow.Children.Add(MakeLabel("超时(ms):"));
        _timeoutBox = MakeBox("超时", "1000", 70);
        paramRow.Children.Add(_timeoutBox);
        root.Children.Add(paramRow);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _startBtn = MakeButton("开始 Ping", StartPing, true, PackIconKind.Play);
        btnRow.Children.Add(_startBtn);
        _stopBtn = MakeButton("停止", StopPing, false, PackIconKind.Stop);
        _stopBtn.IsEnabled = false;
        btnRow.Children.Add(_stopBtn);
        btnRow.Children.Add(MakeButton("清空", ClearResults, false, PackIconKind.Eraser));
        btnRow.Children.Add(MakeButton("导出CSV", ExportCsv, false, PackIconKind.FileDelimited));
        btnRow.Children.Add(MakeButton("导出Excel", ExportExcel, false, PackIconKind.FileExcel));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnRow.Children.Add(_statusText);
        root.Children.Add(btnRow);

        // 结果表格
        _resultGrid.ItemsSource = _results;
        _resultGrid.AutoGenerateColumns = false;
        _resultGrid.IsReadOnly = true;
        _resultGrid.CanUserAddRows = false;
        _resultGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _resultGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _resultGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250));
        _resultGrid.MinHeight = 200;
        _resultGrid.MaxHeight = 400;

        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "目标", Binding = new System.Windows.Data.Binding("Host"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        // 状态列（着色：在线绿 / 超时红 / 部分丢包橙）
        var statusTemplate = new DataTemplate();
        var statusFactory = new FrameworkElementFactory(typeof(TextBlock));
        statusFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Status"));
        statusFactory.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Status") { Converter = new PingStatusColorConverter() });
        statusFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        statusTemplate.VisualTree = statusFactory;
        _resultGrid.Columns.Add(new DataGridTemplateColumn { Header = "状态", Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true, CellTemplate = statusTemplate });

        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "平均延迟(ms)", Binding = new System.Windows.Data.Binding("AvgDelay"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto) });
        _resultGrid.Columns.Add(new DataGridTextColumn { Header = "丢包率", Binding = new System.Windows.Data.Binding("LossRate"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto) });
        root.Children.Add(_resultGrid);

        // 统计行
        _summaryText.FontSize = 13;
        _summaryText.FontWeight = FontWeights.SemiBold;
        _summaryText.Margin = new Thickness(0, 8, 0, 0);
        UpdateSummary();
        root.Children.Add(_summaryText);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ========== 核心逻辑 ==========

    private async void StartPing()
    {
        var hosts = _targets;

        if (hosts.Count == 0)
        {
            SetStatus("请先扫描网段或导入文件", false);
            return;
        }

        _results.Clear();
        _startBtn.IsEnabled = false;
        _stopBtn.IsEnabled = true;
        SetStatus($"正在 Ping {hosts.Count} 个目标...", true);

        int concurrency = int.TryParse(_concurrencyBox.Text, out var c) ? Math.Clamp(c, 1, 200) : 50;
        int count = int.TryParse(_countBox.Text, out var n) ? Math.Clamp(n, 1, 10) : 4;
        int timeout = int.TryParse(_timeoutBox.Text, out var t) ? Math.Clamp(t, 100, 10000) : 1000;

        using var semaphore = new SemaphoreSlim(concurrency);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var tasks = hosts.Select(async host =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var result = await PingOneAsync(host, count, timeout, ct);
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        _results.Add(result);
                        UpdateSummary();
                    });
                }
                catch (OperationCanceledException) { }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            SetStatus($"完成 — {_results.Count} 个目标已检测", true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("已停止", true);
        }
        catch (Exception ex)
        {
            SetStatus($"错误: {ex.Message}", false);
        }
        finally
        {
            _startBtn.IsEnabled = true;
            _stopBtn.IsEnabled = false;
            _cts?.Dispose();
            _cts = null;
            UpdateSummary();
        }
    }

    private static async Task<PingResult> PingOneAsync(string host, int count, int timeout, CancellationToken ct)
    {
        using var ping = new Ping();
        int success = 0;
        long totalDelay = 0;

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, timeout);
                if (reply.Status == IPStatus.Success)
                {
                    success++;
                    totalDelay += reply.RoundtripTime;
                }
            }
            catch { /* 视为失败 */ }
        }

        return new PingResult
        {
            Host = host,
            SuccessCount = success,
            TotalCount = count,
            AvgDelay = success > 0 ? totalDelay / success : 0,
            Status = success == count ? "在线" : success > 0 ? "部分丢包" : "超时/失败"
        };
    }

    private void StopPing()
    {
        _cts?.Cancel();
        _stopBtn.IsEnabled = false;
    }

    private void ScanCidr()
    {
        var input = _cidrBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            SetStatus("请输入网段（如 192.168.1.0/24 或 192.168.1.1-254）", false);
            return;
        }

        try
        {
            _targets = ExpandCidr(input);
            SetStatus($"已展开 {_targets.Count} 个 IP，点击「开始 Ping」执行", true);
        }
        catch (Exception ex)
        {
            SetStatus($"网段解析失败: {ex.Message}", false);
        }
    }

    private void ImportFile()
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "导入 IP 列表",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                _targets = File.ReadAllLines(dlg.FileName)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith('#'))
                    .Distinct()
                    .ToList();
                SetStatus($"已导入 {_targets.Count} 个目标: {Path.GetFileName(dlg.FileName)}", true);
            }
        }
        catch (Exception ex) { SetStatus($"导入失败: {ex.Message}", false); }
    }

    // ========== 网段展开 ==========

    private static List<string> ExpandCidr(string input)
    {
        var result = new List<string>();
        var text = input.Trim();

        if (text.Contains('/'))
        {
            var parts = text.Split('/');
            var baseIp = IPAddress.Parse(parts[0]);
            int prefix = int.Parse(parts[1]);

            if (prefix < 16 || prefix > 30)
                throw new ArgumentException("前缀长度需在 16-30 之间（避免过大网段）");

            uint ip = IpToUint(baseIp);
            uint mask = prefix == 0 ? 0 : (0xFFFFFFFFu << (32 - prefix));
            uint network = ip & mask;
            uint start = network + 1;
            uint end = network + (0xFFFFFFFFu >> prefix) - 1;

            for (uint a = start; a <= end && result.Count < 4096; a++)
                result.Add(UintToIp(a).ToString());
        }
        else if (text.Contains('-'))
        {
            var idx = text.LastIndexOf('.');
            if (idx < 0) throw new ArgumentException("格式错误，应为 192.168.1.1-254");
            var prefix = text[..idx];
            var range = text[(idx + 1)..].Split('-');
            if (range.Length != 2) throw new ArgumentException("范围格式错误");
            int from = int.Parse(range[0]);
            int to = int.Parse(range[1]);
            if (from < 1 || to > 254 || from > to) throw new ArgumentException("范围需在 1-254 之间");
            for (int i = from; i <= to && result.Count < 4096; i++)
                result.Add($"{prefix}.{i}");
        }
        else
        {
            result.Add(text);
        }

        return result;
    }

    private static uint IpToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) throw new ArgumentException("仅支持 IPv4");
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress UintToIp(uint ip) =>
        new(new byte[] { (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip });

    // ========== 导出 ==========

    private void ExportCsv()
    {
        if (_results.Count == 0) { SetStatus("无数据可导出", false); return; }
        var dlg = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = $"群Ping_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("目标,状态,平均延迟(ms),丢包率");
        foreach (var r in _results)
            sb.AppendLine($"{r.Host},{r.Status},{r.AvgDelay},{r.LossRate}");

        File.WriteAllText(dlg.FileName, sb.ToString());
        SetStatus($"已导出 CSV: {dlg.FileName}", true);
    }

    private void ExportExcel()
    {
        if (_results.Count == 0) { SetStatus("无数据可导出", false); return; }
        var dlg = new SaveFileDialog { Filter = "Excel 文件|*.xlsx", FileName = $"群Ping_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx" };
        if (dlg.ShowDialog() != true) return;

        ExcelPackage.License.SetNonCommercialOrganization("ToolHelper");
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("群Ping结果");

        var headers = new[] { "目标", "状态", "平均延迟(ms)", "丢包率" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cells[1, i + 1].Value = headers[i];
            ws.Cells[1, i + 1].Style.Font.Bold = true;
            ws.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 220, 230, 241));
        }

        for (int r = 0; r < _results.Count; r++)
        {
            var row = _results[r];
            ws.Cells[r + 2, 1].Value = row.Host;
            ws.Cells[r + 2, 2].Value = row.Status;
            ws.Cells[r + 2, 3].Value = row.AvgDelay;
            ws.Cells[r + 2, 4].Value = row.LossRate;
        }
        ws.Cells[ws.Dimension.Address].AutoFitColumns();
        pkg.SaveAs(new FileInfo(dlg.FileName));
        SetStatus($"已导出 Excel: {dlg.FileName}", true);
    }

    private void ClearResults()
    {
        _results.Clear();
        UpdateSummary();
        SetStatus("已清空", true);
    }

    private void UpdateSummary()
    {
        var total = _results.Count;
        var online = _results.Count(r => r.Status == "在线");
        var timeout = _results.Count(r => r.Status == "超时/失败");
        var avgDelay = _results.Where(r => r.SuccessCount > 0).Select(r => r.AvgDelay).DefaultIfEmpty(0).Average();
        _summaryText.Text = $"统计：总数 {total}  |  在线 {online}  |  超时 {timeout}  |  平均延迟 {avgDelay:F0}ms";
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    // ========== 辅助 UI ==========

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
    };

    private TextBox MakeBox(string hint, string def = "", int minWidth = 80)
    {
        var tb = new TextBox { FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 13, Margin = new Thickness(0, 0, 6, 0), MinWidth = minWidth, Text = def };
        var style = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) tb.Style = style;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);
        return tb;
    }

    private Button MakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button { Content = sp, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource(primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton") as Style };
        btn.Click += (s, e) => handler();
        return btn;
    }

    private class PingStatusColorConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value?.ToString() switch
            {
                "在线" => new SolidColorBrush(Color.FromRgb(0, 150, 0)),
                "超时/失败" => new SolidColorBrush(Color.FromRgb(220, 50, 50)),
                "部分丢包" => new SolidColorBrush(Color.FromRgb(255, 160, 0)),
                _ => Brushes.Gray
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
}

// ================== 数据模型 ==================

public class PingResult
{
    public string Host { get; set; } = "";
    public string Status { get; set; } = "";
    public long AvgDelay { get; set; }
    public int SuccessCount { get; set; }
    public int TotalCount { get; set; }
    public string LossRate => TotalCount == 0 ? "-" : $"{((TotalCount - SuccessCount) * 100.0 / TotalCount):F0}%";
}