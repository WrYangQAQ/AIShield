using AIShield.Backend.Enums;

namespace AIShield.Backend.Dtos
{
    public class SaveSecurityRuleRequest
    {
        // 业务规则编号，供前端展示和用户识别
        public string RuleId { get; set; } = string.Empty;

        // 规则名称
        public string Name { get; set; } = string.Empty;

        // 规则类型：输入检测或输出过滤
        public RuleType RuleType { get; set; }

        // 匹配方式：正则或关键词
        public RuleMatchType MatchType { get; set; }

        // 关键词或正则表达式内容
        public string Pattern { get; set; } = string.Empty;

        // 命中后的风险等级
        public RiskLevel RiskLevel { get; set; }

        // 命中后的处理动作
        public SecurityAction Action { get; set; }

        // 脱敏替换文本，仅 Mask 动作使用
        public string Replacement { get; set; } = string.Empty;

        // 规则全局启用状态
        public bool Enabled { get; set; } = true;
    }
}
