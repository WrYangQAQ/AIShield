using AIShield.Backend.Data;
using AIShield.Backend.Dtos;
using AIShield.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace AIShield.Backend.Repositories
{
    public class AuditRecordRepository : IAuditRecordRepository
    {
        private readonly AppDbContext _dbContext;

        public AuditRecordRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // 新增审计日志
        public async Task AddAsync(AuditRecord record)
        {
            _dbContext.AuditRecords.Add(record);
            await _dbContext.SaveChangesAsync();
        }

        // 查询指定 Agent 的近期审计日志
        public Task<List<AuditRecord>> ListRecentAsync(long? agentId, int limit)
        {
            return BuildAgentQuery(agentId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        // 查询指定时间范围内的审计日志
        public Task<List<AuditRecord>> ListByTimeRangeAsync(long? agentId, DateTime startTime, DateTime endTime)
        {
            return BuildAgentQuery(agentId)
                .Where(x => x.CreatedAt >= startTime && x.CreatedAt < endTime)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();
        }

        // 统计指定时间范围内的审计日志数量
        public Task<int> CountAsync(long? agentId, DateTime startTime, DateTime endTime)
        {
            return BuildAgentQuery(agentId)
                .CountAsync(x => x.CreatedAt >= startTime && x.CreatedAt < endTime);
        }

        // 根据 Agent 条件构建基础查询，agentId 为空时查询全部日志
        private IQueryable<AuditRecord> BuildAgentQuery(long? agentId)
        {
            var query = _dbContext.AuditRecords.AsQueryable();

            if (agentId.HasValue)
            {
                query = query.Where(x => x.AgentId == agentId.Value);
            }

            return query;
        }

        // 根据DTO里的各类字段搜索审计日志，支持模糊搜索和范围搜索
        public async Task<PagedResult<AuditRecord>> SearchAsync(AuditSearchRequest request)
        {
            var query = _dbContext.AuditRecords.AsNoTracking().AsQueryable();

            if (request.AgentId.HasValue)
            {
                query = query.Where(x => x.AgentId == request.AgentId.Value);
            }

            if (request.Direction.HasValue)
            {
                query = query.Where(x => x.Direction == request.Direction.Value);
            }

            if (request.RiskLevel.HasValue)
            {
                query = query.Where(x => x.RiskLevel == request.RiskLevel.Value);
            }

            if (request.Action.HasValue)
            {
                query = query.Where(x => x.Action == request.Action.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.HitRule))
            {
                query = query.Where(x => x.HitRules.Contains(request.HitRule.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                var keyword = request.Keyword.Trim();
                query = query.Where(x =>
                    x.Reason.Contains(keyword)
                    || x.OriginalContent.Contains(keyword)
                    || (x.ProcessedContent != null && x.ProcessedContent.Contains(keyword)));
            }

            if (request.StartTime.HasValue)
            {
                query = query.Where(x => x.CreatedAt >= request.StartTime.Value);
            }

            if (request.EndTime.HasValue)
            {
                query = query.Where(x => x.CreatedAt < request.EndTime.Value);
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((request.PageIndex - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync();

            return new PagedResult<AuditRecord>
            {
                Items = items,
                Total = total,
                PageIndex = request.PageIndex,
                PageSize = request.PageSize
            };
        }
    }
}
