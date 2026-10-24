# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# s7-200-smart-panel 面板项目约定

## 项目是什么
一个 Windows 面板程序（C# WinForms + S7.Net），通过以太网（ISO-on-TCP 端口 102，S7 协议）连接真实 S7-200 SMART PLC，用来模拟数字/模拟量输入、显示输出。不是软 PLC 模拟器。

## 架构（三层，改代码先分清改哪层）
- `TagMap.cs`：地址映射表。**唯一改 PLC 地址的地方**，UI 代码不硬编码地址。
- `PlcClient.cs`：S7 通信封装（连接/断开、读位/字、写位/字），内部 `_lock` 串行化所有读写；把 `VW100` 这类地址自动转成 S7.Net 认识的 `DB1.DBW100`。
- `MainForm.cs`：纯代码搭 UI（无设计器文件）+ 后台轮询刷新；窗口是 `FormBorderStyle.None`，标题栏（图标/标题/置顶/最小化/关闭）全部自绘（见 `BuildTitleBar`）。
- `Program.cs`：入口（`Application.Run(new MainForm())`）；`version.txt` 存版本号，`app.ico` 是程序图标。

数据流方向（写/读别搞反）：
- 面板按钮 → **写** M 区（虚拟数字输入）、VW200/VW202（虚拟模拟量输入）。
- PLC → **读** Q 区（数字输出灯）、VW300/VW302（模拟量输出显示）。

## PLC 地址约定（S7-200 SMART + S7.Net）
- 虚拟数字输入写 M 区：`M10.0~M10.7`
- 模拟量输入写：`VW200 / VW202`
- 数字输出读：`Q0.0~Q0.7`
- 模拟量输出读：`VW300 / VW302`
- S7.Net 中 V 区映射到 DB1：`VW100 = DB1.DBW100`、`VD100 = DB1.DBD100`、`VB100 = DB1.DBB100`、`V100.0 = DB1.DBX100.0`

## 环境与常用命令
- 需要 .NET 8 SDK（`dotnet --version` 应 ≥ 8.0，当前 8.0.419）。
- 编译检查：`dotnet build Smart200Panel/Smart200Panel.csproj --nologo`（编辑文件后 PostToolUse Hook 会自动执行同一个命令）。
- 本地调试：`dotnet run --project Smart200Panel`。
- 没有自动化测试，验证靠启动程序看界面 + 真机联调（排查见 `.claude/rules/plc-connection.md`）。
- 界面验证：自绘按钮没有文字，用 UI Automation 按 `AccessibleName`（置顶/最小化/关闭）点击；截图用 `PrintWindow`。具体方法见 `.claude/rules/winforms-ui.md`。

## 发布启动流程
每次改完代码固定流程（顺序不能乱），或直接用 `/publish` 技能（不带参数是单文件版，`framework` 是框架依赖版）。日常交付以单文件版（`publish-single/`）为准，框架依赖版仅用于本机快速验证。成品 exe 统一叫 `S7-200SMART-I／O面板.exe`：Windows 文件名不允许半角 `/`，用全角 `／`；程序集名仍是 `Smart200Panel`，只改文件名不影响运行。

1. `taskkill //F //IM "S7-200SMART-I／O面板.exe" || true` —— 关掉正在运行的旧程序，否则发布会报文件被占用；旧程序没运行时 taskkill 报「找不到进程」是正常的，用 `|| true` 忽略
2. 版本号 +1：`v=$(cat Smart200Panel/version.txt); IFS=. read -r a b c <<< "$v"; echo "$a.$b.$((c+1))" > Smart200Panel/version.txt`
3. 打包单文件版：`dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish-single`
4. 改名：`mv -f "publish-single/Smart200Panel.exe" "publish-single/S7-200SMART-I／O面板.exe"`
5. 启动：`powershell -Command "Start-Process '.\publish-single\S7-200SMART-I／O面板.exe'"`

第 3 步的参数不能省：**必须加 `-p:IncludeNativeLibrariesForSelfExtract=true`**，否则 .NET 原生库（如 System.Drawing 的 `*_cor3.dll`）不会合并进 exe，目录里残留一堆 DLL、不是真单文件；`-p:DebugType=None` 顺带去 `.pdb`。

版本号只在 `Smart200Panel/version.txt` 一处维护（如 `1.0.5`），每次打包前自动 +1，界面标题和右下角作者栏的版本都从程序集读取，不用改代码。

⚠️ `version.txt` 是编译必需文件：csproj 启动时直接读取，缺失会导致构建失败，不要删除。

发布目录有**两个**，别搞混：
- `publish-single/`：单文件自包含版（上面流程用这个），单个 exe、含运行时，拷走即用。日常交付用这个。
- `publish/`：框架依赖版，多个文件，本机快速验证用，目标电脑需装 .NET 8 运行时。打包命令：
  `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish`，然后 `mv -f "publish/Smart200Panel.exe" "publish/S7-200SMART-I／O面板.exe"`。

## Git 约定
- 两个远程：`origin` = Gitee（主仓库），`github` = GitHub（镜像）；直接在 `master` 分支提交，提交后两个远程都 push。
- 发布产物（根目录 `publish/`、`publish-single/`、`Smart200Panel/publish/`）和临时目录（`_diag/`）不纳入 git。

## 细规则已拆分到 `.claude/rules/`（按 paths 触发自动加载）
- `rules/threading.md` — 线程模型与断线判定（读 MainForm/PlcClient 时加载）
- `rules/winforms-ui.md` — 数字输入业务约定 + 高 DPI 布局坑（读 MainForm 时加载）
- `rules/plc-connection.md` — Put/Get 服务器与连接排查 + 通信参数（读 PlcClient/TagMap 时加载）
