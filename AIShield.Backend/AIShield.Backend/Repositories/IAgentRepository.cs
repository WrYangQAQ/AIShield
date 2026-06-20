using AIShield.Backend.Models;

namespace AIShield.Backend.Repositories
{
    public interface IAgentRepository
    {
        // 新增 Agent 并保存到数据库
        Task AddAsync(AgentApp agent);

        // 根据数据库主键查找 Agent
        Task<AgentApp?> GetByIdAsync(long agentId);

        // 根据 Agent Key 指纹快速定位候选 Agent
        Task<AgentApp?> GetByKeyFingerprintAsync(string fingerprint);

        // 查询全部 Agent，供管理端展示
        Task<List<AgentApp>> ListAsync();

        // 保存 Agent 表变更
        Task SaveChangesAsync();

        // 删除 Agent
        Task<bool> DeleteAsync(AgentApp agent);
    }
}
