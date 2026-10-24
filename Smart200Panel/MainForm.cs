using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Smart200Panel;

/// <summary>
/// 主窗口：连接 PLC，模拟输入（写 M 区），显示输出（读 Q 区），模拟量显示。
/// 界面用代码直接生成（不用设计器文件），方便阅读和修改。
/// </summary>
public class MainForm : Form
{
    private readonly PlcClient _plc = new();
    private readonly string _version = Application.ProductVersion.Split('+')[0];   // 来自 csproj/version.txt

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
    private readonly Button[] _typeBtns = new Button[TagMap.DigitalInputs.Length];  // 类型切换按钮（常开/常闭）
    private readonly bool[] _isClosed = new bool[TagMap.DigitalInputs.Length];      // 每个点是否常闭
    private readonly bool[] _comboSel = new bool[TagMap.DigitalInputs.Length];
    private RadioButton _normalRb = null!;
    private RadioButton _comboRb = null!;
    private Button _triggerBtn = null!;
    private bool _comboMode;   // false=正常模式, true=组合启动
    private bool _comboBusy;   // 组合触发进行中，防重复点击

    // 模拟量输入（写）/ 模拟量输出（读）
    private readonly NumericUpDown[] _analogInputs = new NumericUpDown[TagMap.AnalogInputs.Length];
    private readonly Button[] _analogWriteBtns = new Button[TagMap.AnalogInputs.Length];
    private readonly TextBox[] _analogOutputs = new TextBox[TagMap.AnalogOutputs.Length];

    private System.Threading.Timer _pollTimer;
    private CancellationTokenSource _cts;   // 连接期间用，点「断开」可取消连接
    private int _polling;                        // 1=轮询进行中，防重入
    private int _readFailCount;                  // 连续整轮读失败次数，用于判定断线
    private readonly bool[] _outsBuf = new bool[TagMap.DigitalOutputs.Length];
    private readonly ushort[] _aoutsBuf = new ushort[TagMap.AnalogOutputs.Length];

    public MainForm()
    {
        Text = $"S7-200 SMART I/O 仿真面板 V{_version}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;            // 标题栏由程序自绘（见 BuildTitleBar）
        MaximizeBox = false;
        ClientSize = new Size(930, 500 + TitleBarHeight);  // 内容区仍是 930x500，上方多一条标题栏
        MinimumSize = MaximumSize = Size;                  // 固定尺寸：贴边、拖动都不能改变大小
        Font = new Font("Microsoft YaHei UI", 10f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);   // 任务栏用 exe 内嵌图标

        BuildUi();
        ApplyStatus(ConnStatus.Disconnected);
    }

    // ======================= 界面搭建 =======================

    private void BuildUi()
    {
        BuildContent();
        BuildTitleBar();

        // 先加内容区（Fill）再加标题栏（Top）：WinForms 按相反顺序停靠，标题栏先占顶部
        Controls.Add(_contentPanel);
        Controls.Add(_titleBar);
    }

    private void BuildContent()
    {
        _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
        _contentPanel.MouseDown += (s, e) => ActiveControl = null;   // 点空白处，让输入框失去焦点

        // 顶部连接区（包进「PLC连接」分组框，控件统一垂直居中）
        var connGroup = AddGroup("PLC连接", 11, 9, 908, 64);
        AddLabelFixed(connGroup, "IP地址", 14, 29, 53);
        _ipBox = AddTextBox(connGroup, 74, 25, 165);
        _ipBox.Height = 26;
        _ipBox.Text = "192.168.2.1";
        _connectBtn = AddButton(connGroup, "连接", 246, 25, 75);
        _connectBtn.Height = 26;
        _disconnectBtn = AddButton(connGroup, "断开", 329, 25, 75);
        _disconnectBtn.Height = 26;
        _statusLabel = new Label
        {
            Text = "未连接",
            AutoSize = false,
            Size = new Size(83, 26),
            Location = new Point(413, 25),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Gray,
            ForeColor = Color.White,
        };
        connGroup.Controls.Add(_statusLabel);

        _connectBtn.Click += (s, e) => ConnectPlc();
        _disconnectBtn.Click += (s, e) => DisconnectPlc();

        // 数字输入（写 M 区）。顶部是模式切换，下面是 8 个点位（按钮 + 信号名 + 常开/常闭圆点）
        _inputGroup = AddGroup("数字输入（写 M10.0~M10.7）", 11, 80, 448, 260);

        // 模式切换：正常模式（点动）/ 组合启动（先选点，再一键触发）
        _normalRb = new RadioButton { Text = "正常模式", AutoSize = true, Location = new Point(14, 24) };
        _comboRb = new RadioButton { Text = "组合启动", AutoSize = true, Location = new Point(100, 24) };
        _inputGroup.Controls.Add(_normalRb);
        _inputGroup.Controls.Add(_comboRb);
        _normalRb.Checked = true;   // 默认正常模式（先设初值再挂事件，避免构建时误触发写入）

        _triggerBtn = new Button { Text = "一键触发", Location = new Point(196, 22), Size = new Size(88, 28), Visible = false };
        _inputGroup.Controls.Add(_triggerBtn);
        _triggerBtn.Click += (s, e) => TriggerCombo();
        _normalRb.CheckedChanged += (s, e) => { if (_normalRb.Checked) SetMode(false); };
        _comboRb.CheckedChanged += (s, e) => { if (_comboRb.Checked) SetMode(true); };

        for (int i = 0; i < _inputBtns.Length; i++)
        {
            int idx = i;
            int cx = 14 + (i % 4) * 108;
            int cy = 60 + (i / 4) * 92;

            var btn = CreatePushButton(TagMap.DigitalInputs[i]);
            btn.Location = new Point(cx, cy);
            btn.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left && !_comboMode) SetDigitalInput(idx, true, btn); };
            btn.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left && !_comboMode) SetDigitalInput(idx, false, btn); };
            btn.MouseLeave += (s, e) => { if (!_comboMode) SetDigitalInput(idx, false, btn); };
            btn.Click += (s, e) => { if (_comboMode) ToggleComboSelect(idx); };
            _inputBtns[i] = btn;
            _inputGroup.Controls.Add(btn);

            // 常开/常闭切换按钮（NO=常开、NC=常闭）
            var typeBtn = MakeTypeButton("NO", new Point(cx, cy + 34));
            typeBtn.Click += (s, e) => SetType(idx, !_isClosed[idx]);
            _typeBtns[idx] = typeBtn;
            _inputGroup.Controls.Add(typeBtn);
            UpdateTypeButtons(idx);   // 默认 NO（常开）

            // 信号名（放在切换按钮下方）
            var nameBox = new TextBox
            {
                Location = new Point(cx, cy + 64),
                Width = 98,
                TextAlign = HorizontalAlignment.Center,
            };
            SetCueBanner(nameBox, "信号名");
            _inputGroup.Controls.Add(nameBox);
        }

        // 数字输出（读 Q 区）。每个点位：名称框在上、指示灯在下，同宽左对齐
        var outGroup = AddGroup("数字输出（读 Q0.0~Q0.7）", 469, 80, 448, 190);
        for (int i = 0; i < _outputLamps.Length; i++)
        {
            int cx = 14 + (i % 4) * 108;
            int cy = 40 + (i / 4) * 78;

            var nameBox = new TextBox
            {
                Location = new Point(cx, cy + 36),
                Width = 98,
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
        _analogInGroup = AddGroup("模拟量输入（写 VW200/VW202）", 11, 348, 448, 105);
        for (int i = 0; i < _analogInputs.Length; i++)
        {
            int idx = i;
            AddLabelFixed(_analogInGroup, TagMap.AnalogInputs[i] + "：", 14, 34 + i * 40, 65);
            var nud = new NumericUpDown
            {
                Location = new Point(85, 31 + i * 40),
                Width = 100,
                Minimum = 0,
                Maximum = 65535,
                Value = 0,
            };
            _analogInputs[i] = nud;
            var btn = new Button { Text = "写入", Location = new Point(192, 31 + i * 40), Width = 70, Height = 28 };
            btn.Click += (s, e) => WriteAnalogInput(idx);
            _analogWriteBtns[i] = btn;
            _analogInGroup.Controls.Add(nud);
            _analogInGroup.Controls.Add(btn);
        }

        // 模拟量输出（读 VW 区），值用只读框显示、固定宽度不溢出
        var analogOutGroup = AddGroup("模拟量输出（读 VW300/VW302）", 469, 348, 448, 105);
        for (int i = 0; i < _analogOutputs.Length; i++)
        {
            AddLabelTo(analogOutGroup, TagMap.AnalogOutputs[i] + " =", 14, 34 + i * 40);
            var val = CreateReadOnlyBox(analogOutGroup, 100, 31 + i * 40, 95);
            val.Text = "0";
            _analogOutputs[i] = val;
        }

        // 底部信息栏：作者 + 版本（靠右）
        _contentPanel.Controls.Add(new Label
        {
            Text = $"作者：niuqiangqiang    版本：V{_version}",
            AutoSize = false,
            Size = new Size(280, 22),
            Location = new Point(635, 470),
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
        _contentPanel.Controls.Add(g);
        return g;
    }

    private Button CreatePushButton(string text)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(98, 32),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = SystemColors.Control,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = Color.Gray;
        return b;
    }

    // 常开/常闭切换按钮：固定尺寸、文字居中，避免高 DPI 下文字被裁
    private Button MakeTypeButton(string text, Point loc)
    {
        var b = new Button
        {
            Text = text,
            Location = loc,
            Size = new Size(98, 28),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = SystemColors.Control,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = Color.Gray;
        return b;
    }

    // 给单行输入框加灰色占位提示（Windows 原生 cue banner）
    // 这是 user32 SendMessage 的字符串版重载，与标题栏用的 IntPtr 版区分开，避免同名两套声明改漏
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageText(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    private static void SetCueBanner(TextBox tb, string cue)
    {
        const int EM_SETCUEBANNER = 0x1501;
        SendMessageText(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
    }

    private Label CreateLamp(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(98, 32),
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

    // ======================= 自定义标题栏 =======================

    // Windows 11 的原生标题栏由 DWM 合成，应用自绘会被覆盖，所以整个标题栏由程序自己搭建：
    // 图标 + 标题 + 置顶 + 最小化 + 关闭。窗口用 FormBorderStyle.None。

    private const int TitleBarHeight = 32;
    private const int TitleButtonWidth = 46;                  // 三个窗口按钮等宽，紧贴排列
    private const int TitleButtonHeight = TitleBarHeight - 1; // 少 1px，让出底部那条分割线
    private const float IconHalf = 5f;                        // 图标可视区半径（10x10）
    private const float IconStroke = 1.6f;                    // 统一线宽，保证三个图标粗细一致
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MAXIMIZE = 0xF030;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_SYSMENU = 0x00080000;

    private enum TitleIcon { Pin, Minimize, Close }

    // 自绘图标的标题栏按钮：屏蔽系统焦点虚线框（点完按钮会留一圈虚线，很难看）
    private sealed class TitleBarButton : Button
    {
        public TitleBarButton()
        {
            SetStyle(ControlStyles.Selectable, false);   // 不参与焦点，点完不会留下焦点框
        }

        protected override bool ShowFocusCues => false;
    }

    private static readonly Color IconColor = Color.FromArgb(70, 70, 70);

    private Panel _titleBar = null!;
    private Panel _contentPanel = null!;
    private PictureBox _iconBox = null!;
    private Button _pinBtn = null!;
    private ToolTip _tip = null!;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // 无边框窗体下 MaximizeBox/MinimizeBox 属性会失效，直接改窗口样式位
            cp.Style |= WS_SYSMENU | WS_MINIMIZEBOX;   // 保留系统菜单（Alt+Space）和任务栏窗口菜单
            cp.Style &= ~WS_MAXIMIZEBOX;               // 禁止最大化，避免自绘按钮位置错乱
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;   // DWMWCP_ROUND：Win11 圆角；旧系统调用失败不影响运行
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    protected override void WndProc(ref Message m)
    {
        // 兜底：任何来源的最大化请求都忽略，窗口尺寸固定不变
        if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == SC_MAXIMIZE) return;
        base.WndProc(ref m);
    }

    private void BuildTitleBar()
    {
        Color barColor = Color.FromArgb(243, 243, 243);
        Color textColor = Color.FromArgb(60, 60, 60);

        _titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = TitleBarHeight,
            BackColor = barColor,
        };
        _titleBar.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 220, 220));
            // 半像素偏移，让 1px 分割线正好落在最后一行，不会被抗锯齿糊成两行
            e.Graphics.DrawLine(pen, 0, TitleBarHeight - 0.5f, _titleBar.Width, TitleBarHeight - 0.5f);
        };

        _iconBox = new PictureBox
        {
            Location = new Point(8, (TitleBarHeight - 16) / 2),
            Size = new Size(16, 16),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };
        if (Icon != null)
        {
            using var small = new Icon(Icon, 16, 16);
            _iconBox.Image = small.ToBitmap();
        }

        var titleLabel = new Label
        {
            Text = Text,
            AutoSize = false,
            Location = new Point(30, 0),
            Size = new Size(650, TitleBarHeight - 1),   // 不压住底部那条分割线
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = textColor,
            BackColor = Color.Transparent,
        };

        // 右侧三个窗口按钮：置顶 / 最小化 / 关闭，等宽紧贴排列（从右往左数）
        _tip = new ToolTip();

        _pinBtn = MakeTitleBarButton(TitleIcon.Pin, barColor);
        _pinBtn.Location = new Point(ClientSize.Width - TitleButtonWidth * 3, 0);
        _pinBtn.AccessibleName = "置顶";
        _pinBtn.Click += (s, e) => { TopMost = !TopMost; UpdatePinButtonVisual(); };
        _tip.SetToolTip(_pinBtn, "置顶（窗口始终显示在最前）");

        var minBtn = MakeTitleBarButton(TitleIcon.Minimize, barColor);
        minBtn.Location = new Point(ClientSize.Width - TitleButtonWidth * 2, 0);
        minBtn.AccessibleName = "最小化";
        minBtn.Click += (s, e) => WindowState = FormWindowState.Minimized;
        _tip.SetToolTip(minBtn, "最小化");

        var closeBtn = MakeTitleBarButton(TitleIcon.Close, barColor);
        closeBtn.Location = new Point(ClientSize.Width - TitleButtonWidth, 0);
        closeBtn.AccessibleName = "关闭";
        closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
        closeBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(200, 10, 30);
        closeBtn.MouseEnter += (s, e) => { closeBtn.ForeColor = Color.White; closeBtn.Invalidate(); };
        closeBtn.MouseLeave += (s, e) => { closeBtn.ForeColor = IconColor; closeBtn.Invalidate(); };
        closeBtn.Click += (s, e) => Close();
        _tip.SetToolTip(closeBtn, "关闭");

        _titleBar.Controls.Add(_iconBox);
        _titleBar.Controls.Add(titleLabel);
        _titleBar.Controls.Add(_pinBtn);
        _titleBar.Controls.Add(minBtn);
        _titleBar.Controls.Add(closeBtn);

        // 按住标题栏空白处拖动窗口（使用系统原生移动，支持贴边）
        _titleBar.MouseDown += StartWindowDrag;
        titleLabel.MouseDown += StartWindowDrag;
        _iconBox.MouseDown += StartWindowDrag;

        UpdatePinButtonVisual();
    }

    private Button MakeTitleBarButton(TitleIcon kind, Color backColor)
    {
        var b = new TitleBarButton
        {
            Size = new Size(TitleButtonWidth, TitleButtonHeight),
            FlatStyle = FlatStyle.Flat,
            ForeColor = IconColor,
            BackColor = backColor,
            UseVisualStyleBackColor = false,
            TabStop = false,
            Cursor = Cursors.Hand,
            Tag = kind,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 229, 229);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(209, 209, 209);
        b.Paint += TitleButtonPaint;
        return b;
    }

    private void StartWindowDrag(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    // 三个窗口按钮统一用矢量绘制：同一图标框、同一线宽，大小和视觉重量才一致
    private void TitleButtonPaint(object sender, PaintEventArgs e)
    {
        var btn = (Button)sender;
        var kind = (TitleIcon)btn.Tag;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float cx = btn.Width / 2f;
        float cy = TitleBarHeight / 2f;   // 按整条标题栏居中（按钮高度少 1px，不能用按钮自身高度算）

        using var pen = new Pen(btn.ForeColor, IconStroke)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        switch (kind)
        {
            case TitleIcon.Minimize:
                g.DrawLine(pen, cx - IconHalf, cy, cx + IconHalf, cy);
                break;

            case TitleIcon.Close:
                g.DrawLine(pen, cx - IconHalf, cy - IconHalf, cx + IconHalf, cy + IconHalf);
                g.DrawLine(pen, cx - IconHalf, cy + IconHalf, cx + IconHalf, cy - IconHalf);
                break;

            case TitleIcon.Pin:
                using (var path = PinPath(cx, cy))
                {
                    if (TopMost)
                    {
                        // 已置顶：把图钉内部填实（不再用整块蓝底）
                        using var brush = new SolidBrush(btn.ForeColor);
                        g.FillPath(brush, path);
                    }
                    g.DrawPath(pen, path);
                }
                break;
        }
    }

    // 图钉轮廓：原始图形按 20 单位见方设计（宽 12、高 14），缩到约 10x12 后以 (cx, cy) 为中心
    private static GraphicsPath PinPath(float cx, float cy)
    {
        const float scale = 10f / 12f;
        PointF P(float x, float y) => new(cx + (x - 10f) * scale, cy + (y - 10f) * scale);

        var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            P(6.5f, 3f), P(13.5f, 3f),      // 顶部横帽
            P(12.5f, 6f), P(16f, 11f),      // 右侧收腰、外扩
            P(11.5f, 11f), P(10f, 17f),     // 底部右缘、针尖
            P(8.5f, 11f), P(4f, 11f),       // 针尖回左、底部左缘
            P(7.5f, 6f),                    // 左侧外扩回腰
        });
        path.CloseFigure();
        return path;
    }

    // 置顶状态只影响图钉本身（实心/空心），按钮背景与最小化、关闭保持一致
    private void UpdatePinButtonVisual()
    {
        _pinBtn.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip?.Dispose();
            _iconBox?.Image?.Dispose();   // PictureBox.Dispose 不会释放 Image，这里手动释放
        }
        base.Dispose(disposing);
    }

    // ======================= 连接 / 断开 =======================

    private async void ConnectPlc()
    {
        string ip = _ipBox.Text.Trim();
        if (ip.Length == 0)
        {
            MessageBox.Show(this, "请先填写 PLC 的 IP 地址。", "提示");
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
            WriteAllNormal();   // 连接成功后把所有输入点写到常态值（常开0/常闭1）
            for (int i = 0; i < _inputBtns.Length; i++) RefreshButtonColor(i);   // 重连后输入按钮恢复常态色

            // 启动后台轮询，每 50ms 刷新一次输出和寄存器
            _pollTimer = new System.Threading.Timer(_ => PollTick(), null, 0, 50);
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
        WriteAllNormal();   // 断开前把所有输入点写回常态值
        _plc.Disconnect();
        ResetDisplay();     // 断开后界面复位（灯灭、数值归零、按钮回常态）
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

    // pressed=true 表示「按下」、false 表示「松开」；具体写的值由常开/常闭决定
    private void SetDigitalInput(int idx, bool pressed, Button btn)
    {
        bool value = pressed ? !NormallyClosed(idx) : NormallyClosed(idx); // 按下=激活值，松开=常态值
        btn.BackColor = value ? Color.LimeGreen : SystemColors.Control;
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

    // ======================= 数字输入：模式 / 常开常闭 / 组合启动 =======================

    // 每个点两个小按钮单选：NC 选中 = 常闭点（常态=1 导通，按下=0 断开）
    private bool NormallyClosed(int idx) => _isClosed[idx];

    // 把所有输入点写成常态值（常开=0，常闭=1）
    private void WriteAllNormal()
    {
        if (!_plc.IsConnected) return;
        for (int i = 0; i < TagMap.DigitalInputs.Length; i++)
        {
            try { _plc.WriteBit(TagMap.DigitalInputs[i], NormallyClosed(i)); }
            catch { return; }   // 一次写失败说明连接已断，剩余点不再写，避免逐个超时卡界面
        }
    }

    private void SetMode(bool combo)
    {
        if (_comboMode == combo) return;
        _comboMode = combo;

        // 清空组合选择，并把所有点写回常态值
        for (int i = 0; i < _comboSel.Length; i++) _comboSel[i] = false;
        WriteAllNormal();

        for (int i = 0; i < _inputBtns.Length; i++) RefreshButtonColor(i);
        _triggerBtn.Visible = combo;
    }

    private void RefreshButtonColor(int idx)
    {
        bool normal = NormallyClosed(idx);   // 常态值：常闭=1(绿)，常开=0(灰)
        Color c;
        if (_comboMode)
            c = _comboSel[idx] ? Color.Gold : (normal ? Color.LimeGreen : SystemColors.Control);
        else
            c = normal ? Color.LimeGreen : SystemColors.Control;
        _inputBtns[idx].BackColor = c;
    }

    private void ToggleComboSelect(int idx)
    {
        _comboSel[idx] = !_comboSel[idx];
        RefreshButtonColor(idx);
    }

    // 点击切换按钮在常开/常闭之间切换
    private void SetType(int idx, bool closed)
    {
        if (_isClosed[idx] == closed) return;
        _isClosed[idx] = closed;
        UpdateTypeButtons(idx);
        OnTypeChanged(idx);
    }

    // 刷新切换按钮的文字和高亮：NO=灰、NC=浅蓝
    private void UpdateTypeButtons(int idx)
    {
        bool closed = _isClosed[idx];
        _typeBtns[idx].Text = closed ? "NC" : "NO";
        _typeBtns[idx].BackColor = closed ? Color.LightSteelBlue : SystemColors.Control;
    }

    // 常开/常闭切换：正常模式立即写新常态值；组合模式只改配置不写 PLC（等一键触发统一写）
    private void OnTypeChanged(int idx)
    {
        if (_comboMode)
            _comboSel[idx] = false;   // 组合模式下切类型，清掉该点选中
        else if (_plc.IsConnected)
            try { _plc.WriteBit(TagMap.DigitalInputs[idx], NormallyClosed(idx)); } catch { }
        RefreshButtonColor(idx);
    }

    // 一键触发：每个点都写一遍——选中的写激活值，没选中的写常态值；300ms 后自动复位回常态
    private async void TriggerCombo()
    {
        if (!_plc.IsConnected || _comboBusy) return;

        _comboBusy = true;
        _triggerBtn.Enabled = false;   // 触发期间禁点，防止 300ms 内重复触发互相干扰
        try
        {
            for (int i = 0; i < TagMap.DigitalInputs.Length; i++)
            {
                bool v = _comboSel[i] ? !NormallyClosed(i) : NormallyClosed(i);
                try { _plc.WriteBit(TagMap.DigitalInputs[i], v); } catch { }
            }

            // 等一个 PLC 扫描周期左右，再自动清回常态、清空选中（给 PLC 足够时间捕捉「1」）
            await Task.Delay(300);
            if (IsDisposed || !_comboMode) return;
            for (int i = 0; i < _comboSel.Length; i++) _comboSel[i] = false;
            WriteAllNormal();
            for (int i = 0; i < _inputBtns.Length; i++) RefreshButtonColor(i);
        }
        finally
        {
            _comboBusy = false;
            if (!IsDisposed) _triggerBtn.Enabled = true;
        }
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
            // 数字输出：一次读 Q 字节拆 8 位（字节号由 TagMap.DigitalOutputs 第一个地址推出）
            try
            {
                bool[] outs = _plc.ReadOutputByteBits(TagMap.DigitalOutputByte);
                for (int i = 0; i < _outsBuf.Length; i++) _outsBuf[i] = outs[i];
                anySuccess = true;
            }
            catch { }

            // 模拟量输出：一次读连续字（起始字节、字数由 TagMap.AnalogOutputs 推出）
            try
            {
                ushort[] aouts = _plc.ReadVWords(TagMap.AnalogOutputStartByte, TagMap.AnalogOutputs.Length);
                for (int i = 0; i < _aoutsBuf.Length; i++) _aoutsBuf[i] = aouts[i];
                anySuccess = true;
            }
            catch { }

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
                try { BeginInvoke(new Action(() => { if (!IsDisposed) { ResetDisplay(); ApplyStatus(ConnStatus.Dropped); } })); }
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
            }));
        }
        catch { }
    }

    private void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    // 断开/断线后把界面显示复位：输出灯灭、模拟量归零、输入按钮熄灭、常开/常闭回到 NO
    private void ResetDisplay()
    {
        for (int i = 0; i < _outputLamps.Length; i++)
            _outputLamps[i].BackColor = Color.Gainsboro;
        for (int i = 0; i < _analogOutputs.Length; i++)
            _analogOutputs[i].Text = "0";
        for (int i = 0; i < _comboSel.Length; i++)
            _comboSel[i] = false;
        for (int i = 0; i < _isClosed.Length; i++)
            _isClosed[i] = false;   // 常开/常闭类型也复位回常开
        for (int i = 0; i < _inputBtns.Length; i++)
            _inputBtns[i].BackColor = SystemColors.Control;   // 输入按钮全部熄灭
        for (int i = 0; i < _typeBtns.Length; i++)
            UpdateTypeButtons(i);   // NO/NC 切换按钮回 NO
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        WriteAllNormal();   // 关程序前把所有输入点写回常态值
        StopPolling();
        _plc.Disconnect();
        base.OnFormClosing(e);
    }
}
