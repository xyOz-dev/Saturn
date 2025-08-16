using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Saturn.Data.Models;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Data;

public class ChatHistoryRepository : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private readonly string _dbPath;

    public ChatHistoryRepository(string? workspacePath = null)
    {
        var saturnDir = workspacePath != null 
            ? Path.Combine(workspacePath, ".saturn")
            : Path.Combine(Environment.CurrentDirectory, ".saturn");
        
        if (!Directory.Exists(saturnDir))
        {
            Directory.CreateDirectory(saturnDir);
        }

        _dbPath = Path.Combine(saturnDir, "chats.db");
        _connectionString = $"Data Source={_dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        
        using (var cmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            cmd.ExecuteNonQuery();

        var createSessionsTable = @"
            CREATE TABLE IF NOT EXISTS ChatSessions (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                ChatType TEXT NOT NULL,
                ParentSessionId TEXT,
                AgentName TEXT,
                Model TEXT,
                SystemPrompt TEXT,
                Temperature REAL,
                MaxTokens INTEGER,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                IsActive INTEGER NOT NULL
            )";

        var createMessagesTable = @"
            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                Role TEXT NOT NULL,
                Content TEXT NOT NULL,
                Name TEXT,
                AgentName TEXT,
                Timestamp TEXT NOT NULL,
                SequenceNumber INTEGER NOT NULL,
                ToolCallsJson TEXT,
                ToolCallId TEXT,
                FOREIGN KEY (SessionId) REFERENCES ChatSessions(Id)
            )";

        var createToolCallsTable = @"
            CREATE TABLE IF NOT EXISTS ToolCalls (
                Id TEXT PRIMARY KEY,
                MessageId TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                ToolName TEXT NOT NULL,
                Arguments TEXT NOT NULL,
                Result TEXT,
                Error TEXT,
                Timestamp TEXT NOT NULL,
                DurationMs INTEGER NOT NULL,
                AgentName TEXT,
                FOREIGN KEY (MessageId) REFERENCES ChatMessages(Id),
                FOREIGN KEY (SessionId) REFERENCES ChatSessions(Id)
            )";

        var createIndices = @"
            CREATE INDEX IF NOT EXISTS idx_messages_session ON ChatMessages(SessionId);
            CREATE INDEX IF NOT EXISTS idx_messages_timestamp ON ChatMessages(Timestamp);
            CREATE INDEX IF NOT EXISTS idx_toolcalls_session ON ToolCalls(SessionId);
            CREATE INDEX IF NOT EXISTS idx_toolcalls_message ON ToolCalls(MessageId);
            CREATE INDEX IF NOT EXISTS idx_sessions_chattype ON ChatSessions(ChatType);
            CREATE INDEX IF NOT EXISTS idx_sessions_parent ON ChatSessions(ParentSessionId);";

        using (var cmd = new SqliteCommand(createSessionsTable, connection))
            cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createMessagesTable, connection))
            cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createToolCallsTable, connection))
            cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createIndices, connection))
            cmd.ExecuteNonQuery();
        
        CleanupOrphanedRecords(connection);
    }
    
    private void CleanupOrphanedRecords(SqliteConnection connection)
    {
        try
        {
            var cleanupToolCalls = @"
                DELETE FROM ToolCalls 
                WHERE SessionId NOT IN (SELECT Id FROM ChatSessions)
                OR MessageId NOT IN (SELECT Id FROM ChatMessages)";
            
            var cleanupMessages = @"
                DELETE FROM ChatMessages 
                WHERE SessionId NOT IN (SELECT Id FROM ChatSessions)";
            
            using (var cmd = new SqliteCommand(cleanupToolCalls, connection))
                cmd.ExecuteNonQuery();
                
            using (var cmd = new SqliteCommand(cleanupMessages, connection))
                cmd.ExecuteNonQuery();
        }
        catch
        {
            // Ignore cleanup errors for backwards compatibility
        }
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var cmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            cmd.ExecuteNonQuery();
        return connection;
    }

    public async Task<ChatSession> CreateSessionAsync(string title, string chatType = "main", 
        string? parentSessionId = null, string? agentName = null, string? model = null,
        string? systemPrompt = null, double? temperature = null, int? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        var session = new ChatSession
        {
            Title = title,
            ChatType = chatType,
            ParentSessionId = parentSessionId,
            AgentName = agentName,
            Model = model,
            SystemPrompt = systemPrompt,
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        using var connection = CreateConnection();

        var sql = @"
            INSERT INTO ChatSessions (Id, Title, ChatType, ParentSessionId, AgentName, Model, 
                SystemPrompt, Temperature, MaxTokens, CreatedAt, UpdatedAt, IsActive)
            VALUES (@Id, @Title, @ChatType, @ParentSessionId, @AgentName, @Model, 
                @SystemPrompt, @Temperature, @MaxTokens, @CreatedAt, @UpdatedAt, @IsActive)";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", session.Id);
        cmd.Parameters.AddWithValue("@Title", session.Title);
        cmd.Parameters.AddWithValue("@ChatType", session.ChatType);
        cmd.Parameters.AddWithValue("@ParentSessionId", (object?)session.ParentSessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgentName", (object?)session.AgentName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Model", (object?)session.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SystemPrompt", (object?)session.SystemPrompt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Temperature", (object?)session.Temperature ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MaxTokens", (object?)session.MaxTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", session.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@UpdatedAt", session.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@IsActive", session.IsActive ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return session;
    }

    public async Task<ChatMessage> SaveMessageAsync(string sessionId, Message message, 
        string? agentName = null, int? sequenceNumber = null, CancellationToken cancellationToken = default)
    {
        var chatMessage = new ChatMessage
        {
            SessionId = sessionId,
            Role = message.Role ?? "assistant",
            Content = message.Content.ValueKind == JsonValueKind.String 
                ? message.Content.GetString() ?? string.Empty
                : message.Content.GetRawText(),
            Name = message.Name,
            AgentName = agentName,
            SequenceNumber = sequenceNumber ?? await GetNextSequenceNumberAsync(sessionId, cancellationToken),
            ToolCallsJson = message.ToolCalls != null 
                ? JsonSerializer.Serialize(message.ToolCalls) 
                : null,
            ToolCallId = message.ToolCallId
        };

        using var connection = CreateConnection();

        var sql = @"
            INSERT INTO ChatMessages (Id, SessionId, Role, Content, Name, AgentName, 
                Timestamp, SequenceNumber, ToolCallsJson, ToolCallId)
            VALUES (@Id, @SessionId, @Role, @Content, @Name, @AgentName, 
                @Timestamp, @SequenceNumber, @ToolCallsJson, @ToolCallId)";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", chatMessage.Id);
        cmd.Parameters.AddWithValue("@SessionId", chatMessage.SessionId);
        cmd.Parameters.AddWithValue("@Role", chatMessage.Role);
        cmd.Parameters.AddWithValue("@Content", chatMessage.Content);
        cmd.Parameters.AddWithValue("@Name", (object?)chatMessage.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgentName", (object?)chatMessage.AgentName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Timestamp", chatMessage.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@SequenceNumber", chatMessage.SequenceNumber);
        cmd.Parameters.AddWithValue("@ToolCallsJson", (object?)chatMessage.ToolCallsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ToolCallId", (object?)chatMessage.ToolCallId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await UpdateSessionTimestampAsync(sessionId, cancellationToken);
        
        return chatMessage;
    }

    public async Task<List<ChatMessage>> SaveMessageBatchAsync(string sessionId, List<Message> messages,
        string? agentName = null, CancellationToken cancellationToken = default)
    {
        var chatMessages = new List<ChatMessage>();
        using var connection = CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            int startSequence = await GetNextSequenceNumberAsync(sessionId, cancellationToken);
            
            foreach (var message in messages)
            {
                var chatMessage = new ChatMessage
                {
                    SessionId = sessionId,
                    Role = message.Role ?? "assistant",
                    Content = message.Content.ValueKind == JsonValueKind.String 
                        ? message.Content.GetString() ?? string.Empty
                        : message.Content.GetRawText(),
                    Name = message.Name,
                    AgentName = agentName,
                    SequenceNumber = startSequence++,
                    ToolCallsJson = message.ToolCalls != null 
                        ? JsonSerializer.Serialize(message.ToolCalls) 
                        : null,
                    ToolCallId = message.ToolCallId
                };

                var sql = @"
                    INSERT INTO ChatMessages (Id, SessionId, Role, Content, Name, AgentName, 
                        Timestamp, SequenceNumber, ToolCallsJson, ToolCallId)
                    VALUES (@Id, @SessionId, @Role, @Content, @Name, @AgentName, 
                        @Timestamp, @SequenceNumber, @ToolCallsJson, @ToolCallId)";

                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", chatMessage.Id);
                cmd.Parameters.AddWithValue("@SessionId", chatMessage.SessionId);
                cmd.Parameters.AddWithValue("@Role", chatMessage.Role);
                cmd.Parameters.AddWithValue("@Content", chatMessage.Content);
                cmd.Parameters.AddWithValue("@Name", (object?)chatMessage.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AgentName", (object?)chatMessage.AgentName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Timestamp", chatMessage.Timestamp.ToString("O"));
                cmd.Parameters.AddWithValue("@SequenceNumber", chatMessage.SequenceNumber);
                cmd.Parameters.AddWithValue("@ToolCallsJson", (object?)chatMessage.ToolCallsJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToolCallId", (object?)chatMessage.ToolCallId ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                chatMessages.Add(chatMessage);
            }

            var updateSql = "UPDATE ChatSessions SET UpdatedAt = @UpdatedAt WHERE Id = @Id";
            using (var updateCmd = new SqliteCommand(updateSql, connection, transaction))
            {
                updateCmd.Parameters.AddWithValue("@Id", sessionId);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("O"));
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return chatMessages;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ToolCallRecord> SaveToolCallAsync(string messageId, string sessionId, 
        string toolName, string arguments, string? agentName = null, CancellationToken cancellationToken = default)
    {
        var toolCall = new ToolCallRecord
        {
            MessageId = messageId,
            SessionId = sessionId,
            ToolName = toolName,
            Arguments = arguments,
            AgentName = agentName
        };

        using var connection = CreateConnection();

        var sql = @"
            INSERT INTO ToolCalls (Id, MessageId, SessionId, ToolName, Arguments, 
                Result, Error, Timestamp, DurationMs, AgentName)
            VALUES (@Id, @MessageId, @SessionId, @ToolName, @Arguments, 
                @Result, @Error, @Timestamp, @DurationMs, @AgentName)";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", toolCall.Id);
        cmd.Parameters.AddWithValue("@MessageId", toolCall.MessageId);
        cmd.Parameters.AddWithValue("@SessionId", toolCall.SessionId);
        cmd.Parameters.AddWithValue("@ToolName", toolCall.ToolName);
        cmd.Parameters.AddWithValue("@Arguments", toolCall.Arguments);
        cmd.Parameters.AddWithValue("@Result", (object?)toolCall.Result ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Error", (object?)toolCall.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Timestamp", toolCall.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@DurationMs", toolCall.DurationMs);
        cmd.Parameters.AddWithValue("@AgentName", (object?)toolCall.AgentName ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return toolCall;
    }

    public async Task<List<ToolCallRecord>> SaveToolCallBatchAsync(string sessionId, 
        List<(string MessageId, string ToolName, string Arguments)> toolCalls,
        string? agentName = null, CancellationToken cancellationToken = default)
    {
        var records = new List<ToolCallRecord>();
        using var connection = CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var (messageId, toolName, arguments) in toolCalls)
            {
                var toolCall = new ToolCallRecord
                {
                    MessageId = messageId,
                    SessionId = sessionId,
                    ToolName = toolName,
                    Arguments = arguments,
                    AgentName = agentName
                };

                var sql = @"
                    INSERT INTO ToolCalls (Id, MessageId, SessionId, ToolName, Arguments, 
                        Result, Error, Timestamp, DurationMs, AgentName)
                    VALUES (@Id, @MessageId, @SessionId, @ToolName, @Arguments, 
                        @Result, @Error, @Timestamp, @DurationMs, @AgentName)";

                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", toolCall.Id);
                cmd.Parameters.AddWithValue("@MessageId", toolCall.MessageId);
                cmd.Parameters.AddWithValue("@SessionId", toolCall.SessionId);
                cmd.Parameters.AddWithValue("@ToolName", toolCall.ToolName);
                cmd.Parameters.AddWithValue("@Arguments", toolCall.Arguments);
                cmd.Parameters.AddWithValue("@Result", (object?)toolCall.Result ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Error", (object?)toolCall.Error ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Timestamp", toolCall.Timestamp.ToString("O"));
                cmd.Parameters.AddWithValue("@DurationMs", toolCall.DurationMs);
                cmd.Parameters.AddWithValue("@AgentName", (object?)toolCall.AgentName ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                records.Add(toolCall);
            }

            await transaction.CommitAsync(cancellationToken);
            return records;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task UpdateToolCallResultAsync(string toolCallId, string? result, string? error, int durationMs)
    {
        using var connection = CreateConnection();

        var sql = @"
            UPDATE ToolCalls 
            SET Result = @Result, Error = @Error, DurationMs = @DurationMs
            WHERE Id = @Id";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", toolCallId);
        cmd.Parameters.AddWithValue("@Result", (object?)result ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationMs", durationMs);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ChatSession>> GetSessionsAsync(string? chatType = null, int limit = 100)
    {
        using var connection = CreateConnection();

        var sql = chatType != null
            ? "SELECT * FROM ChatSessions WHERE ChatType = @ChatType ORDER BY UpdatedAt DESC LIMIT @Limit"
            : "SELECT * FROM ChatSessions ORDER BY UpdatedAt DESC LIMIT @Limit";

        using var cmd = new SqliteCommand(sql, connection);
        if (chatType != null)
            cmd.Parameters.AddWithValue("@ChatType", chatType);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var sessions = new List<ChatSession>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(ReadSession(reader));
        }

        return sessions;
    }

    public async Task<ChatSession?> GetSessionAsync(string sessionId)
    {
        using var connection = CreateConnection();

        var sql = "SELECT * FROM ChatSessions WHERE Id = @Id";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", sessionId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadSession(reader);
        }

        return null;
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(string sessionId)
    {
        using var connection = CreateConnection();

        var sql = "SELECT * FROM ChatMessages WHERE SessionId = @SessionId ORDER BY SequenceNumber";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);

        var messages = new List<ChatMessage>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    public async Task<List<ToolCallRecord>> GetToolCallsAsync(string sessionId)
    {
        using var connection = CreateConnection();

        var sql = "SELECT * FROM ToolCalls WHERE SessionId = @SessionId ORDER BY Timestamp";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);

        var toolCalls = new List<ToolCallRecord>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            toolCalls.Add(ReadToolCall(reader));
        }

        return toolCalls;
    }

    public async Task<List<ChatSession>> GetSubAgentSessionsAsync(string parentSessionId)
    {
        using var connection = CreateConnection();

        var sql = "SELECT * FROM ChatSessions WHERE ParentSessionId = @ParentSessionId ORDER BY CreatedAt";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@ParentSessionId", parentSessionId);

        var sessions = new List<ChatSession>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(ReadSession(reader));
        }

        return sessions;
    }

    public async Task SetSessionInactiveAsync(string sessionId)
    {
        using var connection = CreateConnection();

        var sql = "UPDATE ChatSessions SET IsActive = 0 WHERE Id = @Id";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", sessionId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateSessionTimestampAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        var sql = "UPDATE ChatSessions SET UpdatedAt = @UpdatedAt WHERE Id = @Id";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", sessionId);
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetNextSequenceNumberAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        var sql = "SELECT COALESCE(MAX(SequenceNumber), 0) + 1 FROM ChatMessages WHERE SessionId = @SessionId";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private ChatSession ReadSession(SqliteDataReader reader)
    {
        return new ChatSession
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            ChatType = reader.GetString(reader.GetOrdinal("ChatType")),
            ParentSessionId = reader.IsDBNull(reader.GetOrdinal("ParentSessionId")) 
                ? null : reader.GetString(reader.GetOrdinal("ParentSessionId")),
            AgentName = reader.IsDBNull(reader.GetOrdinal("AgentName")) 
                ? null : reader.GetString(reader.GetOrdinal("AgentName")),
            Model = reader.IsDBNull(reader.GetOrdinal("Model")) 
                ? null : reader.GetString(reader.GetOrdinal("Model")),
            SystemPrompt = reader.IsDBNull(reader.GetOrdinal("SystemPrompt")) 
                ? null : reader.GetString(reader.GetOrdinal("SystemPrompt")),
            Temperature = reader.IsDBNull(reader.GetOrdinal("Temperature")) 
                ? null : reader.GetDouble(reader.GetOrdinal("Temperature")),
            MaxTokens = reader.IsDBNull(reader.GetOrdinal("MaxTokens")) 
                ? null : reader.GetInt32(reader.GetOrdinal("MaxTokens")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
            IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1
        };
    }

    private ChatMessage ReadMessage(SqliteDataReader reader)
    {
        return new ChatMessage
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            SessionId = reader.GetString(reader.GetOrdinal("SessionId")),
            Role = reader.GetString(reader.GetOrdinal("Role")),
            Content = reader.GetString(reader.GetOrdinal("Content")),
            Name = reader.IsDBNull(reader.GetOrdinal("Name")) 
                ? null : reader.GetString(reader.GetOrdinal("Name")),
            AgentName = reader.IsDBNull(reader.GetOrdinal("AgentName")) 
                ? null : reader.GetString(reader.GetOrdinal("AgentName")),
            Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("Timestamp"))),
            SequenceNumber = reader.GetInt32(reader.GetOrdinal("SequenceNumber")),
            ToolCallsJson = reader.IsDBNull(reader.GetOrdinal("ToolCallsJson")) 
                ? null : reader.GetString(reader.GetOrdinal("ToolCallsJson")),
            ToolCallId = reader.IsDBNull(reader.GetOrdinal("ToolCallId")) 
                ? null : reader.GetString(reader.GetOrdinal("ToolCallId"))
        };
    }

    private ToolCallRecord ReadToolCall(SqliteDataReader reader)
    {
        return new ToolCallRecord
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            MessageId = reader.GetString(reader.GetOrdinal("MessageId")),
            SessionId = reader.GetString(reader.GetOrdinal("SessionId")),
            ToolName = reader.GetString(reader.GetOrdinal("ToolName")),
            Arguments = reader.GetString(reader.GetOrdinal("Arguments")),
            Result = reader.IsDBNull(reader.GetOrdinal("Result")) 
                ? null : reader.GetString(reader.GetOrdinal("Result")),
            Error = reader.IsDBNull(reader.GetOrdinal("Error")) 
                ? null : reader.GetString(reader.GetOrdinal("Error")),
            Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("Timestamp"))),
            DurationMs = reader.GetInt32(reader.GetOrdinal("DurationMs")),
            AgentName = reader.IsDBNull(reader.GetOrdinal("AgentName")) 
                ? null : reader.GetString(reader.GetOrdinal("AgentName"))
        };
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}