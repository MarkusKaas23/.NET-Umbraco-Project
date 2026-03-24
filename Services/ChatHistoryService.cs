using MyCustomUmbracoProject.Models;
using Umbraco.Cms.Infrastructure.Scoping;

namespace MyCustomUmbracoProject.Services
{
    public class ChatHistoryService
    {
        private readonly IScopeProvider _scopeProvider;
        private volatile bool _tableEnsured = false;
        private readonly object _lock = new object();

        public ChatHistoryService(IScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        private void EnsureTable()
        {
            if (_tableEnsured) return;
            lock (_lock)
            {
                if (_tableEnsured) return;
                using var scope = _scopeProvider.CreateScope();
                scope.Database.Execute(@"
                    IF NOT EXISTS (
                        SELECT * FROM INFORMATION_SCHEMA.TABLES
                        WHERE TABLE_NAME = 'ChatMessages'
                    )
                    BEGIN
                        CREATE TABLE ChatMessages (
                            Id              INT IDENTITY(1,1) PRIMARY KEY,
                            SessionId       NVARCHAR(128)   NOT NULL,
                            UserId          NVARCHAR(128)   NOT NULL DEFAULT '',
                            UserPrompt      NVARCHAR(MAX)   NOT NULL,
                            ResponseMarkdown NVARCHAR(MAX)  NOT NULL,
                            AiModel         NVARCHAR(100)   NOT NULL,
                            CreatedAt       DATETIME2       NOT NULL DEFAULT GETUTCDATE()
                        );
                        CREATE INDEX IX_ChatMessages_SessionId ON ChatMessages (SessionId);
                        CREATE INDEX IX_ChatMessages_UserId    ON ChatMessages (UserId);
                    END");

                // Add UserId column if it was created before this migration
                scope.Database.Execute(@"
                    IF NOT EXISTS (
                        SELECT * FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_NAME = 'ChatMessages' AND COLUMN_NAME = 'UserId'
                    )
                    BEGIN
                        ALTER TABLE ChatMessages ADD UserId NVARCHAR(128) NOT NULL DEFAULT '';
                        CREATE INDEX IX_ChatMessages_UserId ON ChatMessages (UserId);
                    END");

                scope.Complete();
                _tableEnsured = true;
            }
        }

        public void Save(ChatMessage message)
        {
            EnsureTable();
            message.CreatedAt = DateTime.UtcNow;
            using var scope = _scopeProvider.CreateScope();
            scope.Database.Insert(message);
            scope.Complete();
        }

        public List<ChatMessage> GetBySession(string sessionId, int limit = 20)
        {
            EnsureTable();
            using var scope = _scopeProvider.CreateScope();
            var messages = scope.Database.Fetch<ChatMessage>(
                $"SELECT TOP {limit} * FROM ChatMessages WHERE SessionId = @0 ORDER BY CreatedAt DESC",
                sessionId);
            scope.Complete();
            messages.Reverse();
            return messages;
        }

        public List<ChatSessionSummary> GetAllSessions(string userId)
        {
            EnsureTable();
            using var scope = _scopeProvider.CreateScope();
            var sessions = scope.Database.Fetch<ChatSessionSummary>(@"
                SELECT
                    c.SessionId,
                    (SELECT TOP 1 UserPrompt FROM ChatMessages
                     WHERE SessionId = c.SessionId ORDER BY CreatedAt ASC) AS Title,
                    MAX(c.CreatedAt) AS LastMessageAt
                FROM ChatMessages c
                WHERE c.UserId = @0
                GROUP BY c.SessionId
                ORDER BY MAX(c.CreatedAt) DESC",
                userId);
            scope.Complete();
            return sessions;
        }

        public void ClearSession(string sessionId)
        {
            EnsureTable();
            using var scope = _scopeProvider.CreateScope();
            scope.Database.Execute("DELETE FROM ChatMessages WHERE SessionId = @0", sessionId);
            scope.Complete();
        }
    }
}
