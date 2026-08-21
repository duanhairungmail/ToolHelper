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
/// SQL语句生成与格式化 — 整合 SQL 生成器（SELECT/INSERT/UPDATE/DELETE/CREATE 表单生成）与 SQL 语法格式化（关键字美化）
/// 顶部「SQL 生成 / SQL 格式化」按钮切换两个功能 Tab。
/// </summary>
public class SqlToolView : UserControl
{
    private enum SqlMode { Select, Insert, Update, Delete, Create }

    // ===== Tab 切换 =====
    private TabControl _tabControl = new();
    private Button _tabGenBtn = new();
    private Button _tabFmtBtn = new();
    private bool _built;

    // ===== Tab1: SQL 生成器字段 =====
    private SqlMode _mode = SqlMode.Select;
    private TextBlock _genStatusText = new();
    private TextBox _genOutputBox = new();
    private StackPanel _genFormPanel = new();

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

    // ===== Tab2: SQL 格式化字段 =====
    private TextBox _fmtInputBox = new();
    private TextBox _fmtOutputBox = new();
    private TextBlock _fmtStatusText = new();

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

    public SqlToolView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
        // 宿主内容区包裹了 ScrollViewer（无限高度约束），星号行会退化为按内容自然高度：
        // 钉住视图高度后 TabControl 获得有限高度，格式化 Tab 的输入/输出框才能按比例分配
        ViewportFitHelper.FitToViewport(this, 520);
    }

    // ========== UI 构建 ==========

    private void BuildUI()
    {
        var root = new DockPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        // 顶部固定内容（标题/描述/Tab 切换按钮行）
        var top = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Database, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  SQL语句生成与格式化", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(titleRow);
        top.Children.Add(new TextBlock { Text = "可视化表单生成 SQL + SQL 语法格式化，支持 SELECT / INSERT / UPDATE / DELETE / CREATE TABLE 五种模式", FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        // Tab 切换按钮行（隐藏 Tab 标题，由上方按钮切换）
        var tabBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _tabGenBtn = GenMakeButton("SQL 生成", () => SwitchTab(0), false, PackIconKind.Magnify);
        tabBtnRow.Children.Add(_tabGenBtn);
        _tabFmtBtn = GenMakeButton("SQL 格式化", () => SwitchTab(1), false, PackIconKind.FormatAlignLeft);
        tabBtnRow.Children.Add(_tabFmtBtn);
        top.Children.Add(tabBtnRow);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // TabControl（隐藏标题，由上方按钮切换；作为最后子元素填充剩余空间）
        _tabControl = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var tab1 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab1.Content = BuildGeneratorTab();
        _tabControl.Items.Add(tab1);
        var tab2 = new TabItem { Header = "", Visibility = Visibility.Collapsed };
        tab2.Content = BuildFormatTab();
        _tabControl.Items.Add(tab2);
        root.Children.Add(_tabControl);
        SwitchTab(0);

        Content = root;
    }

    /// <summary>切换 Tab 并同步按钮高亮（当前 Tab 按钮用 Raised 样式）</summary>
    private void SwitchTab(int index)
    {
        _tabControl.SelectedIndex = index;
        var active = TryFindResource("MaterialDesignRaisedButton") as Style;
        var inactive = TryFindResource("MaterialDesignOutlinedButton") as Style;
        _tabGenBtn.Style = index == 0 ? active : inactive;
        _tabFmtBtn.Style = index == 1 ? active : inactive;
    }

    // ================== Tab1: SQL 生成器 ==================

    private ScrollViewer BuildGeneratorTab()
    {
        var root = new StackPanel();

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
            var btn = GenMakeButton(name, () => SwitchMode(mode), false, icon);
            btn.Tag = mode;
            tabBar.Children.Add(btn);
        }
        root.Children.Add(tabBar);

        // 表单区（动态切换）
        _genFormPanel.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(_genFormPanel);

        // 按钮栏
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
        btnPanel.Children.Add(GenMakeButton("生成 SQL", Generate, true, PackIconKind.Play));
        btnPanel.Children.Add(GenMakeButton("复制", GenCopyResult, false, PackIconKind.ContentCopy));
        btnPanel.Children.Add(GenMakeButton("清空", ClearAll, false, PackIconKind.Eraser));
        _genStatusText.VerticalAlignment = VerticalAlignment.Center;
        _genStatusText.Margin = new Thickness(16, 0, 0, 0);
        _genStatusText.FontSize = 13;
        btnPanel.Children.Add(_genStatusText);
        root.Children.Add(btnPanel);

        // 输出区
        root.Children.Add(GenMakeLabel("生成的 SQL 语句"));
        var outputBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            CornerRadius = new CornerRadius(4),
            MinHeight = 160
        };
        _genOutputBox = new TextBox
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
        outputBorder.Child = _genOutputBox;
        root.Children.Add(outputBorder);

        SwitchMode(SqlMode.Select);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void SwitchMode(SqlMode mode)
    {
        _mode = mode;
        _genFormPanel.Children.Clear();
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
        row1.Children.Add(GenMakeLabel("表名:"));
        _selTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_selTable);
        row1.Children.Add(GenMakeLabel("LIMIT:"));
        _selLimit = MakeInput("如:100", 80);
        row1.Children.Add(_selLimit);
        panel.Children.Add(row1);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(GenMakeLabel("查询列:"));
        _selColumns = MakeInput("','分隔 '*'留空，如: id, name...", 500);
        row2.Children.Add(_selColumns);
        panel.Children.Add(row2);

        var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        row3.Children.Add(GenMakeLabel("排序字段:"));
        _selOrderBy = MakeInput("例如: id", 150);
        row3.Children.Add(_selOrderBy);
        row3.Children.Add(GenMakeLabel("方向:"));
        _selOrderDir = MakeCombo(new[] { "ASC", "DESC" }, 0);
        row3.Children.Add(_selOrderDir);
        panel.Children.Add(row3);

        var condHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        condHeader.Children.Add(GenMakeLabel("查询条件:"));
        condHeader.Children.Add(MakeSmallButton("添加条件", () => AddConditionRow(_selConditions)));
        panel.Children.Add(condHeader);
        _selConditions = new StackPanel();
        panel.Children.Add(_selConditions);

        _genFormPanel.Children.Add(panel);
        //AddConditionRow(_selConditions); // 默认一行
    }

    // ========== INSERT ==========

    private void BuildInsertForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(GenMakeLabel("表名:"));
        _insTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_insTable);
        panel.Children.Add(row1);

        var insHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        insHeader.Children.Add(GenMakeLabel("插入数据:"));
        insHeader.Children.Add(MakeSmallButton("添加行", () => AddInsertRow()));
        panel.Children.Add(insHeader);
        _insRows = new StackPanel();
        panel.Children.Add(_insRows);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(GenMakeLabel("可选子句:"));
        _insExtra = MakeInput("如 ON DUPLICATE KEY UPDATE，留空忽略", 500);
        row2.Children.Add(_insExtra);
        panel.Children.Add(row2);

        _genFormPanel.Children.Add(panel);
        AddInsertRow();
        AddInsertRow();
    }

    // ========== UPDATE ==========

    private void BuildUpdateForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(GenMakeLabel("表名:"));
        _updTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_updTable);
        panel.Children.Add(row1);

        var setHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        setHeader.Children.Add(GenMakeLabel("更新数据:"));
        setHeader.Children.Add(MakeSmallButton("添加SET", () => AddSetRow()));
        panel.Children.Add(setHeader);
        _updSets = new StackPanel();
        panel.Children.Add(_updSets);

        var whereHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        whereHeader.Children.Add(GenMakeLabel("更新条件:"));
        whereHeader.Children.Add(MakeSmallButton("添加条件", () => AddConditionRow(_updConditions)));
        panel.Children.Add(whereHeader);
        _updConditions = new StackPanel();
        panel.Children.Add(_updConditions);

        _genFormPanel.Children.Add(panel);
        AddSetRow();
        AddConditionRow(_updConditions);
    }

    // ========== DELETE ==========

    private void BuildDeleteForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(GenMakeLabel("表名:"));
        _delTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_delTable);
        panel.Children.Add(row1);

        var delHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        delHeader.Children.Add(GenMakeLabel("删除条件:"));
        delHeader.Children.Add(MakeSmallButton("添加条件", () => AddConditionRow(_delConditions)));
        panel.Children.Add(delHeader);
        _delConditions = new StackPanel();
        panel.Children.Add(_delConditions);

        _genFormPanel.Children.Add(panel);
        AddConditionRow(_delConditions);
    }

    // ========== CREATE TABLE ==========

    private void BuildCreateForm()
    {
        var panel = new StackPanel();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(GenMakeLabel("表名:"));
        _crtTable = MakeInput("例如: employees", 200);
        row1.Children.Add(_crtTable);
        row1.Children.Add(GenMakeLabel(" 注释:"));
        _crtComment = MakeInput("例如: 员工表", 150);
        row1.Children.Add(_crtComment);
        panel.Children.Add(row1);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(GenMakeLabel("引擎:"));
        _crtEngine = MakeCombo(new[] { "InnoDB", "MyISAM", "MEMORY" }, 0);
        row2.Children.Add(_crtEngine);
        row2.Children.Add(GenMakeLabel("  字符集:"));
        _crtCharset = MakeCombo(new[] { "utf8mb4", "utf8", "latin1" }, 0);
        row2.Children.Add(_crtCharset);
        panel.Children.Add(row2);

        var fieldHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        fieldHeader.Children.Add(GenMakeLabel("字段定义:"));
        fieldHeader.Children.Add(MakeSmallButton("添加字段", () => AddCreateField()));
        panel.Children.Add(fieldHeader);
        _crtFields = new StackPanel();
        panel.Children.Add(_crtFields);

        _genFormPanel.Children.Add(panel);
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
            _genOutputBox.Text = sql;
            GenSetStatus("SQL 生成成功", true);
        }
        catch (Exception ex)
        {
            GenSetStatus($"生成失败: {ex.Message}", false);
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

    // ========== 生成器辅助方法 ==========

    private void GenCopyResult()
    {
        if (string.IsNullOrEmpty(_genOutputBox.Text)) { GenSetStatus("输出为空，无法复制", false); return; }
        Clipboard.SetText(_genOutputBox.Text);
        GenSetStatus("已复制到剪贴板", true);
    }

    private void ClearAll()
    {
        _genOutputBox.Text = "";
        _genStatusText.Text = "";
        SwitchMode(_mode); // 重置表单
    }

    private void GenSetStatus(string msg, bool success)
    {
        _genStatusText.Text = msg;
        _genStatusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    private TextBlock GenMakeLabel(string text) => new()
    {
        Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>带边框的容器面板，用于包裹动态行区域（保留备用）</summary>
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

    private Button GenMakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
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

    // ================== Tab2: SQL 格式化 ==================

    private Grid BuildFormatTab()
    {
        var root = new Grid();

        // 定义行：Auto(输入标签) + 1*(输入框) + Auto(按钮栏) + Auto(输出标签) + 1*(输出框)
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 0: 输入区标签
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: 输入框（弹性）
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 2: 按钮栏
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // 3: 输出区标签
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4: 输出框（弹性）

        // 输入区标签 + 加载按钮
        var inputHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        inputHeader.Children.Add(FmtMakeLabel("输入 SQL"));
        var loadBtn = FmtMakeButton("加载", LoadFile, false, PackIconKind.FileUpload);
        loadBtn.Margin = new Thickness(8, 0, 0, 0);
        inputHeader.Children.Add(loadBtn);
        Grid.SetRow(inputHeader, 0);
        root.Children.Add(inputHeader);

        // 输入框（弹性高度）
        _fmtInputBox = MakeMultiLineBox("请输入SQL语句，或点击加载按钮打开sql文件...");
        Grid.SetRow(_fmtInputBox, 1);
        root.Children.Add(_fmtInputBox);

        // 按钮栏
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        btnPanel.Children.Add(MakePrimaryTextButton("格式化", Format));
        btnPanel.Children.Add(FmtMakeButton("复制输入", CopyInput, false, PackIconKind.ContentCopy));
        btnPanel.Children.Add(FmtMakeButton("复制输出", FmtCopyResult, false, PackIconKind.ContentCopy));
        btnPanel.Children.Add(MakeTextButton("复制输入输出", CopyInputOutput));
        btnPanel.Children.Add(FmtMakeButton("下载", DownloadResult, false, PackIconKind.Download));
        btnPanel.Children.Add(FmtMakeButton("清空输出", ClearOutput, false, PackIconKind.Eraser));
        btnPanel.Children.Add(MakeTextButton("清空输入", ClearInput));
        btnPanel.Children.Add(MakeTextButton("重置所有", ResetAll));
        _fmtStatusText.VerticalAlignment = VerticalAlignment.Center;
        _fmtStatusText.Margin = new Thickness(16, 0, 0, 0);
        _fmtStatusText.FontSize = 13;
        btnPanel.Children.Add(_fmtStatusText);
        Grid.SetRow(btnPanel, 2);
        root.Children.Add(btnPanel);

        // 输出区标签
        var outputLabel = new TextBlock { Text = "输出结果", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(outputLabel, 3);
        root.Children.Add(outputLabel);

        // 输出框（弹性高度）
        _fmtOutputBox = MakeMultiLineBox("", true);
        Grid.SetRow(_fmtOutputBox, 4);
        root.Children.Add(_fmtOutputBox);

        return root;
    }

    private TextBlock FmtMakeLabel(string text)
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
            // 输入框：合并后适配 Tab 高度，降低最小高度避免星号行溢出
            tb.MinHeight = 120;
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

    private Button FmtMakeButton(string text, Action handler, bool primary = false, PackIconKind icon = PackIconKind.Play)
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
                _fmtInputBox.Text = content;
                FmtSetStatus($"文件加载成功: {Path.GetFileName(dlg.FileName)}", true);
            }
        }
        catch (Exception ex) { FmtSetStatus($"加载文件失败: {ex.Message}", false); }
    }

    /// <summary>
    /// 读取输入框内容，调用 FormatSql 格式化后写入输出框
    /// </summary>
    private void Format()
    {
        try
        {
            var input = _fmtInputBox.Text.Trim();
            if (string.IsNullOrEmpty(input)) { FmtSetStatus("请先输入 SQL 代码", false); return; }
            _fmtOutputBox.Text = FormatSql(input);
            FmtSetStatus("格式化成功，关键字已大写", true);
        }
        catch (Exception ex) { FmtSetStatus($"格式化错误: {ex.Message}", false); }
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
        if (string.IsNullOrWhiteSpace(_fmtInputBox.Text)) { FmtSetStatus("输入为空，请先输入 SQL", false); return; }
        Clipboard.SetText(_fmtInputBox.Text);
        FmtSetStatus("已复制输入到剪贴板", true);
    }

    /// <summary>
    /// 将输出框的格式化结果复制到剪贴板；输出为空时跳过并提示
    /// </summary>
    private void FmtCopyResult()
    {
        if (string.IsNullOrWhiteSpace(_fmtOutputBox.Text)) { FmtSetStatus("输出为空，请先格式化", false); return; }
        Clipboard.SetText(_fmtOutputBox.Text);
        FmtSetStatus("已复制输出到剪贴板", true);
    }

    /// <summary>
    /// 将输入和输出合并（以 -- 输入 / -- 输出 为分隔注释）复制到剪贴板；输出为空时跳过并提示
    /// </summary>
    private void CopyInputOutput()
    {
        if (string.IsNullOrWhiteSpace(_fmtOutputBox.Text)) { FmtSetStatus("输出为空，请先格式化", false); return; }
        var input = _fmtInputBox.Text ?? "";
        var output = _fmtOutputBox.Text;
        var combined = $"-- 输入\n{input}\n\n-- 输出\n{output}";
        Clipboard.SetText(combined);
        FmtSetStatus("已复制输入和输出到剪贴板", true);
    }

    /// <summary>
    /// 清空输出框和状态栏，保留输入内容
    /// </summary>
    private void ClearOutput()
    {
        _fmtOutputBox.Text = "";
        _fmtStatusText.Text = "";
    }

    /// <summary>
    /// 清空输入框内容，保留输出和状态
    /// </summary>
    private void ClearInput()
    {
        _fmtInputBox.Text = "";
    }

    /// <summary>
    /// 将输出框的格式化结果保存为 .sql 文件到本地
    /// </summary>
    private void DownloadResult()
    {
        if (string.IsNullOrWhiteSpace(_fmtOutputBox.Text)) { FmtSetStatus("输出为空，请先格式化", false); return; }
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
            File.WriteAllText(dlg.FileName, _fmtOutputBox.Text, Encoding.UTF8);
            FmtSetStatus($"保存成功: {Path.GetFileName(dlg.FileName)}", true);
        }
        catch (Exception ex) { FmtSetStatus($"保存失败: {ex.Message}", false); }
    }

    /// <summary>
    /// 重置所有状态：清空输入框、输出框、状态栏
    /// </summary>
    private void ResetAll()
    {
        _fmtInputBox.Text = "";
        _fmtOutputBox.Text = "";
        _fmtStatusText.Text = "";
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

    private void FmtSetStatus(string msg, bool success)
    {
        _fmtStatusText.Text = msg;
        _fmtStatusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }
}
