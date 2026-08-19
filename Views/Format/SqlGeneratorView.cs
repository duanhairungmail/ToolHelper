using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace ToolHelper.Views.Format;

/// <summary>
/// SQL 生成器 — 可视化表单驱动模板引擎，支持 SELECT/INSERT/UPDATE/DELETE/CREATE 五种模式
/// </summary>
public class SqlGeneratorView : UserControl
{
    private enum SqlMode { Select, Insert, Update, Delete, Create }

    private SqlMode _mode = SqlMode.Select;
    private TextBlock _statusText = new();
    private TextBox _outputBox = new();
    private StackPanel _formPanel = new();

    // SELECT 字段
    private TextBox _selTable = new();
    private TextBox _selColumns = new();
    private TextBox _selOrderBy = new();
    private ComboBox _selOrderDir = new();
    private TextBox _selLimit = new();
    private StackPanel _selConditions = new();

    // INSERT 字段
    private TextBox _insTable = new();
    private StackPanel _insRows = new();
    private TextBox _insExtra = new();

    // UPDATE 字段
    private TextBox _updTable = new();
    private StackPanel _updSets = new();
    private StackPanel _updConditions = new();

    // DELETE 字段
    private TextBox _delTable = new();
    private StackPanel _delConditions = new();

    // CREATE 字段
    private TextBox _crtTable = new();
    private StackPanel _crtFields = new();
    private TextBox _crtComment = new();
    private ComboBox _crtEngine = new();
    private ComboBox _crtCharset = new();

    private bool _built;

    public SqlGeneratorView()
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
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Database, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  SQL 生成器", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(titleRow);
        root.Children.Add(new TextBlock { Text = "可视化表单驱动 SQL 生成，支持 SELECT / INSERT / UPDATE / DELETE / CREATE TABLE 五种模式", FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        // 模式切换按钮
        var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var modes = new (string Name, SqlMode Mode, PackIconKind Icon)[]
        {
            ("SELECT 查询", SqlMode.Select, PackIconKind.Magnify),
            ("INSERT 插入", SqlMode.Insert, PackIconKind.DatabaseImport),
            ("UPDATE 更新", SqlMode.Update, PackIconKind.Pencil),
            ("DELETE 删除", SqlMode.Delete, PackIconKind.Delete),
            ("CREATE 建表", SqlMode.Create, PackIconKind.Table),
        };
        foreach (var (name, mode, icon) in modes)
        {
            var btn = MakeButton(name, () => SwitchMode(mode), false, icon);
            btn.Tag = mode;
            tabBar.Children.Add(btn);
        }
        root.Children.Add(tabBar);

        // 表单区（动态切换）
        _formPanel.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(_formPanel);

        // 按钮栏
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
        btnPanel.Children.Add(MakeButton("生成 SQL", Generate, true, PackIconKind.Play));
        btnPanel.Children.Add(MakeButton("复制", CopyResult, false, PackIconKind.ContentCopy));
        btnPanel.Children.Add(MakeButton("清空", ClearAll, false, PackIconKind.Eraser));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnPanel.Children.Add(_statusText);
        root.Children.Add(btnPanel);

        // 输出区
        root.Children.Add(MakeLabel("生成的 SQL 语句"));
        var outputBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            CornerRadius = new CornerRadius(4),
            MinHeight = 160
        };
        _outputBox = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            FontFamily = new FontFamily("Consolas"), FontSize = 13,
            IsReadOnly = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Brushes.White,
            Padding = new Thickness(6, 4, 6, 4)
        };
        outputBorder.Child = _outputBox;
        root.Children.Add(outputBorder);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SwitchMode(SqlMode.Select);
    }

    private void SwitchMode(SqlMode mode)
    {
        _mode = mode;
        _formPanel.Children.Clear();
        switch (mode)
        {
            case SqlMode.Select: BuildSelectForm(); break;
            case SqlMode.Insert: BuildInsertForm(); break;
            case SqlMode.Update: BuildUpdateForm(); break;
            case SqlMode.Delete: BuildDeleteForm(); break;
            case SqlMode.Create: BuildCreateForm(); break;
        }
    }

    // ========== SELECT ==========

    private void BuildSelectForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(MakeLabel("表名:"));
        _selTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_selTable);
        row1.Children.Add(MakeLabel("LIMIT:"));
        _selLimit = MakeInput("如:100", 80);
        row1.Children.Add(_selLimit);
        panel.Children.Add(row1);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(MakeLabel("查询列:"));
        _selColumns = MakeInput("','分隔 '*'留空，如: id, name...", 500);
        row2.Children.Add(_selColumns);
        panel.Children.Add(row2);

        var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        row3.Children.Add(MakeLabel("排序字段:"));
        _selOrderBy = MakeInput("例如: id", 150);
        row3.Children.Add(_selOrderBy);
        row3.Children.Add(MakeLabel("方向:"));
        _selOrderDir = MakeCombo(new[] { "ASC", "DESC" }, 0);
        row3.Children.Add(_selOrderDir);
        panel.Children.Add(row3);

        var condHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        condHeader.Children.Add(MakeLabel("查询条件:"));
        condHeader.Children.Add(MakeSmallButton("添加条件", () => AddConditionRow(_selConditions)));
        panel.Children.Add(condHeader);
        _selConditions = new StackPanel();
        panel.Children.Add(_selConditions);

        _formPanel.Children.Add(panel);
        //AddConditionRow(_selConditions); // 默认一行
    }

    // ========== INSERT ==========

    private void BuildInsertForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(MakeLabel("表名:"));
        _insTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_insTable);
        panel.Children.Add(row1);

        var insHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        insHeader.Children.Add(MakeLabel("插入数据:"));
        insHeader.Children.Add(MakeSmallButton("添加行", () => AddInsertRow()));
        panel.Children.Add(insHeader);
        _insRows = new StackPanel();
        panel.Children.Add(_insRows);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(MakeLabel("可选子句:"));
        _insExtra = MakeInput("如 ON DUPLICATE KEY UPDATE，留空忽略", 500);
        row2.Children.Add(_insExtra);
        panel.Children.Add(row2);

        _formPanel.Children.Add(panel);
        AddInsertRow();
        AddInsertRow();
    }

    // ========== UPDATE ==========

    private void BuildUpdateForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(MakeLabel("表名:"));
        _updTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_updTable);
        panel.Children.Add(row1);

        var setHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        setHeader.Children.Add(MakeLabel("更新数据:"));
        setHeader.Children.Add(MakeSmallButton("添加SET", () => AddSetRow()));
        panel.Children.Add(setHeader);
        _updSets = new StackPanel();
        panel.Children.Add(_updSets);

        var whereHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        whereHeader.Children.Add(MakeLabel("更新条件:"));
        whereHeader.Children.Add(MakeSmallButton("添加条件", () => AddConditionRow(_updConditions)));
        panel.Children.Add(whereHeader);
        _updConditions = new StackPanel();
        panel.Children.Add(_updConditions);

        _formPanel.Children.Add(panel);
        AddSetRow();
        AddConditionRow(_updConditions);
    }

    // ========== DELETE ==========

    private void BuildDeleteForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(MakeLabel("表名:"));
        _delTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_delTable);
        panel.Children.Add(row1);

        var delHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        delHeader.Children.Add(MakeLabel("删除条件:"));
        delHeader.Children.Add(MakeSmallButton("添加条件", () => AddConditionRow(_delConditions)));
        panel.Children.Add(delHeader);
        _delConditions = new StackPanel();
        panel.Children.Add(_delConditions);

        _formPanel.Children.Add(panel);
        AddConditionRow(_delConditions);
    }

    // ========== CREATE TABLE ==========

    private void BuildCreateForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(MakeLabel("表名:"));
        _crtTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_crtTable);
        row1.Children.Add(MakeLabel(" 注释:"));
        _crtComment = MakeInput("例如: 员工表", 150);
        row1.Children.Add(_crtComment);
        panel.Children.Add(row1);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(MakeLabel("引擎:"));
        _crtEngine = MakeCombo(new[] { "InnoDB", "MyISAM", "MEMORY" }, 0);
        row2.Children.Add(_crtEngine);
        row2.Children.Add(MakeLabel("  字符集:"));
        _crtCharset = MakeCombo(new[] { "utf8mb4", "utf8", "latin1" }, 0);
        row2.Children.Add(_crtCharset);
        panel.Children.Add(row2);

        var fieldHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        fieldHeader.Children.Add(MakeLabel("字段定义:"));
        fieldHeader.Children.Add(MakeSmallButton("添加字段", () => AddCreateField()));
        panel.Children.Add(fieldHeader);
        _crtFields = new StackPanel();
        panel.Children.Add(_crtFields);

        _formPanel.Children.Add(panel);
        AddCreateField();
        AddCreateField();
    }

    // ========== 动态行管理 ==========

    private static readonly string[] ConditionOps = { "= 等于", "!= 不等于", "> 大于", "< 小于", ">= 大于等于", "<= 小于等于", "LIKE 包含", "NOT LIKE 不包含" };

    private void AddConditionRow(StackPanel container)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var field = MakeInput("字段名", 120);
        var op = MakeCombo(ConditionOps, 0);
        var val = MakeInput("值", 150);
        var delBtn = MakeSmallButton("✕", () => container.Children.Remove(row));
        row.Children.Add(field);
        row.Children.Add(op);
        row.Children.Add(val);
        row.Children.Add(delBtn);
        container.Children.Add(row);
    }

    private void AddInsertRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var col = MakeInput("列名", 150);
        var val = MakeInput("值", 250);
        var delBtn = MakeSmallButton("✕", () => _insRows.Children.Remove(row));
        row.Children.Add(col);
        row.Children.Add(new TextBlock { Text = " = ", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        row.Children.Add(val);
        row.Children.Add(delBtn);
        _insRows.Children.Add(row);
    }

    private void AddSetRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var col = MakeInput("列名", 150);
        var val = MakeInput("新值", 250);
        var delBtn = MakeSmallButton("✕", () => _updSets.Children.Remove(row));
        row.Children.Add(col);
        row.Children.Add(new TextBlock { Text = " = ", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        row.Children.Add(val);
        row.Children.Add(delBtn);
        _updSets.Children.Add(row);
    }

    private void AddCreateField()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var name = MakeInput("字段名", 120);
        var type = MakeCombo(new[] { "INT", "VARCHAR(255)", "VARCHAR(50)", "TEXT", "DATE", "DATETIME", "DECIMAL(10,2)", "BIGINT", "TINYINT", "BOOLEAN" }, 0);
        var constraint = MakeInput("如 PRIMARY KEY, NOT NULL", 200);
        var delBtn = MakeSmallButton("✕", () => _crtFields.Children.Remove(row));
        row.Children.Add(name);
        row.Children.Add(type);
        row.Children.Add(constraint);
        row.Children.Add(delBtn);
        _crtFields.Children.Add(row);
    }

    // ========== SQL 生成 ==========

    private void Generate()
    {
        try
        {
            var sql = _mode switch
            {
                SqlMode.Select => BuildSelect(),
                SqlMode.Insert => BuildInsert(),
                SqlMode.Update => BuildUpdate(),
                SqlMode.Delete => BuildDelete(),
                SqlMode.Create => BuildCreate(),
                _ => ""
            };
            _outputBox.Text = sql;
            SetStatus("SQL 生成成功", true);
        }
        catch (Exception ex)
        {
            SetStatus($"生成失败: {ex.Message}", false);
        }
    }

    private string BuildSelect()
    {
        var table = _selTable.Text.Trim();
        if (string.IsNullOrEmpty(table)) throw new Exception("请填写表名");

        var cols = _selColumns.Text.Trim();
        var selectPart = string.IsNullOrEmpty(cols) ? "*" : cols;
        var sql = $"SELECT {selectPart}\nFROM {table}";

        var where = BuildWhereClause(_selConditions);
        if (!string.IsNullOrEmpty(where)) sql += $"\nWHERE {where}";

        var orderBy = _selOrderBy.Text.Trim();
        if (!string.IsNullOrEmpty(orderBy))
        {
            var dir = _selOrderDir.SelectedItem?.ToString() ?? "ASC";
            sql += $"\nORDER BY {orderBy} {dir}";
        }

        var limit = _selLimit.Text.Trim();
        if (!string.IsNullOrEmpty(limit) && int.TryParse(limit, out var n) && n > 0)
            sql += $"\nLIMIT {n}";

        return sql + ";";
    }

    private string BuildInsert()
    {
        var table = _insTable.Text.Trim();
        if (string.IsNullOrEmpty(table)) throw new Exception("请填写表名");

        var columns = new List<string>();
        var values = new List<string>();

        foreach (StackPanel row in _insRows.Children)
        {
            var controls = row.Children;
            var col = ((TextBox)controls[0]).Text.Trim();
            var val = ((TextBox)controls[2]).Text.Trim();
            if (!string.IsNullOrEmpty(col))
            {
                columns.Add(col);
                values.Add(QuoteValue(val));
            }
        }

        if (columns.Count == 0) throw new Exception("请至少添加一组插入数据");

        var sql = $"INSERT INTO {table} ({string.Join(", ", columns)})\nVALUES ({string.Join(", ", values)})";

        var extra = _insExtra.Text.Trim();
        if (!string.IsNullOrEmpty(extra)) sql += $"\n{extra}";

        return sql + ";";
    }

    private string BuildUpdate()
    {
        var table = _updTable.Text.Trim();
        if (string.IsNullOrEmpty(table)) throw new Exception("请填写表名");

        var sets = new List<string>();
        foreach (StackPanel row in _updSets.Children)
        {
            var controls = row.Children;
            var col = ((TextBox)controls[0]).Text.Trim();
            var val = ((TextBox)controls[2]).Text.Trim();
            if (!string.IsNullOrEmpty(col))
                sets.Add($"{col} = {QuoteValue(val)}");
        }
        if (sets.Count == 0) throw new Exception("请至少添加一组 SET 数据");

        var where = BuildWhereClause(_updConditions);
        if (string.IsNullOrEmpty(where)) throw new Exception("UPDATE 必须填写 WHERE 条件，防止全表误更新");

        return $"UPDATE {table}\nSET {string.Join(", ", sets)}\nWHERE {where};";
    }

    private string BuildDelete()
    {
        var table = _delTable.Text.Trim();
        if (string.IsNullOrEmpty(table)) throw new Exception("请填写表名");

        var where = BuildWhereClause(_delConditions);
        if (string.IsNullOrEmpty(where)) throw new Exception("DELETE 必须填写 WHERE 条件，防止全表误删除");

        return $"DELETE FROM {table}\nWHERE {where};";
    }

    private string BuildCreate()
    {
        var table = _crtTable.Text.Trim();
        if (string.IsNullOrEmpty(table)) throw new Exception("请填写表名");

        var fields = new List<string>();
        foreach (StackPanel row in _crtFields.Children)
        {
            var controls = row.Children;
            var name = ((TextBox)controls[0]).Text.Trim();
            var type = ((ComboBox)controls[1]).SelectedItem?.ToString() ?? "INT";
            var constraint = ((TextBox)controls[2]).Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                var def = $"    {name} {type}";
                if (!string.IsNullOrEmpty(constraint)) def += $" {constraint}";
                fields.Add(def);
            }
        }
        if (fields.Count == 0) throw new Exception("请至少添加一个字段");

        var engine = _crtEngine.SelectedItem?.ToString() ?? "InnoDB";
        var charset = _crtCharset.SelectedItem?.ToString() ?? "utf8mb4";
        var comment = _crtComment.Text.Trim();

        var sql = $"CREATE TABLE {table} (\n{string.Join(",\n", fields)}\n) ENGINE={engine} DEFAULT CHARSET={charset}";
        if (!string.IsNullOrEmpty(comment)) sql += $" COMMENT='{EscapeSingleQuote(comment)}'";
        return sql + ";";
    }

    // ========== WHERE 条件构建 ==========

    private string BuildWhereClause(StackPanel conditionsPanel)
    {
        var parts = new List<string>();
        foreach (StackPanel row in conditionsPanel.Children)
        {
            var controls = row.Children;
            var field = ((TextBox)controls[0]).Text.Trim();
            var opText = ((ComboBox)controls[1]).SelectedItem?.ToString() ?? "= 等于";
            var val = ((TextBox)controls[2]).Text.Trim();
            if (string.IsNullOrEmpty(field)) continue;

            var op = ParseOp(opText);
            parts.Add(FormatCondition(field, op, val));
        }
        return parts.Count > 0 ? string.Join(" AND ", parts) : "";
    }

    private static string ParseOp(string display) => display switch
    {
        "= 等于" => "=",
        "!= 不等于" => "!=",
        "> 大于" => ">",
        "< 小于" => "<",
        ">= 大于等于" => ">=",
        "<= 小于等于" => "<=",
        "LIKE 包含" => "LIKE",
        "NOT LIKE 不包含" => "NOT LIKE",
        _ => "="
    };

    private static string FormatCondition(string field, string op, string value)
    {
        if (op == "LIKE") return $"{field} LIKE '%{EscapeSingleQuote(value)}%'";
        if (op == "NOT LIKE") return $"{field} NOT LIKE '%{EscapeSingleQuote(value)}%'";
        return $"{field} {op} {QuoteValue(value)}";
    }

    /// <summary>纯数字不加引号，其余加单引号</summary>
    private static string QuoteValue(string val)
    {
        if (string.IsNullOrEmpty(val)) return "NULL";
        if (double.TryParse(val, out _)) return val;
        return $"'{EscapeSingleQuote(val)}'";
    }

    private static string EscapeSingleQuote(string s) => s.Replace("'", "''");

    // ========== 辅助方法 ==========

    private void CopyResult()
    {
        if (string.IsNullOrEmpty(_outputBox.Text)) { SetStatus("输出为空，无法复制", false); return; }
        Clipboard.SetText(_outputBox.Text);
        SetStatus("已复制到剪贴板", true);
    }

    private void ClearAll()
    {
        _outputBox.Text = "";
        _statusText.Text = "";
        SwitchMode(_mode); // 重置表单
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>带边框的容器面板，用于包裹动态行区域</summary>
    private Border MakeBorderedPanel(out StackPanel inner)
    {
        inner = new StackPanel { Margin = new Thickness(4) };
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 2, 0, 4),
            Child = inner
        };
    }

    private TextBox MakeInput(string hint, double width)
    {
        var tb = new TextBox
        {
            FontSize = 13, Width = width,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var style = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) tb.Style = style;
        HintAssist.SetHint(tb, hint);
        HintAssist.SetIsFloating(tb, true);
        return tb;
    }

    private ComboBox MakeCombo(string[] items, int defaultIndex)
    {
        var cb = new ComboBox
        {
            FontSize = 13, Margin = new Thickness(0, 0, 8, 0), MinWidth = 80,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var style = TryFindResource("MaterialDesignOutlinedComboBox") as Style;
        if (style != null) cb.Style = style;
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
        var btn = new Button { Content = sp, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource(styleName) as Style };
        btn.Click += (s, e) => handler();
        return btn;
    }

    private Button MakeSmallButton(string text, Action handler)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = "+ ", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)) });
        sp.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)) });
        var btn = new Button
        {
            Content = sp,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(4, 1, 4, 1),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (s, e) => handler();
        return btn;
    }

}
