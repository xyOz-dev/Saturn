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
            if (rateLimitInfo?.IsExceeded == true)
            {
                return new ToolPermission
                {
                    IsAllowed = false,
                    Reason = $"Rate limit exceeded. Resets at {rateLimitInfo.ResetTime:HH:mm:ss}",
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
        
        public Task<bool> IsToolAllowedAsync(string toolName, SecurityPrincipal principal)
        {
            var request = new ToolAccessRequest
            {
                ToolName = toolName,
                Principal = principal,
                Parameters = new Dictionary<string, object>()
            };
            
            return Task.FromResult(CheckPermissionAsync(request).Result.IsAllowed);
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
            
            var key = policy.RateLimit.PerUser 
                ? $"{policy.ToolName}:{principal.UserId ?? principal.Id}"
                : $"{policy.ToolName}:{principal.ChannelId ?? "global"}";
            
            var limits = _rateLimits.GetOrAdd(key, _ => new ConcurrentDictionary<string, RateLimitInfo>());
            
            var now = DateTime.UtcNow;
            var minuteKey = now.ToString("yyyy-MM-dd-HH-mm");
            
            var info = limits.GetOrAdd(minuteKey, _ => new RateLimitInfo
            {
                MaxCalls = policy.RateLimit.MaxCallsPerMinute,
                Period = TimeSpan.FromMinutes(1),
                CurrentCount = 0,
                ResetTime = now.AddMinutes(1)
            });
            
            info.CurrentCount++;
            
            limits.TryRemove(now.AddMinutes(-2).ToString("yyyy-MM-dd-HH-mm"), out _);
            
            return info;
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