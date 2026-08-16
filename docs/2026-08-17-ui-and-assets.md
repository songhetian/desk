# WordGuard 界面与资源改造 — 变更记录（2026-08-17）

基于已确认的设计 demo（`prototype/demo-client.html` / `demo-studio.html`，蓝色企业风），
本批改动聚焦 9 项问题清单中的：**icon 缺失、默认提示音缺失、悬浮球右键/拖拽、页面现代化、
告警弹窗现代化、Studio 新增词不落盘、导出无效**。

> 监控核心（UIA 前台窗口探测 + CaptureService 管线）此前已完成并通过 27 项单元测试，本次未改动。

---

## 1. 应用图标与默认提示音（问题 #6 +「无默认声音」）

| 文件 | 改动 |
|---|---|
| `src/WordGuard.Client.App/WordGuard.ico` | 新增：多分辨率盾牌图标（16–256px，PNG 帧 ICO，9 档） |
| `src/WordGuard.Studio.App/WordGuard.ico` | 同上 |
| `src/WordGuard.Client.App/alert.wav` | 新增：内嵌默认提示音（880→1174.7Hz 双音钟声，约 0.48s） |
| `prototype/gen_assets.py` | 图标/声音生成脚本（PIL 手绘盾牌 + 波形合成，可复跑） |
| `WordGuard.Client.App.csproj` / `WordGuard.Studio.App.csproj` | `<ApplicationIcon>WordGuard.ico</ApplicationIcon>`；Client 额外把 `alert.wav` 复制到输出 |
| `TrayController.cs` | 托盘图标改为 `Icon.ExtractAssociatedIcon(exe)`（真实图标），提取失败回退 GDI 盾牌 |
| `HtmlWindow.cs`（两端） | 窗体构造时从 exe 提取图标设为 `Form.Icon` |
| `AlertPopupForm.cs` | 同上 |
| `CaptureHost.cs` | `ResolveSoundPath`：metadata 未配置声音路径时回退到随包 `alert.wav`（不再只有系统 Beep） |

## 2. 悬浮球：拖拽修复 + 现代化右键菜单（问题 #4、#9）

- **拖拽**：`web/orb.html` 不再向 C# 发送浏览器像素坐标（CSS 像素与物理像素在 DPI 缩放下不一致，
  是"拖不动/乱跳"根因）。改为只发 `dragStart/dragMove/dragEnd` 信号；
  `OrbWebViewForm` 用 `Cursor.Position`（物理像素）增量计算 `Left/Top`，任何 DPI 下都精确。
- **右键菜单**：新增 `web/orb-menu.html` + `OrbMenuForm.cs`（独立 WebView2 弹层窗口）。
  品牌头部（WordGuard · 客服合规卫士）+ 入场动画 + 悬停态 + 危险项红色；菜单项点击/Esc/失焦自动关闭。
  点击后派发 `settings/log/simulate/exit` 到与 WinForms 菜单一致的回调。
  WebView2 不可用时（GDI 降级模式）仍使用原 WinForms ContextMenuStrip，功能不丢失。

## 3. Client 页面现代化（问题 #1 客户端部分）

- `web/status.html`：整体重写为蓝色企业风（`#2563EB/#1E40AF` + slate 中性色，白色顶栏 + 状态胶囊），
  去掉原 teal/径向渐变"AI 感"；**恢复默认改用自定义模态确认框**（不再用浏览器原生 `confirm`）；
  toast 保留。
- `web/logs.html`：同风格重写（白顶栏 + 卡片表格 + 级别/处理标签），消息协议不变。

## 4. 告警弹窗现代化（问题 #5 弹窗样式）

- 新增 `web/alert-popup.html` + 重写 `AlertPopupForm.cs`：WinForms 控件弹窗 → WebView2 现代化卡片。
  盾牌徽标（按严重度着色）+ 命中词汇大卡 + 来源窗口/所属分类 +「查看触发内容」高亮折叠区 +
  底部「忽略本次 / 查看详情 / 已知悉」。
  - 已知悉 → 确认去重 + 审计「客服已确认」
  - 忽略本次 / Esc → 审计「已忽略」
  - 查看详情 → 打开审计日志查看器 + 审计「已查看」
  - 60s 超时 → 「未确认（超时）」
  - 分类由 `CaptureHost` 从词库文件按命中词查询后传入（`TriggeredWord` 本身不含分类）。

## 5. Studio：新增词落盘 + 导出另存为（问题 #3、#7）＋ 页面重写（问题 #1、#2）

**两个真实 bug 的根因与修复（`MainForm.cs`）**：
- **新增违禁词"无反应"**：`WordLibraryEditor` 只改内存，`MainForm` 从不写回文件 → 重启即丢。
  新增 `Persist()`（`_lib.UpdatedAt = UtcNow; File.WriteAllText(_path, _lib.ToJson())`），
  在 增/删/改/启停/分类操作/策略保存 后统一落盘；`saveWord` 增加空文本/重复校验并以 toast 反馈。
- **导出无效**：旧逻辑 `File.WriteAllText(_path, json)` —— 把文件写回自己，等于没导出。
  改为 `SaveFileDialog` 另存为（默认 `wordlib-YYYYMMDD-HHmm.json`），成功后 toast 输出完整路径。

**页面重写 `web/studio.html`**：
- 蓝色企业风：白顶栏（品牌 + 词库路径胶囊 + 导出按钮）、左侧导航（违禁词管理 / 分类管理 / 默认策略 / 客户端管理）。
- 违禁词管理：搜索框 + 新增/编辑模态（分类下拉支持"＋ 新建分类…"内联输入，不再用 `prompt`）+ 开关/编辑/删除。
- 分类管理：卡片网格（色点 + 名称 + 词数 + 重命名/删除），新建分类后直接引导新增该分类的首个词。
- 默认策略：监控目标（每行 `EXE | 路径`）+ 三通道开关 + 去重/保留，随词库落盘下发。
- 客户端管理：诚实空状态页（说明文件式分发流程 + 导出按钮），**不伪造在线列表**。
- 全站零原生 `alert/prompt/confirm`，统一自定义模态 + toast。

---

## 验证

- `dotnet build WordGuard.sln`（隔离 SDK `C:\Users\Song\.workbuddy\binaries\dotnet\sdk8` + `--artifacts-path`）：0 错误
  （仅 NU1900 NuGet 漏洞缓存访问警告，本机环境噪音，与代码无关）。
- 单元测试：27/27 通过（Client.Tests 含"目标窗口+违禁词 → 告警"监控真通验证）。
- 说明：以上为 WinForms + WebView2 桌面程序，HTML 在浏览器单独打开时仅展示静态设计，
  消息桥（`chrome.webview`）需在应用内运行才生效。

## 后续（未包含在本批）

- 发布新包（client/studio，自包含单文件 + web/ + ico + alert.wav）。
- 可选：`prototype/gen_assets.py` 可复跑调整图标/声音。
