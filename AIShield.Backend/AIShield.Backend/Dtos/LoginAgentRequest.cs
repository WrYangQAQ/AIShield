namespace AIShield.Backend.Dtos
{
    public class LoginAgentRequest
    {
        // 旧版 Agent 登录使用的 Agent Key，当前主流程已改为本地管理员登录
        public string AgentKey { get; set; } = string.Empty;

        // 旧版 Agent 登录密码，当前主流程已不再使用
        public string Password { get; set; } = string.Empty;
    }
}
