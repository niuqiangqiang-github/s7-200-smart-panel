---
paths:
  - "Smart200Panel/MainForm.cs"
  - "Smart200Panel/PlcClient.cs"
---

# 线程模型规则（改轮询/写入时最容易踩的坑）

- `System.Threading.Timer` 后台轮询间隔为 **50ms**（`MainForm.cs` 中 `_pollTimer`），结果用 `BeginInvoke` 回 UI 线程刷新控件，不能直接在回调里碰控件。
- `_polling` 字段用 `Interlocked.CompareExchange` 防重入：上一轮没读完就跳过本轮。
- 连续 3 轮整轮读失败 → 判定断线：停轮询、`_plc.Disconnect()`、复位显示（`ResetDisplay`）、状态变红「连接已断开」，需手动点「连接」重连。
- 每个点位单独 try/catch，某一次读失败保持上次值，不整组灭灯。
- `PlcClient` 内部 `_lock` 串行化所有读写，UI 线程写入与后台轮询读取不会同时抢 PLC；不要绕过 `PlcClient` 直接 new `Plc`。
