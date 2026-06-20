using AIShield.Backend.Models;
using AIShield.Backend.Services;
using System.Text.Json;

namespace AIShield.Backend.Middleware
{
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiKeyAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        // 按请求类型校验管理员 JWT 或 Agent Key，并把识别出的身份写入 HttpContext
        public async Task InvokeAsync(HttpContext context, AgentService agentService, JwtService jwtService)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

            if (IsPublicPath(path) || context.Request.Method == "OPTIONS" || !path.StartsWith("/api"))
            {
                await _next(context);
                return;
            }

            if (path.StartsWith("/api/security"))
            {
                var agentResult = await AuthenticateSecurityRequestAsync(context, agentService, jwtService);
                if (!agentResult.Succeeded || agentResult.Agent == null)
                {
                    await WriteUnauthorizedResponse(context, agentResult.Message);
                    return;
                }

                context.Items["AgentId"] = agentResult.Agent.Id;
                context.Items["AgentName"] = agentResult.Agent.AgentName;
                await _next(context);
                return;
            }

            var adminResult = AuthenticateAdminRequest(context, jwtService);
            if (!adminResult.Succeeded)
            {
                await WriteUnauthorizedResponse(context, adminResult.Message);
                return;
            }

            context.Items["IsAdmin"] = true;
            await _next(context);
        }

        // 安全检测接口优先使用 Agent Key；管理端测试时可使用 JWT + X-Agent-Id
        private static async Task<AgentAuthenticationResult> AuthenticateSecurityRequestAsync(
            HttpContext context,
            AgentService agentService,
            JwtService jwtService)
        {
            if (context.Request.Headers.TryGetValue("X-API-Key", out var apiKeyValues))
            {
                var requestApiKey = apiKeyValues.FirstOrDefault();

                if (string.IsNullOrWhiteSpace(requestApiKey))
                {
                    return FailAgentAuth("API Key 不能为空");
                }

                return await agentService.AuthenticateByAgentKeyAsync(requestApiKey);
            }

            var adminResult = AuthenticateAdminRequest(context, jwtService);
            if (!adminResult.Succeeded)
            {
                return FailAgentAuth("未提供 API Key 或有效管理端 Token");
            }

            if (!context.Request.Headers.TryGetValue("X-Agent-Id", out var agentIdValues)
                || !long.TryParse(agentIdValues.FirstOrDefault(), out var agentId))
            {
                return FailAgentAuth("管理端测试安全接口时需要提供 X-Agent-Id");
            }

            return await agentService.AuthenticateByAgentIdAsync(agentId);
        }

        // 管理端接口只接受 Bearer JWT
        private static JwtValidationResult AuthenticateAdminRequest(HttpContext context, JwtService jwtService)
        {
            var bearerToken = GetBearerToken(context);

            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return JwtValidationResult.Fail("未提供登录 Token");
            }

            return jwtService.ValidateAdminToken(bearerToken);
        }

        // 从 Authorization 请求头中提取 Bearer Token
        private static string? GetBearerToken(HttpContext context)
        {
            var authorization = context.Request.Headers.Authorization.FirstOrDefault();

            if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return authorization["Bearer ".Length..].Trim();
        }

        // 判断不需要鉴权的路径
        private static bool IsPublicPath(string path)
        {
            return path.StartsWith("/health")
                   || path.StartsWith("/swagger")
                   || path.StartsWith("/api/auth/login");
        }

        // 构建 Agent 鉴权失败结果
        private static AgentAuthenticationResult FailAgentAuth(string message)
        {
            return new AgentAuthenticationResult
            {
                Succeeded = false,
                Message = message
            };
        }

        // 写入 401 响应
        private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";

            var json = JsonSerializer.Serialize(new
            {
                message
            });

            await context.Response.WriteAsync(json);
        }
    }
}
