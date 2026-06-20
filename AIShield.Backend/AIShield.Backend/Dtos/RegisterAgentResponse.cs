namespace AIShield.Backend.Dtos
{
    public class RegisterAgentResponse
    {
        // 新建 Agent 的数据库主键
        public long AgentId { get; set; }

        // 新建 Agent 的名称
        public string AgentName { get; set; } = string.Empty;

        // 只返回一次的 Agent Key，后端只保存哈希
        public string AgentKey { get; set; } = string.Empty;

        // 操作结果提示
        public string Message { get; set; } = string.Empty;
    }
}
