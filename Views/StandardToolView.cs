using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace ToolHelper.Views;

/// <summary>
/// 通用工具页面基类，提供标准的双面板布局（输入 + 按钮 + 输出）
/// 延迟到 Loaded 事件时构建布局，确保资源可用
/// </summary>
public class StandardToolView : UserControl
{
    protected TextBox InputBox = new();
    protected TextBox OutputBox = new();
    protected TextBlock StatusText = new();
    protected StackPanel ButtonPanel = new();

    public string InputPlaceholder { get; set; } = "请输入内容...";
    public string OutputLabel { get; set; } = "输出结果";
    public bool ShowInput { get; set; } = true;
    public bool ShowOutput { get; set; } = true;

    /// <summary>标题图标，派生类可覆盖</summary>
    protected virtual PackIconKind TitleIcon => PackIconKind.CodeBraces;
    /// <summary>标题颜色，派生类可覆盖</summary>
    protected virtual Brush? TitleBrush => null;

    private readonly string _title;
    private readonly string _description;
    private readonly List<(string Text, PackIconKind Icon, Action Handler)> _buttons = new();
    private bool _layoutBuilt;

    public StandardToolView(string title, string description)
    {
        _title = title;
        _description = description;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 在派生类构造函数中调用，注册带图标的按钮
    /// </summary>
    protected void AddHandler(string text, Action handler, PackIconKind icon = PackIconKind.Play)
    {
        _buttons.Add((text, icon, handler));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutBuilt) return;
        _layoutBuilt = true;
        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new StackPanel();
        var titleBrush = TitleBrush ?? (TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue);

        // 标题行（图标 + 文字）
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = TitleIcon, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = $"  {_title}", FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(titleRow);

        root.Children.Add(new TextBlock
        {
            Text = _description, FontSize = 13, Opacity = 0.6,
            Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap
        });

        // 输入区
        if (ShowInput)
        {
            root.Children.Add(new TextBlock
            {
                Text = "输入", FontSize = 13, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var textBoxStyle = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
            InputBox.AcceptsReturn = true;
            InputBox.TextWrapping = TextWrapping.Wrap;
            InputBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            InputBox.MinHeight = 200;
            InputBox.VerticalContentAlignment = VerticalAlignment.Top;
            InputBox.FontFamily = new FontFamily("Microsoft YaHei");
            InputBox.FontSize = 13;
            if (textBoxStyle != null) InputBox.Style = textBoxStyle;
            HintAssist.SetHint(InputBox, InputPlaceholder);
            root.Children.Add(InputBox);
        }

        // 按钮栏
        ButtonPanel.Orientation = Orientation.Horizontal;
        ButtonPanel.Margin = new Thickness(0, 12, 0, 12);
        var raisedStyle = TryFindResource("MaterialDesignRaisedButton") as Style;
        var outlinedStyle = TryFindResource("MaterialDesignOutlinedButton") as Style;

        for (int i = 0; i < _buttons.Count; i++)
        {
            var (text, icon, handler) = _buttons[i];
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

            var btn = new Button
            {
                Content = sp,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (i == 0 ? raisedStyle : outlinedStyle) ?? outlinedStyle
            };
            btn.Click += (s, e) => handler();
            ButtonPanel.Children.Add(btn);
        }

        StatusText.VerticalAlignment = VerticalAlignment.Center;
        StatusText.Margin = new Thickness(16, 0, 0, 0);
        StatusText.FontSize = 13;
        ButtonPanel.Children.Add(StatusText);

        root.Children.Add(ButtonPanel);

        // 输出区
        if (ShowOutput)
        {
            // 输出标题行
            var outHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            outHeader.Children.Add(new PackIcon { Kind = PackIconKind.ConsoleLine, Width = 16, Height = 16, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
            outHeader.Children.Add(new TextBlock
            {
                Text = $"  {OutputLabel}", FontSize = 12, Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            root.Children.Add(outHeader);

            // 暗色输出面板
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                CornerRadius = new CornerRadius(4),
                MinHeight = 200
            };

            OutputBox.AcceptsReturn = true;
            OutputBox.TextWrapping = TextWrapping.Wrap;
            OutputBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            OutputBox.VerticalContentAlignment = VerticalAlignment.Top;
            OutputBox.FontFamily = new FontFamily("Consolas");
            OutputBox.FontSize = 13;
            OutputBox.IsReadOnly = true;
            OutputBox.Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180));
            OutputBox.Background = Brushes.Transparent;
            OutputBox.BorderThickness = new Thickness(0);
            OutputBox.CaretBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
            OutputBox.Padding = new Thickness(6, 4, 6, 4);
            border.Child = OutputBox;
            root.Children.Add(border);
        }

        Content = root;
    }

    protected void SetStatus(string msg, bool success)
    {
        StatusText.Text = msg;
        StatusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }
}
