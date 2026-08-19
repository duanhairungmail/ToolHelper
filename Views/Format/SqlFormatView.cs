using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace ToolHelper.Views.Format;

/// <summary>
/// SQL语法格式化工具 - 支持加载本地文件、格式化、复制、下载、清空
/// </summary>
public class SqlFormatView : UserControl
{
    private TextBox _inputBox = new();
    private TextBox _outputBox = new();
    private TextBlock _statusText = new();
    private bool _built;
    private const double MinViewHeight = 520; // 宿主视口过小时保持的视图最小高度（不足部分由宿主滚动条兜底）

    // SQL 关键字（大写）
    private static readonly string[] MajorKeywords = {
        "SELECT", "FROM", "WHERE", "AND", "OR", "INSERT", "INTO",
        "VALUES", "UPDATE", "SET", "DELETE", "JOIN", "LEFT JOIN", "RIGHT JOIN",
        "INNER JOIN", "OUTER JOIN", "CROSS JOIN", "FULL JOIN", "ON",
        "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET",
        "CREATE", "TABLE", "ALTER", "DROP", "INDEX", "VIEW", "PROCEDURE", "FUNCTION",
        "TRIGGER", "DATABASE", "SCHEMA", "UNION", "UNION ALL", "INTERSECT", "EXCEPT",
        "CASE", "WHEN", "THEN", "ELSE", "END", "BEGIN", "COMMIT", "ROLLBACK",
        "IF", "WHILE", "DECLARE", "EXEC", "EXECUTE", "RETURN",
        "WITH", "AS", "DISTINCT", "TOP", "IN", "NOT IN", "EXISTS", "NOT EXISTS",
        "BETWEEN", "LIKE", "IS NULL", "IS NOT NULL", "ASC", "DESC",
        "PRIMARY KEY", "FOREIGN KEY", "REFERENCES", "CONSTRAINT", "DEFAULT",
        "NOT NULL", "NULL", "AUTO_INCREMENT", "UNIQUE", "CHECK"
    };

    public SqlFormatView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) { Loaded -= OnLoaded; return; }
        _built = true;
        BuildUI();
        // 宿主内容区包裹了 ScrollViewer（无限高度约束），输入/输出框的星号行会退化为按内容自然高度：
        // SQL 文本一长两个框就无限拉长、按钮栏被顶出可视区，故把视图高度钉在宿主视口高度上
        ViewportFitHelper.FitToViewport(this, MinViewHeight);
        Loaded -= OnLoaded;
    }

    private void BuildUI()
    {
        var root = new Grid();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // 定义行：Auto(标题) + Auto(描述) + Auto(输入标签) + 1*(输入框) + Auto(按钮栏) + Auto(输出标签) + 1*(输出框)
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 0: 标题行
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 1: 描述行
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 2: 输入区标签
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: 输入框（弹性）
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 4: 按钮栏
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 5: 输出区标签
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 6: 输出框（弹性）

        // 标题行（图标 + 文字）
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Database, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  SQL语法格式化", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(titleRow, 0);
        root.Children.Add(titleRow);

        var descText = new TextBlock { Text = "格式化和美化 SQL 代码，支持加载本地 .sql 文件进行格式化", FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(descText, 1);
        root.Children.Add(descText);

        // 输入区标签 + 加载按钮
        var inputHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        inputHeader.Children.Add(MakeLabel("输入 SQL"));
        var loadBtn = MakeButton("加载", LoadFile, false, PackIconKind.FileUpload);
        loadBtn.Margin = new Thickness(8, 0, 0, 0);
        inputHeader.Children.Add(loadBtn);
        Grid.SetRow(inputHeader, 2);
        root.Children.Add(inputHeader);

        // 输入框（弹性高度）
        _inputBox = MakeMultiLineBox("请输入SQL语句，或点击加载按钮打开sql文件...");
        Grid.SetRow(_inputBox, 3);
        root.Children.Add(_inputBox);

        // 按钮栏
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        btnPanel.Children.Add(MakePrimaryTextButton("格式化", Format));
        btnPanel.Children.Add(MakeButton("复制输入", CopyInput, false, PackIconKind.ContentCopy));
        btnPanel.Children.Add(MakeButton("复制输出", CopyResult, false, PackIconKind.ContentCopy));
        btnPanel.Children.Add(MakeTextButton("复制输入输出", CopyInputOutput));
        btnPanel.Children.Add(MakeButton("下载", DownloadResult, false, PackIconKind.Download));
        btnPanel.Children.Add(MakeButton("清空输出", ClearOutput, false, PackIconKind.Eraser));
        btnPanel.Children.Add(MakeTextButton("清空输入", ClearInput));
        btnPanel.Children.Add(MakeTextButton("重置所有", ResetAll));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnPanel.Children.Add(_statusText);
        Grid.SetRow(btnPanel, 4);
        root.Children.Add(btnPanel);

        // 输出区标签
        var outputLabel = new TextBlock { Text = "输出结果", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(outputLabel, 5);
        root.Children.Add(outputLabel);

        // 输出框（弹性高度）
        _outputBox = MakeMultiLineBox("", true);
        Grid.SetRow(_outputBox, 6);
        root.Children.Add(_outputBox);

        Content = root;
    }

    // ---- UI 辅助方法 ----

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
    }

    private TextBox MakeMultiLineBox(string hint, bool readOnly = false)
    {
        var tb = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            FontSize = 13,
            IsReadOnly = readOnly
        };

        if (readOnly)
        {
            // 输出框：保持等宽字体，不需要 floating hint
            tb.MinHeight = 80;
            tb.FontFamily = new FontFamily("Consolas");
        }
        else
        {
            // 输入框：与 StandardToolView 一致
            tb.MinHeight = 200;
            tb.FontFamily = new FontFamily("Microsoft YaHei");
        }

        var style = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) tb.Style = style;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);

        if (!readOnly)
        {
            MaterialDesignThemes.Wpf.HintAssist.SetIsFloating(tb, true);
        }

        return tb;
    }

    private Button MakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new PackIcon { Kind = icon, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        var styleName = primary ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton";
        var btn = new Button
        {
            Content = sp,
            Margin = new Thickness(0, 0, 2, 0),
            Padding = new Thickness(6, 1, 6, 1),
            MinWidth = 0,
            MinHeight = 28,
            Height = 30,
            FontSize = 12,
            Style = TryFindResource(styleName) as Style
        };
        btn.Click += (s, e) => handler();
        return btn;
    }

    /// <summary>
    /// 创建纯文字按钮（无图标）
    /// </summary>
    private Button MakeTextButton(string text, Action handler)
    {
        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 2, 0),
            Padding = new Thickness(6, 1, 6, 1),
            MinWidth = 0,
            MinHeight = 28,
            Height = 30,
            FontSize = 12,
            Style = TryFindResource("MaterialDesignOutlinedButton") as Style
        };
        btn.Click += (s, e) => handler();
        return btn;
    }

    // ---- 功能实现 ----

    /// <summary>
    /// 打开文件对话框选择 .sql 文件，读取内容填入输入框
    /// </summary>
    private void LoadFile()
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择 SQL 文件",
                Filter = "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                var content = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                _inputBox.Text = content;
                SetStatus($"文件加载成功: {Path.GetFileName(dlg.FileName)}", true);
            }
        }
        catch (Exception ex) { SetStatus($"加载文件失败: {ex.Message}", false); }
    }

    /// <summary>
    /// 读取输入框内容，调用 FormatSql 格式化后写入输出框
    /// </summary>
    private void Format()
    {
        try
        {
            var input = _inputBox.Text.Trim();
            if (string.IsNullOrEmpty(input)) { SetStatus("请先输入 SQL 代码", false); return; }
            _outputBox.Text = FormatSql(input);
            SetStatus("格式化成功，关键字已大写", true);
        }
        catch (Exception ex) { SetStatus($"格式化错误: {ex.Message}", false); }
    }

    /// <summary>
    /// 创建主要文字按钮（无图标，填充风格）
    /// </summary>
    private Button MakePrimaryTextButton(string text, Action handler)
    {
        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 2, 0),
            Padding = new Thickness(6, 1, 6, 1),
            MinWidth = 0,
            MinHeight = 28,
            Height = 30,
            FontSize = 12,
            Style = TryFindResource("MaterialDesignRaisedButton") as Style
        };
        btn.Click += (s, e) => handler();
        return btn;
    }

    private void CopyInput()
    {
        if (string.IsNullOrWhiteSpace(_inputBox.Text)) { SetStatus("输入为空，请先输入 SQL", false); return; }
        Clipboard.SetText(_inputBox.Text);
        SetStatus("已复制输入到剪贴板", true);
    }

    /// <summary>
    /// 将输出框的格式化结果复制到剪贴板；输出为空时跳过并提示
    /// </summary>
    private void CopyResult()
    {
        if (string.IsNullOrWhiteSpace(_outputBox.Text)) { SetStatus("输出为空，请先格式化", false); return; }
        Clipboard.SetText(_outputBox.Text);
        SetStatus("已复制输出到剪贴板", true);
    }

    /// <summary>
    /// 将输入和输出合并（以 -- 输入 / -- 输出 为分隔注释）复制到剪贴板；输出为空时跳过并提示
    /// </summary>
    private void CopyInputOutput()
    {
        if (string.IsNullOrWhiteSpace(_outputBox.Text)) { SetStatus("输出为空，请先格式化", false); return; }
        var input = _inputBox.Text ?? "";
        var output = _outputBox.Text;
        var combined = $"-- 输入\n{input}\n\n-- 输出\n{output}";
        Clipboard.SetText(combined);
        SetStatus("已复制输入和输出到剪贴板", true);
    }

    /// <summary>
    /// 清空输出框和状态栏，保留输入内容
    /// </summary>
    private void ClearOutput()
    {
        _outputBox.Text = "";
        _statusText.Text = "";
    }

    /// <summary>
    /// 清空输入框内容，保留输出和状态
    /// </summary>
    private void ClearInput()
    {
        _inputBox.Text = "";
    }

    /// <summary>
    /// 将输出框的格式化结果保存为 .sql 文件到本地
    /// </summary>
    private void DownloadResult()
    {
        if (string.IsNullOrWhiteSpace(_outputBox.Text)) { SetStatus("输出为空，请先格式化", false); return; }
        var dlg = new SaveFileDialog
        {
            Title = "保存格式化结果",
            Filter = "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*",
            FileName = $"formatted_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            DefaultExt = ".sql"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _outputBox.Text, Encoding.UTF8);
            SetStatus($"保存成功: {Path.GetFileName(dlg.FileName)}", true);
        }
        catch (Exception ex) { SetStatus($"保存失败: {ex.Message}", false); }
    }

    /// <summary>
    /// 重置所有状态：清空输入框、输出框、状态栏
    /// </summary>
    private void ResetAll()
    {
        _inputBox.Text = "";
        _outputBox.Text = "";
        _statusText.Text = "";
    }

    // ---- SQL 格式化核心 ----

    private string FormatSql(string sql)
    {
        var sb = new StringBuilder();
        int indent = 0;
        const int IndentSize = 4;

        // 预处理：将连续空白合并为单空格，并去除首尾空白
        sql = Regex.Replace(sql, @"\s+", " ").Trim();

        // 词法分析：将 SQL 拆分为 token 列表（注释、字符串、运算符、括号、标识符等）
        var tokens = TokenizeSql(sql);
        bool newLine = true;  // 标记下一个 token 是否位于新行开头
        
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var upper = token.ToUpper().Trim();

            // 行注释 (--...)：原样输出整行注释，换行后补当前缩进以便后续内容对齐
            if (token.StartsWith("--"))
            {
                sb.AppendLine(token);
                sb.Append(new string(' ', indent * IndentSize));
                continue;
            }

            // 主断行关键字 (SELECT/FROM/WHERE/INSERT/UPDATE/DELETE/CREATE/ALTER/DROP/SET/VALUES/UNION/INTERSECT/EXCEPT)：
            //   1. 如果当前行已有内容，先换行
            //   2. 填充当前缩进 + 关键字大写
            //   3. SELECT 后缩进 +1 级；FROM 后缩进归零
            if (IsMajorBreakKeyword(upper))
            {
                if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
                sb.Append(new string(' ', indent * IndentSize));
                sb.Append(upper);
                newLine = true;

                if (upper == "SELECT") indent++;
                else if (upper == "FROM") indent = 0;
                continue;
            }

            // 子句关键字 (AND/OR/ON/JOIN 系列/GROUP BY/ORDER BY/HAVING/LIMIT/OFFSET/INTO/WHEN/THEN/ELSE/END)：
            //   1. 如果当前行已有内容，先换行
            //   2. 有缩进时用当前缩进，无缩进时用 1 级缩进
            if (IsSubClauseKeyword(upper))
            {
                if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
                sb.Append(new string(' ', (indent > 0 ? indent : 1) * IndentSize));
                sb.Append(upper);
                newLine = true;
                continue;
            }

            // 空白 token：已在预处理阶段合并为单空格，此处跳过不输出
            if (token.Trim().Length == 0) continue;

            // 行内空格策略：
            //   - 新行开头：用单空格引导内容（与关键字对齐）
            //   - 其它位置：前一个字符不是空格且不是 '(' 时补空格（逗号和 ')' 不触发前置空格）
            if (newLine)
            {
                sb.Append(" ");
                newLine = false;
            }
            else if (sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '(' && token != "," && token != ")")
            {
                sb.Append(" ");
            }

            // 字符串字面量（以 ' 开头）：原样追加，不做大写转换，跳过关键字匹配
            if (token.StartsWith("'"))
            {
                sb.Append(token);
                newLine = false;
                continue;
            }

            // 匹配 MajorKeywords 列表的关键字转大写输出；其它 token（表名/列名/别名等）原样保留
            if (IsKeyword(upper))
                sb.Append(upper);
            else
                sb.Append(token);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// SQL 词法分析器：逐字符扫描，按类型切分 token
    /// </summary>
    private List<string> TokenizeSql(string sql)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < sql.Length)
        {
            // 空白字符：普通空格/Tab 直接跳过（预处理已合并连续空白）；
            // 换行符 (\r/\n) 跳过并合并连续换行
            if (char.IsWhiteSpace(sql[i]))
            {
                if (sql[i] == '\n' || sql[i] == '\r')
                {
                    int next = i + 1;
                    while (next < sql.Length && (sql[next] == '\r' || sql[next] == '\n')) next++;
                    i = next;
                    continue;
                }
                i++;
                continue;
            }

            // 行注释 (--...)：提取从 -- 到行尾的全部内容作为一个 token
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                int end = sql.IndexOf('\n', i);
                if (end < 0) end = sql.Length;
                tokens.Add(sql[i..end].Trim());
                i = end;
                continue;
            }

            // 块注释 (/* ... */)：提取完整块注释为一个 token；未闭合时取到末尾
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                int end = sql.IndexOf("*/", i + 2);
                if (end < 0)
                {
                    tokens.Add(sql[i..]);
                    i = sql.Length;
                }
                else
                {
                    tokens.Add(sql[i..(end + 2)]);
                    i = end + 2;
                }
                continue;
            }

            // 字符串字面量 ('...')：提取含首尾引号的完整字符串；支持 '' 转义（两个连续单引号视为字面量）
            if (sql[i] == '\'')
            {
                int end = i + 1;
                while (end < sql.Length)
                {
                    if (sql[end] == '\'' && (end + 1 >= sql.Length || sql[end + 1] != '\'')) break;
                    if (sql[end] == '\'' && sql[end + 1] == '\'') { end += 2; continue; }
                    end++;
                }
                tokens.Add(sql[i..(end + 1)]);
                i = end + 1;
                continue;
            }

            // 括号：( 或 ) 各为独立 token
            if (sql[i] == '(' || sql[i] == ')')
            {
                tokens.Add(sql[i].ToString());
                i++;
                continue;
            }

            // 逗号：独立 token（用于参数分隔）
            if (sql[i] == ',')
            {
                tokens.Add(",");
                i++;
                continue;
            }

            // 运算符：= < > ! + - * / %，支持双字符 <=、>=、!=、<> 的识别
            if ("=<>!+-*/%".Contains(sql[i]))
            {
                int start = i;
                if (i + 1 < sql.Length && "<>!=".Contains(sql[i]) && sql[i + 1] == '=') i++;
                tokens.Add(sql[start..(i + 1)]);
                i++;
                continue;
            }

            // 普通 token：标识符、数字、关键字等，遇到空白或分隔符 ( ) , ; 及运算符时截止
            {
                int start = i;
                while (i < sql.Length && !char.IsWhiteSpace(sql[i]) && !"(),;=<>!+-*/%".Contains(sql[i]))
                    i++;
                tokens.Add(sql[start..i]);
            }
        }
        return tokens;
    }

    /// <summary>
    /// 主断行关键字集合：匹配时强制换行并调整缩进
    /// </summary>
    private static bool IsMajorBreakKeyword(string kw)
    {
        return kw is "SELECT" or "FROM" or "WHERE" or "INSERT" or "UPDATE" or "DELETE"
            or "CREATE" or "ALTER" or "DROP" or "SET" or "VALUES"
            or "UNION" or "UNION ALL" or "INTERSECT" or "EXCEPT";
    }

    /// <summary>
    /// 子句关键字集合：匹配时强制换行并应用缩进
    /// </summary>
    private static bool IsSubClauseKeyword(string kw)
    {
        return kw is "AND" or "OR" or "ON" or "JOIN" or "LEFT JOIN" or "RIGHT JOIN"
            or "INNER JOIN" or "OUTER JOIN" or "CROSS JOIN" or "FULL JOIN"
            or "GROUP BY" or "ORDER BY" or "HAVING" or "LIMIT" or "OFFSET"
            or "INTO" or "WHEN" or "THEN" or "ELSE" or "END";
    }

    /// <summary>
    /// 在 MajorKeywords 列表中查找，匹配的关键字将被转为大写
    /// </summary>
    private static bool IsKeyword(string kw)
    {
        return MajorKeywords.Contains(kw);
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }
}
