using AIShield.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIShield.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly AuditService _auditService;

        public DashboardController(AuditService auditService)
        {
            _auditService = auditService;
        }

        // 获取指定 Agent 的风险趋势聚合数据
        [HttpGet("risk-trend")]
        public async Task<IActionResult> GetRiskTrend([FromQuery] long? agentId, [FromQuery] int days = 7)
        {
            var result = await _auditService.QueryRiskTrendAsync(days, agentId);
            return Ok(result);
        }

        // 获取指定 Agent 的健康状态指标
        [HttpGet("health-status")]
        public async Task<IActionResult> GetHealthStatus([FromQuery] long agentId)
        {
            if (agentId <= 0)
            {
                return BadRequest("无法识别 Agent 身份");
            }

            var result = await _auditService.QueryHealthStatusAsync(agentId);
            return Ok(result);
        }

        // 获取指定 Agent 的总览页今日核心指标
        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview([FromQuery] long agentId)
        {
            if (agentId <= 0)
            {
                return BadRequest("无法识别 Agent 身份");
            }

            var result = await _auditService.QueryOverviewAsync(agentId);
            return Ok(result);
        }
    }
}
