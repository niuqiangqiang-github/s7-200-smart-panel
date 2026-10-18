namespace Smart200Panel;

/// <summary>
/// 地址映射表：面板控件 ↔ PLC 地址。
/// 改这里的地址，就能改面板读写哪个点，不用动界面代码。
///
/// 说明（很重要）：
/// - S7-200 SMART 的输入 I / 模拟量输入 AIW 由 CPU 每个扫描周期从物理端子刷新，
///   外部写不进去。所以"虚拟输入"用 M 区、"虚拟模拟量输入"用 V 区。
/// - 你写 PLC 程序时，把本来的 I 点换成 M10.x、本来的 AIW 换成 VW200 即可。
/// </summary>
public static class TagMap
{
    /// <summary>数字输入（面板按钮 → PLC，写 M 区，当"虚拟输入"用）。</summary>
    public static readonly string[] DigitalInputs =
    {
        "M10.0", "M10.1", "M10.2", "M10.3",
        "M10.4", "M10.5", "M10.6", "M10.7",
    };

    /// <summary>数字输出（PLC → 面板灯，读 Q 区，只读显示真实输出）。</summary>
    public static readonly string[] DigitalOutputs =
    {
        "Q0.0", "Q0.1", "Q0.2", "Q0.3",
        "Q0.4", "Q0.5", "Q0.6", "Q0.7",
    };

    /// <summary>模拟量输入（面板数值 → PLC，写 VW 区）。</summary>
    public static readonly string[] AnalogInputs = { "VW200", "VW202" };

    /// <summary>模拟量输出（PLC → 面板，读 VW 区；约定程序把结果写到 VW300/VW302）。</summary>
    public static readonly string[] AnalogOutputs = { "VW300", "VW302" };

    /// <summary>数字输出所在 Q 字节号（由第一个地址 "Q0.0" 推出 0；假定 8 个点连续在同一字节）。</summary>
    public static int DigitalOutputByte => ParseByteIndex(DigitalOutputs[0]);

    /// <summary>模拟量输出起始字节（由第一个地址 "VW300" 推出 300；假定各字连续）。</summary>
    public static int AnalogOutputStartByte => ParseVWordStart(AnalogOutputs[0]);

    // "Q0.0" -> 0：取区号字母之后、'.' 之前的数字
    private static int ParseByteIndex(string addr)
    {
        int dot = addr.IndexOf('.');
        return int.Parse(addr.Substring(1, dot - 1));
    }

    // "VW300" -> 300：取 "VW" 之后的数字
    private static int ParseVWordStart(string addr) => int.Parse(addr.Substring(2));
}
