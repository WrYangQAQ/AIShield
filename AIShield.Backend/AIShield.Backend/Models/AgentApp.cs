namespace AIShield.Backend.Models
{
    public class AgentApp
    {
        // Agent 数据库主键
        public long Id { get; set; }

        // Agent 名称，用于区分不同接入应用
        public string AgentName { get; set; } = string.Empty;

        // Agent 使用场景说明
        public string Scenario { get; set; } = string.Empty;

        // Agent Key 的带盐哈希值，用于最终鉴权校验
        public string AgentKeyHash { get; set; } = string.Empty;

        // Agent Key 指纹，用于数据库索引查询
        public string? AgentKeyFingerprint { get; set; }

        // Agent Key 脱敏预览值，仅用于管理端展示，不参与鉴权
        public string AgentKeyPreview { get; set; } = string.Empty;

        // Agent Key 盐值，用于增强哈希安全性
        public string AgentKeySalt { get; set; } = string.Empty;

        // Agent 是否启用
        public bool Enabled { get; set; } = true;

        // Agent 创建时间
        public DateTime CreatedAt { get; set; }

        // Agent 最后一次通过鉴权的时间
        public DateTime? LastUsedAt { get; set; }

        // Agent 绑定的规则集合
        public List<AgentRule> AgentRules { get; set; } = new();

        // 反向导航：Agent → MemoryRecord集合
        public List<MemoryRecord> MemoryRecords { get; set; } = new();

        // 反向导航：等待该 Agent 同步到主记忆库的操作。
        public List<MemorySyncAction> MemorySyncActions { get; set; } = new();
    }
}
