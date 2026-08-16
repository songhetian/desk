namespace WordGuard.Core;

/// <summary>
/// 一次匹配命中的结果。
/// </summary>
/// <param name="Word">命中的违禁词文本。</param>
/// <param name="Index">命中在输入文本中的起始下标（以 UTF-16 字符计）。</param>
/// <param name="Length">命中词长度（字符数）。</param>
/// <param name="Entry">命中所对应的词库条目。</param>
public sealed record MatchHit(string Word, int Index, int Length, WordEntry Entry);
