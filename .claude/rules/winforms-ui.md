---
paths:
  - "Smart200Panel/MainForm.cs"
---

# WinForms 界面规则（业务约定 + 自绘标题栏 + 高 DPI 布局坑）

## 数字输入区业务约定
- 每个输入点都是**点动**，分两种类型：**常开 NO**（常态 0、按下 1）、**常闭 NC**（常态 1、按下 0）。界面上每个点一个切换按钮，默认 NO。
- 顶部「正常模式 / 组合启动」切换：正常模式=点动；组合启动=先选中点（变黄，不写 PLC），再「一键触发」。
- 组合启动的「一键触发」= 全量写：选中的写激活值、未选中的写常态值；约 300ms 后自动复位回常态（给 PLC 一个扫描周期捕捉）。常闭点在组合模式里同样参与。

## 高 DPI 布局坑（150% 缩放实测）
- `Label.AutoSize` 会把半角字符算成全角、标签变宽压到控件，需用 `AutoSize=false` 固定宽度（项目里有 `AddLabelFixed` 辅助方法）。
- `GroupBox` 的 Location/Size 是外边框，内部控件坐标相对其客户区（顶部偏移约 8px）。
- 含中文文字的 `Button` 高度 < 28 会被裁字（「写入」「一键触发」高度 24 时都被切），新增按钮统一「高 ≥ 28 + `TextAlign=MiddleCenter`」；顶部连接区「连接/断开」高度 26 是历史例外，不要模仿。
- `RadioButton` / `ComboBox` 文字难居中、高 DPI 下易被切；需要「居中显示」或「点击切换」时，用 `Button`（MiddleCenter + 固定尺寸）最稳，别用 RadioButton/ComboBox。
- 界面全部纯代码搭建（无设计器文件），新控件跟随现有 `AddGroup/AddButton/AddLabelFixed/CreateReadOnlyBox` 等辅助方法的风格。

## 自绘标题栏（FormBorderStyle.None）约定
- 标题栏和置顶/最小化/关闭三个按钮全部自绘（`BuildTitleBar`）。按钮统一 **46×31**，图标统一矢量绘制（线宽 1.6、10px 图标区），颜色走 `ForeColor`；按钮点击后不显示焦点框（`TitleBarButton` 关掉 Selectable 和焦点提示）。
- 无边框窗体下 `MaximizeBox/MinimizeBox` 属性**会失效**，必须在 `CreateParams` 里改样式位：加 `WS_SYSMENU | WS_MINIMIZEBOX`（保留系统菜单、任务栏菜单、键盘最小化），去掉 `WS_MAXIMIZEBOX`。
- 窗口尺寸固定：`MinimumSize = MaximumSize = 初始尺寸`，并在 `WndProc` 忽略 `SC_MAXIMIZE`；否则 Win+↑ 会把窗口拉大，而自绘按钮坐标是按客户区宽度算死的，会整体错位。
- 分割线画在标题栏最后一行（`TitleBarHeight - 0.5f`），标题栏子控件高度要比标题栏少 1px，否则不透明的按钮/标签背景会把分割线盖掉。
- 置顶状态只改图钉本身（空心/实心填充），不要改按钮背景色，三个按钮的悬停样式保持一致。

## Win32 互操作约定
- `SendMessage` 有两个 DllImport 重载：`SendMessage(IntPtr, int, IntPtr, IntPtr)` 和 `SendMessageText(..., string)`。字符串版**必须写 `EntryPoint = "SendMessageW"`**：只改方法名不写 EntryPoint 会去 user32 找同名函数，启动直接崩（EntryPointNotFoundException）。
- Windows 11 的 DWM 会覆盖 `WM_NCPAINT` 的自绘内容，**原生标题栏加不了自定义按钮**；也别试 `DWMWA_NCRENDERING_POLICY = DWMNCRP_DISABLED`（标题栏会退化成老式蓝色）。要加按钮就整体自绘标题栏。

## 界面验证方法
- 自绘按钮没有文字，靠 `AccessibleName`（置顶/最小化/关闭）用 UI Automation 的 `InvokePattern` 点击验证；控件类名是 `WindowsForms10.BUTTON...`，不要用 `FindWindowEx(..., "Button", ...)`。
- 截图用 `PrintWindow`（窗口自身渲染，不怕被遮挡）；跨进程截图前先 `SetProcessDpiAwarenessContext(-4)`，否则 150% 缩放下逻辑坐标和物理像素对不上。
