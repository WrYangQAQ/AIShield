using AIShield.Backend.Data;
using AIShield.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace AIShield.Backend.Repositories
{
    public class AgentRepository : IAgentRepository
    {
        private readonly AppDbContext _dbContext;

        public AgentRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // 新增 Agent 并保存到数据库
        public async Task AddAsync(AgentApp agent)
        {
            _dbContext.AgentApps.Add(agent);
            await _dbContext.SaveChangesAsync();
        }

        // 根据数据库主键查找 Agent
        public Task<AgentApp?> GetByIdAsync(long agentId)
        {
            return _dbContext.AgentApps.SingleOrDefaultAsync(x => x.Id == agentId);
        }

        // 根据 Agent Key 指纹查询候选记录，避免全表拉取再逐条校验
        public Task<AgentApp?> GetByKeyFingerprintAsync(string fingerprint)
        {
            return _dbContext.AgentApps.SingleOrDefaultAsync(x => x.AgentKeyFingerprint == fingerprint);
        }

        // 查询全部 Agent，并按创建时间倒序展示
        public Task<List<AgentApp>> ListAsync()
        {
            return _dbContext.AgentApps
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        // 删除 Agent
        public async Task<bool> DeleteAsync(AgentApp agent)
        {
            _dbContext.AgentApps.Remove(agent);
            return await _dbContext.SaveChangesAsync() > 0;
        }

        // 保存 Agent 表变更
        public Task SaveChangesAsync()
        {
            return _dbContext.SaveChangesAsync();
        }
    }
}
