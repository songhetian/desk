# Code Review — WordGuard 逻辑层（2026-08-16）

> 执行方式：本项目未初始化 git、无 Issue Tracker，故按 `code-review` 技能框架变通——**整个 `src/` 工作树视为新增代码**，`docs/PRD.md` + `CONTEXT.md` + `docs/adr/0001~0003` 作为 Spec 源。两轴由并行子代理独立评审后聚合，未合并/未重排。

## Standards

无 git 历史，整树视为新增。基线气味均为判断型；以下硬 concerns 与气味分开标注。

### 硬 concerns（C# 正确性 / 健壮性）

- **线程安全 / Torn read** — `LibraryFileSource.cs`（Reload、`_engine` 重赋值由 `FileSystemWatcher` 线程池线程触发，UI 线程并发读 `Current`）；`AlertDedup.cs` 的 `Dictionary` 同样非线程安全（`ShouldAlert` 写 `_state`）。无锁 / `volatile` / `Interlocked`，存在撕裂读与竞态风险。
- **DateTime.Kind 隐患** — `OrbStateController.cs`（`_alertUntil = DateTime.MinValue` 无 Kind，比较要求调用方恒传 UTC；混入 `DateTime.Now`（本地）语义模糊，可能出错）。建议归一为 `DateTime.UtcNow` 或显式 `DateTimeKind.Utc`。
- **Key 碰撞（Primitive Obsession）** — `AlertDedup.cs` 用 `word + "|" + context` 作键；`("a|b","")` 与 `("a","|b")` 碰撞，且字符串拼装代替 `(string,string)` 键属基元滥用。建议用 `record` 键或 `ValueTuple`。

### 气味（判断型）

- **热重载状态丢失** — `LibraryFileSource.cs` 每次 `Reload()` 新建 `AlertDedup`，会静默清空在途的去重/确认状态。需向需求确认是否合理。
- **类型风格不一致** — `AlertDedup.Entry` 用 public 可变字段，其余实体均为 `record`。
- **严重度聚合歧义** — `MonitorEngine.cs` 同一 `Word` 若对应多条不同 `Severity` 的 `WordEntry`，`agg.Severity` 只取首个命中，可能丢失高严重度。
- **轻微冗余** — `MatchHit` 同时持 `Word` 与 `Entry`（Entry 已含 Text）。

### 正面

- `AuditLogStore` 全程参数化查询，无 SQL 注入。
- `WordLibrary` 对空串 / 损坏 JSON 降级空词库，契约稳健。
- 命名清晰、测试覆盖充分；未见 Speculative Generality / Shotgun Surgery / Repeated Switches。

小注：测试 `Monitored_target_with_banned_word_trggers_...` 拼写 `trggers`；`WordLibrary` 用 `UnsafeRelaxedJsonEscaping`，JSON 落盘被外部读取时需注意转义。

## Spec

### (a) 缺失 / 仅部分实现（需求要求但逻辑层未落地）

1. **`WordEntry` 缺 `id` 与 `matchMode`** — PRD 数据契约要求 `id`(uuid) 与 `matchMode:"contains"`；当前仅 Text/Category/Severity/Enabled。
2. **审计表缺 `window_title`、`alert_channels` 列** — PRD 审计表要求这两字段；`AuditLogStore` DDL 未含。
3. **去重"文本变更 / 新会话复位"缺失** — PRD「确认后…直至文本变更清除该词或开启新会话」；`MonitorEngine.ProcessCapture` 从不调用 `AlertDedup.Reset`，已确认词对 context 永久抑制。
4. **悬浮球离线态未由词库状态驱动** — `LibraryFileSource.Reload` 算出 `LibraryStatus.FileExists=false`，但不调用 `OrbStateController.SetOnline(false)`，灰黄态无法因文件缺失/损坏触发。
5. **60s 确认超时 → "未确认（超时）" disposition** — PRD 要求，无逻辑（属待建告警模块）。
6. **自动清理调度缺失** — `PruneOlderThan` 已实现，但无人按默认 30 天运行。

### (b) 范围内未要求的行为（scope creep）

1. `audit_log` 额外增 `severity INTEGER` 列 + `AuditLogEntry.Severity` — PRD 审计表未列 severity 列。

### (c) 看似实现但对照 spec 有误

1. **跨框去重语义** — PRD「跨输入框的同词也遵循去重窗口，避免刷屏」意在同词跨框也限流；`AlertDedup.Key` 用 `word|context` 独立窗口，同词在不同框 30s 内会立即再告警，违背防刷屏意图。
2. **`triggered_at` 契约不符** — PRD「triggered_at | TEXT | ISO8601」；`AuditLogStore` 用 `ts INTEGER` + unix 秒，偏离约定 schema。
3. **`matched_words` 内容** — PRD「命中词 JSON 数组（含 id/text）」；`MatchedWords` 仅为字符串数组，因 `WordEntry` 无 `id`（叠加 (a)1）。

> 注：UIA 按 EXE 限目标、`Edit/Document` 控件白名单、弹窗/声音/高亮开关、悬浮球面板均属未建 UI / 告警层，不计入缺陷。

## 汇总

- **Standards 轴**：3 类硬 concern（线程安全、DateTime.Kind、Key 碰撞）+ 数条判断型气味；**最严重：热重载与告警去重的并发读写缺乏同步（torn read / 竞态）**。
- **Spec 轴**：6 项缺失 / 部分实现 + 1 项 scope creep + 3 项契约偏差；**最严重：跨框去重语义与 PRD 防刷屏意图相反（同词跨框仍会立即再告警）**，以及 `WordEntry` 缺 `id`/`matchMode`、`triggered_at` 存储格式偏离 ISO8601 契约。
