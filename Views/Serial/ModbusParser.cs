using System.Buffers.Binary;

namespace ToolHelper.Views.Serial;

/// <summary>Modbus RTU 解析器（纯逻辑，可单元测试）</summary>
public static class ModbusParser
{
    /// <summary>Modbus CRC16（多项式 0xA001，初值 0xFFFF，低字节在前）</summary>
    public static byte[] CalcCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0
                    ? (ushort)((crc >> 1) ^ 0xA001)
                    : (ushort)(crc >> 1);
        }
        return new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
    }

    /// <summary>组帧并追加 CRC（低字节在前）</summary>
    public static byte[] BuildFrame(ReadOnlySpan<byte> bodyWithoutCrc)
    {
        var frame = new byte[bodyWithoutCrc.Length + 2];
        bodyWithoutCrc.CopyTo(frame);
        var crc = CalcCrc16(bodyWithoutCrc);
        frame[^2] = crc[0];
        frame[^1] = crc[1];
        return frame;
    }

    /// <summary>校验响应帧 CRC（含 CRC 共 ≥4 字节）</summary>
    public static bool VerifyCrc(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4) return false;
        var crc = CalcCrc16(frame[..^2]);
        return crc[0] == frame[^2] && crc[1] == frame[^1];
    }

    /// <summary>大端读 16 位寄存器</summary>
    public static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    /// <summary>
    /// 响应帧重组：按「功能码 + 字节数」从接收缓冲提取完整帧。
    /// 返回 true 时 frame 为完整帧（不含剩余字节），consumed 为已消费字节数；
    /// 数据不足返回 false（等待更多字节）。
    /// </summary>
    public static bool TryExtractFrame(byte[] buffer, int count,
                                       out byte[] frame, out int consumed)
    {
        frame = Array.Empty<byte>();
        consumed = 0;
        if (count < 4) return false;

        byte fc = buffer[1];
        int total;
        if ((fc & 0x80) != 0)          // 异常帧：地址+功能码+异常码+CRC = 5
        {
            total = 5;
        }
        else if (fc is 0x03 or 0x04)   // 读：地址+功能码+字节数+数据+CRC
        {
            if (count < 3) return false;
            int dataLen = buffer[2];
            total = 3 + dataLen + 2;
        }
        else if (fc is 0x05)           // 写线圈回显：固定 8 字节
        {
            total = 8;
        }
        else return false;             // 未知功能码，交由上层丢弃

        if (count < total) return false;
        frame = buffer[..total];
        consumed = total;
        return true;
    }

    /// <summary>HEX 字节数组转显示字符串（"01 03 ..."）</summary>
    public static string ToHexString(ReadOnlySpan<byte> data) =>
        BitConverter.ToString(data.ToArray()).Replace("-", " ");

    // ========== 响应解析（纯逻辑，填充 DeviceData） ==========

    /// <summary>解析申弘版 0x03 全量响应（0x10 起 22 寄存器，数据区 44 字节）</summary>
    public static void ParseShenHongAll(byte[] frame, DeviceData dev)
    {
        var data = frame.AsSpan(3, frame[2]);
        dev.AlarmLevel = ReadU16(data, 0);
        dev.FaultWord = ReadU16(data, 2);
        dev.WorkStatus = ReadU16(data, 4);
        dev.CommStatus = ReadU16(data, 6);
        dev.IonValue = ReadU16(data, 8);
        for (int i = 0; i < 10 && i < dev.Thresholds.Length; i++)
            dev.Thresholds[i] = ReadU16(data, 10 + i * 2);
        // 设备ID：0x20~0x25 共 12 字节，丢首字节，取 11 字节 ASCII 到 0x00
        var raw = data.Slice(32, 12).ToArray();
        dev.SerialNumber = System.Text.Encoding.ASCII.GetString(raw, 1, 11).TrimEnd('\0');
        dev.Online = true;
        dev.LastUpdate = DateTime.Now;
    }

    /// <summary>解析南瑞怡和版 0x04 全量响应（0x01 起 28 寄存器，数据区 56 字节）</summary>
    public static void ParseNrInput(byte[] frame, DeviceData dev)
    {
        var data = frame.AsSpan(3, frame[2]);
        dev.IonValue = ReadU16(data, 0);
        dev.FaultWord = ReadU16(data, 2);
        dev.AlarmLevel = ReadU16(data, 4);
        dev.WorkStatus = ReadU16(data, 6);
        // 序列号：实测 0x11~0x1B 低字节 ASCII
        var sb = new System.Text.StringBuilder();
        for (int i = 0x11; i <= 0x1B; i++)
            sb.Append((char)(data[(i - 0x01) * 2] & 0xFF));
        dev.SerialNumber = sb.ToString().TrimEnd('\0');
        dev.Online = true;
        dev.LastUpdate = DateTime.Now;
    }

    /// <summary>解析南瑞怡和版 0x03 全量响应（0x08 起 25 寄存器，偶数地址为阈值，0x20 为通讯地址）</summary>
    public static void ParseNrHolding(byte[] frame, DeviceData dev)
    {
        var data = frame.AsSpan(3, frame[2]);
        var addrs = ModbusProtocols.NrThresholdAddrs;
        for (int i = 0; i < addrs.Length && i < dev.Thresholds.Length; i++)
            dev.Thresholds[i] = ReadU16(data, (addrs[i] - 0x08) * 2);
        // 0x20 为通讯地址，用于校验（应与 dev.Address 一致）
        var commAddr = ReadU16(data, (0x20 - 0x08) * 2);
        if (commAddr != dev.Address)
            ToolHelper.Services.FileLogger.Write("Modbus", $"[WARN] 设备{dev.Address} 通讯地址寄存器返回 {commAddr}");
    }
}
