namespace ToolHelper.Views.Serial;

/// <summary>协议类型</summary>
public enum ModbusProtocolKind { ShenHong, NanRuiYiHe }

/// <summary>报警级别（两版一致）</summary>
public static class AlarmLevel
{
    public static readonly string[] Names =
        { "正常", "示警", "预警", "巡警", "火警1", "火警2" };

    /// <summary>按值取中文名，越界返回"未知"</summary>
    public static string Name(ushort level) =>
        level < Names.Length ? Names[level] : "未知";
}

/// <summary>故障字位定义（两版一致，16 位按位；跳过「系统故障8」，不得补位）</summary>
public static class FaultBits
{
    public static readonly string[] Names =
    {
        "气流堵塞", "气流漏气", "风扇故障", "显示屏通信中断",
        "系统故障1", "系统故障2", "系统故障3", "系统故障4",
        "系统故障5", "系统故障6", "系统故障7", "云室通信中断",
        "系统故障9", "系统故障10", "温湿度传感器故障", "电压故障"
    };

    /// <summary>按位解码为中文列表</summary>
    public static List<string> Decode(ushort faultWord)
    {
        var result = new List<string>();
        for (int i = 0; i < 16; i++)
            if ((faultWord & (1 << i)) != 0) result.Add(Names[i]);
        return result;
    }
}

/// <summary>阈值寄存器定义（申弘版顺序；南瑞怡和版由视图按偶数地址映射）</summary>
public sealed record ThresholdReg(int Address, string Name);

/// <summary>协议点表（静态工厂，纯数据无逻辑）</summary>
public static class ModbusProtocols
{
    /// <summary>申弘版阈值顺序：0x15~0x1E（白天火警2/火警1/巡警/预警/示警 + 晚上同名五项）</summary>
    public static readonly ThresholdReg[] Thresholds =
    {
        new(0x15, "白天火警2阈值"), new(0x16, "白天火警1阈值"),
        new(0x17, "白天巡警阈值"), new(0x18, "白天预警阈值"),
        new(0x19, "白天示警阈值"), new(0x1A, "晚上火警2阈值"),
        new(0x1B, "晚上火警1阈值"), new(0x1C, "晚上巡警阈值"),
        new(0x1D, "晚上预警阈值"), new(0x1E, "晚上示警阈值"),
    };

    /// <summary>申弘版：全量读请求（0x03，0x10 起读 22 个寄存器）</summary>
    public static byte[] BuildShenHongReadAll(byte devAddr) => new byte[]
        { devAddr, 0x03, 0x00, 0x10, 0x00, 0x16 };

    /// <summary>南瑞怡和版：0x03 全量读请求（0x08 起读 25 个寄存器）</summary>
    public static byte[] BuildNrReadHolding(byte devAddr) => new byte[]
        { devAddr, 0x03, 0x00, 0x08, 0x00, 0x19 };

    /// <summary>南瑞怡和版：0x04 全量读请求（0x01 起读 28 个寄存器）</summary>
    public static byte[] BuildNrReadInput(byte devAddr) => new byte[]
        { devAddr, 0x04, 0x00, 0x01, 0x00, 0x1C };

    /// <summary>南瑞怡和版：复位命令（0x05 写线圈 0x07，值 FF00）</summary>
    public static byte[] BuildNrReset(byte devAddr) => new byte[]
        { devAddr, 0x05, 0x00, 0x07, 0xFF, 0x00 };

    /// <summary>站端下辖设备清单（地址 0x00，0x0000 起读 16 个寄存器）</summary>
    public static byte[] BuildDeviceListRead() => new byte[]
        { 0x00, 0x03, 0x00, 0x00, 0x00, 0x10 };

    /// <summary>南瑞怡和版：站端下辖设备通讯状态（0x00EE 起读 16 个寄存器）</summary>
    public static byte[] BuildNrCommStatusRead() => new byte[]
        { 0x00, 0x03, 0x00, 0xEE, 0x00, 0x10 };

    /// <summary>从设备清单响应的数据区解析设备地址列表（滤除 0x00，去重）</summary>
    public static List<byte> ParseDeviceList(ReadOnlySpan<byte> data)
    {
        var list = new List<byte>();
        foreach (var b in data)
            if (b != 0x00 && !list.Contains(b))
                list.Add(b);
        return list;
    }

    /// <summary>南瑞怡和版阈值：0x08~0x1A 偶数地址（白天 5 项 + 晚上 5 项）</summary>
    public static readonly int[] NrThresholdAddrs =
        { 0x08, 0x0A, 0x0C, 0x0E, 0x10, 0x12, 0x14, 0x16, 0x18, 0x1A };
}

/// <summary>单台设备状态（纯数据模型，供列表绑定与详情展示）</summary>
public class DeviceData
{
    public byte Address { get; set; }
    public string SerialNumber { get; set; } = "";
    public ushort AlarmLevel { get; set; }
    public ushort FaultWord { get; set; }
    public ushort IonValue { get; set; }
    public ushort WorkStatus { get; set; }
    public ushort CommStatus { get; set; }
    public ushort[] Thresholds { get; } = new ushort[10];
    public bool Online { get; set; }
    public DateTime LastUpdate { get; set; }

    public string AlarmName => ToolHelper.Views.Serial.AlarmLevel.Name(AlarmLevel);

    public string FaultText
    {
        get
        {
            var list = FaultBits.Decode(FaultWord);
            return list.Count > 0 ? string.Join("、", list) : "无";
        }
    }

    public string OnlineMark => Online ? "●" : "○";
    public string UpdateText => LastUpdate == default ? "-" : LastUpdate.ToString("HH:mm:ss");
}
