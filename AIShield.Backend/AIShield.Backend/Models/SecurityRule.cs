using AIShield.Backend.Enums;
using System.Text.Json.Serialization;

namespace AIShield.Backend.Models
{
    public class SecurityRule
    {
        // 规则数据库主键
        public long Id { get; set; }

        // 规则业务编号，例如 R-1001
        public string RuleId { get; set; } = string.Empty;

        // 规则展示名称
        public string Name { get; set; } = string.Empty;

        // 规则所属类型：输入检测或输出过滤
        public RuleType RuleType { get; set; }

        // 规则匹配方式：关键词或正则表达式
        public RuleMatchType MatchType { get; set; }

        // 关键词或正则表达式内容
        public string Pattern { get; set; } = string.Empty;

        // 命中规则后的风险等级
        public RiskLevel RiskLevel { get; set; }

        // 命中规则后的处理动作
        public SecurityAction Action { get; set; }

        // 输出脱敏时使用的替换文本
        public string Replacement { get; set; } = string.Empty;

        // 规则本体是否启用
        public bool Enabled { get; set; } = true;

        // 是否为系统内置规则
        public bool IsSystemRule { get; set; }

        // 规则创建时间
        public DateTime CreatedAt { get; set; }

        // 规则最后更新时间
        public DateTime UpdatedAt { get; set; }

        // 应用该规则的 Agent 绑定集合
        [JsonIgnore]
        public List<AgentRule> AgentRules { get; set; } = new();
    }
}
