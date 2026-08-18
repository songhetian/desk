# WordGuard 修复概述（2026-08-17）

## 修复内容

本次修复针对用户反馈的四个问题：

1. **两端莫名卡死、客户端按钮不可用**
   - 根因：`HtmlWindow.OnLoad` 直接赋值 `_web.Source` 触发异步 WebView2 初始化，未等待完成，导致 UI 线程死锁或消息丢失。
   - 修复：改为 `async void OnLoad`，显式 `await _web.EnsureCoreWebView2Async()`，成功后再挂载消息事件并导航到本地 HTML。

2. **客户端状态页布局与交互**
   - 删除顶部 note 提示条。
   - 标题区改为"本机部署配置 + 沿用/覆盖标签"。
   - 优化监控目标区域：运行中程序勾选列表 + 手动补充更紧凑，空状态提示更明确。
   - `StatusForm.PushInit` 中枚举进程的异常单独捕获，避免阻塞 init 消息导致页面"加载中"。

3. **悬浮球不可移动/不可点击、右键菜单样式老**
   - WebView2 初始化修好后，`orb.html` 的拖拽/双击/右键事件正常上报，现代化右键菜单（`OrbMenuForm`）随之可用。

4. **管理端功能不可用、不需要默认策略**
   - 删除左侧"默认策略"和"客户端管理"导航及页面。
   - `WordLibrary` 新增独立 `Categories` 列表，支持创建空分类并持久化。
   - `WordLibraryEditor` 新增 `AddCategory`，调整分类增删改查逻辑。
   - `MainForm` 新增 `addCategory` 消息处理。
   - 修复新增/编辑违禁词时的严重级别映射（前端 `hi/mid/lo` → 后端 `high/medium/low`）。
   - 清理页面中残留的"默认策略"文案。

## 验证结果

- `dotnet test WordGuard.sln -c Release`：通过 90 条，失败 0。
  - Core.Tests: 33
  - Studio.Tests: 14
  - Client.Tests: 43

## 发布产物

- `E:\System\desk\publish\Client\WordGuard.Client.App.exe`（自包含单文件，无需目标机安装 .NET）
- `E:\System\desk\publish\Studio\WordGuard.Studio.App.exe`（自包含单文件）

## 使用提示

- 发布前若 exe 正在运行会被占用，需先关闭旧版本再发布。
- 客户端若仍出现 WebView2 初始化失败弹窗，说明系统缺少 Microsoft Edge WebView2 运行时，需按提示安装。
