using AIShield.Backend.Enums;
using AIShield.Backend.Models;

namespace AIShield.Backend.Repositories
{
    public interface ISecurityRuleRepository
    {
        // 判断数据库中是否已经存在规则
        Task<bool> HasAnyRuleAsync();

        // 批量新增规则
        Task AddRangeAsync(IEnumerable<SecurityRule> rules);

        // 新增单条规则
        Task AddAsync(SecurityRule rule);

        // 根据业务规则编号查询规则
        Task<SecurityRule?> GetByRuleIdAsync(string ruleId);

        // 查询全部规则
        Task<List<SecurityRule>> ListAllAsync();

        // 查询指定 Agent 已绑定并启用的规则
        Task<List<SecurityRule>> ListEnabledByAgentAsync(long agentId, RuleType? ruleType = null);

        // 查询指定 Agent 已绑定的规则编号集合
        Task<HashSet<long>> ListBoundRuleIdsAsync(long agentId);

        // 新增 Agent 与规则的绑定关系
        Task BindRuleAsync(long agentId, long ruleId, bool enabled = true);

        // 批量新增 Agent 与规则的绑定关系
        Task BindRulesAsync(long agentId, IEnumerable<long> ruleIds, bool enabled = true);

        // 更新 Agent 维度的规则启用状态
        Task<AgentRule?> UpdateAgentRuleEnabledAsync(long agentId, long ruleId, bool enabled);

        // 删除规则
        void Remove(SecurityRule rule);

        // 保存规则表变更
        Task SaveChangesAsync();
    }
}
