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
1. `taskkill //F //IM Smart200Panel.exe` —— 关掉正在运行的旧程序，否则发布会报文件被占用
2. `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish`
3. `powershell -Command "Start-Process 'C:\Users\nqq\Desktop\smart2000\publish\Smart200Panel.exe'"`

## PLC 地址约定（S7-200 SMART + S7.Net）
- 虚拟数字输入写 M 区：`M10.0~M10.7`
- 模拟量输入写：`VW200 / VW202`
- 数字输出读：`Q0.0~Q0.7`
- 模拟量输出读：`VW300 / VW302`
- S7.Net 中 V 区映射到 DB1：`VW100 = DB1.DBW100`、`VD100 = DB1.DBD100`、`VB100 = DB1.DBB100`、`V100.0 = DB1.DBX100.0`

## WinForms 高 DPI 布局坑
- 150% 显示缩放下 `Label.AutoSize` 会把半角字符算成全角、标签变宽压到控件，需用 `AutoSize=false` 固定宽度。
- `GroupBox` 的 Location/Size 是外边框，内部控件坐标相对其客户区（顶部偏移约 8px）。
