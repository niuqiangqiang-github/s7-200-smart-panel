using S7.Net;

namespace Smart200Panel;

/// <summary>
/// 封装与 S7-200 SMART PLC 的以太网通信（S7 协议，ISO-on-TCP，端口 102）。
/// 对外只暴露：连接/断开、读位、写位、读字、写字、读任意地址。
/// 内部加了锁，保证界面线程（写入）和后台轮询线程（读取）不会同时抢 PLC。
/// </summary>
public class PlcClient
{
    private readonly object _lock = new();
    private Plc _plc;

    public bool IsConnected => _plc?.IsConnected ?? false;

    /// <summary>连接 PLC（可取消）。失败或取消会抛异常，由调用方处理。</summary>
    public async Task ConnectAsync(string ip, CancellationToken ct, short rack = 0, short slot = 1)
    {
        Disconnect();
        // S7-200 SMART 必须用 S7200Smart 类型（不是 S71200），否则 V 区/数据块编址不对
        var plc = new Plc(CpuType.S7200Smart, ip, rack, slot)
        {
            ReadTimeout = 1000,   // 读超时 1 秒，断线时不会卡太久
            WriteTimeout = 1000,
        };
        // 先挂到 _plc 上：连接期间调用 Disconnect() 会 Close 掉它，从而中断连接
        _plc = plc;
        try
        {
            await plc.OpenAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try { plc.Close(); } catch { /* 打开失败就关掉半开的连接，避免泄漏 */ }
            _plc = null;
            throw;
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            try { _plc?.Close(); } catch { /* 关闭时的异常忽略 */ }
            _plc = null;
        }
    }

    public bool ReadBit(string address) => (bool)ReadValue(address);

    public void WriteBit(string address, bool value) => WriteValue(address, value);

    public ushort ReadWord(string address) => Convert.ToUInt16(ReadValue(address));

    public void WriteWord(string address, ushort value) => WriteValue(address, value);

    /// <summary>读 Q 区某个字节的 8 个位（一次请求读回），如 byteIndex=0 对应 Q0.0~Q0.7。</summary>
    public bool[] ReadOutputByteBits(int byteIndex)
    {
        lock (_lock)
        {
            if (_plc == null) throw new InvalidOperationException("尚未连接 PLC");
            byte b = _plc.ReadBytes(DataType.Output, 0, byteIndex, 1)[0];
            bool[] bits = new bool[8];
            for (int i = 0; i < 8; i++) bits[i] = (b & (1 << i)) != 0;
            return bits;
        }
    }

    /// <summary>读 V 区从 startByte 开始的连续 count 个字（一次请求，大端序拆字），如 startByte=300 对应 VW300。</summary>
    public ushort[] ReadVWords(int startByte, int count)
    {
        lock (_lock)
        {
            if (_plc == null) throw new InvalidOperationException("尚未连接 PLC");
            byte[] data = _plc.ReadBytes(DataType.DataBlock, 1, startByte, count * 2);
            ushort[] words = new ushort[count];
            for (int i = 0; i < count; i++)
                words[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
            return words;
        }
    }

    /// <summary>读任意地址（如 "VW100"、"VD100"、"M10.0"），返回对应类型的值。</summary>
    public object ReadValue(string address)
    {
        lock (_lock)
        {
            if (_plc == null) throw new InvalidOperationException("尚未连接 PLC");
            return _plc.Read(NormalizeAddress(address));
        }
    }

    private void WriteValue(string address, object value)
    {
        lock (_lock)
        {
            if (_plc == null) throw new InvalidOperationException("尚未连接 PLC");
            _plc.Write(NormalizeAddress(address), value);
        }
    }

    /// <summary>
    /// 把用户习惯的 S7-200 SMART 地址转成 S7.Net 认识的地址。
    /// S7.Net 不支持 "VW100" 这种写法（V 区要写成 DB1），这里自动转换：
    ///   VW100 -> DB1.DBW100（字）   VD100 -> DB1.DBD100（双字）
    ///   VB100 -> DB1.DBB100（字节） V100.0 -> DB1.DBX100.0（位）
    /// 其它地址（M/Q/I/DB1 开头）原样返回。
    /// </summary>
    private static string NormalizeAddress(string address)
    {
        address = address.Trim();
        if (address.Length < 2 || char.ToUpper(address[0]) != 'V') return address;

        char c = char.ToUpper(address[1]);
        if (c == 'W' || c == 'D' || c == 'B')
            return "DB1.DB" + c + address.Substring(2);   // VW100 -> DB1.DBW100
        return "DB1.DBX" + address.Substring(1);          // V100.0 -> DB1.DBX100.0
    }
}
