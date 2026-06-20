using AIShield.Backend.Data;
using AIShield.Backend.Enums;
using AIShield.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace AIShield.Backend.Repositories
{
    public class SecurityRuleRepository : ISecurityRuleRepository
    {
        private readonly AppDbContext _dbContext;

        public SecurityRuleRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // 判断数据库中是否已经存在规则
        public Task<bool> HasAnyRuleAsync()
        {
            return _dbContext.SecurityRules.AnyAsync();
        }

        // 批量新增规则
        public async Task AddRangeAsync(IEnumerable<SecurityRule> rules)
        {
            _dbContext.SecurityRules.AddRange(rules);
            await _dbContext.SaveChangesAsync();
        }

        // 新增单条规则
        public async Task AddAsync(SecurityRule rule)
        {
            _dbContext.SecurityRules.Add(rule);
            await _dbContext.SaveChangesAsync();
        }

        // 根据业务规则编号查询规则
        public Task<SecurityRule?> GetByRuleIdAsync(string ruleId)
        {
            return _dbContext.SecurityRules
                .Include(x => x.AgentRules)
                .SingleOrDefaultAsync(x => x.RuleId == ruleId);
        }

        // 查询全部规则
        public Task<List<SecurityRule>> ListAllAsync()
        {
            return _dbContext.SecurityRules
                .OrderBy(x => x.RuleType)
                .ThenBy(x => x.RuleId)
                .ToListAsync();
        }

        // 查询指定 Agent 已绑定并启用的规则
        public Task<List<SecurityRule>> ListEnabledByAgentAsync(long agentId, RuleType? ruleType = null)
        {
            var query = _dbContext.AgentRules
                .Where(x => x.AgentId == agentId && x.Enabled && x.Rule != null && x.Rule.Enabled)
                .Select(x => x.Rule!);

            if (ruleType.HasValue)
            {
                query = query.Where(x => x.RuleType == ruleType.Value);
            }

            return query
                .OrderBy(x => x.RuleId)
                .ToListAsync();
        }

        // 查询指定 Agent 已绑定的规则编号集合
        public Task<HashSet<long>> ListBoundRuleIdsAsync(long agentId)
        {
            return _dbContext.AgentRules
                .Where(x => x.AgentId == agentId)
                .Select(x => x.RuleId)
                .ToHashSetAsync();
        }

        // 新增 Agent 与规则的绑定关系
        public async Task BindRuleAsync(long agentId, long ruleId, bool enabled = true)
        {
            var exists = await _dbContext.AgentRules.AnyAsync(x => x.AgentId == agentId && x.RuleId == ruleId);
            if (exists)
            {
                return;
            }

            _dbContext.AgentRules.Add(new AgentRule
            {
                AgentId = agentId,
                RuleId = ruleId,
                Enabled = enabled,
                CreatedAt = DateTime.Now
            });

            await _dbContext.SaveChangesAsync();
        }

        // 批量新增 Agent 与规则的绑定关系
        public async Task BindRulesAsync(long agentId, IEnumerable<long> ruleIds, bool enabled = true)
        {
            var existingRuleIds = await ListBoundRuleIdsAsync(agentId);
            var now = DateTime.Now;

            // 只插入当前 Agent 尚未绑定的规则，避免复合主键冲突。
            var bindings = ruleIds
                .Where(ruleId => !existingRuleIds.Contains(ruleId))
                .Select(ruleId => new AgentRule
                {
                    AgentId = agentId,
                    RuleId = ruleId,
                    Enabled = enabled,
                    CreatedAt = now
                })
                .ToList();

            if (bindings.Count == 0)
            {
                return;
            }

            _dbContext.AgentRules.AddRange(bindings);
            await _dbContext.SaveChangesAsync();
        }

        // 更新 Agent 维度的规则启用状态
        public async Task<AgentRule?> UpdateAgentRuleEnabledAsync(long agentId, long ruleId, bool enabled)
        {
            var binding = await _dbContext.AgentRules
                .SingleOrDefaultAsync(x => x.AgentId == agentId && x.RuleId == ruleId);

            if (binding == null)
            {
                return null;
            }

            binding.Enabled = enabled;
            await _dbContext.SaveChangesAsync();

            return binding;
        }

        // 删除规则
        public void Remove(SecurityRule rule)
        {
            _dbContext.SecurityRules.Remove(rule);
        }

        // 保存规则表变更
        public Task SaveChangesAsync()
        {
            return _dbContext.SaveChangesAsync();
        }
    }
}
