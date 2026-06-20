namespace AIShield.Backend.Dtos
{
    public class RegisterAgentRequest
    {
        // Agent 名称，用于管理端展示和日志快照
        public string AgentName { get; set; } = string.Empty;

        // Agent 使用场景说明
        public string Scenario { get; set; } = string.Empty;
    }
}
