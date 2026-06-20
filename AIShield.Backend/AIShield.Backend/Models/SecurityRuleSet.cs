namespace AIShield.Backend.Models
{
    public class SecurityRuleSet
    {
        // 输入检测规则集合
        public List<SecurityRule> InputRules { get; set; } = new();

        // 输出过滤规则集合
        public List<SecurityRule> OutputRules { get; set; } = new();

        // 工具调用安全策略，当前仅用于兼容旧 JSON 种子结构
        public ToolPolicy ToolPolicy { get; set; } = new();
    }
}
