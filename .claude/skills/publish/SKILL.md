---
name: publish
description: 发布 Smart200Panel 面板程序。默认框架依赖版；参数写 single 时打包单文件版。
disable-model-invocation: true
---

发布类型：$ARGUMENTS（空 = 框架依赖版；single = 单文件版）

按顺序执行，顺序不能乱：

1. 关闭旧程序（旧程序没运行时 taskkill 报「找不到进程」属正常，忽略）：
   `taskkill //F //IM Smart200Panel.exe || true`

2. 编译并发布：
   - 框架依赖版：
     `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -o publish`
   - 单文件版：
     `dotnet publish Smart200Panel/Smart200Panel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish-single`

3. 任一编译报错 → 立即停下，把错误原文给我，不要启动程序。

4. 启动新程序：
   - 框架依赖版：
     `powershell -Command "Start-Process '.\publish\Smart200Panel.exe'"`
   - 单文件版：
     `powershell -Command "Start-Process '.\publish-single\Smart200Panel.exe'"`
