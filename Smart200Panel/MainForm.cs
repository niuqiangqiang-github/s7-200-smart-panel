using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Smart200Panel;

/// <summary>
/// 主窗口：连接 PLC，模拟输入（写 M 区），显示输出（读 Q 区），模拟量与寄存器监视。
/// 界面用代码直接生成（不用设计器文件），方便阅读和修改。
/// </summary>
public class MainForm : Form
{
    private readonly PlcClient _plc = new();

    // 顶部连接区
    private TextBox _ipBox = null!;
    private Button _connectBtn = null!;
    private Button _disconnectBtn = null!;
    private Label _statusLabel = null!;

    // 数字输入 / 数字输出
    private GroupBox _inputGroup = null!;
    private GroupBox _analogInGroup = null!;
    private readonly Button[] _inputBtns = new Button[TagMap.DigitalInputs.Length];
    private readonly Label[] _outputLamps = new Label[TagMap.DigitalOutputs.Length];

    // 模拟量输入（写）/ 模拟量输出（读）
    private readonly NumericUpDown[] _analogInputs = new NumericUpDown[TagMap.AnalogInputs.Length];
    private readonly Button[] _analogWriteBtns = new Button[TagMap.AnalogInputs.Length];
    private readonly TextBox[] _analogOutputs = new TextBox[TagMap.AnalogOutputs.Length];

    // 寄存器监视（8 个：4 行 × 2 列，地址可填）
    private readonly TextBox[] _monitorAddrs = new TextBox[8];
    private readonly TextBox[] _monitorValues = new TextBox[8];
    private readonly string[] _monitorAddrCache = new string[8]; // 后台线程只读缓存，避免跨线程读控件

    private System.Threading.Timer _pollTimer;
    private CancellationTokenSource _cts;   // 连接期间用，点「断开」可取消连接
    private int _polling;                        // 1=轮询进行中，防重入
    private int _readFailCount;                  // 连续整轮读失败次数，用于判定断线
    private readonly object _addrLock = new();   // 保护监视地址缓存的跨线程读写
    private readonly bool[] _outsBuf = new bool[TagMap.DigitalOutputs.Length];
    private readonly ushort[] _aoutsBuf = new ushort[TagMap.AnalogOutputs.Length];
    private readonly string[] _monBuf = new string[8];

    public MainForm()
    {
        Text = "S7-200 SMART I/O 仿真面板 V1.0";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 800);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 10f);

        BuildUi();
        ApplyStatus(ConnStatus.Disconnected);
    }

    // ======================= 界面搭建 =======================

    private void BuildUi()
    {
        // 顶部连接区（包进「PLC连接」分组框，控件统一垂直居中）
        var connGroup = AddGroup("PLC连接", 15, 12, 1210, 76);
        AddLabelFixed(connGroup, "IP地址", 18, 35, 70);
        _ipBox = AddTextBox(connGroup, 98, 30, 220);
        _ipBox.Height = 32;
        _ipBox.Text = "192.168.2.1";
        _connectBtn = AddButton(connGroup, "连接", 328, 30, 100);
        _connectBtn.Height = 32;
        _disconnectBtn = AddButton(connGroup, "断开", 438, 30, 100);
        _disconnectBtn.Height = 32;
        _statusLabel = new Label
        {
            Text = "未连接",
            AutoSize = false,
            Size = new Size(110, 32),
            Location = new Point(550, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Gray,
            ForeColor = Color.White,
        };
        connGroup.Controls.Add(_statusLabel);

        _connectBtn.Click += (s, e) => ConnectPlc();
        _disconnectBtn.Click += (s, e) => DisconnectPlc();

        // 数字输入（写 M 区，点动按钮：按住导通、松开断开）。每个点位：名称框在上、按钮在下，同宽左对齐
        _inputGroup = AddGroup("数字输入（写 M10.0~M10.7）", 15, 93, 600, 252);
        for (int i = 0; i < _inputBtns.Length; i++)
        {
            int idx = i;
            int cx = 18 + (i % 4) * 145;
            int cy = 42 + (i / 4) * 96;

            var nameBox = new TextBox
            {
                Location = new Point(cx, cy + 49),
                Width = 130,
                TextAlign = HorizontalAlignment.Center,
            };
            SetCueBanner(nameBox, "信号名");
            _inputGroup.Controls.Add(nameBox);

            var btn = CreatePushButton(TagMap.DigitalInputs[i]);
            btn.Location = new Point(cx, cy);
            btn.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) SetDigitalInput(idx, true, btn); };
            btn.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) SetDigitalInput(idx, false, btn); };
            btn.MouseLeave += (s, e) => SetDigitalInput(idx, false, btn);
            _inputBtns[i] = btn;
            _inputGroup.Controls.Add(btn);
        }

        // 数字输出（读 Q 区）。每个点位：名称框在上、指示灯在下，同宽左对齐
        var outGroup = AddGroup("数字输出（读 Q0.0~Q0.7）", 625, 93, 600, 252);
        for (int i = 0; i < _outputLamps.Length; i++)
        {
            int cx = 18 + (i % 4) * 145;
            int cy = 42 + (i / 4) * 96;

            var nameBox = new TextBox
            {
                Location = new Point(cx, cy + 49),
                Width = 130,
                TextAlign = HorizontalAlignment.Center,
            };
            SetCueBanner(nameBox, "信号名");
            outGroup.Controls.Add(nameBox);

            var lamp = CreateLamp(TagMap.DigitalOutputs[i]);
            lamp.Location = new Point(cx, cy);
            _outputLamps[i] = lamp;
            outGroup.Controls.Add(lamp);
        }

        // 模拟量输入（写 VW 区）
        _analogInGroup = AddGroup("模拟量输入（写 VW200/VW202）", 15, 353, 600, 140);
        for (int i = 0; i < _analogInputs.Length; i++)
        {
            int idx = i;
            AddLabelFixed(_analogInGroup, TagMap.AnalogInputs[i] + "：", 18, 45 + i * 52, 60);
            var nud = new NumericUpDown
            {
                Location = new Point(88, 41 + i * 52),
                Width = 130,
                Minimum = 0,
                Maximum = 65535,
                Value = 0,
            };
            _analogInputs[i] = nud;
            var btn = new Button { Text = "写入", Location = new Point(230, 40 + i * 52), Width = 90, Height = 30 };
            btn.Click += (s, e) => WriteAnalogInput(idx);
            _analogWriteBtns[i] = btn;
            _analogInGroup.Controls.Add(nud);
            _analogInGroup.Controls.Add(btn);
        }

        // 模拟量输出（读 VW 区），值用只读框显示、固定宽度不溢出
        var analogOutGroup = AddGroup("模拟量输出（读 VW300/VW302）", 625, 353, 600, 140);
        for (int i = 0; i < _analogOutputs.Length; i++)
        {
            AddLabelTo(analogOutGroup, TagMap.AnalogOutputs[i] + " =", 18, 45 + i * 52);
            var val = CreateReadOnlyBox(analogOutGroup, 130, 41 + i * 52, 120);
            val.Text = "0";
            _analogOutputs[i] = val;
        }

        // 寄存器监视（8 个：4 行 × 2 列），值用只读框显示、固定宽度不溢出
        var monGroup = AddGroup("寄存器监视（填地址，如 VW100 / VD100 / M10.0，自动刷新）", 15, 501, 1210, 260);
        string[] cues = { "如 VW100", "如 VD100", "如 M10.0", "如 VB100", "如 VW200", "如 VW202", "如 MD100", "如 VW204" };
        for (int i = 0; i < _monitorAddrs.Length; i++)
        {
            int idx = i;
            int col = i % 2;              // 0=左列，1=右列
            int row = i / 2;
            int x = col == 0 ? 18 : 620;
            int y = 40 + row * 50;

            var tb = new TextBox { Location = new Point(x, y), Width = 150 };
            tb.TextChanged += (s, e) => { lock (_addrLock) _monitorAddrCache[idx] = tb.Text.Trim(); };
            _monitorAddrs[i] = tb;
            monGroup.Controls.Add(tb);
            SetCueBanner(tb, cues[i]);

            var val = CreateReadOnlyBox(monGroup, x + 172, y, 200);
            val.Text = "—";
            _monitorValues[i] = val;
        }

        // 底部信息栏：作者 + 版本（靠右）
        Controls.Add(new Label
        {
            Text = "作者：大卫博士    版本：V1.0",
            AutoSize = false,
            Size = new Size(300, 22),
            Location = new Point(925, 770),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Gray,
        });
    }

    private Label AddLabelTo(Control parent, string text, int x, int y)
    {
        var l = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
        parent.Controls.Add(l);
        return l;
    }

    // 固定宽度标签（右对齐）：避免 AutoSize 在某些显示缩放下把半角字符算成全角、导致标签变宽压到输入框
    private Label AddLabelFixed(Control parent, string text, int x, int y, int w)
    {
        var l = new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(w, 22),
            Location = new Point(x, y),
            TextAlign = ContentAlignment.MiddleRight,
        };
        parent.Controls.Add(l);
        return l;
    }

    private TextBox AddTextBox(Control parent, int x, int y, int w)
    {
        var tb = new TextBox { Location = new Point(x, y), Width = w };
        parent.Controls.Add(tb);
        return tb;
    }

    private Button AddButton(Control parent, string text, int x, int y, int w)
    {
        var b = new Button { Text = text, Location = new Point(x, y), Width = w };
        parent.Controls.Add(b);
        return b;
    }

    private GroupBox AddGroup(string title, int x, int y, int w, int h)
    {
        var g = new GroupBox { Text = title, Location = new Point(x, y), Size = new Size(w, h) };
        g.MouseDown += (s, e) => ActiveControl = null; // 点分组框空白处，让输入框失去焦点
        Controls.Add(g);
        return g;
    }

    private Button CreatePushButton(string text)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(130, 44),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = SystemColors.Control,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = Color.Gray;
        return b;
    }

    // 给单行输入框加灰色占位提示（Windows 原生 cue banner）
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    private static void SetCueBanner(TextBox tb, string cue)
    {
        const int EM_SETCUEBANNER = 0x1501;
        SendMessage(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
    }

    private Label CreateLamp(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(130, 44),
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Gainsboro,
        };
    }

    // 只读显示框：固定宽度、白底带边框、居中，值再多也不溢出
    private TextBox CreateReadOnlyBox(Control parent, int x, int y, int w)
    {
        var tb = new TextBox
        {
            Location = new Point(x, y),
            Width = w,
            Height = 24,
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Center,
        };
        parent.Controls.Add(tb);
        return tb;
    }

    // ======================= 连接 / 断开 =======================

    private async void ConnectPlc()
    {
        string ip = _ipBox.Text.Trim();
        if (ip.Length == 0)
        {
            MessageBox.Show("请先填写 PLC 的 IP 地址。", "提示");
            return;
        }

        ApplyStatus(ConnStatus.Connecting);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            // 连接是阻塞的网络操作，放到后台线程，避免界面卡死；可被「断开」取消
            await Task.Run(async () => await _plc.ConnectAsync(ip, token));

            if (IsDisposed || token.IsCancellationRequested) { _plc.Disconnect(); return; }
            ApplyStatus(ConnStatus.Connected);
            _readFailCount = 0;

            // 启动后台轮询，每 500ms 刷新一次输出和寄存器
            _pollTimer = new System.Threading.Timer(_ => PollTick(), null, 0, 500);
        }
        catch (Exception)
        {
            if (IsDisposed) return;
            // 用户主动取消 → 回「未连接」；否则 → 红色「连接失败」，不弹窗
            ApplyStatus(token.IsCancellationRequested ? ConnStatus.Disconnected : ConnStatus.Failed);
        }
    }

    private void DisconnectPlc()
    {
        _cts?.Cancel();   // 若正在连接，取消连接
        _cts?.Dispose();
        _cts = null;
        StopPolling();
        _plc.Disconnect();
        ApplyStatus(ConnStatus.Disconnected);
    }

    private enum ConnStatus { Disconnected, Connecting, Connected, Failed, Dropped }

    private void ApplyStatus(ConnStatus s)
    {
        switch (s)
        {
            case ConnStatus.Disconnected:
                SetStatus("未连接", Color.Gray);
                _connectBtn.Enabled = true;
                _disconnectBtn.Enabled = true;
                break;
            case ConnStatus.Connecting:
                SetStatus("连接中…", Color.Orange);
                _connectBtn.Enabled = false;
                _disconnectBtn.Enabled = true;   // 连接期间允许中断
                break;
            case ConnStatus.Connected:
                SetStatus("连接成功", Color.ForestGreen);
                _connectBtn.Enabled = false;
                _disconnectBtn.Enabled = true;
                break;
            case ConnStatus.Failed:
                SetStatus("连接失败", Color.Red);
                _connectBtn.Enabled = true;
                _disconnectBtn.Enabled = true;
                break;
            case ConnStatus.Dropped:
                SetStatus("连接已断开", Color.Red);
                _connectBtn.Enabled = true;
                _disconnectBtn.Enabled = true;
                break;
        }
    }

    // 状态条：彩色底 + 白字
    private void SetStatus(string text, Color backColor)
    {
        _statusLabel.Text = text;
        _statusLabel.BackColor = backColor;
        _statusLabel.ForeColor = Color.White;
    }

    // ======================= 写入操作（界面线程） =======================

    private void SetDigitalInput(int idx, bool value, Button btn)
    {
        btn.BackColor = value ? Color.LimeGreen : SystemColors.Control; // 按下亮、松开灭
        WriteDigitalInput(idx, value);
    }

    private void WriteDigitalInput(int idx, bool value)
    {
        if (!_plc.IsConnected) return; // 未连接时只切换按钮视觉，不写 PLC
        try { _plc.WriteBit(TagMap.DigitalInputs[idx], value); }
        catch { /* 写失败静默：断线检测会更新状态，不弹窗打扰 */ }
    }

    private void WriteAnalogInput(int idx)
    {
        if (!_plc.IsConnected) return; // 未连接时不写 PLC
        try { _plc.WriteWord(TagMap.AnalogInputs[idx], (ushort)_analogInputs[idx].Value); }
        catch { /* 写失败静默：断线检测会更新状态，不弹窗打扰 */ }
    }

    // ======================= 后台轮询（后台线程） =======================

    private void PollTick()
    {
        if (IsDisposed) return;

        // 已断开（连接对象没了）：更新状态并停轮询
        if (!_plc.IsConnected)
        {
            StopPolling();
            try { BeginInvoke(new Action(() => { if (!IsDisposed) ApplyStatus(ConnStatus.Dropped); })); }
            catch { }
            return;
        }

        // 防重入：上一轮没读完就跳过本轮，避免弱网时回调堆积
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;

        bool anySuccess = false;
        try
        {
            // 数字输出：每个点位单独容错，读失败保持上次值（不整组灭灯）
            for (int i = 0; i < _outsBuf.Length; i++)
            {
                try { _outsBuf[i] = _plc.ReadBit(TagMap.DigitalOutputs[i]); anySuccess = true; }
                catch { }
            }

            // 模拟量输出：同样逐项容错
            for (int i = 0; i < _aoutsBuf.Length; i++)
            {
                try { _aoutsBuf[i] = _plc.ReadWord(TagMap.AnalogOutputs[i]); anySuccess = true; }
                catch { }
            }

            // 寄存器监视：读地址缓存加锁
            for (int i = 0; i < _monBuf.Length; i++)
            {
                string a;
                lock (_addrLock) { a = _monitorAddrCache[i]; }
                if (string.IsNullOrEmpty(a)) { _monBuf[i] = "—"; continue; }
                try { _monBuf[i] = _plc.ReadValue(a).ToString() ?? "—"; anySuccess = true; }
                catch { _monBuf[i] = "错误"; }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }

        // 断线判定：整轮一次都没读成功，连续 3 轮就判定断线
        if (!anySuccess)
        {
            if (Interlocked.Increment(ref _readFailCount) >= 3)
            {
                StopPolling();
                _plc.Disconnect();
                try { BeginInvoke(new Action(() => { if (!IsDisposed) ApplyStatus(ConnStatus.Dropped); })); }
                catch { }
                return;
            }
        }
        else
        {
            Interlocked.Exchange(ref _readFailCount, 0);
        }

        // 回到界面线程刷新控件
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                for (int i = 0; i < _outsBuf.Length; i++)
                    _outputLamps[i].BackColor = _outsBuf[i] ? Color.LimeGreen : Color.Gainsboro;
                for (int i = 0; i < _aoutsBuf.Length; i++)
                    _analogOutputs[i].Text = _aoutsBuf[i].ToString();
                for (int i = 0; i < _monBuf.Length; i++)
                    _monitorValues[i].Text = _monBuf[i];
            }));
        }
        catch { }
    }

    private void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        ActiveControl = null; // 点窗体空白处，让输入框失去焦点
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopPolling();
        _plc.Disconnect();
        base.OnFormClosing(e);
    }
}
