namespace AIShield.Backend.Dtos
{
    public class RuleOptionsResponse
    {
        // 规则类型枚举选项
        public List<string> RuleTypes { get; set; } = new();

        // 匹配方式枚举选项
        public List<string> MatchTypes { get; set; } = new();

        // 风险等级枚举选项
        public List<string> RiskLevels { get; set; } = new();

        // 处理动作枚举选项
        public List<string> Actions { get; set; } = new();
    }
}
