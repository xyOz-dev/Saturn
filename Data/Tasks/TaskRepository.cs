using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Saturn.Data.Tasks
{
    public class TaskRepository : IDisposable
    {
        private readonly string _connectionString;
        private readonly bool _includeRuntimeTables;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _readLock = new(26, 26);

        public string DbPath { get; }

        public TaskRepository(string dbPath, bool includeRuntimeTables)
        {
            DbPath = dbPath;
            _includeRuntimeTables = includeRuntimeTables;
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Private";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = CreateConnection();
            using var cmd = new SqliteCommand(@"
                CREATE TABLE IF NOT EXISTS Tasks (
                    Id TEXT PRIMARY KEY,
                    Scope TEXT NOT NULL,
                    Board TEXT NOT NULL DEFAULT 'default',
                    Title TEXT NOT NULL,
                    Notes TEXT,
                    Status TEXT NOT NULL DEFAULT 'pending',
                    Priority TEXT NOT NULL DEFAULT 'normal',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedBy TEXT NOT NULL DEFAULT 'user',
                    AgentAvailable INTEGER NOT NULL DEFAULT 0,
                    RequiresApproval INTEGER NOT NULL DEFAULT 0,
                    UserHandoffOnly INTEGER NOT NULL DEFAULT 0,
                    ClaimStatus TEXT NOT NULL DEFAULT 'none',
                    ClaimedBy TEXT,
                    RecurrenceKind TEXT NOT NULL DEFAULT 'none',
                    RecurrenceIntervalSeconds INTEGER,
                    RecurrenceCron TEXT,
                    CatchUpPolicy TEXT NOT NULL DEFAULT 'run_once',
                    NextRunAt TEXT,
                    LastRunAt TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CompletedAt TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_tasks_scope_board ON Tasks(Scope, Board);
                CREATE INDEX IF NOT EXISTS idx_tasks_status ON Tasks(Status);
                CREATE INDEX IF NOT EXISTS idx_tasks_nextrun ON Tasks(NextRunAt) WHERE NextRunAt IS NOT NULL;

                CREATE TABLE IF NOT EXISTS TaskDependencies (
                    TaskId TEXT NOT NULL,
                    BlockedByTaskId TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    PRIMARY KEY (TaskId, BlockedByTaskId)
                );

                CREATE TABLE IF NOT EXISTS TaskRuns (
                    Id TEXT PRIMARY KEY,
                    TaskId TEXT NOT NULL,
                    ScheduledFor TEXT NOT NULL,
                    FiredAt TEXT NOT NULL,
                    Outcome TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_runs_task ON TaskRuns(TaskId);
            ", connection);
            cmd.ExecuteNonQuery();

            if (_includeRuntimeTables)
            {
                using var runtimeCmd = new SqliteCommand(@"
                    CREATE TABLE IF NOT EXISTS TaskWaiters (
                        Id TEXT PRIMARY KEY,
                        WaitTargetKind TEXT NOT NULL,
                        WaitTargetId TEXT NOT NULL,
                        WaiterKind TEXT NOT NULL,
                        WaiterAgentId TEXT,
                        WaiterAgentName TEXT,
                        PromptTemplate TEXT,
                        CreatedAt TEXT NOT NULL,
                        DeliveredAt TEXT,
                        DeliveryAttempts INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE INDEX IF NOT EXISTS idx_waiters_target ON TaskWaiters(WaitTargetId) WHERE DeliveredAt IS NULL;

                    CREATE TABLE IF NOT EXISTS TaskDispatches (
                        Id TEXT PRIMARY KEY,
                        TaskId TEXT NOT NULL,
                        AgentManagerTaskId TEXT,
                        AgentId TEXT,
                        AgentName TEXT,
                        StartedAt TEXT NOT NULL,
                        CompletedAt TEXT,
                        Success INTEGER,
                        Result TEXT,
                        Orphaned INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE INDEX IF NOT EXISTS idx_dispatch_mgr ON TaskDispatches(AgentManagerTaskId);
                    CREATE INDEX IF NOT EXISTS idx_dispatch_open ON TaskDispatches(TaskId) WHERE CompletedAt IS NULL;

                    CREATE TABLE IF NOT EXISTS WakeQueue (
                        Id TEXT PRIMARY KEY,
                        Kind TEXT NOT NULL,
                        TaskId TEXT,
                        DedupeKey TEXT UNIQUE,
                        Prompt TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        DeliveredAt TEXT
                    );
                ", connection);
                runtimeCmd.ExecuteNonQuery();
            }
        }

        private SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using (var cmd = new SqliteCommand("PRAGMA journal_mode = WAL;", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand("PRAGMA busy_timeout = 5000;", connection)) cmd.ExecuteNonQuery();
            return connection;
        }

        private async Task<T> WithWriteLockAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
        {
            await _writeLock.WaitAsync(ct);
            try { return await action(); }
            finally { _writeLock.Release(); }
        }

        private async Task<T> WithReadLockAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
        {
            await _readLock.WaitAsync(ct);
            try { return await action(); }
            finally { _readLock.Release(); }
        }

        // ---------- Tasks ----------

        public Task<SaturnTask> InsertTaskAsync(SaturnTask t, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    INSERT INTO Tasks (Id, Scope, Board, Title, Notes, Status, Priority, SortOrder, CreatedBy,
                        AgentAvailable, RequiresApproval, UserHandoffOnly, ClaimStatus, ClaimedBy,
                        RecurrenceKind, RecurrenceIntervalSeconds, RecurrenceCron, CatchUpPolicy,
                        NextRunAt, LastRunAt, CreatedAt, UpdatedAt, CompletedAt)
                    VALUES (@Id, @Scope, @Board, @Title, @Notes, @Status, @Priority,
                        (SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Tasks WHERE Scope = @Scope AND Board = @Board),
                        @CreatedBy, @AgentAvailable, @RequiresApproval, @UserHandoffOnly, @ClaimStatus, @ClaimedBy,
                        @RecurrenceKind, @RecurrenceIntervalSeconds, @RecurrenceCron, @CatchUpPolicy,
                        @NextRunAt, @LastRunAt, @CreatedAt, @UpdatedAt, @CompletedAt)", connection);
                BindTask(cmd, t);
                await cmd.ExecuteNonQueryAsync(ct);
                return t;
            }, ct);
        }

        public Task<bool> UpdateTaskAsync(SaturnTask t, CancellationToken ct = default)
        {
            t.UpdatedAt = DateTime.UtcNow;
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    UPDATE Tasks SET Scope=@Scope, Board=@Board, Title=@Title, Notes=@Notes, Status=@Status,
                        Priority=@Priority, SortOrder=@SortOrder, CreatedBy=@CreatedBy, AgentAvailable=@AgentAvailable,
                        RequiresApproval=@RequiresApproval, UserHandoffOnly=@UserHandoffOnly, ClaimStatus=@ClaimStatus,
                        ClaimedBy=@ClaimedBy, RecurrenceKind=@RecurrenceKind,
                        RecurrenceIntervalSeconds=@RecurrenceIntervalSeconds, RecurrenceCron=@RecurrenceCron,
                        CatchUpPolicy=@CatchUpPolicy, NextRunAt=@NextRunAt, LastRunAt=@LastRunAt,
                        UpdatedAt=@UpdatedAt, CompletedAt=@CompletedAt
                    WHERE Id=@Id", connection);
                BindTask(cmd, t);
                cmd.Parameters.AddWithValue("@SortOrder", t.SortOrder);
                return await cmd.ExecuteNonQueryAsync(ct) > 0;
            }, ct);
        }

        public Task<SaturnTask?> GetTaskAsync(string id, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand("SELECT * FROM Tasks WHERE Id = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", id);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                return await reader.ReadAsync(ct) ? ReadTask(reader) : null;
            }, ct);
        }

        public Task<bool> DeleteTaskAsync(string id, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "DELETE FROM Tasks WHERE Id = @Id; DELETE FROM TaskDependencies WHERE TaskId = @Id OR BlockedByTaskId = @Id;",
                    connection);
                cmd.Parameters.AddWithValue("@Id", id);
                return await cmd.ExecuteNonQueryAsync(ct) > 0;
            }, ct);
        }

        public Task<List<SaturnTask>> QueryTasksAsync(string? scope = null, string? board = null, string? status = null, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                var sql = "SELECT * FROM Tasks WHERE 1=1";
                if (scope != null) sql += " AND Scope = @Scope";
                if (board != null) sql += " AND Board = @Board";
                if (status != null) sql += " AND Status = @Status";
                sql += " ORDER BY SortOrder, CreatedAt";
                using var cmd = new SqliteCommand(sql, connection);
                if (scope != null) cmd.Parameters.AddWithValue("@Scope", scope);
                if (board != null) cmd.Parameters.AddWithValue("@Board", board);
                if (status != null) cmd.Parameters.AddWithValue("@Status", status);
                return await ReadTasksAsync(cmd, ct);
            }, ct);
        }

        public Task<List<SaturnTask>> GetDueRecurringAsync(DateTime asOfUtc, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "SELECT * FROM Tasks WHERE NextRunAt IS NOT NULL AND NextRunAt <= @Now AND RecurrenceKind != 'none' AND Status NOT IN ('cancelled')",
                    connection);
                cmd.Parameters.AddWithValue("@Now", asOfUtc.ToString("O"));
                return await ReadTasksAsync(cmd, ct);
            }, ct);
        }

        public Task<bool> TryClaimRecurrenceAsync(string id, DateTime expectedNextRun, DateTime? newNextRun, DateTime lastRun, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    UPDATE Tasks SET NextRunAt = @NewNextRun, LastRunAt = @LastRun, UpdatedAt = @Now
                    WHERE Id = @Id AND NextRunAt = @Expected", connection);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Expected", expectedNextRun.ToString("O"));
                cmd.Parameters.AddWithValue("@NewNextRun", (object?)newNextRun?.ToString("O") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LastRun", lastRun.ToString("O"));
                cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                return await cmd.ExecuteNonQueryAsync(ct) == 1;
            }, ct);
        }

        // ---------- Dependencies ----------

        public Task SetDependenciesAsync(string taskId, IReadOnlyList<string> blockedBy, CancellationToken ct = default)
        {
            return WithWriteLockAsync<object?>(async () =>
            {
                using var connection = CreateConnection();
                using var transaction = connection.BeginTransaction();
                using (var del = new SqliteCommand("DELETE FROM TaskDependencies WHERE TaskId = @TaskId", connection, transaction))
                {
                    del.Parameters.AddWithValue("@TaskId", taskId);
                    await del.ExecuteNonQueryAsync(ct);
                }
                foreach (var blocker in blockedBy)
                {
                    using var ins = new SqliteCommand(
                        "INSERT OR IGNORE INTO TaskDependencies (TaskId, BlockedByTaskId, CreatedAt) VALUES (@TaskId, @Blocker, @Now)",
                        connection, transaction);
                    ins.Parameters.AddWithValue("@TaskId", taskId);
                    ins.Parameters.AddWithValue("@Blocker", blocker);
                    ins.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                    await ins.ExecuteNonQueryAsync(ct);
                }
                transaction.Commit();
                return null;
            }, ct);
        }

        public Task<List<string>> GetDependenciesAsync(string taskId, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand("SELECT BlockedByTaskId FROM TaskDependencies WHERE TaskId = @TaskId", connection);
                cmd.Parameters.AddWithValue("@TaskId", taskId);
                return await ReadStringsAsync(cmd, ct);
            }, ct);
        }

        public Task<List<string>> GetDependentsAsync(string blockerId, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand("SELECT TaskId FROM TaskDependencies WHERE BlockedByTaskId = @Blocker", connection);
                cmd.Parameters.AddWithValue("@Blocker", blockerId);
                return await ReadStringsAsync(cmd, ct);
            }, ct);
        }

        public Task<List<(string TaskId, string BlockedByTaskId)>> GetAllDependenciesAsync(CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand("SELECT TaskId, BlockedByTaskId FROM TaskDependencies", connection);
                var result = new List<(string, string)>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add((reader.GetString(0), reader.GetString(1)));
                }
                return result;
            }, ct);
        }

        // ---------- Runs ----------

        public Task InsertRunAsync(TaskRun run, CancellationToken ct = default)
        {
            return WithWriteLockAsync<object?>(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "INSERT INTO TaskRuns (Id, TaskId, ScheduledFor, FiredAt, Outcome) VALUES (@Id, @TaskId, @ScheduledFor, @FiredAt, @Outcome)",
                    connection);
                cmd.Parameters.AddWithValue("@Id", run.Id);
                cmd.Parameters.AddWithValue("@TaskId", run.TaskId);
                cmd.Parameters.AddWithValue("@ScheduledFor", run.ScheduledFor.ToString("O"));
                cmd.Parameters.AddWithValue("@FiredAt", run.FiredAt.ToString("O"));
                cmd.Parameters.AddWithValue("@Outcome", (object?)run.Outcome ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
                return null;
            }, ct);
        }

        public Task SetLatestRunOutcomeAsync(string taskId, string outcome, CancellationToken ct = default)
        {
            return WithWriteLockAsync<object?>(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    UPDATE TaskRuns SET Outcome = @Outcome
                    WHERE Id = (SELECT Id FROM TaskRuns WHERE TaskId = @TaskId ORDER BY FiredAt DESC LIMIT 1)",
                    connection);
                cmd.Parameters.AddWithValue("@TaskId", taskId);
                cmd.Parameters.AddWithValue("@Outcome", outcome);
                await cmd.ExecuteNonQueryAsync(ct);
                return null;
            }, ct);
        }

        public Task<List<TaskRun>> GetRunsAsync(string taskId, int limit = 20, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "SELECT * FROM TaskRuns WHERE TaskId = @TaskId ORDER BY FiredAt DESC LIMIT @Limit", connection);
                cmd.Parameters.AddWithValue("@TaskId", taskId);
                cmd.Parameters.AddWithValue("@Limit", limit);
                var runs = new List<TaskRun>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    runs.Add(new TaskRun
                    {
                        Id = reader.GetString(reader.GetOrdinal("Id")),
                        TaskId = reader.GetString(reader.GetOrdinal("TaskId")),
                        ScheduledFor = ReadDate(reader, "ScheduledFor")!.Value,
                        FiredAt = ReadDate(reader, "FiredAt")!.Value,
                        Outcome = ReadNullableString(reader, "Outcome")
                    });
                }
                return runs;
            }, ct);
        }

        // ---------- Waiters (runtime) ----------

        public Task<TaskWaiter> InsertWaiterAsync(TaskWaiter w, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    INSERT INTO TaskWaiters (Id, WaitTargetKind, WaitTargetId, WaiterKind, WaiterAgentId, WaiterAgentName,
                        PromptTemplate, CreatedAt, DeliveredAt, DeliveryAttempts)
                    VALUES (@Id, @WaitTargetKind, @WaitTargetId, @WaiterKind, @WaiterAgentId, @WaiterAgentName,
                        @PromptTemplate, @CreatedAt, NULL, 0)", connection);
                cmd.Parameters.AddWithValue("@Id", w.Id);
                cmd.Parameters.AddWithValue("@WaitTargetKind", w.WaitTargetKind);
                cmd.Parameters.AddWithValue("@WaitTargetId", w.WaitTargetId);
                cmd.Parameters.AddWithValue("@WaiterKind", w.WaiterKind);
                cmd.Parameters.AddWithValue("@WaiterAgentId", (object?)w.WaiterAgentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@WaiterAgentName", (object?)w.WaiterAgentName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PromptTemplate", (object?)w.PromptTemplate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedAt", w.CreatedAt.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
                return w;
            }, ct);
        }

        public Task<List<TaskWaiter>> GetPendingWaitersAsync(string? targetId = null, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                var sql = "SELECT * FROM TaskWaiters WHERE DeliveredAt IS NULL";
                if (targetId != null) sql += " AND WaitTargetId = @TargetId";
                using var cmd = new SqliteCommand(sql, connection);
                if (targetId != null) cmd.Parameters.AddWithValue("@TargetId", targetId);
                var waiters = new List<TaskWaiter>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    waiters.Add(new TaskWaiter
                    {
                        Id = reader.GetString(reader.GetOrdinal("Id")),
                        WaitTargetKind = reader.GetString(reader.GetOrdinal("WaitTargetKind")),
                        WaitTargetId = reader.GetString(reader.GetOrdinal("WaitTargetId")),
                        WaiterKind = reader.GetString(reader.GetOrdinal("WaiterKind")),
                        WaiterAgentId = ReadNullableString(reader, "WaiterAgentId"),
                        WaiterAgentName = ReadNullableString(reader, "WaiterAgentName"),
                        PromptTemplate = ReadNullableString(reader, "PromptTemplate"),
                        CreatedAt = ReadDate(reader, "CreatedAt")!.Value,
                        DeliveredAt = ReadDate(reader, "DeliveredAt"),
                        DeliveryAttempts = reader.GetInt32(reader.GetOrdinal("DeliveryAttempts"))
                    });
                }
                return waiters;
            }, ct);
        }

        public Task<bool> MarkWaiterDeliveredAsync(string waiterId, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "UPDATE TaskWaiters SET DeliveredAt = @Now WHERE Id = @Id AND DeliveredAt IS NULL", connection);
                cmd.Parameters.AddWithValue("@Id", waiterId);
                cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                return await cmd.ExecuteNonQueryAsync(ct) == 1;
            }, ct);
        }

        public Task IncrementWaiterAttemptsAsync(string waiterId, CancellationToken ct = default)
        {
            return WithWriteLockAsync<object?>(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "UPDATE TaskWaiters SET DeliveryAttempts = DeliveryAttempts + 1 WHERE Id = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", waiterId);
                await cmd.ExecuteNonQueryAsync(ct);
                return null;
            }, ct);
        }

        // ---------- Dispatches (runtime) ----------

        public Task<TaskDispatch> InsertDispatchAsync(TaskDispatch d, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    INSERT INTO TaskDispatches (Id, TaskId, AgentManagerTaskId, AgentId, AgentName, StartedAt, CompletedAt, Success, Result, Orphaned)
                    VALUES (@Id, @TaskId, @AgentManagerTaskId, @AgentId, @AgentName, @StartedAt, NULL, NULL, NULL, 0)", connection);
                cmd.Parameters.AddWithValue("@Id", d.Id);
                cmd.Parameters.AddWithValue("@TaskId", d.TaskId);
                cmd.Parameters.AddWithValue("@AgentManagerTaskId", (object?)d.AgentManagerTaskId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AgentId", (object?)d.AgentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AgentName", (object?)d.AgentName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StartedAt", d.StartedAt.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
                return d;
            }, ct);
        }

        public Task<TaskDispatch?> GetDispatchByManagerTaskIdAsync(string mgrTaskId, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand("SELECT * FROM TaskDispatches WHERE AgentManagerTaskId = @Mgr", connection);
                cmd.Parameters.AddWithValue("@Mgr", mgrTaskId);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                return await reader.ReadAsync(ct) ? ReadDispatch(reader) : null;
            }, ct);
        }

        public Task<List<TaskDispatch>> GetDispatchesForTaskAsync(string taskId, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "SELECT * FROM TaskDispatches WHERE TaskId = @TaskId ORDER BY StartedAt DESC", connection);
                cmd.Parameters.AddWithValue("@TaskId", taskId);
                return await ReadDispatchesAsync(cmd, ct);
            }, ct);
        }

        public Task<List<TaskDispatch>> GetOpenDispatchesAsync(CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "SELECT * FROM TaskDispatches WHERE CompletedAt IS NULL AND Orphaned = 0", connection);
                return await ReadDispatchesAsync(cmd, ct);
            }, ct);
        }

        public Task CompleteDispatchAsync(string dispatchId, bool success, string result, CancellationToken ct = default)
        {
            return WithWriteLockAsync<object?>(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    UPDATE TaskDispatches SET CompletedAt = @Now, Success = @Success, Result = @Result
                    WHERE Id = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", dispatchId);
                cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("@Success", success ? 1 : 0);
                cmd.Parameters.AddWithValue("@Result", result);
                await cmd.ExecuteNonQueryAsync(ct);
                return null;
            }, ct);
        }

        public Task MarkDispatchOrphanedAsync(string dispatchId, CancellationToken ct = default)
        {
            return WithWriteLockAsync<object?>(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand("UPDATE TaskDispatches SET Orphaned = 1 WHERE Id = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", dispatchId);
                await cmd.ExecuteNonQueryAsync(ct);
                return null;
            }, ct);
        }

        // ---------- Wake queue (runtime) ----------

        public Task<bool> TryEnqueueWakeAsync(WakeItem item, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(@"
                    INSERT OR IGNORE INTO WakeQueue (Id, Kind, TaskId, DedupeKey, Prompt, CreatedAt, DeliveredAt)
                    VALUES (@Id, @Kind, @TaskId, @DedupeKey, @Prompt, @CreatedAt, NULL)", connection);
                cmd.Parameters.AddWithValue("@Id", item.Id);
                cmd.Parameters.AddWithValue("@Kind", item.Kind);
                cmd.Parameters.AddWithValue("@TaskId", (object?)item.TaskId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DedupeKey", (object?)item.DedupeKey ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Prompt", item.Prompt);
                cmd.Parameters.AddWithValue("@CreatedAt", item.CreatedAt.ToString("O"));
                return await cmd.ExecuteNonQueryAsync(ct) == 1;
            }, ct);
        }

        public Task<WakeItem?> PeekOldestWakeAsync(CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "SELECT * FROM WakeQueue WHERE DeliveredAt IS NULL ORDER BY CreatedAt LIMIT 1", connection);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                return await reader.ReadAsync(ct) ? ReadWake(reader) : null;
            }, ct);
        }

        public Task<List<WakeItem>> GetPendingWakesAsync(CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "SELECT * FROM WakeQueue WHERE DeliveredAt IS NULL ORDER BY CreatedAt", connection);
                var wakes = new List<WakeItem>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    wakes.Add(ReadWake(reader));
                }
                return wakes;
            }, ct);
        }

        public Task<int> CountRecentWakesAsync(DateTime sinceUtc, CancellationToken ct = default)
        {
            return WithReadLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM WakeQueue WHERE CreatedAt >= @Since", connection);
                cmd.Parameters.AddWithValue("@Since", sinceUtc.ToString("O"));
                var result = await cmd.ExecuteScalarAsync(ct);
                return Convert.ToInt32(result);
            }, ct);
        }

        public Task<bool> MarkWakeDeliveredAsync(string id, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand(
                    "UPDATE WakeQueue SET DeliveredAt = @Now WHERE Id = @Id AND DeliveredAt IS NULL", connection);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                return await cmd.ExecuteNonQueryAsync(ct) == 1;
            }, ct);
        }

        public Task<bool> DeleteWakeAsync(string id, CancellationToken ct = default)
        {
            return WithWriteLockAsync(async () =>
            {
                using var connection = CreateConnection();
                using var cmd = new SqliteCommand("DELETE FROM WakeQueue WHERE Id = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", id);
                return await cmd.ExecuteNonQueryAsync(ct) > 0;
            }, ct);
        }

        // ---------- Helpers ----------

        private static void BindTask(SqliteCommand cmd, SaturnTask t)
        {
            cmd.Parameters.AddWithValue("@Id", t.Id);
            cmd.Parameters.AddWithValue("@Scope", t.Scope);
            cmd.Parameters.AddWithValue("@Board", t.Board);
            cmd.Parameters.AddWithValue("@Title", t.Title);
            cmd.Parameters.AddWithValue("@Notes", (object?)t.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", t.Status);
            cmd.Parameters.AddWithValue("@Priority", t.Priority);
            cmd.Parameters.AddWithValue("@CreatedBy", t.CreatedBy);
            cmd.Parameters.AddWithValue("@AgentAvailable", t.AgentAvailable ? 1 : 0);
            cmd.Parameters.AddWithValue("@RequiresApproval", t.RequiresApproval ? 1 : 0);
            cmd.Parameters.AddWithValue("@UserHandoffOnly", t.UserHandoffOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("@ClaimStatus", t.ClaimStatus);
            cmd.Parameters.AddWithValue("@ClaimedBy", (object?)t.ClaimedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RecurrenceKind", t.RecurrenceKind);
            cmd.Parameters.AddWithValue("@RecurrenceIntervalSeconds", (object?)t.RecurrenceIntervalSeconds ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RecurrenceCron", (object?)t.RecurrenceCron ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CatchUpPolicy", t.CatchUpPolicy);
            cmd.Parameters.AddWithValue("@NextRunAt", (object?)t.NextRunAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LastRunAt", (object?)t.LastRunAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", t.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@UpdatedAt", t.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@CompletedAt", (object?)t.CompletedAt?.ToString("O") ?? DBNull.Value);
        }

        private static async Task<List<SaturnTask>> ReadTasksAsync(SqliteCommand cmd, CancellationToken ct)
        {
            var tasks = new List<SaturnTask>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }

        private static async Task<List<string>> ReadStringsAsync(SqliteCommand cmd, CancellationToken ct)
        {
            var values = new List<string>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                values.Add(reader.GetString(0));
            }
            return values;
        }

        private static async Task<List<TaskDispatch>> ReadDispatchesAsync(SqliteCommand cmd, CancellationToken ct)
        {
            var dispatches = new List<TaskDispatch>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                dispatches.Add(ReadDispatch(reader));
            }
            return dispatches;
        }

        private static SaturnTask ReadTask(System.Data.Common.DbDataReader reader)
        {
            return new SaturnTask
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                Scope = reader.GetString(reader.GetOrdinal("Scope")),
                Board = reader.GetString(reader.GetOrdinal("Board")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Notes = ReadNullableString(reader, "Notes"),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Priority = reader.GetString(reader.GetOrdinal("Priority")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                CreatedBy = reader.GetString(reader.GetOrdinal("CreatedBy")),
                AgentAvailable = reader.GetInt32(reader.GetOrdinal("AgentAvailable")) == 1,
                RequiresApproval = reader.GetInt32(reader.GetOrdinal("RequiresApproval")) == 1,
                UserHandoffOnly = reader.GetInt32(reader.GetOrdinal("UserHandoffOnly")) == 1,
                ClaimStatus = reader.GetString(reader.GetOrdinal("ClaimStatus")),
                ClaimedBy = ReadNullableString(reader, "ClaimedBy"),
                RecurrenceKind = reader.GetString(reader.GetOrdinal("RecurrenceKind")),
                RecurrenceIntervalSeconds = reader.IsDBNull(reader.GetOrdinal("RecurrenceIntervalSeconds"))
                    ? null : reader.GetInt32(reader.GetOrdinal("RecurrenceIntervalSeconds")),
                RecurrenceCron = ReadNullableString(reader, "RecurrenceCron"),
                CatchUpPolicy = reader.GetString(reader.GetOrdinal("CatchUpPolicy")),
                NextRunAt = ReadDate(reader, "NextRunAt"),
                LastRunAt = ReadDate(reader, "LastRunAt"),
                CreatedAt = ReadDate(reader, "CreatedAt")!.Value,
                UpdatedAt = ReadDate(reader, "UpdatedAt")!.Value,
                CompletedAt = ReadDate(reader, "CompletedAt")
            };
        }

        private static TaskDispatch ReadDispatch(System.Data.Common.DbDataReader reader)
        {
            return new TaskDispatch
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                TaskId = reader.GetString(reader.GetOrdinal("TaskId")),
                AgentManagerTaskId = ReadNullableString(reader, "AgentManagerTaskId"),
                AgentId = ReadNullableString(reader, "AgentId"),
                AgentName = ReadNullableString(reader, "AgentName"),
                StartedAt = ReadDate(reader, "StartedAt")!.Value,
                CompletedAt = ReadDate(reader, "CompletedAt"),
                Success = reader.IsDBNull(reader.GetOrdinal("Success")) ? null : reader.GetInt32(reader.GetOrdinal("Success")) == 1,
                Result = ReadNullableString(reader, "Result"),
                Orphaned = reader.GetInt32(reader.GetOrdinal("Orphaned")) == 1
            };
        }

        private static WakeItem ReadWake(System.Data.Common.DbDataReader reader)
        {
            return new WakeItem
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                Kind = reader.GetString(reader.GetOrdinal("Kind")),
                TaskId = ReadNullableString(reader, "TaskId"),
                DedupeKey = ReadNullableString(reader, "DedupeKey"),
                Prompt = reader.GetString(reader.GetOrdinal("Prompt")),
                CreatedAt = ReadDate(reader, "CreatedAt")!.Value,
                DeliveredAt = ReadDate(reader, "DeliveredAt")
            };
        }

        private static string? ReadNullableString(System.Data.Common.DbDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static DateTime? ReadDate(System.Data.Common.DbDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }
            return DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        public void Dispose()
        {
            _writeLock.Dispose();
            _readLock.Dispose();
        }
    }
}
