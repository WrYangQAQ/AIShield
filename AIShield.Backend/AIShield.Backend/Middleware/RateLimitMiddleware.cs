using System.Collections.Concurrent;
using System.Text.Json;

namespace AIShield.Backend.Middleware
{
    public class RateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private static readonly ConcurrentDictionary<string, RequestCounter> Counters = new();
        private static DateTimeOffset _lastCleanupAt = DateTimeOffset.MinValue;
        private static readonly object CleanupLock = new();

        public RateLimitMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
        }

        // 对 API 请求做轻量级固定窗口限流，防止短时间高频调用耗尽资源。
        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

            if (!path.StartsWith("/api") || context.Request.Method == "OPTIONS")
            {
                await _next(context);
                return;
            }

            // 从配置获取限流参数
            var permitLimit = _configuration.GetValue("RateLimiting:PermitLimit", 60);         // 窗口内允许的最大请求数，默认60
            var windowSeconds = _configuration.GetValue("RateLimiting:WindowSeconds", 60);     // 时间窗口长度（秒），默认60
            var key = BuildLimitKey(context);   // 根据 AgentId/IP 构建限流键
            var now = DateTimeOffset.UtcNow;

            CleanupExpiredCounters(now, windowSeconds);

            // 更新请求计数器，计数器会在窗口过期时重置。
            var counter = Counters.AddOrUpdate
            (
                key,
                _ => new RequestCounter(now, 1),
                (_, existing) => existing.Add(now, windowSeconds)
            );

            if (counter.Count > permitLimit)
            {
                await WriteTooManyRequestsResponse(context, permitLimit, windowSeconds);
                return;
            }

            await _next(context);
        }


        // ===========     以下是工具函数     ===========

        // 定期清理过期限流键，避免长时间运行后字典持续增长。
        private static void CleanupExpiredCounters(DateTimeOffset now, int windowSeconds)
        {
            var cleanupIntervalSeconds = Math.Max(windowSeconds, 60);

            if ((now - _lastCleanupAt).TotalSeconds < cleanupIntervalSeconds)
            {
                return;
            }

            lock (CleanupLock)
            {
                if ((now - _lastCleanupAt).TotalSeconds < cleanupIntervalSeconds)
                {
                    return;
                }

                foreach (var item in Counters)
                {
                    if (item.Value.IsExpired(now, windowSeconds))
                    {
                        Counters.TryRemove(item.Key, out _);
                    }
                }

                _lastCleanupAt = now;
            }
        }

        // 构建限流键，优先使用已鉴权的Id，如果没有则退化为IP
        private static string BuildLimitKey(HttpContext context)
        {
            var appId = context.Items["AgentId"]?.ToString()
                ?? context.Items["AgentName"]?.ToString()
                ?? context.Request.Headers["X-App-Id"].FirstOrDefault();
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            
            if (string.IsNullOrWhiteSpace(appId))
            {
                return $"ip:{clientIp}";
            }
            return $"app:{appId}:ip:{clientIp}";
        }

        // 当请求过于频繁时，返回429响应（Too Many Requests），并附带友好的提示信息
        private static async Task WriteTooManyRequestsResponse(
            HttpContext context,
            int permitLimit,
            int windowSeconds)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json; charset=utf-8";

            var json = JsonSerializer.Serialize(new
            {
                message = $"请求过于频繁，请在 {windowSeconds} 秒窗口内最多调用 {permitLimit} 次"
            });

            await context.Response.WriteAsync(json);
        }

        // 请求计数器构造函数：当窗口过期时，返回新的计数器对象，所以使用record保证安全
        private sealed record RequestCounter(DateTimeOffset WindowStartedAt, int Count)
        {
            public RequestCounter Add(DateTimeOffset now, int windowSeconds)
            {
                if ((now - WindowStartedAt).TotalSeconds >= windowSeconds)    // 如果超出窗口时间，重置计数器
                {
                    return new RequestCounter(now, 1);
                }

                return this with { Count = Count + 1 };       // 未超出窗口时间，copy新的计数器对象并返回，计数加1
            }

            public bool IsExpired(DateTimeOffset now, int windowSeconds)
            {
                return (now - WindowStartedAt).TotalSeconds >= windowSeconds;
            }
        }
    }
}
