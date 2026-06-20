using System.Diagnostics;
using AIShield.Backend.Dtos;
using AIShield.Backend.Enums;
using AIShield.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIShield.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SecurityController : ControllerBase
    {
        private readonly InputSecurityService _inputSecurityService;
        private readonly OutputSecurityService _outputSecurityService;
        private readonly ToolCallGuard _toolCallGuard;
        private readonly AuditService _auditService;
        private readonly MemoryService _memoryService;
        private readonly RagRerankService _ragRerankService;
        private readonly TopicDriftService _topicDriftService;

        public SecurityController(
            InputSecurityService inputSecurityService,
            OutputSecurityService outputSecurityService,
            ToolCallGuard toolCallGuard,
            AuditService auditService,
            MemoryService memoryService,
            RagRerankService ragRerankService,
            TopicDriftService topicDriftService)
        {
            _inputSecurityService = inputSecurityService;
            _outputSecurityService = outputSecurityService;
            _toolCallGuard = toolCallGuard;
            _auditService = auditService;
            _memoryService = memoryService;
            _ragRerankService = ragRerankService;
            _topicDriftService = topicDriftService;
        }

        // 检查用户输入或外部内容是否存在提示词注入、越权或危险操作诱导
        [HttpPost("check-input")]
        public async Task<IActionResult> CheckInput([FromBody] SecurityCheckRequest? request)
        {
            var stopwatch = Stopwatch.StartNew();
            var agentId = GetAuthenticatedAgentId();
            var agentName = GetAuthenticatedAgentName();
            var result = await _inputSecurityService.CheckInputAsync(agentId, request);
            stopwatch.Stop();

            await _auditService.AddRecordAsync(
                request,
                result,
                AuditDirection.Input,
                GetClientIp(),
                agentId,
                agentName,
                stopwatch.ElapsedMilliseconds);

            return Ok(result);
        }

        // 检查模型输出是否包含敏感信息，并按规则执行脱敏或阻断
        [HttpPost("check-output")]
        public async Task<IActionResult> CheckOutput([FromBody] SecurityCheckRequest? request)
        {
            var stopwatch = Stopwatch.StartNew();
            var agentId = GetAuthenticatedAgentId();
            var agentName = GetAuthenticatedAgentName();
            var result = await _outputSecurityService.CheckOutputAsync(agentId, request);
            stopwatch.Stop();

            await _auditService.AddRecordAsync(
                request,
                result,
                AuditDirection.Output,
                GetClientIp(),
                agentId,
                agentName,
                stopwatch.ElapsedMilliseconds);

            return Ok(result);
        }

        // 执行工具前检查工具名称和参数风险，防止越权工具调用
        [HttpPost("check-tool-call")]
        public async Task<IActionResult> CheckToolCall([FromBody] ToolCallCheckRequest? request)
        {
            var stopwatch = Stopwatch.StartNew();
            var agentId = GetAuthenticatedAgentId();
            var agentName = GetAuthenticatedAgentName();
            var result = await _toolCallGuard.CheckToolCallAsync(agentId, request);
            stopwatch.Stop();

            await _auditService.AddToolCallRecordAsync(
                request,
                result,
                GetClientIp(),
                agentId,
                agentName,
                stopwatch.ElapsedMilliseconds);

            return Ok(result);
        }

        // 使用本地主题漂移算法检测多轮对话或分段生成过程中的连续偏移。
        [HttpPost("check-topic-drift")]
        public IActionResult CheckTopicDrift([FromBody] TopicDriftRequest request)
        {
            var result = _topicDriftService.Check(request);
            return Ok(result);
        }

        // 使用 AIShield 本体规则执行记忆写入前安全检测，不实际保存记忆。
        [HttpPost("memory-write")]
        public async Task<IActionResult> CheckMemoryWrite([FromBody] MemoryWriteRequest request)
        {
            var result = await _memoryService.CheckWriteAsync(GetAuthenticatedAgentId(), request);
            return Ok(result);
        }

        // 使用本地记忆服务新增或更新单条记忆，并执行冲突检测。
        [HttpPost("memory-put")]
        public async Task<IActionResult> PutMemory([FromBody] MemoryPutRequest request)
        {
            var result = await _memoryService.PutAsync(
                GetAuthenticatedAgentId(),
                request,
                cancellationToken: HttpContext.RequestAborted);
            return Ok(result);
        }

        // 使用本地记忆服务批量保存记忆，可选择跳过冲突检测。
        [HttpPost("memory-bulk")]
        public async Task<IActionResult> BulkPutMemory([FromBody] MemoryBulkPutRequest request)
        {
            var result = await _memoryService.BulkPutAsync(
                GetAuthenticatedAgentId(),
                request,
                HttpContext.RequestAborted);
            return Ok(result);
        }

        // 查询记忆条目元数据，不返回记忆明文内容。
        [HttpGet("memory/{memoryId}")]
        public async Task<IActionResult> GetMemory(string memoryId)
        {
            var result = await _memoryService.GetAsync(GetAuthenticatedAgentId(), memoryId, HttpContext.RequestAborted);
            return Ok(result);
        }

        // 软删除记忆条目，保留归档状态用于审计。
        [HttpDelete("memory/{memoryId}")]
        public async Task<IActionResult> DeleteMemory(string memoryId)
        {
            var result = await _memoryService.DeleteAsync(GetAuthenticatedAgentId(), memoryId, HttpContext.RequestAborted);
            return Ok(result);
        }

        // 使用半衰期模型衰减记忆置信度，建议由定时任务批量触发。
        [HttpPost("memory-decay")]
        public async Task<IActionResult> ApplyMemoryDecay([FromBody] MemoryDecayRequest request)
        {
            var result = await _memoryService.ApplyDecayAsync(GetAuthenticatedAgentId(), request, HttpContext.RequestAborted);
            return Ok(result);
        }

        // 使用本地混合评分算法过滤并重排 RAG 候选文档。
        [HttpPost("rag-rerank")]
        public IActionResult RerankRagCandidates([FromBody] RagRerankRequest request)
        {
            var result = _ragRerankService.Rerank(request);
            return Ok(result);
        }

        // 同步记忆的正向引用时间，不需要重新上传记忆正文。
        [HttpPost("memory/{memoryId}/reference")]
        public async Task<IActionResult> UpdateMemoryReference(
            string memoryId,
            [FromBody] MemoryReferenceRequest request)
        {
            var result = await _memoryService.UpdateReferenceAsync(
                GetAuthenticatedAgentId(),
                memoryId,
                request,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 分批衰减当前认证 Agent 的未归档记忆。
        [HttpPost("memory/decay-batch")]
        public async Task<IActionResult> BatchDecayMemories(
            [FromBody] MemoryBatchDecayRequest request)
        {
            var result = await _memoryService.BatchDecayAsync(
                GetAuthenticatedAgentId(),
                request,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 分页查询当前认证 Agent 的记忆安全索引。
        [HttpGet("memories")]
        public async Task<IActionResult> SearchMemories(
            [FromQuery] MemorySearchRequest request)
        {
            var result = await _memoryService.SearchAsync(
                GetAuthenticatedAgentId(),
                request,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 恢复当前认证 Agent 的已归档记忆。
        [HttpPost("memory/{memoryId}/restore")]
        public async Task<IActionResult> RestoreMemory(
            string memoryId,
            [FromBody] MemoryRestoreRequest request)
        {
            var result = await _memoryService.RestoreAsync(
                GetAuthenticatedAgentId(),
                memoryId,
                request,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 拉取当前认证 Agent 尚未处理的记忆同步动作。
        [HttpGet("memory-sync-actions")]
        public async Task<IActionResult> GetPendingMemorySyncActions(
            [FromQuery] MemorySyncActionQueryRequest request)
        {
            var result = await _memoryService.GetPendingSyncActionsAsync(
                GetAuthenticatedAgentId(),
                request,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 确认用户 Agent 已成功执行指定的记忆同步动作。
        [HttpPost("memory-sync-actions/{actionId:guid}/confirm")]
        public async Task<IActionResult> ConfirmMemorySyncAction(Guid actionId)
        {
            var result = await _memoryService.ConfirmSyncActionAsync(
                GetAuthenticatedAgentId(),
                actionId,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 报告用户 Agent 执行指定记忆同步动作失败。
        [HttpPost("memory-sync-actions/{actionId:guid}/fail")]
        public async Task<IActionResult> FailMemorySyncAction(
            Guid actionId,
            [FromBody] MemorySyncFailureRequest request)
        {
            var result = await _memoryService.FailSyncActionAsync(
                GetAuthenticatedAgentId(),
                actionId,
                request,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 将失败的记忆同步动作重新放回待处理队列。
        [HttpPost("memory-sync-actions/{actionId:guid}/retry")]
        public async Task<IActionResult> RetryMemorySyncAction(Guid actionId)
        {
            var result = await _memoryService.RetrySyncActionAsync(
                GetAuthenticatedAgentId(),
                actionId,
                HttpContext.RequestAborted);

            return Ok(result);
        }

        // 获取客户端 IP
        private string GetClientIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        // 从中间件写入的上下文读取 AgentId
        private long GetAuthenticatedAgentId()
        {
            return HttpContext.Items["AgentId"] is long agentId ? agentId : 0;
        }

        // 从中间件写入的上下文读取 Agent 名称
        private string GetAuthenticatedAgentName()
        {
            return HttpContext.Items["AgentName"]?.ToString() ?? "unknown";
        }
    }
}
