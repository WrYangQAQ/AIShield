using System.Text.Json;
using AIShield.Backend.Dtos;
using AIShield.Backend.Enums;

namespace AIShield.Backend.Services
{
    public class ToolCallGuard
    {
        private readonly RuleEngine _ruleEngine;
        private readonly RuleConfigService _ruleConfigService;

        public ToolCallGuard(RuleEngine ruleEngine, RuleConfigService ruleConfigService)
        {
            _ruleEngine = ruleEngine;
            _ruleConfigService = ruleConfigService;
        }

        // 执行工具前检查工具名称和参数，防止危险工具调用或越权参数
        public async Task<SecurityCheckResponse> CheckToolCallAsync(long agentId, ToolCallCheckRequest? request)
        {
            if (request == null)
            {
                return BuildBlockedResponse("SYS001", "请求体不能为空", RiskLevel.High);
            }

            if (string.IsNullOrWhiteSpace(request.ToolName))
            {
                return BuildBlockedResponse("TG000", "工具名称不能为空", RiskLevel.Low);
            }

            var argumentsJson = JsonSerializer.Serialize(request.Arguments);
            var detectionContent = $"{request.ToolName}\n{argumentsJson}";
            var rules = await _ruleConfigService.GetInputRulesAsync(agentId);
            var hitRules = new List<string>();
            var hitRuleNames = new List<string>();
            var finalRiskLevel = RiskLevel.None;
            var finalAction = SecurityAction.Allow;

            foreach (var rule in rules)
            {
                // 工具调用暂时复用输入规则，后续可拆成专门的 ToolCall 规则类型。
                if (!_ruleEngine.IsMatch(detectionContent, rule))
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
                    ProcessedContent = argumentsJson,
                    Reason = $"工具调用命中安全规则：{string.Join("、", hitRuleNames)}",
                    HitRules = hitRules
                };
            }

            return new SecurityCheckResponse
            {
                Allowed = true,
                Action = SecurityAction.Allow,
                RiskLevel = RiskLevel.None,
                ProcessedContent = argumentsJson,
                Reason = "工具调用未命中风险规则，允许执行",
                HitRules = new List<string>()
            };
        }

        // 构建拦截响应
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
