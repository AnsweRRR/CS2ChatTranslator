using CS2ChatTranslator.Common;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;

namespace CS2ChatTranslator.Overlay;

public sealed class ChatHistoryStore : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString;

    public ChatHistoryStore(int retentionDays)
    {
        var dataDirectory = AppDataPaths.GetDataDirectory();

        DatabasePath = Path.Combine(dataDirectory, "chat-history.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        RetentionDays = NormalizeRetentionDays(retentionDays);
        CurrentSessionId = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
    }

    public string DatabasePath { get; }
    public string CurrentSessionId { get; }
    public int RetentionDays { get; set; }

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT PRIMARY KEY,
                    started_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    timestamp_utc TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    player TEXT NOT NULL,
                    original_text TEXT NOT NULL,
                    translated_text TEXT NULL,
                    source_language TEXT NOT NULL,
                    target_language TEXT NOT NULL,
                    is_favorite INTEGER NOT NULL DEFAULT 0,
                    reply_text TEXT NULL,
                    reply_translated_text TEXT NULL,
                    FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_messages_session_time
                    ON messages(session_id, timestamp_utc);
                CREATE INDEX IF NOT EXISTS idx_messages_language
                    ON messages(source_language);
                CREATE INDEX IF NOT EXISTS idx_messages_favorite
                    ON messages(is_favorite);

                INSERT OR IGNORE INTO sessions(id, started_utc)
                VALUES ($sessionId, $startedUtc);
                """;
            command.Parameters.AddWithValue("$sessionId", CurrentSessionId);
            command.Parameters.AddWithValue("$startedUtc", ToDatabaseTimestamp(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _gate.Release();
        }

        await PruneAsync();
    }

    public async Task<long> AddMessageAsync(
        ChatMessage message,
        string? translatedText,
        string sourceLanguage,
        string targetLanguage)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO messages (
                    session_id,
                    timestamp_utc,
                    channel,
                    player,
                    original_text,
                    translated_text,
                    source_language,
                    target_language)
                VALUES (
                    $sessionId,
                    $timestampUtc,
                    $channel,
                    $player,
                    $originalText,
                    $translatedText,
                    $sourceLanguage,
                    $targetLanguage);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$sessionId", CurrentSessionId);
            command.Parameters.AddWithValue(
                "$timestampUtc",
                ToDatabaseTimestamp(new DateTimeOffset(message.Timestamp).ToUniversalTime()));
            command.Parameters.AddWithValue("$channel", message.Channel);
            command.Parameters.AddWithValue("$player", message.Player);
            command.Parameters.AddWithValue("$originalText", message.Message);
            command.Parameters.AddWithValue("$translatedText", (object?)translatedText ?? DBNull.Value);
            command.Parameters.AddWithValue("$sourceLanguage", NormalizeLanguage(sourceLanguage));
            command.Parameters.AddWithValue("$targetLanguage", NormalizeLanguage(targetLanguage));

            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveReplyAsync(long messageId, string replyText, string translatedReply)
    {
        await ExecuteAsync(
            """
            UPDATE messages
            SET reply_text = $replyText,
                reply_translated_text = $translatedReply
            WHERE id = $id;
            """,
            ("$replyText", replyText),
            ("$translatedReply", translatedReply),
            ("$id", messageId));
    }

    public Task SetFavoriteAsync(long messageId, bool isFavorite)
        => ExecuteAsync(
            "UPDATE messages SET is_favorite = $favorite WHERE id = $id;",
            ("$favorite", isFavorite ? 1 : 0),
            ("$id", messageId));

    public Task DeleteMessageAsync(long messageId)
        => ExecuteAsync("DELETE FROM messages WHERE id = $id;", ("$id", messageId));

    public async Task<IReadOnlyList<ChatHistoryMessage>> GetRecentTranslatedMessagesAsync(int limit)
    {
        var messages = await QueryMessagesInternalAsync(
            whereClause: "translated_text IS NOT NULL AND length(trim(translated_text)) > 0",
            parameters: [],
            orderBy: "id DESC",
            limit: Math.Clamp(limit, 1, 100));

        return messages.Reverse().ToList();
    }

    public async Task<IReadOnlyList<ChatSessionSummary>> GetSessionsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var result = new List<ChatSessionSummary>();
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    s.id,
                    s.started_utc,
                    MAX(m.timestamp_utc) AS last_message_utc,
                    COUNT(m.id) AS message_count
                FROM sessions s
                JOIN messages m ON m.session_id = s.id
                GROUP BY s.id, s.started_utc
                ORDER BY s.started_utc DESC;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ChatSessionSummary(
                    reader.GetString(0),
                    ParseDatabaseTimestamp(reader.GetString(1)),
                    ParseDatabaseTimestamp(reader.GetString(2)),
                    reader.GetInt32(3)));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetLanguagesAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var result = new List<string>();
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT source_language
                FROM messages
                WHERE source_language <> ''
                ORDER BY source_language;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(reader.GetString(0));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<ChatHistoryMessage>> QueryMessagesAsync(ChatHistoryFilter filter, int limit = 2000)
    {
        var clauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(filter.SessionId))
        {
            clauses.Add("session_id = $sessionId");
            parameters.Add(("$sessionId", filter.SessionId));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            clauses.Add("""
                (player LIKE $search COLLATE NOCASE
                 OR original_text LIKE $search COLLATE NOCASE
                 OR translated_text LIKE $search COLLATE NOCASE
                 OR reply_text LIKE $search COLLATE NOCASE
                 OR reply_translated_text LIKE $search COLLATE NOCASE)
                """);
            parameters.Add(("$search", $"%{filter.SearchText.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            clauses.Add("channel LIKE $channel");
            parameters.Add(("$channel", $"{filter.Channel}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Language))
        {
            clauses.Add("source_language = $language");
            parameters.Add(("$language", NormalizeLanguage(filter.Language)));
        }

        if (filter.FavoritesOnly)
            clauses.Add("is_favorite = 1");

        var where = clauses.Count == 0 ? "1 = 1" : string.Join(" AND ", clauses);
        return QueryMessagesInternalAsync(where, parameters, "timestamp_utc ASC, id ASC", Math.Clamp(limit, 1, 10000));
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var deleteMessages = connection.CreateCommand())
            {
                deleteMessages.Transaction = (SqliteTransaction)transaction;
                deleteMessages.CommandText = "DELETE FROM messages;";
                await deleteMessages.ExecuteNonQueryAsync();
            }

            await using (var deleteSessions = connection.CreateCommand())
            {
                deleteSessions.Transaction = (SqliteTransaction)transaction;
                deleteSessions.CommandText = "DELETE FROM sessions;";
                await deleteSessions.ExecuteNonQueryAsync();
            }

            await using (var restoreSession = connection.CreateCommand())
            {
                restoreSession.Transaction = (SqliteTransaction)transaction;
                restoreSession.CommandText = "INSERT INTO sessions(id, started_utc) VALUES ($id, $startedUtc);";
                restoreSession.Parameters.AddWithValue("$id", CurrentSessionId);
                restoreSession.Parameters.AddWithValue("$startedUtc", ToDatabaseTimestamp(DateTimeOffset.UtcNow));
                await restoreSession.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PruneAsync()
    {
        var retentionDays = NormalizeRetentionDays(RetentionDays);
        if (retentionDays == 0)
            return;

        var cutoff = ToDatabaseTimestamp(DateTimeOffset.UtcNow.AddDays(-retentionDays));
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM messages
                WHERE timestamp_utc < $cutoff
                  AND is_favorite = 0;

                DELETE FROM sessions
                WHERE id <> $currentSession
                  AND NOT EXISTS (
                      SELECT 1 FROM messages WHERE messages.session_id = sessions.id
                  );
                """;
            command.Parameters.AddWithValue("$cutoff", cutoff);
            command.Parameters.AddWithValue("$currentSession", CurrentSessionId);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ChatHistoryMessage>> QueryMessagesInternalAsync(
        string whereClause,
        IReadOnlyList<(string Name, object Value)> parameters,
        string orderBy,
        int limit)
    {
        await _gate.WaitAsync();
        try
        {
            var result = new List<ChatHistoryMessage>();
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    id,
                    session_id,
                    timestamp_utc,
                    channel,
                    player,
                    original_text,
                    translated_text,
                    source_language,
                    target_language,
                    is_favorite,
                    reply_text,
                    reply_translated_text
                FROM messages
                WHERE {whereClause}
                ORDER BY {orderBy}
                LIMIT $limit;
                """;

            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            command.Parameters.AddWithValue("$limit", limit);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(ReadMessage(reader));

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static ChatHistoryMessage ReadMessage(SqliteDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            ParseDatabaseTimestamp(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetInt32(9) != 0,
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));

    private static string NormalizeLanguage(string language)
        => string.IsNullOrWhiteSpace(language) ? "unknown" : language.Trim().ToLowerInvariant();

    private static string ToDatabaseTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDatabaseTimestamp(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static int NormalizeRetentionDays(int value)
        => value <= 0 ? 0 : Math.Clamp(value, 1, 3650);

    public void Dispose()
        => _gate.Dispose();
}

public sealed record ChatHistoryMessage(
    long Id,
    string SessionId,
    DateTimeOffset Timestamp,
    string Channel,
    string Player,
    string OriginalText,
    string? TranslatedText,
    string SourceLanguage,
    string TargetLanguage,
    bool IsFavorite,
    string? ReplyText,
    string? ReplyTranslatedText);

public sealed record ChatSessionSummary(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastMessageAt,
    int MessageCount)
{
    public string DisplayDate => StartedAt.ToLocalTime().ToString("MMM d, yyyy");
    public string DisplayTime => StartedAt.ToLocalTime().ToString("HH:mm");
}

public sealed record ChatHistoryFilter(
    string? SessionId,
    string SearchText,
    string Channel,
    string Language,
    bool FavoritesOnly);
