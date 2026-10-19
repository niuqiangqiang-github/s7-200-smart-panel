---
paths:
  - "Smart200Panel/MainForm.cs"
---

# WinForms 界面规则（业务约定 + 高 DPI 布局坑）

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
