namespace AIShield.Backend.Dtos
{
    public class AgentSummaryResponse
    {
        // Agent 数据库主键
        public long AgentId { get; set; }

        // Agent 名称
        public string AgentName { get; set; } = string.Empty;

        // Agent 使用场景说明
        public string Scenario { get; set; } = string.Empty;

        // Agent Key 脱敏预览值，仅用于页面展示
        public string AgentKeyPreview { get; set; } = string.Empty;

        // Agent 是否启用
        public bool Enabled { get; set; }

        // Agent 创建时间
        public DateTime CreatedAt { get; set; }

        // Agent 最后一次通过鉴权的时间
        public DateTime? LastUsedAt { get; set; }
    }
}
