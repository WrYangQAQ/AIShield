using System.Text.RegularExpressions;
using AIShield.Backend.Enums;
using AIShield.Backend.Models;

namespace AIShield.Backend.Services
{
    public class RuleEngine
    {
        // 判断指定内容是否命中安全规则
        public bool IsMatch(string content, SecurityRule rule)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(rule.Pattern))
            {
                return false;
            }

            if (rule.MatchType == RuleMatchType.Keyword)
            {
                // 关键词规则走普通字符串匹配，避免不必要的正则开销。
                return content.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);
            }

            return Regex.IsMatch(content, rule.Pattern, RegexOptions.IgnoreCase);
        }

        // 根据输出规则中的替换文本，对敏感内容执行脱敏
        public string ReplaceSensitiveContent(string content, SecurityRule rule)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Replacement))
            {
                return content;
            }

            if (rule.MatchType == RuleMatchType.Keyword)
            {
                // 关键词规则不走 Regex，避免用户输入的特殊字符被当成正则语法。
                return content.Replace(rule.Pattern, rule.Replacement, StringComparison.OrdinalIgnoreCase);
            }

            return Regex.Replace(content, rule.Pattern, rule.Replacement, RegexOptions.IgnoreCase);
        }

        // 多条规则命中时保留更高的风险等级
        public RiskLevel GetHigherRiskLevel(RiskLevel current, RiskLevel incoming)
        {
            return incoming > current ? incoming : current;
        }
    }
}
