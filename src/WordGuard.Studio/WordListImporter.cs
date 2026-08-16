using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using WordGuard.Core;

namespace WordGuard.Studio;

/// <summary>导入问题类型：错误（该行被跳过）/ 警告（已纠正默认并保留）。</summary>
public enum ImportIssueKind { Error, Warning }

/// <summary>单行导入问题（行号为 1-based，含表头行）。</summary>
/// <param name="RowNumber">出错行号（从 1 计，含表头）。</param>
/// <param name="Message">问题描述。</param>
/// <param name="Kind">错误（跳过该行）或警告（已纠正）。</param>
public sealed record ImportIssue(int RowNumber, string Message, ImportIssueKind Kind);

/// <summary>导入结果汇总。</summary>
public sealed class ImportResult
{
    /// <summary>成功解析出的词条（已分配新 Id）。</summary>
    public List<WordEntry> Words { get; } = new();

    /// <summary>逐行问题（空文本错误、重复警告、严重度无法识别警告等）。</summary>
    public List<ImportIssue> Issues { get; } = new();

    public int ImportedCount => Words.Count;
    public int ErrorCount => Issues.Count(i => i.Kind == ImportIssueKind.Error);
    public int WarningCount => Issues.Count(i => i.Kind == ImportIssueKind.Warning);
}

/// <summary>列映射配置：默认按列序号；开启 <see cref="HasHeader"/> 时按表头名自动识别列。</summary>
public sealed record ImportColumnMapping
{
    /// <summary>首行是否为表头（用于按列名定位，而非固定列序号）。</summary>
    public bool HasHeader { get; init; }

    /// <summary>文本列序号（默认 0）。</summary>
    public int TextColumn { get; init; }

    /// <summary>分类列序号（默认 1）。</summary>
    public int CategoryColumn { get; init; } = 1;

    /// <summary>严重度列序号（默认 2）。</summary>
    public int SeverityColumn { get; init; } = 2;

    /// <summary>启用状态列序号（默认 3）。</summary>
    public int EnabledColumn { get; init; } = 3;

    /// <summary>匹配模式列序号（-1 表示无此列，默认 Contains）。</summary>
    public int MatchModeColumn { get; init; } = -1;

    /// <summary>是否跳过与已有文本重复的词（大小写/首尾空白不敏感）。默认 true。</summary>
    public bool SkipDuplicates { get; init; } = true;
}

/// <summary>
/// 批量导入：把 CSV / Excel(.xlsx) 的行列解析为 <see cref="WordEntry"/> 列表。
/// 解析与校验逻辑与数据源无关（先统一成 <c>string[][]</c>），便于单测；Excel 通过 ExcelDataReader 读取，无需 Office/COM。
/// </summary>
public sealed class WordListImporter
{
    private static readonly string[] TextHeaders = { "text", "word", "content", "词", "违禁词", "词语" };
    private static readonly string[] CategoryHeaders = { "category", "分类", "类别", "分组" };
    private static readonly string[] SeverityHeaders = { "severity", "严重", "严重度", "级别", "等级" };
    private static readonly string[] EnabledHeaders = { "enabled", "启用", "启用状态", "是否启用" };
    private static readonly string[] MatchModeHeaders = { "matchmode", "匹配模式", "匹配" };

    /// <summary>从 CSV 文本导入（RFC4180 风格：支持引号包裹与转义逗号）。</summary>
    public ImportResult ImportCsv(string csv, ImportColumnMapping? mapping = null) =>
        ImportRows(ParseCsv(csv), mapping);

    /// <summary>从 Excel(.xlsx) 文件导入（取指定工作表，默认第一张）。</summary>
    public ImportResult ImportExcel(string path, ImportColumnMapping? mapping = null, int sheetIndex = 0) =>
        ImportRows(ReadExcelRows(path, sheetIndex), mapping);

    /// <summary>核心：把已拆分为「行→单元格」的二维数组转换为词条，按映射解析并校验。</summary>
    /// <param name="existingTexts">既有词库文本集合，用于跨库去重（可选）。</param>
    public ImportResult ImportRows(string[][] rows, ImportColumnMapping? mapping = null, IEnumerable<string>? existingTexts = null)
    {
        var m = mapping ?? new ImportColumnMapping();
        var result = new ImportResult();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingTexts is not null)
            foreach (var t in existingTexts) seen.Add(t);

        int textCol, catCol, sevCol, enCol, mmCol, startRow = 0;
        if (m.HasHeader && rows.Length > 0)
        {
            var header = rows[0].Select(x => (x ?? "").Trim()).ToArray();
            textCol = IndexOfHeader(header, TextHeaders);
            catCol = IndexOfHeader(header, CategoryHeaders);
            sevCol = IndexOfHeader(header, SeverityHeaders);
            enCol = IndexOfHeader(header, EnabledHeaders);
            mmCol = IndexOfHeader(header, MatchModeHeaders);
            startRow = 1;
        }
        else
        {
            textCol = m.TextColumn; catCol = m.CategoryColumn; sevCol = m.SeverityColumn;
            enCol = m.EnabledColumn; mmCol = m.MatchModeColumn;
        }

        for (var r = startRow; r < rows.Length; r++)
        {
            var row = rows[r];
            if (row.All(x => string.IsNullOrWhiteSpace(x))) // 整行空白，静默跳过
                continue;

            string Cell(int col) => col >= 0 && col < row.Length ? (row[col] ?? "").Trim() : "";

            var text = Cell(textCol);
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Issues.Add(new ImportIssue(r + 1, "文本为空，已跳过", ImportIssueKind.Error));
                continue;
            }
            if (m.SkipDuplicates && !seen.Add(text))
            {
                result.Issues.Add(new ImportIssue(r + 1, $"与已有词条重复（{text}），已跳过", ImportIssueKind.Warning));
                continue;
            }

            var (sev, sevWarn) = ParseSeverity(Cell(sevCol));
            if (sevWarn)
                result.Issues.Add(new ImportIssue(r + 1, $"严重度「{Cell(sevCol)}」无法识别，按中处理", ImportIssueKind.Warning));

            result.Words.Add(new WordEntry
            {
                Text = text,
                Category = Cell(catCol),
                Severity = sev,
                Enabled = ParseEnabled(Cell(enCol)),
                MatchMode = ParseMatchMode(Cell(mmCol)),
            });
        }
        return result;
    }

    /// <summary>解析 CSV 为二维单元格数组（支持引号包裹、"" 转义、CRLF/LF）。</summary>
    public static string[][] ParseCsv(string csv)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(csv)) return rows.ToArray();

        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { /* 由 \n 统一处理换行 */ }
                else if (c == '\n') { row.Add(field.ToString()); rows.Add(row.ToArray()); row.Clear(); field.Clear(); }
                else field.Append(c);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows.ToArray();
    }

    /// <summary>用 ExcelDataReader 读取 .xlsx 首个工作表为单元格二维数组。</summary>
    public static string[][] ReadExcelRows(string path, int sheetIndex = 0)
    {
        // ExcelDataReader 读取部分编码需要 CodePages 提供程序（只需注册一次）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        using var ds = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false },
        });
        var table = ds.Tables[sheetIndex];
        var rows = new List<string[]>();
        foreach (DataRow r in table.Rows)
        {
            var cells = new string[table.Columns.Count];
            for (var c = 0; c < table.Columns.Count; c++)
                cells[c] = r[c]?.ToString() ?? "";
            rows.Add(cells);
        }
        return rows.ToArray();
    }

    private static int IndexOfHeader(string[] header, string[] names)
    {
        for (var i = 0; i < header.Length; i++)
            if (names.Any(n => header[i].Equals(n, StringComparison.OrdinalIgnoreCase)))
                return i;
        return -1;
    }

    private static (Severity Severity, bool Warn) ParseSeverity(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (Severity.Medium, false);
        var t = s.Trim().ToLowerInvariant();
        if (t is "high" or "h" or "高" or "2") return (Severity.High, false);
        if (t is "medium" or "m" or "中" or "1") return (Severity.Medium, false);
        if (t is "low" or "l" or "低" or "0") return (Severity.Low, false);
        return (Severity.Medium, true);
    }

    private static bool ParseEnabled(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var t = s.Trim().ToLowerInvariant();
        if (t is "false" or "0" or "否" or "n" or "no" or "停用" or "禁用") return false;
        return true;
    }

    private static MatchMode ParseMatchMode(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return MatchMode.Contains;
        var t = s.Trim().ToLowerInvariant();
        return t is "contains" or "包含" ? MatchMode.Contains : MatchMode.Contains;
    }
}
