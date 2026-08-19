using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Word = DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Win32;
using WpfDataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using WpfBorder = System.Windows.Controls.Border;

namespace ToolHelper.Views.Security;

public class DruidScanView : UserControl
{
    private TextBox _ipBox = new();
    private TextBox _portBox = new();
    private TextBox _urlBox = new();
    private Button _scanBtn = new();
    private TextBlock _statusText = new();
    private DataGrid _resultGrid = new();
    private DataGrid _detailGrid = new();
    private TextBox _suggestBox = new();
    private DockPanel _suggestPanel = new();
    private WpfBorder _suggestOuter = new(); // 整改建议独立区域（含边框，随建议区一起显示/隐藏）
    private RowDefinition _suggestRow = new(); // 整改建议所在网格行（隐藏时行高0，显示时三行等高）
    private readonly List<DruidScanResult> _allResults = new();
    private bool _built;
    private const double MinViewHeight = 600; // 宿主视口过小时保持的视图最小高度（不足部分由宿主滚动条兜底）

    public DruidScanView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        // 宿主内容区包裹了 ScrollViewer（无限高度约束），三行等高的星号行会退化为按内容自然高度：
        // 扫描结果条数一多就把详情区、整改建议区顶出可视区，故把视图高度钉在宿主视口高度上
        ViewportFitHelper.FitToViewport(this, MinViewHeight);
    }

    private TextBox MakeBox(string hint, string def = "")
    {
        var tb = new TextBox { FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 13, Margin = new Thickness(0, 0, 6, 0), MinWidth = 120, Text = def };
        var style = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) tb.Style = style;
        HintAssist.SetHint(tb, hint);
        return tb;
    }

    private TextBlock MakeLabel(string text) => new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

    private Button MakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button { Content = sp, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource(primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton") as Style };
        btn.Click += (s, e) => handler();
        return btn;
    }

    private void BuildUI()
    {
        var root = new DockPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // 顶部
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.ShieldAlert, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  Druid 漏洞检测", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        topPanel.Children.Add(titleRow);
        topPanel.Children.Add(new TextBlock { Text = "Alibaba Druid 未授权访问漏洞检测，输入目标IP和端口即可检测 Druid 监控页面是否存在未授权访问漏洞。", FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        // IP + 端口行
        var ipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        ipRow.Children.Add(MakeLabel("IP地址:"));
        _ipBox = MakeBox("目标IP地址", "192.168.1.3");
        _ipBox.MinWidth = 200;
        _ipBox.TextChanged += (s, e) => AutoFillUrl();
        ipRow.Children.Add(_ipBox);
        ipRow.Children.Add(MakeLabel("端口:"));
        _portBox = MakeBox("端口", "8088");
        _portBox.MinWidth = 80;
        _portBox.TextChanged += (s, e) => AutoFillUrl();
        ipRow.Children.Add(_portBox);
        topPanel.Children.Add(ipRow);

        // 目标URL行
        var urlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        urlRow.Children.Add(MakeLabel("目标URL:"));
        _urlBox = MakeBox("http://IP:端口/druid/index.html", "http://192.168.1.3:8088/druid/index.html");
        _urlBox.MinWidth = 350;
        urlRow.Children.Add(_urlBox);
        _scanBtn = MakeButton("检测", DoScan, true, PackIconKind.SearchWeb);
        urlRow.Children.Add(_scanBtn);
        topPanel.Children.Add(urlRow);

        // 操作按钮行
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal };
        actionRow.Children.Add(MakeButton("导出 xlsx", ExportXlsx, false, PackIconKind.FileExcelOutline));
        actionRow.Children.Add(MakeButton("导出 docx", ExportDocx, false, PackIconKind.FileWordOutline));
        actionRow.Children.Add(MakeButton("清空结果", ClearResults, false, PackIconKind.TrashCan));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        actionRow.Children.Add(_statusText);
        topPanel.Children.Add(actionRow);

        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        // 中部内容区：三行等高网格（扫描结果 / 选中目标的详情 / 整改建议）
        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 行0 扫描结果
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 行1 选中目标的详情
        _suggestRow = new RowDefinition { Height = new GridLength(0) }; // 行2 整改建议（默认隐藏，行高0）
        contentGrid.RowDefinitions.Add(_suggestRow);
        root.Children.Add(contentGrid);

        // 行2 - 整改建议（仅有漏洞时显示）
        _suggestOuter = new WpfBorder
        {
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 235)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Visibility = Visibility.Collapsed
        };
        _suggestPanel = new DockPanel { Margin = new Thickness(6), Visibility = Visibility.Collapsed };
        var suggestLabel = new TextBlock { Text = "整改建议", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(suggestLabel, Dock.Top);
        _suggestPanel.Children.Add(suggestLabel);
        var suggestBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        suggestBtnRow.Children.Add(MakeButton("复制方案一(关闭监控)", () => CopySuggestion("方案一"), false, PackIconKind.ContentCopy));
        suggestBtnRow.Children.Add(MakeButton("复制方案二(加密码)", () => CopySuggestion("方案二"), false, PackIconKind.ContentCopy));
        DockPanel.SetDock(suggestBtnRow, Dock.Bottom);
        _suggestPanel.Children.Add(suggestBtnRow);
        _suggestBox.AcceptsReturn = true;
        _suggestBox.TextWrapping = TextWrapping.Wrap;
        _suggestBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _suggestBox.IsReadOnly = true;
        _suggestBox.FontFamily = new FontFamily("Consolas");
        _suggestBox.FontSize = 12;
        _suggestBox.Background = new SolidColorBrush(Color.FromRgb(40, 44, 52));
        _suggestBox.Foreground = new SolidColorBrush(Color.FromRgb(171, 178, 191));
        _suggestBox.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 72));
        var sbStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (sbStyle != null) _suggestBox.Style = sbStyle;
        _suggestPanel.Children.Add(_suggestBox); // 最后添加，填充剩余空间
        _suggestOuter.Child = _suggestPanel;
        Grid.SetRow(_suggestOuter, 2);
        contentGrid.Children.Add(_suggestOuter);

        // 行1 - 选中目标的详情（扫描结果下边，独立区域）
        var detailOuter = new WpfBorder
        {
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 235)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true
        };
        var detailPanel = new DockPanel { Margin = new Thickness(6) };
        var detailLabel = new TextBlock { Text = "选中目标的详情", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(detailLabel, Dock.Top);
        detailPanel.Children.Add(detailLabel);
        _detailGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserAddRows = false,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 245, 250)),
            RowBackground = Brushes.White,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(230, 230, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 235)),
            BorderThickness = new Thickness(1)
        };
        _detailGrid.Columns.Add(new WpfDataGridTextColumn { Header = "页面路径", Binding = new System.Windows.Data.Binding("Path"), Width = new DataGridLength(200) });
        _detailGrid.Columns.Add(new WpfDataGridTextColumn { Header = "状态码", Binding = new System.Windows.Data.Binding("StatusCode"), Width = new DataGridLength(60) });
        _detailGrid.Columns.Add(new WpfDataGridTextColumn { Header = "可访问", Binding = new System.Windows.Data.Binding("AccessibleText"), Width = new DataGridLength(60) });
        _detailGrid.Columns.Add(new WpfDataGridTextColumn { Header = "风险", Binding = new System.Windows.Data.Binding("RiskLevel"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _detailGrid.Columns.Add(new WpfDataGridTextColumn { Header = "内容大小", Binding = new System.Windows.Data.Binding("ContentLength"), Width = new DataGridLength(80) });
        detailPanel.Children.Add(_detailGrid); // 最后添加，填充剩余空间
        detailOuter.Child = detailPanel;
        Grid.SetRow(detailOuter, 1);
        contentGrid.Children.Add(detailOuter);

        // 行0 - 扫描结果
        var resultPanel = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var gridLabel = new TextBlock { Text = "扫描结果", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(gridLabel, Dock.Top);
        resultPanel.Children.Add(gridLabel);

        _resultGrid.AutoGenerateColumns = false;
        _resultGrid.IsReadOnly = true;
        _resultGrid.SelectionMode = DataGridSelectionMode.Single;
        _resultGrid.CanUserAddRows = false;
        _resultGrid.SelectionChanged += OnResultSelected;
        // 扫描结果 DataGrid 列
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "序号", Binding = new System.Windows.Data.Binding("Index"), Width = new DataGridLength(90) });
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "目标URL", Binding = new System.Windows.Data.Binding("TargetUrl"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "授权状态", Binding = new System.Windows.Data.Binding("AuthStatusText"), Width = new DataGridLength(150) });
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "风险等级", Binding = new System.Windows.Data.Binding("RiskText"), Width = new DataGridLength(140) });
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "暴露页面", Binding = new System.Windows.Data.Binding("ExposedCount"), Width = new DataGridLength(140) });
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "弱口令", Binding = new System.Windows.Data.Binding("WeakCredential"), Width = new DataGridLength(150) });
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "耗时(ms)", Binding = new System.Windows.Data.Binding("ResponseTimeMs"), Width = new DataGridLength(140) });
        _resultGrid.Columns.Add(new WpfDataGridTextColumn { Header = "检测时间", Binding = new System.Windows.Data.Binding("DetectTime"){ StringFormat = "yyyy-MM-dd HH:mm:ss" }, Width = new DataGridLength(200) });
        var gridBorder = new WpfBorder
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 235)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = _resultGrid
        };
        resultPanel.Children.Add(gridBorder);
        Grid.SetRow(resultPanel, 0);
        contentGrid.Children.Add(resultPanel);

        Content = root;
        SetSuggestVisibility(false);
    }

    private void OnResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_resultGrid.SelectedItem is not DruidScanResult row)
        {
            _detailGrid.ItemsSource = null;
            SetSuggestVisibility(false);
            return;
        }
        _detailGrid.ItemsSource = row.Pages;
        // 仅有漏洞时才显示整改建议
        if (row.Risk >= RiskLevel.Medium)
        {
            SetSuggestVisibility(true);
            const string FIX1 = "方案一（关闭监控页面，推荐）:\n" +
                "spring.datasource.druid.stat-view-servlet.enabled=false\n";
            const string FIX2 = "\n方案二（保留监控 + 强密码 + IP白名单）:\n" +
                "spring.datasource.druid.stat-view-servlet.enabled=true\n" +
                "spring.datasource.druid.stat-view-servlet.login-username=monitor\n" +
                "spring.datasource.druid.stat-view-servlet.login-password=<强密码>\n" +
                "spring.datasource.druid.stat-view-servlet.allow=127.0.0.1\n";
            _suggestBox.Text = $"[ {row.TargetUrl}  风险: {RiskLabel(row.Risk)} ]\n\n" + FIX1 + FIX2;
        }
        else
        {
            SetSuggestVisibility(false);
        }
    }

    private void CopySuggestion(string which)
    {
        var text = which == "方案一"
            ? "spring.datasource.druid.stat-view-servlet.enabled=false"
            : "spring.datasource.druid.stat-view-servlet.enabled=true\n" +
              "spring.datasource.druid.stat-view-servlet.login-username=monitor\n" +
              "spring.datasource.druid.stat-view-servlet.login-password=<强密码>\n" +
              "spring.datasource.druid.stat-view-servlet.allow=127.0.0.1";
        try { Clipboard.SetText(text); SetStatus("已复制到剪贴板", true); }
        catch { SetStatus("复制失败", false); }
    }

    private void SetSuggestVisibility(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        _suggestOuter.Visibility = v;
        _suggestPanel.Visibility = v;
        // 建议区显示时三行等高（各占1份）；隐藏时该行高度归零，扫描结果与详情两行等高
        _suggestRow.Height = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    private void RefreshGrid()
    {
        for (int i = 0; i < _allResults.Count; i++) _allResults[i].Index = i + 1;
        _resultGrid.ItemsSource = null;
        _resultGrid.ItemsSource = _allResults;
    }

    private void AutoFillUrl()
    {
        var ip = _ipBox.Text.Trim();
        var port = _portBox.Text.Trim();
        if (!string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(port))
            _urlBox.Text = $"http://{ip}:{port}/druid/index.html";
    }
    // ================== 检测引擎 ==================
    private static HttpClient CreateClient(int timeoutSec = 5)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseProxy = false
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSec) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (DruidScanner/1.0)");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/json,*/*");
        return client;
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        return url.TrimEnd('/');
    }

    private async Task<DruidScanResult> ScanAsync(string targetUrl, CancellationToken ct = default)
    {
        var normalizedUrl = NormalizeUrl(targetUrl);
        var result = new DruidScanResult
        {
            TargetUrl = targetUrl,
            NormalizedUrl = normalizedUrl,
            DetectTime = DateTime.Now,
            Pages = new List<DruidPageResult>()
        };

        // 提取 baseUrl（去掉 /druid/index.html 部分，用于子页面探测）
        var baseUrl = normalizedUrl;
        var druidIdx = baseUrl.IndexOf("/druid/", StringComparison.OrdinalIgnoreCase);
        if (druidIdx > 0) baseUrl = baseUrl.Substring(0, druidIdx);

        using var client = CreateClient(5);
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? indexResp = null;

        try
        {
            ct.ThrowIfCancellationRequested();
            indexResp = await client.GetAsync(normalizedUrl, ct);
        }
        catch (TaskCanceledException) { result.AuthStatus = DruidAuthStatus.Unreachable; result.Risk = RiskLevel.Safe; return result; }
        catch (HttpRequestException) { result.AuthStatus = DruidAuthStatus.Unreachable; result.Risk = RiskLevel.Safe; return result; }
        catch { result.AuthStatus = DruidAuthStatus.Unreachable; result.Risk = RiskLevel.Safe; return result; }

        sw.Stop();
        result.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
        int code = (int)indexResp.StatusCode;

        switch (code)
        {
            case 200:
                string html = await indexResp.Content.ReadAsStringAsync(ct);
                result.AuthStatus = AnalyzeAuth(html);
                break;
            case 301:
            case 302:
                string loc = indexResp.Headers.Location?.ToString() ?? "";
                result.AuthStatus = loc.Contains("login") ? DruidAuthStatus.AuthRequired : DruidAuthStatus.Unknown;
                break;
            case 401: result.AuthStatus = DruidAuthStatus.AuthRequired; break;
            case 403: result.AuthStatus = DruidAuthStatus.Blocked; break;
            case 404: result.AuthStatus = DruidAuthStatus.NotEnabled; break;
            default: result.AuthStatus = DruidAuthStatus.Unknown; break;
        }

        if (result.AuthStatus == DruidAuthStatus.Unauthorized)
        {
            result.Pages = await ProbeSubPagesAsync(client, baseUrl, ct);
            result.Risk = CalculateRisk(result.Pages);
            string? weak = await TryWeakPasswordAsync(client, baseUrl, ct);
            if (weak != null) result.WeakCredential = weak;
        }
        else if (result.AuthStatus == DruidAuthStatus.AuthRequired)
        {
            string? weak = await TryWeakPasswordAsync(client, baseUrl, ct);
            if (weak != null) { result.AuthStatus = DruidAuthStatus.WeakPassword; result.WeakCredential = weak; result.Risk = RiskLevel.High; }
            else result.Risk = RiskLevel.Low;
        }
        else if (result.AuthStatus == DruidAuthStatus.NotEnabled || result.AuthStatus == DruidAuthStatus.Blocked)
            result.Risk = RiskLevel.Safe;
        else
            result.Risk = RiskLevel.Safe;

        return result;
    }

    private static DruidAuthStatus AnalyzeAuth(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return DruidAuthStatus.Unknown;
        if (html.Contains("loginUsername") || html.Contains("login_password") || html.Contains("Druid Stat Login"))
            return DruidAuthStatus.AuthRequired;
        if (html.Contains("Druid Stat Index") || html.Contains("Druid Monitor") || html.Contains("DruidDrivers") || html.Contains("druidVersion"))
            return DruidAuthStatus.Unauthorized;
        if (html.Contains("<title>404") || html.Contains("Not Found") || html.Length < 500)
            return DruidAuthStatus.NotEnabled;
        return DruidAuthStatus.Unknown;
    }

    private static readonly string[] SubPages = { "/druid/sql.html", "/druid/websession.html", "/druid/weburi.html", "/druid/webapp.html", "/druid/datasource.json", "/druid/spring.html", "/druid/basic.json" };

    private async Task<List<DruidPageResult>> ProbeSubPagesAsync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        var tasks = SubPages.Select(async path =>
        {
            var pr = new DruidPageResult { Path = path, RiskLevel = AssessPageRisk(path) };
            try
            {
                var resp = await client.GetAsync(baseUrl + path, ct);
                pr.StatusCode = (int)resp.StatusCode;
                if (resp.IsSuccessStatusCode)
                {
                    pr.IsAccessible = true;
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    pr.ContentLength = body.Length;
                    pr.PageTitle = ExtractTitle(body);
                }
            }
            catch { pr.IsAccessible = false; pr.StatusCode = 0; }
            return pr;
        });
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private static string AssessPageRisk(string path) => path switch
    {
        "/druid/sql.html" => "超危 - SQL执行记录泄露",
        "/druid/websession.html" => "超危 - Session信息泄露",
        "/druid/datasource.json" => "超危 - 数据库连接凭据泄露",
        "/druid/weburi.html" => "中危 - API接口路径暴露",
        "/druid/spring.html" => "中危 - Spring Bean信息泄露",
        "/druid/webapp.html" => "低危 - 应用基本信息",
        "/druid/basic.json" => "低危 - 基本统计信息",
        _ => "未知"
    };

    private static RiskLevel CalculateRisk(List<DruidPageResult> pages)
    {
        if (pages.Any(p => p.IsAccessible && p.Path == "/druid/sql.html")) return RiskLevel.Critical;
        if (pages.Any(p => p.IsAccessible && p.Path == "/druid/websession.html")) return RiskLevel.Critical;
        if (pages.Any(p => p.IsAccessible && p.Path == "/druid/datasource.json")) return RiskLevel.Critical;
        if (pages.Count(p => p.IsAccessible) >= 2) return RiskLevel.High;
        if (pages.Any(p => p.IsAccessible)) return RiskLevel.Medium;
        return RiskLevel.Safe;
    }

    private static string ExtractTitle(string html)
    {
        var m = Regex.Match(html, @"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private async Task<string?> TryWeakPasswordAsync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        var users = new[] { "admin", "druid", "root", "monitor", "sa" };
        var passes = new[] { "admin", "druid", "123456", "admin123", "druid123", "password", "root", "1q2w3e" };
        foreach (var u in users)
        {
            foreach (var p in passes)
            {
                if (ct.IsCancellationRequested) return null;
                try
                {
                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("loginUsername", u),
                        new KeyValuePair<string, string>("loginPassword", p)
                    });
                    var resp = await client.PostAsync(baseUrl + "/druid/submitLogin", content, ct);
                    if (resp.StatusCode == HttpStatusCode.Redirect)
                    {
                        string loc = resp.Headers.Location?.ToString() ?? "";
                        if (!loc.Contains("login")) return $"{u}:{p}";
                    }
                }
                catch { /* ignore single attempt failure */ }
            }
        }
        return null;
    }

    // ================== UI 事件 ==================
    private async void DoScan()
    {
        var url = _urlBox.Text.Trim();
        if (string.IsNullOrEmpty(url) || url == "http://") { SetStatus("请输入目标URL", false); return; }
        _scanBtn.IsEnabled = false;
        SetStatus("正在检测...", true);
        try
        {
            var result = await Task.Run(() => ScanAsync(url));
            _allResults.Add(result);
            RefreshGrid();
            SetStatus($"检测完成: {result.TargetUrl} - {AuthLabel(result.AuthStatus)} / {RiskLabel(result.Risk)}", true);
        }
        catch (Exception ex) { SetStatus($"检测失败: {ex.Message}", false); }
        finally { _scanBtn.IsEnabled = true; }
    }

    private void ClearResults()
    {
        _allResults.Clear();
        RefreshGrid();
        _detailGrid.ItemsSource = null;
        SetSuggestVisibility(false);
        SetStatus("已清空", true);
    }

    // ================== 导出 ==================
    private void ExportXlsx()
    {
        if (_allResults.Count == 0) { SetStatus("无数据可导出", false); return; }
        var dlg = new SaveFileDialog { Filter = "Excel 文件|*.xlsx", FileName = $"Druid漏洞检测_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx" };
        if (dlg.ShowDialog() != true) return;

        ExcelPackage.License.SetNonCommercialOrganization("ToolHelper");
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("检测结果");

        var headers = new[] { "序号", "目标URL", "授权状态", "风险等级", "暴露页面数", "弱口令", "响应时间(ms)", "检测时间" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cells[1, i + 1].Value = headers[i];
            ws.Cells[1, i + 1].Style.Font.Bold = true;
            ws.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 220, 230, 241));
        }

        for (int r = 0; r < _allResults.Count; r++)
        {
            var row = _allResults[r];
            ws.Cells[r + 2, 1].Value = r + 1;
            ws.Cells[r + 2, 2].Value = row.TargetUrl;
            ws.Cells[r + 2, 3].Value = AuthLabel(row.AuthStatus);
            ws.Cells[r + 2, 4].Value = RiskLabel(row.Risk);
            ws.Cells[r + 2, 5].Value = row.Pages.Count(p => p.IsAccessible);
            ws.Cells[r + 2, 6].Value = row.WeakCredential ?? "";
            ws.Cells[r + 2, 7].Value = row.ResponseTimeMs;
            ws.Cells[r + 2, 8].Value = row.DetectTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        ws.Cells[ws.Dimension.Address].AutoFitColumns();
        pkg.SaveAs(new FileInfo(dlg.FileName));
        SetStatus($"已导出: {dlg.FileName}", true);
    }

    private async void ExportDocx()
    {
        if (_allResults.Count == 0) { SetStatus("无数据可导出", false); return; }
        var dlg = new SaveFileDialog { Filter = "Word 文档|*.docx", FileName = $"Druid漏洞检测报告_{DateTime.Now:yyyyMMdd_HHmmss}.docx" };
        if (dlg.ShowDialog() != true) return;

        var fileName = dlg.FileName;
        var now = DateTime.Now;
        var results = _allResults.ToList(); // 快照，避免导出期间集合被修改

        try
        {
            await Task.Run(() =>
            {
                using var doc = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Word.Document();
                var body = mainPart.Document.AppendChild(new Word.Body());

                // 标题
                var title = new Word.Paragraph(new Word.Run(new Word.RunProperties(new Word.Bold(), new Word.FontSize { Val = "48" }), new Word.Text("Druid 未授权漏洞检测报告")));
                body.Append(title);
                body.Append(new Word.Paragraph(new Word.Run(new Word.Text($"生成时间: {now:yyyy-MM-dd HH:mm:ss}"))));
                body.Append(new Word.Paragraph());

                // 概况
                body.Append(HeadPara("一、检测概况"));
                int crit = results.Count(r => r.Risk == RiskLevel.Critical);
                int high = results.Count(r => r.Risk == RiskLevel.High);
                int med = results.Count(r => r.Risk == RiskLevel.Medium);
                int safe = results.Count(r => r.Risk == RiskLevel.Safe);
                body.Append(TextPara($"扫描目标总数: {results.Count}"));
                body.Append(TextPara($"超危(需立即修复): {crit} 个"));
                body.Append(TextPara($"高危: {high} 个"));
                body.Append(TextPara($"中危: {med} 个"));
                body.Append(TextPara($"安全: {safe} 个"));
                body.Append(new Word.Paragraph());

                // 高危目标详情
                body.Append(HeadPara("二、风险目标详情"));
                foreach (var row in results.Where(r => r.Risk >= RiskLevel.Medium))
                {
                    body.Append(TextPara($"{row.TargetUrl}  授权: {AuthLabel(row.AuthStatus)}  风险: {RiskLabel(row.Risk)}"));
                    foreach (var pg in row.Pages.Where(p => p.IsAccessible))
                        body.Append(TextPara($"  - {pg.Path}  ({pg.RiskLevel})"));
                    if (!string.IsNullOrEmpty(row.WeakCredential))
                        body.Append(TextPara($"  - 弱口令: {row.WeakCredential}"));
                    body.Append(new Word.Paragraph());
                }

                // 整改方案
                body.Append(HeadPara("三、整改方案"));
                body.Append(TextPara("方案一（关闭监控页面，推荐）:"));
                body.Append(TextPara("  spring.datasource.druid.stat-view-servlet.enabled=false"));
                body.Append(new Word.Paragraph());
                body.Append(TextPara("方案二（保留监控 + 强密码 + IP白名单）:"));
                body.Append(TextPara("  spring.datasource.druid.stat-view-servlet.enabled=true"));
                body.Append(TextPara("  spring.datasource.druid.stat-view-servlet.login-username=monitor"));
                body.Append(TextPara("  spring.datasource.druid.stat-view-servlet.login-password=<强密码>"));
                body.Append(TextPara("  spring.datasource.druid.stat-view-servlet.allow=127.0.0.1"));

                mainPart.Document.Save();
            });

            SetStatus($"已导出: {fileName}", true);
        }
        catch (Exception ex)
        {
            SetStatus($"导出失败: {ex.Message}", false);
        }
    }

    private static Word.Paragraph HeadPara(string text) =>
        new Word.Paragraph(new Word.Run(new Word.RunProperties(new Word.Bold(), new Word.FontSize { Val = "28" }), new Word.Text(text)));
    private static Word.Paragraph TextPara(string text) =>
        new Word.Paragraph(new Word.Run(new Word.Text(text)));

    // ================== 标签 ==================
    private static string AuthLabel(DruidAuthStatus s) => s switch
    {
        DruidAuthStatus.Unauthorized => "未授权",
        DruidAuthStatus.AuthRequired => "需登录",
        DruidAuthStatus.WeakPassword => "弱口令",
        DruidAuthStatus.Blocked => "被拦截",
        DruidAuthStatus.NotEnabled => "未启用",
        DruidAuthStatus.Unreachable => "不可达",
        _ => "未知"
    };

    private static string RiskLabel(RiskLevel r) => r switch
    {
        RiskLevel.Critical => "超危",
        RiskLevel.High => "高危",
        RiskLevel.Medium => "中危",
        RiskLevel.Low => "低危",
        _ => "安全"
    };
}

// ================== 数据模型 ==================
public class DruidScanResult
{
    public int Index { get; set; }
    public string TargetUrl { get; set; } = "";
    public string NormalizedUrl { get; set; } = "";
    public DruidAuthStatus AuthStatus { get; set; }
    public RiskLevel Risk { get; set; }
    public int ResponseTimeMs { get; set; }
    public string? WeakCredential { get; set; }
    public List<DruidPageResult> Pages { get; set; } = new();
    public DateTime DetectTime { get; set; }

    // DataGrid 绑定
    public string AuthStatusText => AuthLabel(AuthStatus);
    public string RiskText => RiskLabel(Risk);
    public int ExposedCount => Pages.Count(p => p.IsAccessible);
    private static string AuthLabel(DruidAuthStatus s) => s switch
    {
        DruidAuthStatus.Unauthorized => "未授权",
        DruidAuthStatus.AuthRequired => "需登录",
        DruidAuthStatus.WeakPassword => "弱口令",
        DruidAuthStatus.Blocked => "被拦截",
        DruidAuthStatus.NotEnabled => "未启用",
        DruidAuthStatus.Unreachable => "不可达",
        _ => "未知"
    };
    private static string RiskLabel(RiskLevel r) => r switch
    {
        RiskLevel.Critical => "超危",
        RiskLevel.High => "高危",
        RiskLevel.Medium => "中危",
        RiskLevel.Low => "低危",
        _ => "安全"
    };
}

public class DruidPageResult
{
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public bool IsAccessible { get; set; }
    public string AccessibleText => IsAccessible ? "是" : "否";
    public string PageTitle { get; set; } = "";
    public string RiskLevel { get; set; } = "";
    public int ContentLength { get; set; }
}

public enum DruidAuthStatus { Unknown, NotEnabled, Unauthorized, AuthRequired, WeakPassword, Blocked, Unreachable }
public enum RiskLevel { Safe, Low, Medium, High, Critical }
