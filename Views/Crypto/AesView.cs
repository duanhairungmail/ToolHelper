using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PackIcon = MaterialDesignThemes.Wpf.PackIcon;
using PackIconKind = MaterialDesignThemes.Wpf.PackIconKind;

namespace ToolHelper.Views.Crypto;

public class AesView : UserControl
{
    private ComboBox _modeCb = new();
    private ComboBox _paddingCb = new();
    private ComboBox _keyLenCb = new();
    private ComboBox _encodingCb = new();
    private ComboBox _formatCb = new();
    private ComboBox _keyFormatCb = new();
    private ComboBox _ivFormatCb = new();
    private TextBox _keyBox = new();
    private TextBox _ivBox = new();
    private TextBox _inputBox = new();
    private TextBox _outputBox = new();
    private TextBlock _statusText = new();
    private bool _built;

    public AesView()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildUI();
    }

    private ComboBox MakeCombo(string[] items, int selected = 0)
    {
        var cb = new ComboBox { Margin = new Thickness(0, 0, 12, 0), MinWidth = 120 };
        foreach (var item in items) cb.Items.Add(item);
        cb.SelectedIndex = selected;
        return cb;
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private TextBox MakeSingleLineBox(string hint, string defaultText = "")
    {
        var tb = new TextBox
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 200,
            Text = defaultText
        };
        var style = TryFindResource("MaterialDesignOutlinedTextBox") as Style;
        if (style != null) tb.Style = style;
        MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);
        return tb;
    }

    private TextBox MakeMultiLineBox(string hint, bool readOnly = false)
    {
        var tb = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 120,
            VerticalContentAlignment = VerticalAlignment.Top,
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 13,
            IsReadOnly = readOnly
        };
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
        var root = new StackPanel();
        var titleBrush = TryFindResource("PrimaryHueMidBrush") as Brush ?? Brushes.DarkBlue;

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new PackIcon { Kind = PackIconKind.Lock, Width = 28, Height = 28, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = "  AES 加密/解密", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = titleBrush, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(titleRow);
        root.Children.Add(new TextBlock { Text = "AES 对称加密算法，支持多种运算模式、填充模式、密钥长度、字符编码和输出格式", FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        // 第一行选项：运算模式 + 填充模式
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row1.Children.Add(MakeLabel("运算模式:"));
        _modeCb = MakeCombo(new[] { "CBC", "ECB", "OFB", "CFB", "CTS", "CTR", "GCM" }, 0);
        _modeCb.SelectionChanged += (s, e) => UpdateIvVisibility();
        row1.Children.Add(_modeCb);
        row1.Children.Add(MakeLabel("填充模式:"));
        _paddingCb = MakeCombo(new[] { "PKCS7", "无", "零填充", "ANSIX923", "ISO10126" }, 0);
        row1.Children.Add(_paddingCb);
        root.Children.Add(row1);

        // 第二行选项：密钥长度 + 字符编码 + 输出格式
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row2.Children.Add(MakeLabel("密钥长度:"));
        _keyLenCb = MakeCombo(new[] { "128位", "192位", "256位" }, 2);
        row2.Children.Add(_keyLenCb);
        row2.Children.Add(MakeLabel("字符编码:"));
        _encodingCb = MakeCombo(new[] { "UTF-8", "UTF-16", "UTF-32", "ASCII", "GBK", "ISO-8859-1" }, 0);
        row2.Children.Add(_encodingCb);
        row2.Children.Add(MakeLabel("输出格式:"));
        _formatCb = MakeCombo(new[] { "Base64", "十六进制" }, 0);
        row2.Children.Add(_formatCb);
        root.Children.Add(row2);

        // 密钥输入行
        var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row3.Children.Add(MakeLabel("密钥:"));
        _keyBox = MakeSingleLineBox("输入密钥KEY（如:32DGoR8HdfIiw1judwJHY&^%1_aFSSJw）");
        _keyBox.MinWidth = 350;
        row3.Children.Add(_keyBox);
        row3.Children.Add(MakeLabel("格式:"));
        _keyFormatCb = MakeCombo(new[] { "文本", "十六进制" }, 0);
        row3.Children.Add(_keyFormatCb);
        root.Children.Add(row3);

        // 偏移输入行
        var row4 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row4.Children.Add(MakeLabel("偏移:"));
        _ivBox = MakeSingleLineBox("输入偏移量IV（如:32DGoR8HdfIiw1ju）");
        _ivBox.MinWidth = 350;
        row4.Children.Add(_ivBox);
        row4.Children.Add(MakeLabel("格式:"));
        _ivFormatCb = MakeCombo(new[] { "文本", "十六进制" }, 0);
        row4.Children.Add(_ivFormatCb);
        row4.Name = "IvRow";
        root.Children.Add(row4);

        // 输入区
        root.Children.Add(MakeLabel("输入"));
        _inputBox = MakeMultiLineBox("加密时输入明文，解密时输入密文...");
        root.Children.Add(_inputBox);

        // 按钮栏
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        btnPanel.Children.Add(MakeButton("加密", Encrypt, true, PackIconKind.Lock));
        btnPanel.Children.Add(MakeButton("解密", Decrypt, false, PackIconKind.LockOpen));
        btnPanel.Children.Add(MakeButton("清空", Clear, false, PackIconKind.Eraser));
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(16, 0, 0, 0);
        _statusText.FontSize = 13;
        btnPanel.Children.Add(_statusText);
        root.Children.Add(btnPanel);

        // 输出区
        root.Children.Add(MakeLabel("输出结果"));
        _outputBox = MakeMultiLineBox("", true);
        root.Children.Add(_outputBox);

        Content = root;
        UpdateIvVisibility();
    }

    private void UpdateIvVisibility()
    {
        var mode = _modeCb.SelectedItem?.ToString() ?? "CBC";
        bool needIv = mode != "ECB";
        _ivBox.IsEnabled = needIv;
        _ivBox.Opacity = needIv ? 1.0 : 0.4;
    }

    private Encoding GetEncoding()
    {
        return (_encodingCb.SelectedItem?.ToString()) switch
        {
            "UTF-8" => Encoding.UTF8,
            "UTF-16" => Encoding.Unicode,
            "UTF-32" => Encoding.UTF32,
            "ASCII" => Encoding.ASCII,
            "GBK" => Encoding.GetEncoding("GBK"),
            "ISO-8859-1" => Encoding.Latin1,
            _ => Encoding.UTF8
        };
    }

    private byte[] GetKeyBytes()
    {
        var raw = _keyBox.Text.Trim();
        if (string.IsNullOrEmpty(raw)) throw new Exception("请输入密钥");
        if (_keyFormatCb.SelectedItem?.ToString() == "十六进制")
            return HexToBytes(raw);
        return GetEncoding().GetBytes(raw);
    }

    private byte[] GetIvBytes()
    {
        var mode = _modeCb.SelectedItem?.ToString() ?? "CBC";
        if (mode == "ECB") return Array.Empty<byte>();
        var raw = _ivBox.Text.Trim();
        if (string.IsNullOrEmpty(raw)) throw new Exception("请输入偏移量(IV)");
        if (_ivFormatCb.SelectedItem?.ToString() == "十六进制")
            return HexToBytes(raw);
        return GetEncoding().GetBytes(raw);
    }

    private int GetKeyLength()
    {
        return (_keyLenCb.SelectedItem?.ToString()) switch
        {
            "128位" => 128,
            "192位" => 192,
            "256位" => 256,
            _ => 256
        };
    }

    private string FormatOutput(byte[] data)
    {
        return (_formatCb.SelectedItem?.ToString()) switch
        {
            "十六进制" => BitConverter.ToString(data).Replace("-", "").ToLower(),
            _ => Convert.ToBase64String(data)
        };
    }

    private byte[] ParseCipherInput(string text)
    {
        text = text.Trim();
        return (_formatCb.SelectedItem?.ToString()) switch
        {
            "十六进制" => HexToBytes(text),
            _ => Convert.FromBase64String(text)
        };
    }

    private void Encrypt()
    {
        try
        {
            var key = GetKeyBytes();
            var iv = GetIvBytes();
            var encoding = GetEncoding();
            var plaintext = encoding.GetBytes(_inputBox.Text);
            var mode = _modeCb.SelectedItem?.ToString() ?? "CBC";
            var padding = _paddingCb.SelectedItem?.ToString() ?? "PKCS7";
            int keyBits = GetKeyLength();

            byte[] result = AesEncrypt(key, iv, plaintext, mode, padding, keyBits);
            _outputBox.Text = FormatOutput(result);
            SetStatus($"加密成功 | 模式: {mode} | 填充: {padding} | 密钥: {keyBits} bits", true);
        }
        catch (Exception ex) { SetStatus($"错误: {ex.Message}", false); }
    }

    private void Decrypt()
    {
        try
        {
            var key = GetKeyBytes();
            var iv = GetIvBytes();
            var encoding = GetEncoding();
            var ciphertext = ParseCipherInput(_inputBox.Text);
            var mode = _modeCb.SelectedItem?.ToString() ?? "CBC";
            var padding = _paddingCb.SelectedItem?.ToString() ?? "PKCS7";
            int keyBits = GetKeyLength();

            byte[] result = AesDecrypt(key, iv, ciphertext, mode, padding, keyBits);
            _outputBox.Text = encoding.GetString(result);
            SetStatus($"解密成功 | 模式: {mode} | 填充: {padding} | 密钥: {keyBits} bits", true);
        }
        catch (Exception ex) { SetStatus($"错误: {ex.Message}", false); }
    }

    // ---- AES 加解密核心 ----

    private static byte[] AesEncrypt(byte[] key, byte[] iv, byte[] data, string mode, string padding, int keyBits)
    {
        key = AdjustKeyLength(key, keyBits);
        if (mode == "CTR") return AesCtr(key, iv, data, false);
        if (mode == "GCM") return AesGcmEncrypt(key, iv, data);

        using var aes = Aes.Create();
        aes.KeySize = keyBits;
        aes.Key = key;
        aes.Mode = mode switch
        {
            "CBC" => CipherMode.CBC,
            "ECB" => CipherMode.ECB,
            "OFB" => CipherMode.OFB,
            "CFB" => CipherMode.CFB,
            "CTS" => CipherMode.CTS,
            _ => CipherMode.CBC
        };
        aes.Padding = padding switch
        {
            "无" => PaddingMode.None,
            "PKCS7" => PaddingMode.PKCS7,
            "零填充" => PaddingMode.Zeros,
            "ANSIX923" => PaddingMode.ANSIX923,
            "ISO10126" => PaddingMode.ISO10126,
            _ => PaddingMode.PKCS7
        };
        aes.BlockSize = 128;
        if (mode != "ECB") { aes.IV = iv.Length >= 16 ? iv[..16] : PadTo16(iv); }
        var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] AesDecrypt(byte[] key, byte[] iv, byte[] data, string mode, string padding, int keyBits)
    {
        key = AdjustKeyLength(key, keyBits);
        if (mode == "CTR") return AesCtr(key, iv, data, false);
        if (mode == "GCM") return AesGcmDecrypt(key, iv, data);

        using var aes = Aes.Create();
        aes.KeySize = keyBits;
        aes.Key = key;
        aes.Mode = mode switch
        {
            "CBC" => CipherMode.CBC,
            "ECB" => CipherMode.ECB,
            "OFB" => CipherMode.OFB,
            "CFB" => CipherMode.CFB,
            "CTS" => CipherMode.CTS,
            _ => CipherMode.CBC
        };
        aes.Padding = padding switch
        {
            "无" => PaddingMode.None,
            "PKCS7" => PaddingMode.PKCS7,
            "零填充" => PaddingMode.Zeros,
            "ANSIX923" => PaddingMode.ANSIX923,
            "ISO10126" => PaddingMode.ISO10126,
            _ => PaddingMode.PKCS7
        };
        aes.BlockSize = 128;
        if (mode != "ECB") { aes.IV = iv.Length >= 16 ? iv[..16] : PadTo16(iv); }
        var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    // CTR 模式手动实现（.NET 不原生支持）
    private static byte[] AesCtr(byte[] key, byte[] iv, byte[] data, bool isDecrypt)
    {
        var nonce = iv.Length >= 16 ? iv[..16] : PadTo16(iv);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();

        var result = new byte[data.Length];
        var counter = new byte[16];
        Array.Copy(nonce, counter, Math.Min(nonce.Length, 16));
        var keystream = new byte[16];

        for (int offset = 0; offset < data.Length; offset += 16)
        {
            encryptor.TransformBlock(counter, 0, 16, keystream, 0);
            int blockLen = Math.Min(16, data.Length - offset);
            for (int i = 0; i < blockLen; i++)
                result[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            // 递增计数器
            for (int i = 15; i >= 0; i--)
            {
                if (++counter[i] != 0) break;
            }
        }
        return result;
    }

    // GCM 模式使用 .NET 内置 API
    private static byte[] AesGcmEncrypt(byte[] key, byte[] iv, byte[] plaintext)
    {
        var nonce = iv.Length >= 12 ? iv[..12] : PadTo(iv, 12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using var aesGcm = new AesGcm(key, tag.Length);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        // 输出: nonce(12) + tag(16) + ciphertext
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Array.Copy(nonce, 0, result, 0, nonce.Length);
        Array.Copy(tag, 0, result, nonce.Length, tag.Length);
        Array.Copy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);
        return result;
    }

    private static byte[] AesGcmDecrypt(byte[] key, byte[] iv, byte[] data)
    {
        // 数据格式: nonce(12) + tag(16) + ciphertext
        if (data.Length < 28) throw new Exception("GCM 数据太短（应包含 nonce+tag+密文）");
        var nonce = data[..12];
        var tag = data[12..28];
        var ciphertext = data[28..];
        var plaintext = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(key, tag.Length);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    // ---- 辅助方法 ----

    private static byte[] AdjustKeyLength(byte[] key, int bits)
    {
        int bytes = bits / 8;
        if (key.Length == bytes) return key;
        if (key.Length > bytes) return key[..bytes];
        var adjusted = new byte[bytes];
        Array.Copy(key, adjusted, key.Length);
        return adjusted;
    }

    private static byte[] PadTo16(byte[] data) => PadTo(data, 16);

    private static byte[] PadTo(byte[] data, int size)
    {
        if (data.Length >= size) return data[..size];
        var result = new byte[size];
        Array.Copy(data, result, data.Length);
        return result;
    }

    private static byte[] HexToBytes(string hex)
    {
        hex = hex.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        hex = hex.Replace(" ", "").Replace("-", "");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    private void SetStatus(string msg, bool success)
    {
        _statusText.Text = msg;
        _statusText.Foreground = success ? Brushes.Green : Brushes.Red;
    }

    private void Clear()
    {
        _keyBox.Text = "";
        _ivBox.Text = "";
        _inputBox.Text = "";
        _outputBox.Text = "";
        _statusText.Text = "";
    }
}
