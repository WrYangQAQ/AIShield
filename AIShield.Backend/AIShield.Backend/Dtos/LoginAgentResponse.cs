namespace AIShield.Backend.Dtos
{
    public class LoginAgentResponse
    {
        // 旧版登录是否成功
        public bool Success { get; set; }

        // 旧版登录关联的 AgentId
        public long? AgentId { get; set; }

        // 旧版登录关联的 Agent 名称
        public string AgentName { get; set; } = string.Empty;

        // 旧版登录返回的 Token
        public string Token { get; set; } = string.Empty;

        // 旧版登录 Token 过期时间
        public DateTimeOffset? TokenExpiresAt { get; set; }

        // 操作结果提示
        public string Message { get; set; } = string.Empty;
    }
}
