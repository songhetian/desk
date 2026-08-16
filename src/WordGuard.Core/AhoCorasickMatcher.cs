using System.Collections.Generic;

namespace WordGuard.Core;

/// <summary>
/// 基于 Aho-Corasick 多模自动机的实时违禁词匹配器。
/// 一次扫描即可找出文本中所有命中的词及其位置，词库规模变大也不退化。
/// 仅对 Enabled 且非空的词建立索引。
/// </summary>
public sealed class AhoCorasickMatcher : IMatcher
{
    private readonly Node _root = new();

    public AhoCorasickMatcher(IEnumerable<WordEntry> words)
    {
        foreach (var entry in words)
        {
            if (!entry.Enabled || string.IsNullOrEmpty(entry.Text))
                continue;
            AddWord(entry);
        }
        BuildFailureLinks();
    }

    private void AddWord(WordEntry entry)
    {
        var node = _root;
        foreach (var c in entry.Text)
        {
            node = node.Children.TryGetValue(c, out var next)
                ? next
                : (node.Children[c] = new Node());
        }
        node.Entries.Add(entry);
    }

    private void BuildFailureLinks()
    {
        var queue = new Queue<Node>();
        foreach (var child in _root.Children.Values)
        {
            child.Failure = _root;
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var (c, child) in node.Children)
            {
                var failure = node.Failure!;
                while (failure != _root && !failure.Children.ContainsKey(c))
                    failure = failure.Failure!;
                child.Failure = failure.Children.TryGetValue(c, out var f) ? f : _root;
                queue.Enqueue(child);
            }
        }
    }

    public IReadOnlyList<MatchHit> Match(string text)
    {
        var hits = new List<MatchHit>();
        var node = _root;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            while (node != _root && !node.Children.ContainsKey(c))
                node = node.Failure!;
            if (node.Children.TryGetValue(c, out var next))
                node = next;

            // 沿失败链回溯，收集所有以当前位置结尾的词
            for (var t = node; t != null; t = t.Failure)
            {
                if (t.Entries.Count == 0)
                    continue;
                foreach (var entry in t.Entries)
                    hits.Add(new MatchHit(entry.Text, i - entry.Text.Length + 1, entry.Text.Length, entry));
            }
        }

        return hits;
    }

    private sealed class Node
    {
        public Dictionary<char, Node> Children { get; } = new();
        public Node? Failure { get; set; }
        public List<WordEntry> Entries { get; } = new();
    }
}
