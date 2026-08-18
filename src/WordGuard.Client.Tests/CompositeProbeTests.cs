using WordGuard.Client;
using WordGuard.Core;
using Xunit;

namespace WordGuard.Client.Tests;

/// <summary>
/// 组合探针测试：按顺序尝试多个 IWindowProbe，返回第一个有结果的。
/// 用于实现「UIA 主方案 + 键盘钩子兜底」的策略。
/// </summary>
public class CompositeProbeTests
{
    private sealed class FakeProbe : IWindowProbe
    {
        public WindowSnapshot? Result { get; set; }
        public int CallCount { get; private set; }

        public WindowSnapshot? Probe()
        {
            CallCount++;
            return Result;
        }
    }

    private static WindowSnapshot Snap(string text = "测试内容")
        => new("test.exe", "", "标题", text, "ctx1");

    [Fact]
    public void First_probe_returns_result_second_not_called()
    {
        var p1 = new FakeProbe { Result = Snap("来自UIA") };
        var p2 = new FakeProbe { Result = Snap("来自键盘钩子") };
        var composite = new CompositeProbe(p1, p2);

        var result = composite.Probe();

        Assert.NotNull(result);
        Assert.Equal("来自UIA", result!.Text);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(0, p2.CallCount); // 第一个有结果，第二个不调用
    }

    [Fact]
    public void First_probe_returns_null_falls_back_to_second()
    {
        var p1 = new FakeProbe { Result = null };
        var p2 = new FakeProbe { Result = Snap("兜底结果") };
        var composite = new CompositeProbe(p1, p2);

        var result = composite.Probe();

        Assert.NotNull(result);
        Assert.Equal("兜底结果", result!.Text);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(1, p2.CallCount);
    }

    [Fact]
    public void All_probes_return_null_returns_null()
    {
        var p1 = new FakeProbe { Result = null };
        var p2 = new FakeProbe { Result = null };
        var p3 = new FakeProbe { Result = null };
        var composite = new CompositeProbe(p1, p2, p3);

        var result = composite.Probe();

        Assert.Null(result);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(1, p2.CallCount);
        Assert.Equal(1, p3.CallCount);
    }

    [Fact]
    public void Empty_probe_list_returns_null()
    {
        var composite = new CompositeProbe();
        Assert.Null(composite.Probe());
    }

    [Fact]
    public void Single_probe_works_normally()
    {
        var p1 = new FakeProbe { Result = Snap("唯一探针") };
        var composite = new CompositeProbe(p1);

        var result = composite.Probe();

        Assert.NotNull(result);
        Assert.Equal("唯一探针", result!.Text);
        Assert.Equal(1, p1.CallCount);
    }
}
