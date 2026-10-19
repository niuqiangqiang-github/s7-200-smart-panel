# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# smart2000 面板项目约定

## 项目是什么
一个 Windows 面板程序（C# WinForms + S7.Net），通过以太网（ISO-on-TCP 端口 102，S7 协议）连接真实 S7-200 SMART PLC，用来模拟数字/模拟量输入、显示输出。不是软 PLC 模拟器。

## 架构（三层，改代码先分清改哪层）
- `TagMap.cs`：地址映射表。**唯一改 PLC 地址的地方**，UI 代码不硬编码地址。
- `PlcClient.cs`：S7 通信封装（连接/断开、读位/字、写位/字），内部 `_lock` 串行化所有读写；把 `VW100` 这类地址自动转成 S7.Net 认识的 `DB1.DBW100`。
- `MainForm.cs`：纯代码搭 UI（无设计器文件）+ 后台轮询刷新。

数据流方向（写/读别搞反）：
- 面板按钮 → **写** M 区（虚拟数字输入）、VW200/VW202（虚拟模拟量输入）。
- PLC → **读** Q 区（数字输出灯）、VW300/VW302（模拟量输出显示）。

## PLC 地址约定（S7-200 SMART + S7.Net）
- 虚拟数字输入写 M 区：`M10.0~M10.7`
- 模拟量输入写：`VW200 / VW202`
- 数字输出读：`Q0.0~Q0.7`
- 模拟量输出读：`VW300 / VW302`
- S7.Net 中 V 区映射到 DB1：`VW100 = DB1.DBW100`、`VD100 = DB1.DBD100`、`VB100 = DB1.DBB100`、`V100.0 = DB1.DBX100.0`

## 发布启动流程
每次改完代码固定三步（顺序不能乱），或直接用 `/publish` 技能（single 参数打包单文件版）：
1. `taskkill //F //IM Smart200Panel.exe || true` —— 关掉正在运行的旧程序，否则发布会报文件被占用；旧程序没运行时 taskkill 报「找不到进程」是正常的，用 `|| true` 忽略
2. `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish`
3. `powershell -Command "Start-Process 'C:\Users\nqq\Desktop\smart2000\publish\Smart200Panel.exe'"`

发布目录有**两个**，别搞混：
- `publish/`：框架依赖版（上面三步用这个），多个文件，目标电脑需装 .NET 8 运行时。
- `publish-single/`：单文件自包含版（单个 exe，含运行时，拷走即用），打包命令：
  `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish-single`
  注意：**必须加 `-p:IncludeNativeLibrariesForSelfExtract=true`**，否则 WPF 原生库（`*_cor3.dll`）不会合并进 exe，目录里残留一堆 DLL、不是真单文件；`-p:DebugType=None` 顺带去 `.pdb`。

## Git 约定
- 项目托管 Gitee，直接在 `master` 分支提交并 push。
- 发布产物（根目录 `publish/`、`publish-single/`、`Smart200Panel/publish/`）和临时目录（`_diag/`）不纳入 git。

## 细规则已拆分到 `.claude/rules/`（按 paths 触发自动加载）
- `rules/threading.md` — 线程模型与断线判定（读 MainForm/PlcClient 时加载）
- `rules/winforms-ui.md` — 数字输入业务约定 + 高 DPI 布局坑（读 MainForm 时加载）
- `rules/plc-connection.md` — Put/Get 服务器与连接排查 + 通信参数（读 PlcClient/TagMap 时加载）
