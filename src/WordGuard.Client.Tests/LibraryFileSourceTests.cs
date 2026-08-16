using System.IO;
using WordGuard.Client;
using WordGuard.Core;
using Xunit;

namespace WordGuard.Client.Tests;

public class LibraryFileSourceTests
{
    [Fact]
    public void Builds_engine_that_detects_words_from_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"metadata\":{\"targets\":[{\"exeName\":\"cs.exe\"}]},\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), watch: false);
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
        File.WriteAllText(path, "{\"schemaVersion\":1,\"metadata\":{\"targets\":[{\"exeName\":\"cs.exe\"}]},\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"medium\"}]}");

        try
        {
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), watch: false);

            // 加入新词后重载
            File.WriteAllText(path, "{\"schemaVersion\":1,\"metadata\":{\"targets\":[{\"exeName\":\"cs.exe\"}]},\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"medium\"},{\"text\":\"加微信\",\"enabled\":true,\"severity\":\"high\"}]}");
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
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), watch: false);
            // 词库缺失 → metadata 为空 → 无监控目标（配置锁定：目标必须随词库下发）
            var r = source.Current.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", DateTime.UtcNow));
            Assert.False(r.IsMonitoredTarget); // 无目标配置即非目标，不误监控
            Assert.Empty(r.Triggered); // 监控继续运行，不崩溃
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
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), watch: false, orb: orb);

            // 词库缺失 → 悬浮球进入离线态（评审遗留：此前离线态未由词库状态驱动）
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
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), watch: false, orb: orb);

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
            using var source = new LibraryFileSource(path, TimeSpan.FromSeconds(30), watch: false);
            var exceptions = 0;
            var reader = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 200; i++)
                    {
                        var eng = source.Current;       // 并发读
                        Assert.NotNull(eng);
                        _ = eng.ProcessCapture(new CaptureInput("x", "cs.exe", "", "b", DateTime.UtcNow));
                    }
                }
                catch (Exception) { System.Threading.Interlocked.Increment(ref exceptions); }
            });
            for (int i = 0; i < 200; i++)
                source.Reload();                        // 并发写（替换引擎）
            await reader;
            Assert.Equal(0, exceptions);
            Assert.NotNull(source.Current);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
