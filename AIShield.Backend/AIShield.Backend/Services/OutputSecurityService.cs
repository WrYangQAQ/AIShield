using AIShield.Backend.Dtos;
using AIShield.Backend.Enums;

namespace AIShield.Backend.Services
{
    public class OutputSecurityService
    {
        private readonly RuleEngine _ruleEngine;
        private readonly RuleConfigService _ruleConfigService;

        public OutputSecurityService(RuleEngine ruleEngine, RuleConfigService ruleConfigService)
        {
            _ruleEngine = ruleEngine;
            _ruleConfigService = ruleConfigService;
        }

        // 检查 Agent 输出内容，并按当前 Agent 绑定的输出规则执行脱敏
        public async Task<SecurityCheckResponse> CheckOutputAsync(long agentId, SecurityCheckRequest? request)
        {
            if (request == null)
            {
                return new SecurityCheckResponse
                {
                    Allowed = false,
                    Action = SecurityAction.Block,
                    RiskLevel = RiskLevel.High,
                    ProcessedContent = null,
                    Reason = "请求体不能为空",
                    HitRules = new List<string> { "SYS001" }
                };
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return new SecurityCheckResponse
                {
                    Allowed = true,
                    Action = SecurityAction.Allow,
                    RiskLevel = RiskLevel.None,
                    ProcessedContent = request.Content,
                    Reason = "输出内容为空，无需处理",
                    HitRules = new List<string>()
                };
            }

            var processedContent = request.Content;
            var rules = await _ruleConfigService.GetOutputRulesAsync(agentId);
            var hitRules = new List<string>();
            var hitRuleNames = new List<string>();
            var finalRiskLevel = RiskLevel.None;
            var finalAction = SecurityAction.Allow;

            foreach (var rule in rules)
            {
                if (!_ruleEngine.IsMatch(processedContent, rule))
                {
                    continue;
                }

                hitRules.Add(rule.RuleId);
                hitRuleNames.Add(rule.Name);
                finalRiskLevel = _ruleEngine.GetHigherRiskLevel(finalRiskLevel, rule.RiskLevel);
                finalAction = rule.Action;

                // 只有 Mask 动作才修改输出内容，Block/Warn 只记录处理建议。
                if (rule.Action == SecurityAction.Mask)
                {
                    processedContent = _ruleEngine.ReplaceSensitiveContent(processedContent, rule);
                }
            }

            if (hitRules.Count > 0)
            {
                return new SecurityCheckResponse
                {
                    Allowed = finalAction != SecurityAction.Block,
                    Action = finalAction,
                    RiskLevel = finalRiskLevel,
                    ProcessedContent = processedContent,
                    Reason = $"输出内容命中安全规则：{string.Join("、", hitRuleNames)}",
                    HitRules = hitRules
                };
            }

            return new SecurityCheckResponse
            {
                Allowed = true,
                Action = SecurityAction.Allow,
                RiskLevel = RiskLevel.None,
                ProcessedContent = processedContent,
                Reason = "输出内容未命中敏感信息规则，允许返回给用户",
                HitRules = new List<string>()
            };
        }
    }
}
