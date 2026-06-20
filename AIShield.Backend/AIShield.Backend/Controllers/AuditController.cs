using AIShield.Backend.Services;
using AIShield.Backend.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace AIShield.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuditController : ControllerBase
    {
        private readonly AuditService _auditService;

        public AuditController(AuditService auditService)
        {
            _auditService = auditService;
        }

        // 查询近期安全审计记录，agentId 为空时返回全部 Agent 的近期记录
        [HttpGet]
        public async Task<IActionResult> GetAuditRecords([FromQuery] long? agentId, [FromQuery] int limit = 100)
        {
            var records = await _auditService.QueryRecordsAsync(agentId, limit);
            return Ok(records);
        }

        // 根据前端传入的过滤条件查询审计记录，支持 agentId、时间范围、规则 ID 等多维度过滤
        [HttpPost("search")]
        public async Task<IActionResult> SearchAuditRecords([FromBody] AuditSearchRequest request)
        {
            var records = await _auditService.SearchRecordsAsync(request);
            return Ok(records);
        }
    }
}
