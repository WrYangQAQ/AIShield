using AIShield.Backend.Dtos;
using AIShield.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIShield.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RulesController : ControllerBase
    {
        private readonly RuleConfigService _ruleConfigService;

        public RulesController(RuleConfigService ruleConfigService)
        {
            _ruleConfigService = ruleConfigService;
        }

        // 获取规则配置；agentId 为空时返回全局规则，否则返回该 Agent 已启用规则
        [HttpGet]
        public async Task<IActionResult> GetRules([FromQuery] long? agentId)
        {
            return Ok(await _ruleConfigService.GetRuleSetAsync(agentId));
        }

        // 获取下拉框枚举选项，供前端傻瓜式配置规则
        [HttpGet("options")]
        public IActionResult GetRuleOptions()
        {
            return Ok(_ruleConfigService.GetRuleOptions());
        }

        // 新增规则；agentId 非空时新增后自动绑定到该 Agent
        [HttpPost]
        public async Task<IActionResult> AddRule([FromBody] SaveSecurityRuleRequest request, [FromQuery] long? agentId)
        {
            try
            {
                return Ok(await _ruleConfigService.AddRuleAsync(request, agentId));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // 修改指定规则
        [HttpPut("{ruleId}")]
        public async Task<IActionResult> UpdateRule(string ruleId, [FromBody] SaveSecurityRuleRequest request)
        {
            try
            {
                return Ok(await _ruleConfigService.UpdateRuleAsync(ruleId, request));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // 启用或禁用规则；agentId 非空时只修改该 Agent 的绑定状态
        [HttpPatch("{ruleId}/enabled")]
        public async Task<IActionResult> UpdateEnabled(
            string ruleId,
            [FromBody] UpdateRuleEnabledRequest request,
            [FromQuery] long? agentId)
        {
            try
            {
                return Ok(await _ruleConfigService.UpdateEnabledAsync(ruleId, request.Enabled, agentId));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // 删除指定规则，并级联删除 Agent 绑定关系
        [HttpDelete("{ruleId}")]
        public async Task<IActionResult> DeleteRule(string ruleId)
        {
            try
            {
                await _ruleConfigService.DeleteRuleAsync(ruleId);
                return Ok(new { message = "规则删除成功" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // 对单条规则进行测试，查看测试内容是否命中
        [HttpPost("test")]
        public async Task<IActionResult> TestRule([FromBody] TestRuleRequest request)
        {
            try
            {
                return Ok(await _ruleConfigService.TestRuleAsync(request));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
