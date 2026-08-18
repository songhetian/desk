using System.Collections.Generic;

namespace WordGuard.Core;

/// <summary>
/// 基于 Aho-Corasick 多模自动机的实时违禁词匹配器。
/// 一次扫描即可找出文本中所有命中的词及其位置，词库规模变大也不退化。
/// 仅对 Enabled 且非空的词建立索引。
/// 支持 Contains（精确子串）和 FuzzyContains（允许中间插干扰字符）两种模式。
/// </summary>
public sealed class AhoCorasickMatcher : IMatcher
{
    private readonly Node _exactRoot = new();
    private readonly Node _fuzzyRoot = new();
    private readonly bool _hasFuzzyWords;

    public AhoCorasickMatcher(IEnumerable<WordEntry> words)
    {
        var exactWords = new List<WordEntry>();
        var fuzzyWords = new List<WordEntry>();

        foreach (var entry in words)
        {
            if (!entry.Enabled || string.IsNullOrEmpty(entry.Text))
                continue;
            if (entry.MatchMode == MatchMode.FuzzyContains)
                fuzzyWords.Add(entry);
            else
                exactWords.Add(entry);
        }

        foreach (var entry in exactWords)
            AddWord(_exactRoot, entry);
        BuildFailureLinks(_exactRoot);

        if (fuzzyWords.Count > 0)
        {
            _hasFuzzyWords = true;
            foreach (var entry in fuzzyWords)
                AddWord(_fuzzyRoot, entry);
            BuildFailureLinks(_fuzzyRoot);
        }
    }

    private static void AddWord(Node root, WordEntry entry)
    {
        var node = root;
        foreach (var c in entry.Text)
        {
            node = node.Children.TryGetValue(c, out var next)
                ? next
                : (node.Children[c] = new Node());
        }
        node.Entries.Add(entry);
    }

    private static void BuildFailureLinks(Node root)
    {
        var queue = new Queue<Node>();
        foreach (var child in root.Children.Values)
        {
            child.Failure = root;
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var (c, child) in node.Children)
            {
                var failure = node.Failure!;
                while (failure != root && !failure.Children.ContainsKey(c))
                    failure = failure.Failure!;
                child.Failure = failure.Children.TryGetValue(c, out var f) ? f : root;
                queue.Enqueue(child);
            }
        }
    }

    public IReadOnlyList<MatchHit> Match(string text)
    {
        var hits = new List<MatchHit>();
        if (string.IsNullOrEmpty(text))
            return hits;

        // 精确子串匹配（Contains 模式）
        MatchOnRoot(text, _exactRoot, hits);

        // 模糊匹配（FuzzyContains 模式）：先去噪再匹配
        if (_hasFuzzyWords)
        {
            var normalized = NormalizeText(text, out var map);
            var fuzzyHits = new List<MatchHit>();
            MatchOnRoot(normalized, _fuzzyRoot, fuzzyHits);
            foreach (var hit in fuzzyHits)
            {
                // 映射回原文位置：用 map 数组将去噪后的下标转回原下标
                var origStart = map[hit.Index];
                var origEnd = map[hit.Index + hit.Length - 1];
                var origLen = origEnd - origStart + 1;
                hits.Add(new MatchHit(hit.Word, origStart, origLen, hit.Entry));
            }
        }

        return hits;
    }

    private static void MatchOnRoot(string text, Node root, List<MatchHit> hits)
    {
        var node = root;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            while (node != root && !node.Children.ContainsKey(c))
                node = node.Failure!;
            if (node.Children.TryGetValue(c, out var next))
                node = next;

            for (var t = node; t != null; t = t.Failure)
            {
                if (t.Entries.Count == 0)
                    continue;
                foreach (var entry in t.Entries)
                    hits.Add(new MatchHit(entry.Text, i - entry.Text.Length + 1, entry.Text.Length, entry));
            }
        }
    }

    /// <summary>
    /// 去掉文本中的"干扰字符"（非汉字、非字母、非数字），
    /// 同时输出位置映射：map[normalizedIndex] = originalIndex。
    /// map 长度等于 normalized 文本长度 + 1（方便取 end 位置）。
    /// </summary>
    private static string NormalizeText(string text, out int[] map)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var indices = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsSignificantChar(c))
            {
                sb.Append(c);
                indices.Add(i);
            }
        }
        map = indices.ToArray();
        return sb.ToString();
    }

    /// <summary>
    /// 判断字符是否为"有意义字符"——汉字、字母、数字参与匹配，其余视为干扰符（空格/标点/符号等）。
    /// </summary>
    private static bool IsSignificantChar(char c)
    {
        // CJK 统一汉字 (0x4E00-0x9FFF) + 扩展 A (0x3400-0x4DBF)
        if ((c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF'))
            return true;
        // 字母或数字
        if (char.IsLetterOrDigit(c))
            return true;
        return false;
    }

    private sealed class Node
    {
        public Dictionary<char, Node> Children { get; } = new();
        public Node? Failure { get; set; }
        public List<WordEntry> Entries { get; } = new();
    }
}
