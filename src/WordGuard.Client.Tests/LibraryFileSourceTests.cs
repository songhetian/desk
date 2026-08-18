using System;
using System.IO;
using WordGuard.Client;
using WordGuard.Core;
using Xunit;

namespace WordGuard.Client.Tests;

public class LibraryFileSourceTests
{
    private static readonly LibraryMetadata ConfigWithCsExe = new()
    {
        Targets = new() { new() { ExeName = "cs.exe" } },
    };

    /// <summary>需求#6：监控目标从客户端 AppSettings 提供，不从 wordlib.json metadata 读取。</summary>
    [Fact]
    public void Uses_targets_from_constructor_not_from_wordlib_metadata()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        // wordlib.json 无 metadata（新格式：管理端只导出违禁词数据）
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), ConfigWithCsExe, watch: false);
            var r = source.Current.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", DateTime.UtcNow));
            Assert.True(r.IsMonitoredTarget);
            Assert.True(Assert.Single(r.Triggered).ShouldAlert);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Builds_engine_that_detects_words_from_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), ConfigWithCsExe, watch: false);
            var r = source.Current.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", DateTime.UtcNow));
            Assert.True(Assert.Single(r.Triggered).ShouldAlert);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reload_picks_up_new_words()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"medium\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), ConfigWithCsExe, watch: false);

            // 加入新词后重载
            File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"medium\"},{\"text\":\"加微信\",\"enabled\":true,\"severity\":\"high\"}]}");
            source.Reload();

            var r = source.Current.ProcessCapture(new CaptureInput("退货并可加微信", "cs.exe", "", "box1", DateTime.UtcNow));
            Assert.Equal(2, r.Triggered.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_falls_back_to_empty_library()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json"); // 故意不写文件

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), new LibraryMetadata(), watch: false);
            var r = source.Current.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", DateTime.UtcNow));
            Assert.False(r.IsMonitoredTarget);
            Assert.Empty(r.Triggered);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_drives_orb_offline()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json"); // 故意不写文件

        try
        {
            var orb = new OrbStateController(TimeSpan.FromSeconds(3));
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), new LibraryMetadata(), watch: false, orb: orb);

            Assert.Equal(OrbState.Offline, orb.CurrentState(DateTime.UtcNow));
            Assert.False(source.Status.FileExists);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Present_file_keeps_orb_online()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[]}");

        try
        {
            var orb = new OrbStateController(TimeSpan.FromSeconds(3));
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), new LibraryMetadata(), watch: false, orb: orb);

            Assert.Equal(OrbState.Normal, orb.CurrentState(DateTime.UtcNow));
            Assert.True(source.Status.FileExists);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Current_stays_consistent_under_concurrent_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"severity\":\"medium\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), ConfigWithCsExe, watch: false);
            var exceptions = 0;
            var reader = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 200; i++)
                    {
                        var eng = source.Current;
                        Assert.NotNull(eng);
                        _ = eng.ProcessCapture(new CaptureInput("x", "cs.exe", "", "b", DateTime.UtcNow));
                    }
                }
                catch (Exception) { System.Threading.Interlocked.Increment(ref exceptions); }
            });
            for (int i = 0; i < 200; i++)
                source.Reload();
            await reader;
            Assert.Equal(0, exceptions);
            Assert.NotNull(source.Current);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>需求#6：客户端配置变更后调用 UpdateConfig 使新目标立即生效。</summary>
    [Fact]
    public void UpdateConfig_reloads_engine_with_new_targets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), new LibraryMetadata(), watch: false);
            // 初始无目标 → 不监控
            Assert.False(source.Current.ProcessCapture(new CaptureInput("退货", "cs.exe", "", "b", DateTime.UtcNow)).IsMonitoredTarget);

            // 更新配置 → 新目标立即生效
            source.UpdateConfig(ConfigWithCsExe);
            Assert.True(source.Current.ProcessCapture(new CaptureInput("退货", "cs.exe", "", "b", DateTime.UtcNow)).IsMonitoredTarget);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_json_file_falls_back_to_empty_library_without_crashing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        // 损坏的 JSON：词库导出写了一半
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货");

        try
        {
            // 构造时加载损坏文件：不应抛异常，引擎应为空词库
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), ConfigWithCsExe, watch: false);
            var r = source.Current.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", DateTime.UtcNow));
            Assert.True(r.IsMonitoredTarget);
            Assert.Empty(r.Triggered);
            Assert.True(source.Status.FileExists); // 文件存在但内容损坏
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reload_from_good_to_corrupt_reverts_to_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), ConfigWithCsExe, watch: false);
            // 初始正常
            Assert.Single(source.Current.ProcessCapture(new CaptureInput("退货", "cs.exe", "", "b", DateTime.UtcNow)).Triggered);

            // 热重载成损坏文件 → 降级为空，不崩
            File.WriteAllText(path, "this is not json at all {{");
            source.Reload();

            var r = source.Current.ProcessCapture(new CaptureInput("退货", "cs.exe", "", "b", DateTime.UtcNow));
            Assert.True(r.IsMonitoredTarget);
            Assert.Empty(r.Triggered);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reload_from_corrupt_back_to_good_recovers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        // 初始损坏
        File.WriteAllText(path, "broken json");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), ConfigWithCsExe, watch: false);
            Assert.Empty(source.Current.ProcessCapture(new CaptureInput("退货", "cs.exe", "", "b", DateTime.UtcNow)).Triggered);

            // 修复后热重载 → 恢复正常
            File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}");
            source.Reload();

            var r = source.Current.ProcessCapture(new CaptureInput("退货", "cs.exe", "", "b", DateTime.UtcNow));
            Assert.Single(r.Triggered);
            Assert.True(Assert.Single(r.Triggered).ShouldAlert);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
