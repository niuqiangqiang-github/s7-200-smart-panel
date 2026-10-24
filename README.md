# S7-200 SMART I/O 仿真面板

一个 Windows 面板程序，通过以太网连接**真实的西门子 S7-200 SMART PLC**（不是软 PLC 模拟器），用来在电脑上直接调试 PLC 的输入输出。

## 能干什么

- 🎛️ **模拟数字量输入** —— 点按钮把信号写进 PLC 的 M 区
- 📈 **模拟模拟量输入** —— 填数值写进 PLC 的模拟量输入
- 💡 **显示数字量输出** —— 实时读 PLC 的 Q 区，用指示灯显示
- 📊 **显示模拟量输出** —— 实时读 PLC 的模拟量输出值

## 技术栈

| 项目 | 说明 |
|------|------|
| 语言 / 界面 | C# / WinForms |
| 运行环境 | .NET 8（Windows） |
| PLC 通信库 | S7.Net（S7netplus 0.20.0） |
| 通信协议 | S7 协议（ISO-on-TCP，端口 102） |

## 如何运行

需要先安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

日常交付用**单文件版**（自带运行时，拷到任意 Windows 电脑即可运行）；本机快速验证可以用框架依赖版。

### 方式一：单文件版（推荐）

按顺序执行，顺序不能乱：

```bash
# 1. 关掉正在运行的旧程序（旧程序没运行时提示找不到进程属正常）
taskkill //F //IM "S7-200SMART-I／O面板.exe" || true

# 2. 版本号 +1（界面标题和右下角的版本显示会自动跟着变）
v=$(cat Smart200Panel/version.txt); IFS=. read -r a b c <<< "$v"; echo "$a.$b.$((c+1))" > Smart200Panel/version.txt

# 3. 打包单文件版
dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish-single

# 4. 改名（Windows 文件名不允许半角 /，这里用全角 ／）
mv -f "publish-single/Smart200Panel.exe" "publish-single/S7-200SMART-I／O面板.exe"

# 5. 启动
powershell -Command "Start-Process '.\publish-single\S7-200SMART-I／O面板.exe'"
```

说明：

- `-p:IncludeNativeLibrariesForSelfExtract=true` 不能省，否则原生库不会合进 exe，目录里会残留一堆 DLL，就不是真正的单文件了。
- 产物只有 `publish-single/S7-200SMART-I／O面板.exe` 一个文件，拷走即用。

### 方式二：框架依赖版（本机快速验证）

```bash
taskkill //F //IM "S7-200SMART-I／O面板.exe" || true
dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish
mv -f "publish/Smart200Panel.exe" "publish/S7-200SMART-I／O面板.exe"
powershell -Command "Start-Process '.\publish\S7-200SMART-I／O面板.exe'"
```

这个版本体积小，但目标电脑必须先装 .NET 8 运行时。

### 本地调试

```bash
dotnet run --project Smart200Panel
```

程序打开后，在「PLC连接」区填 PLC 的 IP 地址，点「连接」即可。

## 连不上 PLC？先设这个（Put/Get 服务器）

S7-200 SMART 出厂默认**禁止电脑通过以太网读写数据**，不改的话面板连不上。

设置一次即可（换项目或重新下载后可能要重设）：

1. 打开 **STEP 7-Micro/WIN SMART** 软件，连上 PLC。
2. 左侧点「系统块」→「安全」→「通信设置」。
3. 勾上 **「启用 Put/Get 服务器」**。
4. 下载系统块。
5. **给 PLC 断电再上电**（最关键，只下载不重启不生效）。

> 注意：这个勾是存在**项目文件**里的，不是存在 PLC 里。换了项目、或重新下载程序，可能会把勾冲掉，导致又连不上——连不上时先回来检查这个勾。

排查口诀：**先勾 → 下载 → 断电重启 → 点连接**。

## PLC 地址约定

| 功能 | 方向 | 地址 |
|------|------|------|
| 虚拟数字输入 | 面板 → PLC（写） | `M10.0` ~ `M10.7` |
| 模拟量输入 | 面板 → PLC（写） | `VW200` / `VW202` |
| 数字输出 | PLC → 面板（读） | `Q0.0` ~ `Q0.7` |
| 模拟量输出 | PLC → 面板（读） | `VW300` / `VW302` |

> S7.Net 里 V 区映射到 DB1：`VW100` = `DB1.DBW100`、`VD100` = `DB1.DBD100`、`VB100` = `DB1.DBB100`、`V100.0` = `DB1.DBX100.0`。

## 目录结构

```
s7-200-smart-panel/
├── Smart200Panel/      # 主程序（WinForms 面板）
│   ├── MainForm.cs     # 界面 + 后台轮询
│   ├── PlcClient.cs    # S7 通信封装（连接/读写）
│   ├── TagMap.cs       # 地址映射表（唯一改 PLC 地址的地方）
│   └── Program.cs      # 程序入口
├── _verifyc/           # 通信验证小工具
├── publish/            # 框架依赖版发布产物（已 git 忽略）
└── publish-single/     # 单文件版发布产物（已 git 忽略）
```

## 更新日志

> 版本号统一在 `Smart200Panel/version.txt` 维护，每次打包自动 +1，界面标题和右下角版本显示会跟着变。

### 1.0.5（2026-10-24）

- 修复：无边框窗体仍能被最大化（Win+↑ 或系统命令），导致标题栏按钮和内容区错位；现在窗口尺寸完全固定
- 恢复：系统菜单（Alt+Space）、任务栏窗口菜单和键盘最小化，窗口被拖出屏幕后可以找回来
- 优化：合并重复的 Win32 函数声明、删除失效代码、关闭时释放标题栏图标位图

### 1.0.4（2026-10-24）

- 优化：置顶、最小化、关闭三个按钮统一为矢量绘制，图标大小和间距完全一致
- 修复：三个按钮遮挡标题栏下方分割线
- 优化：置顶激活时的效果从「整块蓝底」改为「图钉填充实心」
- 优化：去掉点击按钮后残留的焦点虚线框

### 1.0.3（2026-10-24）

- 优化：「置顶」按钮从中文文字改为图钉图标，未置顶空心、已置顶实心

### 1.0.2（2026-10-24）

- 新增：置顶开关（窗口始终显示在最前），每次启动默认关闭
- 变更：Windows 11 下原生标题栏无法绘制自定义按钮，改为自绘标题栏（图标 + 标题 + 置顶 + 最小化 + 关闭）

### 1.0.1（2026-10-23）

- 首个带版本号的发布版本：新增程序图标、作者署名，打包时版本号自动递增
