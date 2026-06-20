namespace AIShield.Backend.Dtos
{
    public class TestRuleRequest
    {
        // 测试规则Id
        public string RuleId { get; set; } = string.Empty;

        // 模拟请求的输入内容
        public string TestContent { get; set; } = string.Empty;
    }
}
