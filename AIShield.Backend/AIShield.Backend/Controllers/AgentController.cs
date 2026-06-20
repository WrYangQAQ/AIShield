using AIShield.Backend.Dtos;
using AIShield.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AIShield.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AgentController : ControllerBase
    {
        private readonly AgentService _agentService;

        public AgentController(AgentService agentService)
        {
            _agentService = agentService;
        }

        // 注册接入 AIShield 的 Agent，并返回只展示一次的 Agent Key
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterAgentRequest request)
        {
            try
            {
                var response = await _agentService.RegisterAsync(request);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // 查询全部 Agent，供前端选择当前管理对象
        [HttpGet]
        public async Task<IActionResult> List()
        {
            return Ok(await _agentService.ListAsync());
        }

        // 查询单个 Agent 详情
        [HttpGet("{agentId:long}")]
        public async Task<IActionResult> Get(long agentId)
        {
            var agent = await _agentService.GetAsync(agentId);

            if (agent == null)
            {
                return NotFound(new { message = "Agent 不存在" });
            }

            return Ok(agent);
        }

        // 启用或禁用 Agent
        [HttpPatch("{agentId:long}/enabled")]
        public async Task<IActionResult> UpdateEnabled(long agentId, [FromBody] UpdateAgentEnabledRequest request)
        {
            var agent = await _agentService.UpdateEnabledAsync(agentId, request.Enabled);

            if (agent == null)
            {
                return NotFound(new { message = "Agent 不存在" });
            }

            return Ok(agent);
        }

        // 删除Agent
        [HttpDelete("{agentId:long}")]
        public async Task<IActionResult> Delete(long agentId)
        {
            var result = await _agentService.DeleteAsync(agentId);

            if (!result)
            {
                return NotFound(new { message = "Agent 不存在" });
            }

            return NoContent();
        }

        // 修改Agent信息
        [HttpPut("{agentId:long}")]
        public async Task<IActionResult> UpdateInfo(long agentId, [FromBody] ModifyAgentRequest request)
        {
            if(agentId != request.Id)
            {
                return BadRequest(new { message = "请求参数错误" });
            }
            var agent = await _agentService.ModifyAsync(request);
            if (agent == null)
            {
                return NotFound(new { message = "Agent 不存在" });
            }
            return Ok(agent);
        }
    }
}
