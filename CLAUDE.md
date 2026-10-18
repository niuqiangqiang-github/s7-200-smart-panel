# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# smart2000 面板项目约定

## 项目是什么
一个 Windows 面板程序（C# WinForms + S7.Net），通过以太网（ISO-on-TCP 端口 102，S7 协议）连接真实 S7-200 SMART PLC，用来模拟数字/模拟量输入、显示输出、监视寄存器。不是软 PLC 模拟器。

## 架构（三层，改代码先分清改哪层）
- `TagMap.cs`：地址映射表。**唯一改 PLC 地址的地方**，UI 代码不硬编码地址。
- `PlcClient.cs`：S7 通信封装（连接/断开、读位/字、写位/字、读任意地址），内部 `_lock` 串行化所有读写；把 `VW100` 这类地址自动转成 S7.Net 认识的 `DB1.DBW100`。
- `MainForm.cs`：纯代码搭 UI（无设计器文件）+ 后台轮询刷新。

数据流方向（写/读别搞反）：
- 面板按钮 → **写** M 区（虚拟数字输入）、VW200/VW202（虚拟模拟量输入）。
- PLC → **读** Q 区（数字输出灯）、VW300/VW302（模拟量输出显示）、寄存器监视读任意地址。

## 线程模型（改轮询/写入时最容易踩的坑）
- `System.Threading.Timer` 后台轮询（500ms），结果用 `BeginInvoke` 回 UI 线程刷新控件，不能直接在回调里碰控件。
- `_polling` 字段用 `Interlocked` 防重入：上一轮没读完就跳过本轮。
- 连续 3 轮整轮读失败 → 判定断线，状态变红「连接已断开」并停轮询，需手动点「连接」重连。
- 每个点位单独 try/catch，某一次读失败保持上次值，不整组灭灯。

## 发布启动流程
每次改完代码固定三步（顺序不能乱）：
1. `taskkill //F //IM Smart200Panel.exe || true` —— 关掉正在运行的旧程序，否则发布会报文件被占用；旧程序没运行时 taskkill 报「找不到进程」是正常的，用 `|| true` 忽略
2. `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish`
3. `powershell -Command "Start-Process 'C:\Users\nqq\Desktop\smart2000\publish\Smart200Panel.exe'"`

发布目录有**两个**，别搞混：
- `publish/`：框架依赖版（上面三步用这个），多个文件，目标电脑需装 .NET 8 运行时。
- `publish-single/`：单文件自包含版（单个 exe，含运行时，拷走即用），打包命令：
  `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-single`

## PLC 地址约定（S7-200 SMART + S7.Net）
- 虚拟数字输入写 M 区：`M10.0~M10.7`
- 模拟量输入写：`VW200 / VW202`
- 数字输出读：`Q0.0~Q0.7`
- 模拟量输出读：`VW300 / VW302`
- S7.Net 中 V 区映射到 DB1：`VW100 = DB1.DBW100`、`VD100 = DB1.DBD100`、`VB100 = DB1.DBB100`、`V100.0 = DB1.DBX100.0`

## PLC 通信设置与连接排查（连不上先看这里）

### Put/Get 服务器（最常见的坑）
- S7-200 SMART 默认「禁用 Put/Get 服务器」→ S7.Net 连不上，报 TCP 10054（COTP 握手阶段被 PLC 主动断开）。
- 启用：Micro/WIN SMART → 系统块 → 安全 → 通信设置 → 勾「启用 Put/Get 服务器」→ 下载系统块 → **断电重启 PLC**（只下载不重启不生效）。
- 关键坑：这个勾存在「项目文件(.smart)」里，不在 PLC 里；换项目/重新下载会把 PLC 里已启用的设置覆盖回禁用 → 又连不上。
- 防错口诀：先勾 → 下载 → 断电重启 → 点连接。

### 连不上排查顺序
1. ping PLC IP（192.168.2.1）：不通 = 查网线/PLC 上电/电脑网段；通 = 网络 OK，往下查。
2. 看 Put/Get 勾是否还在（换项目/下载常把它冲掉）。

### PLC IP
- 真实 PLC IP = 192.168.2.1（面板默认填这个）。

## 数字输入区业务约定（改 UI 前先懂）
- 每个输入点都是**点动**，分两种类型：**常开 NO**（常态 0、按下 1）、**常闭 NC**（常态 1、按下 0）。界面上每个点一个切换按钮，默认 NO。
- 顶部「正常模式 / 组合启动」切换：正常模式=点动；组合启动=先选中点（变黄，不写 PLC），再「一键触发」。
- 组合启动的「一键触发」= 全量写：选中的写激活值、未选中的写常态值；约 300ms 后自动复位回常态（给 PLC 一个扫描周期捕捉）。常闭点在组合模式里同样参与。
- 寄存器监视按地址类型显示进制：位 → `0/1`，字节（VB）→ 十六进制（`0x1A`），字/双字（VW/VD）→ 十进制。

## WinForms 高 DPI 布局坑
- 150% 显示缩放下 `Label.AutoSize` 会把半角字符算成全角、标签变宽压到控件，需用 `AutoSize=false` 固定宽度。
- `GroupBox` 的 Location/Size 是外边框，内部控件坐标相对其客户区（顶部偏移约 8px）。
- `Button` 高度 < 28 会裁掉中文文字（「写入」「一键触发」高度 24 时都被切），统一用「高 ≥ 28 + `TextAlign=MiddleCenter`」。
- `RadioButton` / `ComboBox` 文字难居中、高 DPI 下易被切；需要「居中显示」或「点击切换」时，用 `Button`（MiddleCenter + 固定尺寸）最稳，别用 RadioButton/ComboBox。

## Git 约定
- 项目托管 Gitee，直接在 `master` 分支提交并 push。
- 发布产物（`publish/`、`publish-single/`）和临时目录（`_diag/`）不纳入 git。
