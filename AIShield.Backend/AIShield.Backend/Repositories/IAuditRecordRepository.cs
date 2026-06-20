using AIShield.Backend.Dtos;
using AIShield.Backend.Models;

namespace AIShield.Backend.Repositories
{
    public interface IAuditRecordRepository
    {
        // 新增审计日志
        Task AddAsync(AuditRecord record);

        // 查询指定 Agent 的近期审计日志
        Task<List<AuditRecord>> ListRecentAsync(long? agentId, int limit);

        // 查询指定时间范围内的审计日志
        Task<List<AuditRecord>> ListByTimeRangeAsync(long? agentId, DateTime startTime, DateTime endTime);

        // 统计指定时间范围内的审计日志数量
        Task<int> CountAsync(long? agentId, DateTime startTime, DateTime endTime);

        // 根据大量条件过滤搜索
        Task<PagedResult<AuditRecord>> SearchAsync(AuditSearchRequest request);
    }
}
