using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WordGuard.Core;

/// <summary>一条命中词的结构化引用（审计日志命中词列表项）。</summary>
/// <param name="Id">对应 <see cref="WordEntry.Id"/>，便于回指词库条目。</param>
/// <param name="Text">命中词文本。</param>
public sealed record MatchedWord(string Id, string Text);

/// <summary>一条监控告警审计记录（对应 PRD 监控日志：触发时间、目标软件、触发内容、处理结果）。</summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.MinValue;  // UTC
    public string TargetSoftware { get; set; } = "";  // 进程名，如 cs.exe
    public string WindowTitle { get; set; } = "";     // 目标窗口标题（PRD 审计表要求）
    public string TriggeredContent { get; set; } = ""; // 触发内容（含客户信息，敏感）
    public List<MatchedWord> MatchedWords { get; set; } = new(); // 命中词 [{id,text}]
    public Severity Severity { get; set; }
    public string Disposition { get; set; } = "";      // 已弹窗/已响铃/已记日志/已确认/未确认（超时）
    public string AlertChannels { get; set; } = "";    // 实际触发的告警通道（popup,sound,highlight），逗号分隔
}

/// <summary>
/// 审计日志存储：SQLite 落地 + 按时间范围（可选内容检索）查询。
/// 内存库测试用 <c>Data Source=:memory:</c>（连接常驻，随实例 Dispose 关闭）。
/// <para><b>契约对齐 PRD</b>：<c>triggered_at</c> 存 ISO8601 UTC 文本（可字典序排序），补 <c>window_title</c> /
/// <c>alert_channels</c> 两列；<c>matched_words</c> 存结构化 JSON 数组（含 id+text）。</para>
/// </summary>
public sealed class AuditLogStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public AuditLogStore(string connectionString)
    {
        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS audit_log (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                triggered_at TEXT NOT NULL,
                target        TEXT NOT NULL,
                window_title  TEXT NOT NULL DEFAULT '',
                content       TEXT NOT NULL,
                matched_words TEXT NOT NULL,
                severity      INTEGER NOT NULL,
                disposition   TEXT NOT NULL,
                alert_channels TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_audit_log_ts ON audit_log(triggered_at);";
        cmd.ExecuteNonQuery();
    }

    public void Add(AuditLogEntry entry)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO audit_log (triggered_at, target, window_title, content, matched_words, severity, disposition, alert_channels)
            VALUES (@triggered_at, @target, @window_title, @content, @matched_words, @severity, @disposition, @alert_channels);";
        cmd.Parameters.Add(new SqliteParameter("@triggered_at", ToIso(entry.Timestamp)));
        cmd.Parameters.Add(new SqliteParameter("@target", entry.TargetSoftware));
        cmd.Parameters.Add(new SqliteParameter("@window_title", entry.WindowTitle));
        cmd.Parameters.Add(new SqliteParameter("@content", entry.TriggeredContent));
        cmd.Parameters.Add(new SqliteParameter("@matched_words", JsonSerializer.Serialize(entry.MatchedWords)));
        cmd.Parameters.Add(new SqliteParameter("@severity", (int)entry.Severity));
        cmd.Parameters.Add(new SqliteParameter("@disposition", entry.Disposition));
        cmd.Parameters.Add(new SqliteParameter("@alert_channels", entry.AlertChannels));
        cmd.ExecuteNonQuery();
        // 回填自增 Id，便于后续更新（确认/超时）
        using var idCmd = _conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid();";
        entry.Id = (long)idCmd.ExecuteScalar()!;
    }

    /// <summary>更新某条日志的处理结果（客服确认 / 超时未确认）。</summary>
    public void UpdateDisposition(long id, string disposition)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE audit_log SET disposition = @disposition WHERE id = @id;";
        cmd.Parameters.Add(new SqliteParameter("@disposition", disposition));
        cmd.Parameters.Add(new SqliteParameter("@id", id));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AuditLogEntry> Query(DateTime from, DateTime to, string? contentFilter = null)
    {
        using var cmd = _conn.CreateCommand();
        var sql = "SELECT id, triggered_at, target, window_title, content, matched_words, severity, disposition, alert_channels " +
                  "FROM audit_log WHERE triggered_at >= @from AND triggered_at <= @to";
        cmd.Parameters.Add(new SqliteParameter("@from", ToIso(from)));
        cmd.Parameters.Add(new SqliteParameter("@to", ToIso(to)));
        if (!string.IsNullOrWhiteSpace(contentFilter))
        {
            sql += " AND content LIKE @content";
            cmd.Parameters.Add(new SqliteParameter("@content", "%" + contentFilter + "%"));
        }
        sql += " ORDER BY triggered_at DESC;";
        cmd.CommandText = sql;

        var list = new List<AuditLogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AuditLogEntry
            {
                Id = reader.GetInt64(0),
                Timestamp = FromIso(reader.GetString(1)),
                TargetSoftware = reader.GetString(2),
                WindowTitle = reader.GetString(3),
                TriggeredContent = reader.GetString(4),
                MatchedWords = JsonSerializer.Deserialize<List<MatchedWord>>(reader.GetString(5)) ?? new List<MatchedWord>(),
                Severity = (Severity)reader.GetInt32(6),
                Disposition = reader.GetString(7),
                AlertChannels = reader.GetString(8),
            });
        }
        return list;
    }

    /// <summary>删除早于 cutoff（UTC）的日志，用于按保留天数清理敏感数据。返回被删除条数。</summary>
    public int PruneOlderThan(DateTime cutoff)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM audit_log WHERE triggered_at < @cutoff; SELECT changes();";
        cmd.Parameters.Add(new SqliteParameter("@cutoff", ToIso(cutoff)));
        return (int)(long)cmd.ExecuteScalar()!;
    }

    /// <summary>当前日志总条数。</summary>
    public int Count
    {
        get
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM audit_log;";
            return (int)(long)cmd.ExecuteScalar()!;
        }
    }

    /// <summary>ISO8601 UTC（"o" 格式可字典序排序，满足 PRD 契约）。</summary>
    private static string ToIso(DateTime dt) => dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTime FromIso(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public void Dispose() => _conn.Dispose();
}
