using System.Text.Json;
using AIShield.Backend.Dtos;
using AIShield.Backend.Enums;
using AIShield.Backend.Models;
using AIShield.Backend.Repositories;

namespace AIShield.Backend.Services
{
    public class AuditService
    {
        private readonly IAuditRecordRepository _auditRepository;

        public AuditService(IAuditRecordRepository auditRepository)
        {
            _auditRepository = auditRepository;
        }

        // 记录输入检测或输出过滤结果
        public async Task<AuditRecord> AddRecordAsync(
            SecurityCheckRequest? request,
            SecurityCheckResponse response,
            AuditDirection direction,
            string clientIp,
            long agentId,
            string agentName,
            long durationMs = 0)
        {
            var record = new AuditRecord
            {
                AgentId = agentId,
                AgentName = agentName,
                SubjectHash = request?.SubjectHash,
                CreatedAt = DateTime.Now,
                Direction = direction,
                OriginalContent = request?.Content ?? string.Empty,
                ProcessedContent = response.ProcessedContent,
                RiskLevel = response.RiskLevel,
                Action = response.Action,
                HitRules = string.Join(",", response.HitRules),
                Reason = response.Reason,
                ClientIp = clientIp,
                DurationMs = durationMs
            };

            await _auditRepository.AddAsync(record);
            return record;
        }

        // 记录工具调用检测结果
        public async Task<AuditRecord> AddToolCallRecordAsync(
            ToolCallCheckRequest? request,
            SecurityCheckResponse response,
            string clientIp,
            long agentId,
            string agentName,
            long durationMs = 0)
        {
            var originalContent = request == null
                ? string.Empty
                : JsonSerializer.Serialize(new
                {
                    request.ToolName,
                    request.Arguments
                });

            var record = new AuditRecord
            {
                AgentId = agentId,
                AgentName = agentName,
                SubjectHash = request?.SubjectHash,
                CreatedAt = DateTime.Now,
                Direction = AuditDirection.ToolCall,
                OriginalContent = originalContent,
                ProcessedContent = response.ProcessedContent,
                RiskLevel = response.RiskLevel,
                Action = response.Action,
                HitRules = string.Join(",", response.HitRules),
                Reason = response.Reason,
                ClientIp = clientIp,
                DurationMs = durationMs
            };

            await _auditRepository.AddAsync(record);
            return record;
        }

        // 查询近期审计记录，支持按 Agent 过滤
        public async Task<List<AuditRecord>> QueryRecordsAsync(long? agentId, int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 500);
            return await _auditRepository.ListRecentAsync(agentId, limit);
        }

        // 按天聚合风险趋势数据，缺失日期会补 0，方便前端直接绘图
        public async Task<RiskTrendResponse> QueryRiskTrendAsync(int days, long? agentId)
        {
            days = Math.Clamp(days, 1, 30);
            var today = DateTime.Today;
            var startDate = today.AddDays(-(days - 1));
            var endDate = today.AddDays(1);
            var records = await _auditRepository.ListByTimeRangeAsync(agentId, startDate, endDate);

            var points = Enumerable.Range(0, days)
                .Select(offset =>
                {
                    var date = startDate.AddDays(offset);
                    var dayRecords = records.Where(x => x.CreatedAt.Date == date);

                    return new RiskTrendPointResponse
                    {
                        Date = DateOnly.FromDateTime(date),
                        Label = date.ToString("MM-dd"),
                        BlockCount = dayRecords.Count(x => x.Action == SecurityAction.Block),
                        MaskCount = dayRecords.Count(x => x.Action == SecurityAction.Mask),
                        HighRiskCount = dayRecords.Count(x => x.RiskLevel is RiskLevel.High or RiskLevel.Critical)
                    };
                })
                .ToList();

            return new RiskTrendResponse
            {
                Days = days,
                Points = points
            };
        }

        // 查询 Agent 健康状态指标
        public async Task<HealthStatusResponse> QueryHealthStatusAsync(long agentId)
        {
            var today = DateTime.Today;
            var startTime = today.AddDays(-1);
            var endTime = today;
            var records = await _auditRepository.ListByTimeRangeAsync(agentId, startTime, endTime);
            var totalCount = records.Count;
            var blockCount = records.Count(x => x.Action == SecurityAction.Block);
            var errorCount = records.Count(x => x.RiskLevel == RiskLevel.Critical);
            var averageResponseTime = records.Count > 0 ? records.Average(x => (double)x.DurationMs) : 0;

            // 健康分按拦截和严重风险粗略扣分，最后限制在 0~100。
            var rawScore = totalCount > 0
                ? 100 - (blockCount * 50 + errorCount * 100) / (double)totalCount
                : 100;

            return new HealthStatusResponse
            {
                HealthScore = Math.Clamp(rawScore, 0, 100),
                AverageResponseTime = averageResponseTime,
                ErrorRate = totalCount > 0 ? errorCount / (double)totalCount : 0,
                Availability = totalCount > 0 ? (totalCount - errorCount) / (double)totalCount : 1
            };
        }

        // 查询总览页今日核心指标
        public async Task<OverviewResponse> QueryOverviewAsync(long agentId)
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var records = await _auditRepository.ListByTimeRangeAsync(agentId, today, tomorrow);

            return new OverviewResponse
            {
                DayRequestCount = records.Count,
                DayBlockedCount = records.Count(x => x.Action == SecurityAction.Block),
                DayMaskedCount = records.Count(x => x.Action == SecurityAction.Mask),
                DayRiskEventCount = records.Count(x => x.RiskLevel is RiskLevel.High or RiskLevel.Critical)
            };
        }

        // 根据前端传入的过滤条件查询审计记录，支持 agentId、时间范围、规则 ID 等多维度过滤
        public Task<PagedResult<AuditRecord>> SearchRecordsAsync(AuditSearchRequest request)
        {
            request.PageIndex = Math.Max(request.PageIndex, 1);
            request.PageSize = Math.Clamp(request.PageSize, 1, 100);

            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                request.Keyword = request.Keyword.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.HitRule))
            {
                request.HitRule = request.HitRule.Trim();
            }

            if (request.StartTime.HasValue && request.EndTime.HasValue
                && request.StartTime > request.EndTime)
            {
                throw new ArgumentException("开始时间不能晚于结束时间");
            }

            return _auditRepository.SearchAsync(request);
        }
    }
}
