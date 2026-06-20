using AIShield.Backend.Dtos;
using AIShield.Backend.Enums;

namespace AIShield.Backend.Services
{
    public class InputSecurityService
    {
        private readonly RuleEngine _ruleEngine;
        private readonly RuleConfigService _ruleConfigService;
        private readonly ContentNormalizer _contentNormalizer;

        public InputSecurityService(
            RuleEngine ruleEngine,
            RuleConfigService ruleConfigService,
            ContentNormalizer contentNormalizer)
        {
            _ruleEngine = ruleEngine;
            _ruleConfigService = ruleConfigService;
            _contentNormalizer = contentNormalizer;
        }

        // 检查进入 Agent 前的内容，按当前 Agent 绑定的输入规则执行拦截判断
        public async Task<SecurityCheckResponse> CheckInputAsync(long agentId, SecurityCheckRequest? request)
        {
            if (request == null)
            {
                return BuildBlockedResponse("SYS001", "请求体不能为空", RiskLevel.High);
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BuildBlockedResponse("SYS002", "输入内容不能为空", RiskLevel.Low);
            }

            if (request.Content.Length > 2000)
            {
                return BuildBlockedResponse("SYS003", "输入内容超过最大长度限制", RiskLevel.Medium);
            }

            var rules = await _ruleConfigService.GetInputRulesAsync(agentId);
            var detectionVariants = _contentNormalizer.BuildDetectionVariants(request.Content);
            var hitRules = new List<string>();
            var hitRuleNames = new List<string>();
            var finalRiskLevel = RiskLevel.None;
            var finalAction = SecurityAction.Allow;

            foreach (var rule in rules)
            {
                // 原文和规范化文本任一命中，都视为命中该规则。
                if (!detectionVariants.Any(variant => _ruleEngine.IsMatch(variant, rule)))
                {
                    continue;
                }

                hitRules.Add(rule.RuleId);
                hitRuleNames.Add(rule.Name);
                finalRiskLevel = _ruleEngine.GetHigherRiskLevel(finalRiskLevel, rule.RiskLevel);
                finalAction = rule.Action;
            }

            if (hitRules.Count > 0)
            {
                return new SecurityCheckResponse
                {
                    Allowed = finalAction is SecurityAction.Allow or SecurityAction.Warn,
                    Action = finalAction,
                    RiskLevel = finalRiskLevel,
                    ProcessedContent = null,
                    Reason = $"命中输入安全规则：{string.Join("、", hitRuleNames)}",
                    HitRules = hitRules
                };
            }

            return new SecurityCheckResponse
            {
                Allowed = true,
                Action = SecurityAction.Allow,
                RiskLevel = RiskLevel.None,
                ProcessedContent = request.Content,
                Reason = "输入内容未命中风险规则，允许继续调用 Agent",
                HitRules = new List<string>()
            };
        }

        // 构建拦截响应，减少系统校验分支里的重复代码
        private static SecurityCheckResponse BuildBlockedResponse(string ruleId, string reason, RiskLevel riskLevel)
        {
            return new SecurityCheckResponse
            {
                Allowed = false,
                Action = SecurityAction.Block,
                RiskLevel = riskLevel,
                ProcessedContent = null,
                Reason = reason,
                HitRules = new List<string> { ruleId }
            };
        }
    }
}
