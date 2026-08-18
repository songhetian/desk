using System;
using System.IO;
using System.Threading;
using WordGuard.Core;

namespace WordGuard.Client;

/// <summary>
/// 词库文件源：把"文件分发"架构落成代码。读取本地 <c>wordlib.json</c>，据此构建监控引擎；
/// 文件变更时（FileSystemWatcher）自动热重载，无需重启或轮询服务端。
/// 文件缺失/损坏时降级为空词库，监控继续运行（仅不匹配），并按 <see cref="LibraryStatus.FileExists"/>
/// 把悬浮球置为离线，使"离线态由词库状态驱动"（评审遗留修复）。
/// <para>所有对 <see cref="_engine"/> / <see cref="_status"/> 的读写均加锁，避免 FileSystemWatcher 线程重写引擎与
/// UI 线程并发读取之间的撕裂读 / 竞态。</para>
/// </summary>
public sealed class LibraryFileSource : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _cooldown;

    /// <summary>被监控词库文件的绝对/相对路径。</summary>
    public string FilePath => _path;
    private readonly FileSystemWatcher? _watcher;
    private readonly OrbStateController? _orb;
    private readonly List<TargetSpec> _extraTargets = new();
    private readonly object _gate = new();

    private MonitorEngine _engine = null!;
    private LibraryStatus _status = new(false, false);
    private LibraryMetadata _config = new();

    /// <summary>每次成功重载后触发（含构造时的首次加载），便于 UI 刷新「词库来源 / 启用词数」。</summary>
    public event Action<MonitorEngine, LibraryStatus>? Reloaded;

    /// <summary>
    /// 构造词库文件源。
    /// 需求#6：部署配置（监控目标/告警通道/声音路径等）由客户端本地 AppSettings 提供，
    /// 不再从 wordlib.json 的 metadata 段读取。
    /// </summary>
    public LibraryFileSource(string path, TimeSpan cooldown, LibraryMetadata config, bool watch = true, OrbStateController? orb = null)
    {
        _path = path;
        _cooldown = cooldown;
        _config = config ?? new LibraryMetadata();
        _orb = orb;
        Reload();

        if (watch)
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(path) ?? ".", Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            };
            _watcher.Changed += (_, _) => Reload();
            _watcher.Created += (_, _) => Reload();
            _watcher.Renamed += (_, _) => Reload();
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>当前生效的监控引擎（每次重载后内部替换；加锁读取，杜绝撕裂读）。</summary>
    public MonitorEngine Current
    {
        get { lock (_gate) return _engine; }
    }

    /// <summary>最后一次加载的词库状态（是否存在、是否高版本）。加锁读取。</summary>
    public LibraryStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>当前生效的部署配置（来自客户端 AppSettings），加锁读取。</summary>
    public LibraryMetadata Metadata
    {
        get { lock (_gate) return _config; }
    }

    /// <summary>标记某「词 + 输入框」已被客服确认（委托给当前引擎的去重器）。</summary>
    public void Acknowledge(string word, string context)
    {
        MonitorEngine engine;
        lock (_gate) engine = _engine;
        engine.Acknowledge(word, context);
    }

    /// <summary>
    /// 本地调试叠加监控目标（仅开发/自测用，生产应留空）：与词库 metadata 白名单取并集判定。
    /// 设置后即时重建引擎使其生效（热重载路径同样会包含这些叠加目标）。
    /// </summary>
    public IEnumerable<string> ExtraTargetExes
    {
        set
        {
            _extraTargets.Clear();
            foreach (var s in (value ?? Enumerable.Empty<string>())
                         .Select(x => x.Trim()).Where(x => x.Length > 0))
                _extraTargets.Add(new TargetSpec { ExeName = s });
            Reload();
        }
    }

    /// <summary>
    /// 更新部署配置（监控目标/告警通道等）并立即重建引擎。
    /// 需求#6：客户端本地配置变更后调用此方法使新配置生效。
    /// </summary>
    public void UpdateConfig(LibraryMetadata config)
    {
        lock (_gate)
        {
            _config = config ?? new LibraryMetadata();
        }
        Reload();
    }

    /// <summary>手动触发热重载（测试或菜单「立即同步」调用）。</summary>
    public void Reload()
    {
        var exists = File.Exists(_path);
        var lib = WordLibrary.LoadFromFile(_path);
        var matcher = new AhoCorasickMatcher(lib.Words);

        // 构建拼音匹配器：把每个违禁词转成拼音后建索引（用于键盘钩子兜底模式）
        var pinyinWords = lib.Words
            .Where(w => w.Enabled && !string.IsNullOrEmpty(w.Text))
            .Select(w => new WordEntry
            {
                Id = w.Id,
                Text = PinyinHelper.ToPinyin(w.Text), // 转成拼音
                Category = w.Category,
                Severity = w.Severity,
                Enabled = w.Enabled,
            })
            .Where(w => w.Text.Length >= 2) // 拼音太短的过滤掉（如单个字母）
            .ToList();
        var pinyinMatcher = new AhoCorasickMatcher(pinyinWords);

        // 需求#6：监控目标从客户端 AppSettings 配置读取，叠加本地调试目标
        var targets = _config.Targets.Concat(_extraTargets).ToList();
        var engine = new MonitorEngine(matcher, pinyinMatcher, new AlertDedup(_cooldown), targets);
        var status = new LibraryStatus(exists, lib.NewerSchemaDetected);

        lock (_gate)
        {
            _engine = engine;
            _status = status;
        }

        // 词库缺失 → 悬浮球离线；恢复 → 退出离线（评审遗留修复）
        _orb?.SetOnline(status.FileExists);
        Reloaded?.Invoke(engine, status);
    }

    public void Dispose() => _watcher?.Dispose();
}

/// <summary>词库文件加载状态，驱动悬浮球「离线」态与状态栏提示。</summary>
/// <param name="FileExists">词库文件是否存在（缺失即视为离线/异常）。</param>
/// <param name="NewerSchema">文件 schemaVersion 高于本程序可理解版本。</param>
public sealed record LibraryStatus(bool FileExists, bool NewerSchema);
