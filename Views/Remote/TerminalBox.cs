using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace ToolHelper.Views.Remote;

/// <summary>
/// 支持 ANSI 转义序列的终端控件，基于 RichTextBox 实现彩色输出。
/// 支持标准 8/16 色、256 色、24-bit RGB 颜色，以及常用终端控制序列。
/// </summary>
public class TerminalBox : RichTextBox
{
    // ===== ANSI 解析状态 =====
    private enum ParseState { Normal, Escape, CsiParam, OscString }
    private ParseState _state;
    private StringBuilder _escParams = new();
    private char _escIntermediate;

    // ===== 当前文本属性 =====
    private int _fgIndex = 7;    // 默认前景 (白/浅灰)
    private int _bgIndex = 0;    // 默认背景 (黑)
    private bool _bold;
    private bool _useRgbFg;
    private bool _useRgbBg;
    private Color _rgbFg;
    private Color _rgbBg;

    // ===== 文档结构 =====
    private FlowDocument _doc;
    private Paragraph _para;
    private StringBuilder _textBuf = new();
    private readonly Color[] _palette = new Color[256];

    private static readonly Color ColorDefault = Color.FromRgb(204, 204, 204);
    private static readonly Color BgDefault = Color.FromRgb(30, 30, 30);

    public TerminalBox()
    {
        IsReadOnly = true;
        AcceptsReturn = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Background = new SolidColorBrush(BgDefault);
        Foreground = new SolidColorBrush(ColorDefault);
        CaretBrush = Brushes.White;
        FontFamily = new FontFamily("Consolas");
        FontSize = 14;
        VerticalContentAlignment = VerticalAlignment.Top;
        BorderThickness = new Thickness(0);
        IsDocumentEnabled = false;
        SpellCheck.IsEnabled = false;
        Padding = new Thickness(4);

        _doc = new FlowDocument
        {
            Background = new SolidColorBrush(BgDefault),
            Foreground = new SolidColorBrush(ColorDefault),
            PageWidth = 100000,
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = FontSize
        };
        Document = _doc;

        _para = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = double.NaN,
            TextIndent = 0
        };
        _doc.Blocks.Add(_para);

        InitPalette();
        ResetAttributes();
    }

    #region 调色板初始化

    private void InitPalette()
    {
        // 标准 16 色 ANSI
        var std = new uint[]
        {
            0x000000, 0xCC0000, 0x00CC00, 0xCCCC00,
            0x0000CC, 0xCC00CC, 0x00CCCC, 0xCCCCCC,
            0x808080, 0xFF0000, 0x00FF00, 0xFFFF00,
            0x5555FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF
        };
        for (int i = 0; i < 16; i++)
            _palette[i] = RGB(std[i]);

        // 216 色立方体 (索引 16-231)
        int[] levels = { 0, 95, 135, 175, 215, 255 };
        for (int r = 0; r < 6; r++)
            for (int g = 0; g < 6; g++)
                for (int b = 0; b < 6; b++)
                    _palette[16 + r * 36 + g * 6 + b] =
                        Color.FromRgb((byte)levels[r], (byte)levels[g], (byte)levels[b]);

        // 24 级灰度 (索引 232-255)
        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            _palette[232 + i] = Color.FromRgb(v, v, v);
        }
    }

    private static Color RGB(uint hex) =>
        Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8 & 0xFF), (byte)(hex & 0xFF));

    #endregion

    #region 文本追加（核心解析）

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (char c in text)
        {
            switch (_state)
            {
                case ParseState.Normal:
                    ProcessNormal(c);
                    break;

                case ParseState.Escape:
                    if (c == '[') { _state = ParseState.CsiParam; _escParams.Clear(); _escIntermediate = '\0'; }
                    else if (c == '(' || c == ')') { _escIntermediate = c; } // 字符集选择，忽略
                    else if (c == ']') { _state = ParseState.OscString; _escParams.Clear(); }
                    else if (c == 'c') { ClearScreen(); _state = ParseState.Normal; } // 完全重置
                    else if (c == '7' || c == '8') { _state = ParseState.Normal; } // 保存/恢复光标
                    else { _state = ParseState.Normal; }
                    break;

                case ParseState.CsiParam:
                    if (c >= '0' && c <= '9' || c == ';' || c == '?' || c == '!')
                        _escParams.Append(c);
                    else if (c >= 0x20 && c <= 0x2F)
                        _escIntermediate = c; // 中间字节
                    else
                    {
                        Flush();
                        ProcessCsi(c);
                        _state = ParseState.Normal;
                    }
                    break;

                case ParseState.OscString:
                    if (c == '\a' || c == '\x07') { _state = ParseState.Normal; }
                    else if (c == '\x1b') { _state = ParseState.Escape; }
                    // 其余 OSC 字符忽略
                    break;
            }
        }
        Flush();
    }

    private void ProcessNormal(char c)
    {
        if (c == '\x1b')
        {
            Flush();
            _state = ParseState.Escape;
        }
        else if (c == '\n')
        {
            Flush();
            _para.Inlines.Add(new LineBreak());
        }
        else if (c == '\r')
        {
            // 忽略 \r（\r\n 中的 \r），FlowDocument 不需要
        }
        else if (c == '\t')
        {
            _textBuf.Append("        "); // 8 空格 tab
        }
        else if (c == '\b')
        {
            // 退格：删除最后一个字符
            if (_textBuf.Length > 0) _textBuf.Length--;
        }
        else if (c == '\a')
        {
            // Bell，忽略
        }
        else if (c >= 0x20 || c == '\t')
        {
            _textBuf.Append(c);
        }
    }

    #endregion

    #region 刷新缓冲区 & 创建 Run

    private void Flush()
    {
        if (_textBuf.Length == 0) return;

        var run = new Run(_textBuf.ToString())
        {
            Foreground = new SolidColorBrush(GetFgColor())
        };
        if (_bold) run.FontWeight = FontWeights.Bold;

        _para.Inlines.Add(run);
        _textBuf.Clear();
    }

    private Color GetFgColor()
    {
        if (_useRgbFg) return _rgbFg;
        int idx = _fgIndex;
        if (_bold && idx < 8) idx += 8; // Bold 使标准色变亮
        return (idx >= 0 && idx < 256) ? _palette[idx] : ColorDefault;
    }

    private Color GetBgColor()
    {
        if (_useRgbBg) return _rgbBg;
        return (_bgIndex >= 0 && _bgIndex < 256) ? _palette[_bgIndex] : BgDefault;
    }

    #endregion

    #region CSI 命令处理

    private void ProcessCsi(char cmd)
    {
        string p = _escParams.ToString().TrimStart('?').TrimStart('!');

        switch (cmd)
        {
            case 'm': // SGR - 设置图形 rendition
                ProcessSgr(p);
                break;
            case 'J': // 清除屏幕
                if (p == "2" || p == "3") ClearScreen();
                break;
            case 'K': // 清除行（简化：忽略）
                break;
            case 'H': // 光标移动（简化：忽略，仅加换行）
            case 'f':
                break;
            case 'A': case 'B': case 'C': case 'D': // 光标上下左右
                break;
            case 's': case 'u': // 保存/恢复光标
                break;
            case 'l': case 'h': // 模式设置（如光标显示/隐藏）
                break;
            case 'r': // 滚动区域
                break;
            case 't': // 窗口操作
                break;
        }
    }

    private void ProcessSgr(string paramStr)
    {
        if (string.IsNullOrEmpty(paramStr))
        {
            ResetAttributes();
            return;
        }

        var parts = paramStr.Split(';');
        var nums = new List<int>();
        foreach (var part in parts)
            nums.Add(int.TryParse(part, out int v) ? v : 0);

        for (int i = 0; i < nums.Count; i++)
        {
            int code = nums[i];
            switch (code)
            {
                case 0: ResetAttributes(); break;
                case 1: _bold = true; break;
                case 2: /* dim */ break;
                case 3: /* italic */ break;
                case 4: /* underline */ break;
                case 5: case 6: /* blink */ break;
                case 7: /* reverse - swap fg/bg */
                    (_fgIndex, _bgIndex) = (_bgIndex, _fgIndex);
                    (_useRgbFg, _useRgbBg) = (_useRgbBg, _useRgbFg);
                    (_rgbFg, _rgbBg) = (_rgbBg, _rgbFg);
                    break;
                case 8: /* hidden */ break;
                case 9: /* strikethrough */ break;
                case 21: case 22: _bold = false; break;
                case 23: case 24: case 25: case 26: case 27: case 28: case 29: break;

                // 标准前景色
                case >= 30 and <= 37: _fgIndex = code - 30; _useRgbFg = false; break;
                case 38: // 扩展前景色
                    i = ParseExtendedColor(nums, i, out var fc, out var fi, out var useRgb);
                    if (useRgb) { _useRgbFg = true; _rgbFg = fc; } else { _fgIndex = fi; _useRgbFg = false; }
                    break;
                case 39: _fgIndex = 7; _useRgbFg = false; break; // 默认前景

                // 标准背景色
                case >= 40 and <= 47: _bgIndex = code - 40; _useRgbBg = false; break;
                case 48: // 扩展背景色
                    i = ParseExtendedColor(nums, i, out var bc, out var bi, out var useBgRgb);
                    if (useBgRgb) { _useRgbBg = true; _rgbBg = bc; } else { _bgIndex = bi; _useRgbBg = false; }
                    break;
                case 49: _bgIndex = 0; _useRgbBg = false; break; // 默认背景

                // 明亮前景色
                case >= 90 and <= 97: _fgIndex = code - 90 + 8; _useRgbFg = false; break;
                // 明亮背景色
                case >= 100 and <= 107: _bgIndex = code - 100 + 8; _useRgbBg = false; break;
            }
        }
    }

    /// <summary>
    /// 解析 38/48 后的扩展颜色参数（256色 或 RGB）
    /// </summary>
    private static int ParseExtendedColor(List<int> nums, int i,
        out Color rgb, out int index, out bool useRgb)
    {
        rgb = ColorDefault;
        index = 7;
        useRgb = false;

        if (i + 1 >= nums.Count) return i;
        int type = nums[++i];

        if (type == 5 && i + 1 < nums.Count)
        {
            // 256 色: 38;5;N
            index = Math.Clamp(nums[++i], 0, 255);
            useRgb = false;
        }
        else if (type == 2 && i + 3 < nums.Count)
        {
            // RGB: 38;2;R;G;B
            byte r = (byte)Math.Clamp(nums[i + 1], 0, 255);
            byte g = (byte)Math.Clamp(nums[i + 2], 0, 255);
            byte b = (byte)Math.Clamp(nums[i + 3], 0, 255);
            rgb = Color.FromRgb(r, g, b);
            i += 3;
            useRgb = true;
        }

        return i;
    }

    private void ResetAttributes()
    {
        _fgIndex = 7;
        _bgIndex = 0;
        _bold = false;
        _useRgbFg = false;
        _useRgbBg = false;
        _rgbFg = ColorDefault;
        _rgbBg = BgDefault;
    }

    #endregion

    #region 清屏 & 滚动

    public void Clear()
    {
        ClearScreen();
        ResetAttributes();
        _state = ParseState.Normal;
        _escParams.Clear();
    }

    /// <summary>
    /// 获取终端中所有文本内容（用于同步到独立窗口）
    /// </summary>
    public string GetAllText()
    {
        var sb = new StringBuilder();
        foreach (Block block in _doc.Blocks)
        {
            if (block is Paragraph para)
            {
                foreach (Inline inline in para.Inlines)
                {
                    if (inline is Run run) sb.Append(run.Text);
                    else if (inline is LineBreak) sb.Append('\n');
                }
            }
        }
        return sb.ToString();
    }

    private void ClearScreen()
    {
        _doc.Blocks.Clear();
        _para = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = double.NaN,
            TextIndent = 0
        };
        _doc.Blocks.Add(_para);
        _textBuf.Clear();
    }

    public new void ScrollToEnd()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                UpdateLayout();
                CaretPosition = Document.ContentEnd;
                base.ScrollToEnd();
            }
            catch { }
        }));
    }

    #endregion
}
