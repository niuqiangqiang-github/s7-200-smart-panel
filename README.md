# S7-200 SMART I/O 仿真面板

一个 Windows 面板程序，通过以太网连接**真实的西门子 S7-200 SMART PLC**（不是软 PLC 模拟器），用来在电脑上直接调试 PLC 的输入输出。

## 能干什么

- 🎛️ **模拟数字量输入** —— 点按钮把信号写进 PLC 的 M 区
- 📈 **模拟模拟量输入** —— 填数值写进 PLC 的模拟量输入
- 💡 **显示数字量输出** —— 实时读 PLC 的 Q 区，用指示灯显示
- 📊 **显示模拟量输出** —— 实时读 PLC 的模拟量输出值
- 🔍 **监视寄存器** —— 填任意地址，实时查看寄存器的值

## 技术栈

| 项目 | 说明 |
|------|------|
| 语言 / 界面 | C# / WinForms |
| 运行环境 | .NET 8（Windows） |
| PLC 通信库 | S7.Net（S7netplus 0.20.0） |
| 通信协议 | S7 协议（ISO-on-TCP，端口 102） |

## 如何运行

需要先安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

发布并启动（三步，顺序不能乱）：

```bash
taskkill //F //IM Smart200Panel.exe
dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish
powershell -Command "Start-Process 'C:\Users\nqq\Desktop\smart2000\publish\Smart200Panel.exe'"
```

1. 第一步：关掉正在运行的旧程序（否则发布会报文件被占用）。
2. 第二步：编译发布，产物输出到 `publish/` 目录。
3. 第三步：启动新程序。

程序打开后，在「PLC连接」区填 PLC 的 IP 地址，点「连接」即可。

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
smart2000/
├── Smart200Panel/      # 主程序（WinForms 面板）
│   ├── MainForm.cs     # 界面 + 后台轮询
│   ├── PlcClient.cs    # S7 通信封装（连接/读写）
│   ├── TagMap.cs       # 地址映射表（唯一改 PLC 地址的地方）
│   └── Program.cs      # 程序入口
├── _verifyc/           # 通信验证小工具
└── publish/            # 发布产物（已 git 忽略）
```
