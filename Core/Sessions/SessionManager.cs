using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Core.Sessions
{
    public class SessionManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, PlatformSession> _sessions = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userSessions = new();
        private readonly ConcurrentDictionary<string, string> _platformMappings = new();
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(30);
        
        private static readonly Lazy<SessionManager> _instance = new(() => new SessionManager());
        public static SessionManager Instance => _instance.Value;
        
        public event EventHandler<SessionEventArgs>? SessionCreated;
        public event EventHandler<SessionEventArgs>? SessionTerminated;
        public event EventHandler<SessionEventArgs>? SessionExpired;
        
        private SessionManager()
        {
            _cleanupTimer = new Timer(CleanupExpiredSessions, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
        
        public PlatformSession CreateSession(
            PlatformType platform,
            string? userId = null,
            string? channelId = null,
            SessionConfiguration? configuration = null)
        {
            var session = new PlatformSession
            {
                Platform = platform,
                UserId = userId,
                ChannelId = channelId,
                Configuration = configuration ?? new SessionConfiguration()
            };
            
            _sessions[session.SessionId] = session;
            
            if (!string.IsNullOrEmpty(userId))
            {
                var userSessionSet = _userSessions.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
                userSessionSet.TryAdd(session.SessionId, 0);
            }
            
            var platformKey = GetPlatformKey(platform, channelId);
            if (!string.IsNullOrEmpty(platformKey))
            {
                _platformMappings[platformKey] = session.SessionId;
            }
            
            SessionCreated?.Invoke(this, new SessionEventArgs { Session = session });
            
            return session;
        }
        
        public PlatformSession? GetSession(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
        
        public PlatformSession? GetOrCreateSession(
            PlatformType platform,
            string? userId = null,
            string? channelId = null,
            SessionConfiguration? configuration = null)
        {
            var platformKey = GetPlatformKey(platform, channelId);
            
            if (!string.IsNullOrEmpty(platformKey) && 
                _platformMappings.TryGetValue(platformKey, out var existingSessionId) &&
                _sessions.TryGetValue(existingSessionId, out var existingSession))
            {
                existingSession.UpdateActivity();
                return existingSession;
            }
            
            return CreateSession(platform, userId, channelId, configuration);
        }
        
        public List<PlatformSession> GetUserSessions(string userId)
        {
            if (!_userSessions.TryGetValue(userId, out var userSessionSet))
                return new List<PlatformSession>();
            
            // Take a snapshot of session IDs to avoid racey enumeration
            var sessionIds = userSessionSet.Keys.ToList();
            
            return sessionIds
                .Select(id => GetSession(id))
                .Where(s => s != null)
                .Cast<PlatformSession>()
                .ToList();
        }
        
        public List<PlatformSession> GetActiveSessions()
        {
            return _sessions.Values
                .Where(s => s.State == SessionState.Active)
                .ToList();
        }
        
        public async Task<bool> TerminateSessionAsync(string sessionId)
        {
            if (!_sessions.TryRemove(sessionId, out var session))
                return false;
            
            session.State = SessionState.Terminated;
            
            if (!string.IsNullOrEmpty(session.UserId))
            {
                if (_userSessions.TryGetValue(session.UserId, out var userSessionSet))
                {
                    userSessionSet.TryRemove(sessionId, out _);
                }
            }
            
            var platformKey = GetPlatformKey(session.Platform, session.ChannelId);
            if (!string.IsNullOrEmpty(platformKey))
            {
                _platformMappings.TryRemove(platformKey, out _);
            }
            
            SessionTerminated?.Invoke(this, new SessionEventArgs { Session = session });
            
            return await Task.FromResult(true);
        }
        
        public async Task TerminateUserSessionsAsync(string userId)
        {
            if (!_userSessions.TryGetValue(userId, out var userSessionSet))
                return;
            
            // Take a snapshot of session IDs to avoid racey enumeration
            var sessionIds = userSessionSet.Keys.ToList();
            var tasks = sessionIds.Select(TerminateSessionAsync);
            await Task.WhenAll(tasks);
        }
        
        public void UpdateSessionActivity(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.UpdateActivity();
            }
        }
        
        public void SuspendSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.State = SessionState.Suspended;
            }
        }
        
        public void ResumeSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.State = SessionState.Active;
                session.UpdateActivity();
            }
        }
        
        private void CleanupExpiredSessions(object? state)
        {
            var expiredSessions = _sessions.Values
                .Where(s => s.State == SessionState.Active && 
                           s.IsExpired(s.Configuration.SessionTimeout))
                .ToList();
            
            foreach (var session in expiredSessions)
            {
                session.State = SessionState.Idle;
                
                if (session.IsExpired(session.Configuration.SessionTimeout * 2))
                {
                    _ = TerminateSessionAsync(session.SessionId);
                    SessionExpired?.Invoke(this, new SessionEventArgs { Session = session });
                }
            }
        }
        
        private string GetPlatformKey(PlatformType platform, string? channelId)
        {
            return string.IsNullOrEmpty(channelId) 
                ? $"{platform}" 
                : $"{platform}:{channelId}";
        }
        
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            
            foreach (var session in _sessions.Values)
            {
                session.State = SessionState.Terminated;
            }
            
            _sessions.Clear();
            _userSessions.Clear();
            _platformMappings.Clear();
        }
    }
    
    public class SessionEventArgs : EventArgs
    {
        public PlatformSession Session { get; set; } = null!;
    }
}