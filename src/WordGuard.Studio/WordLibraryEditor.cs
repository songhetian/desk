using WordGuard.Core;

namespace WordGuard.Studio;

/// <summary>向词库添加词条的结果。</summary>
public enum AddWordResult
{
    /// <summary>添加成功。</summary>
    Success,
    /// <summary>文本为空或仅空白，拒绝。</summary>
    EmptyText,
    /// <summary>文本与已有词条重复（大小写/首尾空白不敏感），拒绝。</summary>
    Duplicate,
}

/// <summary>
/// 词库编辑：围绕 <see cref="WordLibrary"/> 的增删改与批量操作。
/// 词条本身是不可变记录，编辑以「按 Id 替换」实现；校验保证词库不含空文本与重复文本。
/// 纯逻辑、与 UI 解耦，便于单测与管理端复用。
/// </summary>
public sealed class WordLibraryEditor
{
    private readonly WordLibrary _library;

    public WordLibraryEditor(WordLibrary library) => _library = library;

    /// <summary>被编辑的词库实例（同一引用，编辑直接作用于其 <c>Words</c> 列表）。</summary>
    public WordLibrary Library => _library;

    /// <summary>添加一个词条。自动去除首尾空白；空文本或重复文本返回对应失败原因。</summary>
    public AddWordResult Add(WordEntry entry)
    {
        var text = (entry.Text ?? "").Trim();
        if (text.Length == 0)
            return AddWordResult.EmptyText;
        if (_library.Words.Any(w => w.Text.Trim().Equals(text, StringComparison.OrdinalIgnoreCase)))
            return AddWordResult.Duplicate;

        _library.Words.Add(entry with { Text = text });
        return AddWordResult.Success;
    }

    /// <summary>按 Id 删除词条；不存在返回 false。</summary>
    public bool Remove(Guid id) => _library.Words.RemoveAll(w => w.Id == id) > 0;

    /// <summary>按 Id 替换整条词条（保留原 Id）；不存在返回 false。</summary>
    public bool Update(Guid id, WordEntry newEntry)
    {
        var idx = _library.Words.FindIndex(w => w.Id == id);
        if (idx < 0) return false;
        _library.Words[idx] = newEntry with { Id = id };
        return true;
    }

    /// <summary>按 Id 切换词条的启用/停用；不存在返回 false。</summary>
    public bool SetEnabled(Guid id, bool enabled)
    {
        var idx = _library.Words.FindIndex(w => w.Id == id);
        if (idx < 0) return false;
        _library.Words[idx] = _library.Words[idx] with { Enabled = enabled };
        return true;
    }

    /// <summary>按 Id 修改分类；不存在返回 false。</summary>
    public bool SetCategory(Guid id, string category)
    {
        var idx = _library.Words.FindIndex(w => w.Id == id);
        if (idx < 0) return false;
        _library.Words[idx] = _library.Words[idx] with { Category = category };
        return true;
    }

    /// <summary>所有分类及其词条数量（按名称升序），空分类名记为"未分类"。</summary>
    public IReadOnlyList<(string Name, int Count)> GetCategories()
    {
        return _library.Words
            .GroupBy(w => w.Category is { Length: > 0 } ? w.Category : "未分类")
            .Select(g => (g.Key, g.Count()))
            .OrderBy(x => x.Key == "未分类" ? "鿿" : x.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>重命名分类（作用于所有该分类词条）。新名为空或与旧名相同则忽略。返回受影响条数。</summary>
    public int RenameCategory(string oldName, string newName)
    {
        newName = (newName ?? "").Trim();
        oldName = oldName ?? "";
        if (newName.Length == 0 || oldName == newName) return 0;
        var n = 0;
        for (var i = 0; i < _library.Words.Count; i++)
        {
            var w = _library.Words[i];
            if ((w.Category ?? "") == oldName)
            {
                _library.Words[i] = w with { Category = newName };
                n++;
            }
        }
        return n;
    }

    /// <summary>删除分类：把该分类词条改挂到 <paramref name="reassignTo"/>（空=归入"未分类"）。返回受影响条数。</summary>
    public int DeleteCategory(string name, string? reassignTo)
    {
        name = name ?? "";
        reassignTo = string.IsNullOrWhiteSpace(reassignTo) ? "" : reassignTo.Trim();
        if (name == reassignTo) return 0;
        var n = 0;
        for (var i = 0; i < _library.Words.Count; i++)
        {
            var w = _library.Words[i];
            if ((w.Category ?? "") == name)
            {
                _library.Words[i] = w with { Category = reassignTo };
                n++;
            }
        }
        return n;
    }

    /// <summary>批量设置全部词条的启用/停用；返回实际发生变化的条数（已符合目标态的不计）。</summary>
    public int SetEnabledForAll(bool enabled)
    {
        var changed = 0;
        for (var i = 0; i < _library.Words.Count; i++)
        {
            var w = _library.Words[i];
            if (w.Enabled != enabled)
            {
                _library.Words[i] = w with { Enabled = enabled };
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// 导出权威 <c>wordlib.json</c>：写入最后更新时间（UTC）并返回缩进 JSON。
    /// 供管理端「导出分发」按钮调用；客户端按 <c>updatedAt</c> 判断分发是否生效。
    /// </summary>
    public string Export()
    {
        _library.UpdatedAt = DateTime.UtcNow;
        return _library.ToJson();
    }
}
