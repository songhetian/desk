# WordGuard（雷犀）UI 重构 + 告警弹窗/语音 — 自测报告

> 生成时间：2026-08-17 13:43
> 适用范围：本轮重构全部切片（T1–T15）收口验证
> 验收约定：两端都做扎实 + 告警引擎真能拦 + 我先自测、你再抽检

---

## 1. 编译与测试总览

| 项目 | 结果 |
|---|---|
| `dotnet build WordGuard.sln -c Release` | **0 警告 / 0 错误** |
| `dotnet test WordGuard.sln -c Release` | **109 通过 / 0 失败**（Core 34 + Studio 18 + Client 57） |
| 无头管线冒烟 `WordGuard.Client.Smoke` | 10/10 通过 |

新增测试覆盖（相对重构前 104 条）：
- `AlertDispatcherTests` +2：语音通道在 `metadata.AlertVoice` 开关下正确纳入/排除
- `AlertVoiceTests` +3：`BuildMessage` 纯函数（含全部命中词与分类、非空、去换行、去重）
- `WordLibraryEditorTests` +4：批量启用/禁用、批量删除（边界与幂等）

---

## 2. 发布产物（自包含单文件 exe）

| 端 | 路径 | 大小 | 时间戳 |
|---|---|---|---|
| 客户端 | `publish/Client/WordGuard.Client.App.exe` | 155 MB | 13:42 |
| 管理端 | `publish/Studio/WordGuard.Studio.App.exe` | 155 MB | 13:32 |

`publish/Client/web/` 仅含 5 个 HTML（已彻底移除死文件 `arco.css`）：
`status.html`(13:29) · `alert-popup.html`(13:30) · `orb.html`(13:40) · `orb-menu.html`(13:40) · `logs.html`(13:41)

`publish/Studio/web/`：`studio.html`(13:01)

---

## 3. 功能验证矩阵

| 功能 | 验证方式 | 状态 |
|---|---|---|
| 管理端：词库 CRUD（增/改/删/启用） | C# 单测 + MainForm 消息桥保留 | ✅ 自动 |
| 管理端：**多选 + 批量启用/禁用/删除（带确认弹窗）** | `BulkSetEnabled`/`BulkRemove` 单测 + 前端接入 `bulkSetEnabled`/`bulkDelete` | ✅ 自动 |
| 管理端：分类内联新增 / 导出 `wordlib.json` | 消息桥 `addCategory`/`export` 不变 + 单测 | ✅ 自动 |
| 客户端：导入词库 + 监控目标勾选 + 告警设置持久化 | `StatusForm` 消息桥（`ready/saveDeploy/resetDeploy/importLibrary` + `init/toast`）保留 | ✅ 自动 |
| **告警弹窗换肤**（新密集工具风） | 消息契约 `init`/`ready`/`popupAction` 字节级保留 | ✅ 自动（契约校验） |
| **语音播报通道**（命中词 SAPI 朗读） | `AlertChannel.Voice` → `Metadata` → `AppSettings` → `AlertDispatcher` → `VoiceAnnouncer`，单测覆盖派遣与文案 | ✅ 自动（文案/派遣） |
| 客户端「语音播报」开关 + 管理端「部署配置」语音复选框 | `StatusForm`/`DeployConfigForm`/`MainForm` 一致性 | ✅ 自动 |
| 悬浮球/右键菜单/日志页换肤 | 契约字符串 grep 校验全保留（`dragStart/dragMove/…`、`menu`/`simulate`/`close`、`init/rows/query/ready`） | ✅ 自动（契约校验） |
| 悬浮球拖拽 / 右键「模拟告警测试」 | `OrbWebViewForm` 拖拽用物理像素增量移动窗体；`simulate` 已接到 `OnSimulate` | ✅ 代码确认 |
| **监控引擎真拦词**（UIA 取前台窗口文本 → 匹配 → 弹窗+声音+语音） | 链路代码确认接好：`CaptureHost` 500ms 定时器 → `CaptureService` → `UiaWindowProbe` → `MonitorEngine` → `AlertRaised` | ⚠️ 需真机抽检 |

---

## 4. 需要你在真机抽检的项（本环境无 GUI，无法自动跑）

1. **双击 `publish/Client/WordGuard.Client.App.exe`** → 右下角出现悬浮球。
2. **右键悬浮球 → 点「模拟告警测试」**：应立刻弹出告警窗 + 提示音 + **语音朗读命中词**。这是最快的端到端验证路径，不用真去聊天框打字。
3. **真实拦截**：在微信/企业微信里输入命中词 → 弹窗+声音+语音同时触发；命中词在弹窗内以 `<mark>` 标红强调。
4. **语音真实出声前提**：Windows 需安装中文(zh-CN)语音包（Win10/11 一般自带「Microsoft 云语音 - 中文(简体)」或「晓妍/云希」）。没装则 `VoiceAnnouncer` 优雅降级（不崩、不报错、仅无声），文案构建逻辑本身正确。可在「设置 → 时间和语言 → 语音」里确认。
5. **管理端**：双击 `publish/Studio/WordGuard.Studio.App.exe` → 新增分类（内联回车）、新增/编辑违禁词（居中弹窗）、勾选多条 → 批量启用/禁用/删除（删除走确认框）→ 部署配置勾选四项通道 → 导出 `wordlib.json`。

---

## 5. 已知限制 / 设计决策

- **「高亮」通道的最终定义**：第三方窗口内文本无法用 UIA 就地高亮，故 `AlertHighlight` 实际含义为「在**自有告警弹窗与审计日志**中强调命中词」，而非改变对方窗口显示。已在 `alert-popup.html` 的触发内容 `<mark>` 标红落实。
- **语音依赖系统 TTS**：无中文语音包时静默降级，不影响其他通道。
- **管理端持久化**：仅导出 `wordlib.json`（内存编辑，不存本地工程），符合原契约「管理端只生成词库，配置在 Client」。
- **字体**：中文 `Microsoft YaHei UI` + 等宽 `Cascadia Code/Consolas`，均 Win 自带，WebView2 离线渲染不依赖 CDN。

---

## 6. 验收结论

- 自动化能锁死的（编译、109 单测、契约不破坏、批量/语音逻辑、web 资源随包拷贝）**全部通过**。
- 唯一需人工确认的是「真机弹窗/语音实际发声 + 真实聊天窗口拦截」，代码链路已确认接好，请按第 4 节走一遍。
