using System.Collections.Generic;
using System.Linq;
using WordGuard.Core;

namespace WordGuard.Client;

/// <summary>
/// 组合探针：按顺序尝试多个 IWindowProbe，返回第一个非 null 的结果。
/// 用于实现「主方案 + 兜底方案」的策略，例如 UIA 主方案 + 键盘钩子兜底。
/// </summary>
public sealed class CompositeProbe : IWindowProbe
{
    private readonly IWindowProbe[] _probes;

    public CompositeProbe(params IWindowProbe[] probes)
    {
        _probes = probes ?? System.Array.Empty<IWindowProbe>();
    }

    public CompositeProbe(IEnumerable<IWindowProbe> probes)
    {
        _probes = probes?.ToArray() ?? System.Array.Empty<IWindowProbe>();
    }

    /// <summary>依次尝试各探针，返回第一个有结果的；都没结果则返回 null。</summary>
    public WindowSnapshot? Probe()
    {
        foreach (var p in _probes)
        {
            try
            {
                var r = p.Probe();
                if (r is not null) return r;
            }
            catch { /* 单个探针失败不影响整体，继续试下一个 */ }
        }
        return null;
    }
}
