---
name: publish
description: 发布 Smart200Panel 面板程序。默认打包单文件版；参数写 framework 时打包框架依赖版。
disable-model-invocation: true
---

发布类型：$ARGUMENTS（空 = 单文件版；framework = 框架依赖版）

日常交付以单文件版为准，框架依赖版只用于本机快速验证。

打包产物统一改名为 `S7-200SMART-I／O面板.exe`。Windows 文件名不允许半角 `/`，用全角 `／`；程序集名仍是 `Smart200Panel`，只改文件名不影响运行。

按顺序执行，顺序不能乱：

1. 关闭旧程序（旧程序没运行时 taskkill 报「找不到进程」属正常，忽略）：
   `taskkill //F //IM "S7-200SMART-I／O面板.exe" || true`

2. 版本号 +1（每次打包自动递增补丁号，写入 `Smart200Panel/version.txt`，界面标题和右下角版本显示自动跟着变）：
   `v=$(cat Smart200Panel/version.txt); IFS=. read -r a b c <<< "$v"; echo "$a.$b.$((c+1))" > Smart200Panel/version.txt`

3. 编译并发布：
   - 单文件版（默认）：
     `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish-single`
   - 框架依赖版（参数 framework）：
     `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish`

4. 把刚生成的 `Smart200Panel.exe` 改成目标文件名（重复发布时覆盖旧的）：
   - 单文件版：`mv -f "publish-single/Smart200Panel.exe" "publish-single/S7-200SMART-I／O面板.exe"`
   - 框架依赖版：`mv -f "publish/Smart200Panel.exe" "publish/S7-200SMART-I／O面板.exe"`

5. 任一编译报错 → 立即停下，把错误原文给我，不要启动程序。

6. 启动新程序：
   - 单文件版：
     `powershell -Command "Start-Process '.\publish-single\S7-200SMART-I／O面板.exe'"`
   - 框架依赖版：
     `powershell -Command "Start-Process '.\publish\S7-200SMART-I／O面板.exe'"`