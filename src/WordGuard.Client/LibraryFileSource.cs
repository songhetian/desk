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
    private readonly object _gate = new();

    private MonitorEngine _engine = null!;
    private LibraryStatus _status = new(false, false);
    private LibraryMetadata _metadata = new();

    /// <summary>每次成功重载后触发（含构造时的首次加载），便于 UI 刷新「词库来源 / 启用词数」。</summary>
    public event Action<MonitorEngine, LibraryStatus>? Reloaded;

    /// <summary>
    /// 构造词库文件源。
    /// 注意：被监控目标、告警开关、声音路径等部署配置已从 <c>wordlib.json</c> 的 metadata 段读取，
    /// 由管理员锁定、随词库下发（客户端只读），不再由本地 <see cref="AppSettings"/> 提供。
    /// <paramref name="cooldown"/> 仅作为 metadata 缺失时的兜底去重窗口。
    /// </summary>
    public LibraryFileSource(string path, TimeSpan cooldown, bool watch = true, OrbStateController? orb = null)
    {
        _path = path;
        _cooldown = cooldown;
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

    /// <summary>当前生效的部署配置（监控目标 / 三通道开关 / 声音路径 / 去重 / 保留），加锁读取。</summary>
    public LibraryMetadata Metadata
    {
        get { lock (_gate) return _metadata; }
    }

    /// <summary>标记某「词 + 输入框」已被客服确认（委托给当前引擎的去重器）。</summary>
    public void Acknowledge(string word, string context)
    {
        MonitorEngine engine;
        lock (_gate) engine = _engine;
        engine.Acknowledge(word, context);
    }

    /// <summary>手动触发热重载（测试或菜单「立即同步」调用）。</summary>
    public void Reload()
    {
        var exists = File.Exists(_path);
        var lib = WordLibrary.LoadFromFile(_path);
        var matcher = new AhoCorasickMatcher(lib.Words);
        // 配置锁定：监控目标从词库 metadata 读取（管理员下发，客户端只读）
        var engine = new MonitorEngine(matcher, new AlertDedup(_cooldown), lib.Metadata.Targets);
        var status = new LibraryStatus(exists, lib.NewerSchemaDetected);

        lock (_gate)
        {
            _engine = engine;
            _status = status;
            _metadata = lib.Metadata;
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
