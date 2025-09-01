using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Saturn.Core.Security
{
    public class DefaultToolSecurityContext : IToolSecurityContext
    {
        private readonly ConcurrentDictionary<string, ToolSecurityPolicy> _policies = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RateLimitInfo>> _rateLimits = new();
        private readonly List<ToolAccessLog> _accessLogs = new();
        private readonly object _logLock = new();
        
        public DefaultToolSecurityContext()
        {
            InitializeDefaultPolicies();
        }
        
        private void InitializeDefaultPolicies()
        {
            SetPolicy("execute_command", new ToolSecurityPolicy
            {
                ToolName = "execute_command",
                RequiredLevel = SecurityLevel.Elevated,
                RequireApproval = true,
                RequireAuthentication = true,
                ParameterRestrictions = new List<ParameterRestriction>
                {
                    new ParameterRestriction
                    {
                        ParameterName = "command",
                        Type = RestrictionType.Blacklist,
                        ForbiddenValues = new[] { "rm -rf /", "format", "del /f /s /q" },
                        ValidationMessage = "Potentially destructive command blocked"
                    }
                },
                Audit = new AuditPolicy
                {
                    LogAllAccess = true,
                    LogParameters = true,
                    LogResults = true
                }
            });
            
            SetPolicy("delete_file", new ToolSecurityPolicy
            {
                ToolName = "delete_file",
                RequiredLevel = SecurityLevel.Elevated,
                RequireApproval = true,
                ParameterRestrictions = new List<ParameterRestriction>
                {
                    new ParameterRestriction
                    {
                        ParameterName = "path",
                        Type = RestrictionType.Pattern,
                        Pattern = @"^(?!.*\.\.(\/|\\)).*$",
                        ValidationMessage = "Path traversal detected"
                    }
                }
            });
            
            SetPolicy("read_file", new ToolSecurityPolicy
            {
                ToolName = "read_file",
                RequiredLevel = SecurityLevel.Normal,
                RateLimit = new RateLimitPolicy
                {
                    MaxCallsPerMinute = 60,
                    MaxCallsPerHour = 500,
                    PerUser = true
                }
            });
            
            SetPolicy("web_fetch", new ToolSecurityPolicy
            {
                ToolName = "web_fetch",
                RequiredLevel = SecurityLevel.Normal,
                ParameterRestrictions = new List<ParameterRestriction>
                {
                    new ParameterRestriction
                    {
                        ParameterName = "url",
                        Type = RestrictionType.Pattern,
                        Pattern = @"^https?:\/\/",
                        ValidationMessage = "Invalid URL format"
                    }
                },
                RateLimit = new RateLimitPolicy
                {
                    MaxCallsPerMinute = 30,
                    MaxCallsPerHour = 200,
                    PerUser = true
                }
            });
        }
        
        public async Task<ToolPermission> CheckPermissionAsync(ToolAccessRequest request)
        {
            var policy = GetPolicy(request.ToolName);
            
            if (policy.RequireAuthentication && !request.Principal.IsAuthenticated)
            {
                return new ToolPermission
                {
                    IsAllowed = false,
                    Reason = "Authentication required",
                    Level = policy.RequiredLevel
                };
            }
            
            if (!CheckRoleAccess(policy, request.Principal))
            {
                return new ToolPermission
                {
                    IsAllowed = false,
                    Reason = "Insufficient role permissions",
                    Level = policy.RequiredLevel
                };
            }
            
            if (!CheckUserAccess(policy, request.Principal))
            {
                return new ToolPermission
                {
                    IsAllowed = false,
                    Reason = "User not authorized",
                    Level = policy.RequiredLevel
                };
            }
            
            if (!CheckChannelAccess(policy, request.Principal))
            {
                return new ToolPermission
                {
                    IsAllowed = false,
                    Reason = "Channel not authorized",
                    Level = policy.RequiredLevel
                };
            }
            
            var paramValidation = await ValidateParametersAsync(request.ToolName, request.Parameters, request.Principal);
            if (!paramValidation)
            {
                return new ToolPermission
                {
                    IsAllowed = false,
                    Reason = "Parameter validation failed",
                    Level = policy.RequiredLevel,
                    ParameterRestrictions = policy.ParameterRestrictions
                };
            }
            
            var rateLimitInfo = CheckRateLimit(policy, request.Principal);
            if (rateLimitInfo?.IsAnyLimitExceeded == true)
            {
                var reason = "Rate limit exceeded:";
                if (rateLimitInfo.IsMinuteExceeded)
                    reason += $" per-minute limit ({rateLimitInfo.MinuteCount}/{rateLimitInfo.MaxPerMinute}), resets at {rateLimitInfo.MinuteResetTime:HH:mm:ss}";
                if (rateLimitInfo.IsHourExceeded)
                    reason += $" per-hour limit ({rateLimitInfo.HourCount}/{rateLimitInfo.MaxPerHour}), resets at {rateLimitInfo.HourResetTime:HH:mm:ss}";
                if (rateLimitInfo.IsDayExceeded)
                    reason += $" per-day limit ({rateLimitInfo.DayCount}/{rateLimitInfo.MaxPerDay}), resets at {rateLimitInfo.DayResetTime:HH:mm:ss}";
                
                return new ToolPermission
                {
                    IsAllowed = false,
                    Reason = reason,
                    Level = policy.RequiredLevel,
                    RateLimit = rateLimitInfo
                };
            }
            
            return new ToolPermission
            {
                IsAllowed = true,
                Level = policy.RequiredLevel,
                RateLimit = rateLimitInfo,
                RequiredApprovals = policy.RequireApproval ? new List<string> { "user" } : null,
                ParameterRestrictions = policy.ParameterRestrictions
            };
        }
        
        public async Task<bool> IsToolAllowedAsync(string toolName, SecurityPrincipal principal)
        {
            var request = new ToolAccessRequest
            {
                ToolName = toolName,
                Principal = principal,
                Parameters = new Dictionary<string, object>()
            };
            
            var permission = await CheckPermissionAsync(request);
            return permission.IsAllowed;
        }
        
        public Task<bool> ValidateParametersAsync(string toolName, Dictionary<string, object> parameters, SecurityPrincipal principal)
        {
            var policy = GetPolicy(toolName);
            
            foreach (var restriction in policy.ParameterRestrictions)
            {
                if (!parameters.TryGetValue(restriction.ParameterName, out var value))
                    continue;
                
                var valueStr = value?.ToString() ?? string.Empty;
                
                switch (restriction.Type)
                {
                    case RestrictionType.Whitelist:
                        if (restriction.AllowedValues is IEnumerable<string> allowed)
                        {
                            if (!allowed.Contains(valueStr, StringComparer.OrdinalIgnoreCase))
                                return Task.FromResult(false);
                        }
                        break;
                        
                    case RestrictionType.Blacklist:
                        if (restriction.ForbiddenValues is IEnumerable<string> forbidden)
                        {
                            if (forbidden.Any(f => valueStr.Contains(f, StringComparison.OrdinalIgnoreCase)))
                                return Task.FromResult(false);
                        }
                        break;
                        
                    case RestrictionType.Pattern:
                        if (!string.IsNullOrEmpty(restriction.Pattern))
                        {
                            if (!Regex.IsMatch(valueStr, restriction.Pattern))
                                return Task.FromResult(false);
                        }
                        break;
                        
                    case RestrictionType.Range:
                        if (value is IComparable comparable && 
                            restriction.AllowedValues is ValueTuple<object, object> range)
                        {
                            if (comparable.CompareTo(range.Item1) < 0 || 
                                comparable.CompareTo(range.Item2) > 0)
                                return Task.FromResult(false);
                        }
                        break;
                }
            }
            
            return Task.FromResult(true);
        }
        
        public Task LogToolAccessAsync(ToolAccessLog log)
        {
            lock (_logLock)
            {
                _accessLogs.Add(log);
                
                if (_accessLogs.Count > 10000)
                {
                    _accessLogs.RemoveRange(0, 1000);
                }
            }
            
            return Task.CompletedTask;
        }
        
        public ToolSecurityPolicy GetPolicy(string toolName)
        {
            return _policies.GetOrAdd(toolName, _ => new ToolSecurityPolicy
            {
                ToolName = toolName,
                RequiredLevel = SecurityLevel.Normal,
                RequireAuthentication = false
            });
        }
        
        public void SetPolicy(string toolName, ToolSecurityPolicy policy)
        {
            _policies[toolName] = policy;
        }
        
        private bool CheckRoleAccess(ToolSecurityPolicy policy, SecurityPrincipal principal)
        {
            if (policy.DeniedRoles.Any() && 
                principal.Roles.Any(r => policy.DeniedRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }
            
            if (policy.AllowedRoles.Any())
            {
                return principal.Roles.Any(r => policy.AllowedRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
            }
            
            return true;
        }
        
        private bool CheckUserAccess(ToolSecurityPolicy policy, SecurityPrincipal principal)
        {
            var userId = principal.UserId ?? principal.Id;
            
            if (policy.DeniedUsers.Contains(userId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            
            if (policy.AllowedUsers.Any())
            {
                return policy.AllowedUsers.Contains(userId, StringComparer.OrdinalIgnoreCase);
            }
            
            return true;
        }
        
        private bool CheckChannelAccess(ToolSecurityPolicy policy, SecurityPrincipal principal)
        {
            if (string.IsNullOrEmpty(principal.ChannelId))
                return true;
            
            if (policy.DeniedChannels.Contains(principal.ChannelId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            
            if (policy.AllowedChannels.Any())
            {
                return policy.AllowedChannels.Contains(principal.ChannelId, StringComparer.OrdinalIgnoreCase);
            }
            
            return true;
        }
        
        private RateLimitInfo? CheckRateLimit(ToolSecurityPolicy policy, SecurityPrincipal principal)
        {
            if (policy.RateLimit == null)
                return null;
            
            // Build composite key including tool + user + channel
            var userKey = policy.RateLimit.PerUser ? (principal.UserId ?? principal.Id ?? "anon") : "anon";
            var channelKey = policy.RateLimit.PerChannel ? (principal.ChannelId ?? "global") : "global";
            var compositeKey = $"{policy.ToolName}:{userKey}:{channelKey}";
            
            var limits = _rateLimits.GetOrAdd(compositeKey, _ => new ConcurrentDictionary<string, RateLimitInfo>());
            
            var now = DateTime.UtcNow;
            var minuteKey = now.ToString("yyyy-MM-dd-HH-mm");
            
            // Get or create the minute bucket
            var minuteBucket = limits.GetOrAdd(minuteKey, _ => new RateLimitInfo
            {
                MaxCalls = policy.RateLimit.MaxCallsPerMinute,
                Period = TimeSpan.FromMinutes(1),
                CurrentCount = 0,
                ResetTime = now.AddMinutes(1)
            });
            
            // Increment the current minute bucket
            minuteBucket.CurrentCount++;
            
            // Calculate rolling sums for hour and day windows
            var minuteCount = minuteBucket.CurrentCount;
            var hourCount = CalculateRollingSum(limits, now, 60); // Last 60 minutes
            var dayCount = CalculateRollingSum(limits, now, 1440); // Last 1440 minutes (24 hours)
            
            // Clean up old buckets beyond 1440 minutes to bound memory
            CleanupOldBuckets(limits, now, 1440);
            
            // Create comprehensive rate limit info
            var rateLimitInfo = new RateLimitInfo
            {
                // Legacy properties for backward compatibility
                MaxCalls = policy.RateLimit.MaxCallsPerMinute,
                Period = TimeSpan.FromMinutes(1),
                CurrentCount = minuteCount,
                ResetTime = now.AddMinutes(1),
                
                // Multi-window properties
                MinuteCount = minuteCount,
                HourCount = hourCount,
                DayCount = dayCount,
                MaxPerMinute = policy.RateLimit.MaxCallsPerMinute,
                MaxPerHour = policy.RateLimit.MaxCallsPerHour,
                MaxPerDay = policy.RateLimit.MaxCallsPerDay,
                MinuteResetTime = now.AddMinutes(1),
                HourResetTime = now.AddMinutes(60 - now.Minute).AddSeconds(-now.Second),
                DayResetTime = now.Date.AddDays(1)
            };
            
            return rateLimitInfo;
        }
        
        private int CalculateRollingSum(ConcurrentDictionary<string, RateLimitInfo> limits, DateTime now, int windowMinutes)
        {
            var sum = 0;
            for (int i = 0; i < windowMinutes; i++)
            {
                var bucketTime = now.AddMinutes(-i);
                var bucketKey = bucketTime.ToString("yyyy-MM-dd-HH-mm");
                if (limits.TryGetValue(bucketKey, out var bucket))
                {
                    sum += bucket.CurrentCount;
                }
            }
            return sum;
        }
        
        private void CleanupOldBuckets(ConcurrentDictionary<string, RateLimitInfo> limits, DateTime now, int keepMinutes)
        {
            var cutoffTime = now.AddMinutes(-keepMinutes);
            var keysToRemove = new List<string>();
            
            foreach (var kvp in limits)
            {
                if (DateTime.TryParseExact(kvp.Key, "yyyy-MM-dd-HH-mm", null, System.Globalization.DateTimeStyles.None, out var bucketTime))
                {
                    if (bucketTime < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
            }
            
            foreach (var key in keysToRemove)
            {
                limits.TryRemove(key, out _);
            }
        }
        
        public List<ToolAccessLog> GetAccessLogs(DateTime? since = null, string? toolName = null, string? userId = null)
        {
            lock (_logLock)
            {
                var query = _accessLogs.AsEnumerable();
                
                if (since.HasValue)
                    query = query.Where(l => l.AccessTime >= since.Value);
                    
                if (!string.IsNullOrEmpty(toolName))
                    query = query.Where(l => l.ToolName == toolName);
                    
                if (!string.IsNullOrEmpty(userId))
                    query = query.Where(l => l.Principal.UserId == userId);
                
                return query.ToList();
            }
        }
    }
}