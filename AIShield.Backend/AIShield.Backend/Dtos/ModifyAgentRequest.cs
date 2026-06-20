namespace AIShield.Backend.Dtos
{
    public class ModifyAgentRequest
    {
        // Agent 数据库主键
        public long Id { get; set; }

        // Agent 名称，用于区分不同接入应用
        public string AgentName { get; set; } = string.Empty;

        // Agent 使用场景说明
        public string Scenario { get; set; } = string.Empty;
    }
}
