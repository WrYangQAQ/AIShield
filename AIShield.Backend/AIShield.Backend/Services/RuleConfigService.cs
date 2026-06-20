using System.Text.Json;
using System.Text.Json.Serialization;
using AIShield.Backend.Dtos;
using AIShield.Backend.Enums;
using AIShield.Backend.Models;
using AIShield.Backend.Repositories;

namespace AIShield.Backend.Services
{
    public class RuleConfigService
    {
        private readonly string _seedRuleFilePath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ISecurityRuleRepository _ruleRepository;
        private readonly RuleEngine _ruleEngine;

        public RuleConfigService(
            IWebHostEnvironment environment,
            ISecurityRuleRepository ruleRepository,
            RuleEngine ruleEngine)
        {
            _seedRuleFilePath = Path.Combine(environment.ContentRootPath, "security-rules.json");
            _ruleRepository = ruleRepository;
            _ruleEngine = ruleEngine;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        // 首次启动时把 JSON 默认规则导入数据库，之后运行时以数据库为准
        public async Task EnsureSeedRulesAsync()
        {
            if (await _ruleRepository.HasAnyRuleAsync() || !File.Exists(_seedRuleFilePath))
            {
                return;
            }

            var seedRuleSet = ReadSeedRuleSet();
            var now = DateTime.Now;
            var rules = seedRuleSet.InputRules
                .Concat(seedRuleSet.OutputRules)
                .Select(rule =>
                {
                    rule.Id = 0;
                    rule.IsSystemRule = true;
                    rule.CreatedAt = now;
                    rule.UpdatedAt = now;
                    return rule;
                })
                .ToList();

            if (rules.Count > 0)
            {
                await _ruleRepository.AddRangeAsync(rules);
            }
        }

        // 读取完整规则配置；agentId 为空时返回全局规则，非空时返回该 Agent 已绑定的规则
        public async Task<SecurityRuleSet> GetRuleSetAsync(long? agentId = null)
        {
            var rules = agentId.HasValue
                ? await _ruleRepository.ListEnabledByAgentAsync(agentId.Value)
                : await _ruleRepository.ListAllAsync();

            return ToRuleSet(rules);
        }

        // 获取固定枚举选项，前端可用下拉框展示
        public RuleOptionsResponse GetRuleOptions()
        {
            return new RuleOptionsResponse
            {
                RuleTypes = Enum.GetNames<RuleType>().ToList(),
                MatchTypes = Enum.GetNames<RuleMatchType>().ToList(),
                RiskLevels = Enum.GetNames<RiskLevel>().ToList(),
                Actions = Enum.GetNames<SecurityAction>().ToList()
            };
        }

        // 新增安全规则，若传入 agentId 则同步绑定到该 Agent
        public async Task<SecurityRule> AddRuleAsync(SaveSecurityRuleRequest request, long? agentId = null)
        {
            ValidateRuleRequest(request);

            if (await _ruleRepository.GetByRuleIdAsync(request.RuleId.Trim()) != null)
            {
                throw new ArgumentException($"规则编号 {request.RuleId} 已存在");
            }

            var rule = ToSecurityRule(request);
            await _ruleRepository.AddAsync(rule);

            if (agentId.HasValue)
            {
                await _ruleRepository.BindRuleAsync(agentId.Value, rule.Id, rule.Enabled);
            }

            return rule;
        }

        // 修改指定规则，规则本体仍保持全局唯一，Agent 维度只控制启用状态
        public async Task<SecurityRule> UpdateRuleAsync(string ruleId, SaveSecurityRuleRequest request)
        {
            ValidateRuleRequest(request);

            var existing = await _ruleRepository.GetByRuleIdAsync(ruleId);
            if (existing == null)
            {
                throw new KeyNotFoundException($"未找到规则 {ruleId}");
            }

            var requestedRuleId = request.RuleId.Trim();
            if (!string.Equals(ruleId, requestedRuleId, StringComparison.OrdinalIgnoreCase)
                && await _ruleRepository.GetByRuleIdAsync(requestedRuleId) != null)
            {
                throw new ArgumentException($"规则编号 {requestedRuleId} 已存在");
            }

            existing.RuleId = requestedRuleId;
            existing.Name = request.Name.Trim();
            existing.RuleType = request.RuleType;
            existing.MatchType = request.MatchType;
            existing.Pattern = request.Pattern.Trim();
            existing.RiskLevel = request.RiskLevel;
            existing.Action = request.Action;
            existing.Replacement = request.Replacement.Trim();
            existing.Enabled = request.Enabled;
            existing.UpdatedAt = DateTime.Now;

            await _ruleRepository.SaveChangesAsync();
            return existing;
        }

        // 启用或禁用规则；agentId 为空时改全局状态，非空时只改该 Agent 的绑定状态
        public async Task<SecurityRule> UpdateEnabledAsync(string ruleId, bool enabled, long? agentId = null)
        {
            var rule = await _ruleRepository.GetByRuleIdAsync(ruleId);
            if (rule == null)
            {
                throw new KeyNotFoundException($"未找到规则 {ruleId}");
            }

            if (agentId.HasValue)
            {
                var binding = await _ruleRepository.UpdateAgentRuleEnabledAsync(agentId.Value, rule.Id, enabled);
                if (binding == null)
                {
                    await _ruleRepository.BindRuleAsync(agentId.Value, rule.Id, enabled);
                }

                return rule;
            }

            rule.Enabled = enabled;
            rule.UpdatedAt = DateTime.Now;
            await _ruleRepository.SaveChangesAsync();
            return rule;
        }

        // 删除指定规则，同时级联删除 Agent 绑定关系
        public async Task DeleteRuleAsync(string ruleId)
        {
            var rule = await _ruleRepository.GetByRuleIdAsync(ruleId);
            if (rule == null)
            {
                throw new KeyNotFoundException($"未找到规则 {ruleId}");
            }

            _ruleRepository.Remove(rule);
            await _ruleRepository.SaveChangesAsync();
        }

        // 对单条规则进行测试，供前端调试匹配内容
        public async Task<TestRuleResponse> TestRuleAsync(TestRuleRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TestContent))
            {
                throw new ArgumentException("测试内容不能为空");
            }

            var rule = await _ruleRepository.GetByRuleIdAsync(request.RuleId);
            if (rule == null)
            {
                throw new ArgumentException("当前规则不存在");
            }

            var isMatch = _ruleEngine.IsMatch(request.TestContent.Trim(), rule);

            return new TestRuleResponse
            {
                IsMatch = isMatch,
                MatchDetails = isMatch
                    ? $"测试内容匹配规则 {rule.RuleId} - {rule.Name}"
                    : "测试内容不匹配当前规则"
            };
        }

        // 查询指定 Agent 的输入规则
        public Task<List<SecurityRule>> GetInputRulesAsync(long agentId)
        {
            return _ruleRepository.ListEnabledByAgentAsync(agentId, RuleType.Input);
        }

        // 查询指定 Agent 的输出规则
        public Task<List<SecurityRule>> GetOutputRulesAsync(long agentId)
        {
            return _ruleRepository.ListEnabledByAgentAsync(agentId, RuleType.Output);
        }

        // 把全部现有规则绑定到指定 Agent，用于新 Agent 初始化
        public async Task BindAllRulesToAgentAsync(long agentId)
        {
            var rules = await _ruleRepository.ListAllAsync();
            await _ruleRepository.BindRulesAsync(agentId, rules.Select(x => x.Id));
        }

        // 从 JSON 种子文件读取默认规则
        private SecurityRuleSet ReadSeedRuleSet()
        {
            var json = File.ReadAllText(_seedRuleFilePath);
            return JsonSerializer.Deserialize<SecurityRuleSet>(json, _jsonOptions) ?? new SecurityRuleSet();
        }

        // 把规则列表按类型组装成前端原有的规则集合结构
        private static SecurityRuleSet ToRuleSet(IEnumerable<SecurityRule> rules)
        {
            var ruleList = rules.ToList();

            return new SecurityRuleSet
            {
                InputRules = ruleList.Where(x => x.RuleType == RuleType.Input).ToList(),
                OutputRules = ruleList.Where(x => x.RuleType == RuleType.Output).ToList(),
                ToolPolicy = new ToolPolicy()
            };
        }

        // 将前端请求 DTO 转换为数据库规则实体
        private static SecurityRule ToSecurityRule(SaveSecurityRuleRequest request)
        {
            var now = DateTime.Now;

            return new SecurityRule
            {
                RuleId = request.RuleId.Trim(),
                Name = request.Name.Trim(),
                RuleType = request.RuleType,
                MatchType = request.MatchType,
                Pattern = request.Pattern.Trim(),
                RiskLevel = request.RiskLevel,
                Action = request.Action,
                Replacement = request.Replacement.Trim(),
                Enabled = request.Enabled,
                IsSystemRule = false,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        // 校验前端提交的规则配置
        private static void ValidateRuleRequest(SaveSecurityRuleRequest request)
        {
            if (request == null)
            {
                throw new ArgumentException("请求体不能为空");
            }

            if (string.IsNullOrWhiteSpace(request.RuleId))
            {
                throw new ArgumentException("规则编号不能为空");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ArgumentException("规则名称不能为空");
            }

            if (string.IsNullOrWhiteSpace(request.Pattern))
            {
                throw new ArgumentException("匹配内容不能为空");
            }

            if (request.RuleType == RuleType.Input && request.Action == SecurityAction.Mask)
            {
                throw new ArgumentException("输入检测规则不支持 Mask 动作，请选择 Allow、Warn、Block 或 NeedApproval");
            }

            if (request.RuleType == RuleType.Output
                && request.Action == SecurityAction.Mask
                && string.IsNullOrWhiteSpace(request.Replacement))
            {
                throw new ArgumentException("输出脱敏规则需要填写替换文本");
            }
        }
    }
}
