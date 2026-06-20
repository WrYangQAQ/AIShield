using System.Text.Json.Serialization;

namespace AIShield.Backend.Models
{
    public class AgentRule
    {
        // Agent 主键
        public long AgentId { get; set; }

        // 规则主键
        public long RuleId { get; set; }

        // 当前 Agent 是否启用该规则
        public bool Enabled { get; set; } = true;

        // 绑定创建时间
        public DateTime CreatedAt { get; set; }

        // 绑定的 Agent
        [JsonIgnore]
        public AgentApp? Agent { get; set; }

        // 绑定的安全规则
        [JsonIgnore]
        public SecurityRule? Rule { get; set; }
    }
}
