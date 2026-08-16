using System.Collections.Generic;

namespace WordGuard.Core;

/// <summary>
/// 违禁词匹配器公共接口。实现可替换（朴素扫描 / Aho-Corasick）。
/// </summary>
public interface IMatcher
{
    /// <summary>
    /// 在给定文本中找出所有命中的违禁词，返回命中列表（位置 + 条目）。
    /// </summary>
    IReadOnlyList<MatchHit> Match(string text);
}
