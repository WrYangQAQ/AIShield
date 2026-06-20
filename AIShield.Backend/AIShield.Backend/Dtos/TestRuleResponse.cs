namespace AIShield.Backend.Dtos
{
    public class TestRuleResponse
    {
        // 对于测试规则的响应结果
        public bool IsMatch { get; set; }

        // 详细信息，例如哪些部分匹配，匹配的规则内容
        public string MatchDetails { get; set; } = string.Empty;
    }
}
