using AIShield.Backend.Data;
using AIShield.Backend.Dtos;
using AIShield.Backend.Enums;
using AIShield.Backend.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIShield.Backend.Services
{
    public class MemoryService
    {
        private readonly AppDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly InputSecurityService _inputSecurityService;

        public MemoryService(
            AppDbContext dbContext,
            IConfiguration configuration,
            InputSecurityService inputSecurityService)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _inputSecurityService = inputSecurityService;
        }

        // 根据输入安全规则检查记忆内容是否允许写入
        public async Task<GuardAlgorithmResult<MemoryWriteDetails>> CheckWriteAsync(
            long agentId,
            MemoryWriteRequest request)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var securityResult = await CheckContentAsync(agentId, request.Content);   // 检查记忆内容是否被投毒（走规则匹配）

            if (!securityResult.Allowed)   // 如果记忆内容有问题
            {
                return BuildFailure<MemoryWriteDetails>(   // 构造失败响应并返回 
                    requestId,
                    securityResult.Reason,
                    "unsafe_memory_content",
                    new MemoryWriteDetails
                    {
                        Source = request.Source,
                        HitRules = securityResult.HitRules,
                        Reason = securityResult.Reason
                    });
            }

            // 记忆没有问题，构造成功响应并返回
            return BuildSuccess(requestId, new MemoryWriteDetails
            {
                ProcessedContent = securityResult.ProcessedContent ?? request.Content,
                Source = request.Source,
                HitRules = securityResult.HitRules,
                Reason = securityResult.Reason
            });
        }

        // 新增或更新单条记忆，并对同来源、同业务槽位的旧记忆执行冲突降权。
        public async Task<GuardAlgorithmResult<MemoryDetails>> PutAsync(
            long agentId,
            MemoryPutRequest request,
            bool skipConflictCheck = false,
            CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var validationError = ValidatePutRequest(request);  // 对记忆更新做验证
            if (validationError != null)
            {   // 校验未通过则构建失败响应并返回
                return BuildFailure<MemoryDetails>(requestId, validationError, "invalid_memory");
            }

            // 写入接口自身强制执行安全检测，调用方不能通过跳过 memory-write 接口绕过检查。
            var securityResult = await CheckContentAsync(agentId, request.Content);
            if (!securityResult.Allowed)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    securityResult.Reason,
                    "unsafe_memory_content");
            }

            var now = DateTime.UtcNow;
            var source = request.Source;
            var content = (securityResult.ProcessedContent ?? request.Content).Trim();
            var contentHash = ComputeSha256(content);
            var lastPositiveRef = ParseLastPositiveRef(request.LastPositiveRef);   // 将时间转化为UTC，如果为空字符串则代表未进行过正向引用
            var memoryCreatedAt = ParseMemoryCreatedAt(request.MemoryCreatedAt);
            var existing = await _dbContext.MemoryRecords
                .SingleOrDefaultAsync(x => x.AgentId == agentId 
                                      && x.ExternalMemoryId == request.MemoryId, 
                                      cancellationToken);   // 查询同一个agent是否有具有相同id的Memory记录

            // 执行冲突检测，但如果skip了则不进行检测
            var conflicts = skipConflictCheck
                ? new List<MemoryConflictDetails>()
                : await DetectAndApplyConflictsAsync(
                    agentId,
                    request.MemoryId,
                    content,
                    contentHash,
                    source,
                    request.MemoryKey,
                    request.Embedding,
                    cancellationToken);

            // 如果当前数据库不存在请求体所携带的记忆id，则新增
            if (existing == null)
            {
                existing = new MemoryRecord
                {
                    AgentId = agentId,
                    ExternalMemoryId = request.MemoryId.Trim(),
                    MemoryCreatedAt = memoryCreatedAt ?? now,
                    CreatedAt = now
                };
                _dbContext.MemoryRecords.Add(existing);
            }

            // 写入最新业务值，并清除可能存在的软归档状态。
            existing.Content = content;
            existing.ContentHash = contentHash;
            existing.Source = source;
            existing.MemoryKey = NormalizeOptional(request.MemoryKey);
            existing.Confidence = Math.Clamp(request.Confidence, 0, 1);
            existing.EmbeddingJson = SerializeEmbedding(request.Embedding);
            existing.LastPositiveRef = lastPositiveRef;
            existing.UpdatedAt = now;
            existing.IsArchived = false;
            existing.ArchiveReason = null;
            if (memoryCreatedAt.HasValue)
            {
                existing.MemoryCreatedAt = memoryCreatedAt.Value;
            }

            QueueConflictSyncActions(agentId, conflicts, now);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BuildFailure<MemoryDetails>(requestId, "记忆已被其他请求修改，请重试。", "memory_concurrency_conflict");
            }

            return BuildSuccess(requestId, new MemoryDetails
            {
                MemoryId = existing.ExternalMemoryId,
                Confidence = existing.Confidence,
                Source = existing.Source,
                MemoryKey = existing.MemoryKey,
                LastPositiveRef = existing.LastPositiveRef,
                Action = existing.CreatedAt == existing.UpdatedAt ? "created" : "updated",
                Conflicts = conflicts,
                MemoryCreatedAt = existing.MemoryCreatedAt
            });
        }

        // 批量写入记忆；单条失败不会中断其余条目，返回每条记录的独立处理结果。
        public async Task<GuardAlgorithmResult<MemoryBulkDetails>> BulkPutAsync(
            long agentId,
            MemoryBulkPutRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var requestId = Guid.NewGuid().ToString("N");

            // 先对参数做校验，完成安全监测和数据normalize
            var prepareResult = await PrepareBulkRequestAsync(agentId, request);

            if (prepareResult.Error != null || prepareResult.Items == null)
            {
                return BuildFailure<MemoryBulkDetails>(
                    requestId,
                    prepareResult.Error ?? "批量记忆与处理失败！",
                    "invalid_memory_batch");
            }

            var preparedItems = prepareResult.Items;
            var now = DateTime.UtcNow;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // 一次性加载已有记录和冲突候选
                var state = await LoadBulkMemoryStateAsync(agentId, preparedItems, cancellationToken);

                var incomingMemoryIds = preparedItems
                    .Select(x => x.MemoryId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // 批次中的记录按照请求顺序逐步加入候选集 → 列表最后的新记忆可以覆盖或降权前面的旧记忆
                var workingCandidates = state.ConflictCandidates
                    .Where(x => !incomingMemoryIds.Contains(x.ExternalMemoryId))
                    .ToList();

                // 防止同一条旧记忆在一个批次中被重复降权
                var penalizedMemoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var processedItems = new List<(
                    PreparedMemoryItem Item,
                    MemoryRecord Entity,
                    string Action,
                    List<MemoryConflictDetails> Conflicts)>();

                foreach (var item in preparedItems.OrderBy(x => x.Index))
                {
                    var existed = state.ExistingByMemoryId.TryGetValue(item.MemoryId, out var entity);

                    if (entity == null)
                    {
                        entity = new MemoryRecord
                        {
                            AgentId = agentId,
                            ExternalMemoryId = item.MemoryId,
                            MemoryCreatedAt = item.MemoryCreatedAt ?? now,
                            CreatedAt = now
                        };

                        _dbContext.MemoryRecords.Add(entity);
                    }

                    var conflicts = request.SkipConflictCheck
                        ? new List<MemoryConflictDetails>()
                        : DetectAndApplyBulkConflicts(
                            item,
                            workingCandidates,
                            penalizedMemoryIds,
                            now);

                    entity.Content = item.Content;
                    entity.ContentHash = item.ContentHash;
                    entity.Source = item.Source;
                    entity.MemoryKey = item.MemoryKey;
                    entity.Confidence = item.Confidence;
                    entity.EmbeddingJson = item.EmbeddingJson;
                    entity.LastPositiveRef = item.LastPositiveRef;
                    entity.UpdatedAt = now;
                    entity.IsArchived = false;
                    entity.ArchiveReason = null;
                    if (item.MemoryCreatedAt.HasValue)
                    {
                        entity.MemoryCreatedAt = item.MemoryCreatedAt.Value;
                    }

                    // 当前记忆加入工作候选集，供批次中后面的记忆进行冲突检测。
                    workingCandidates.Add(entity);

                    processedItems.Add((
                        item,
                        entity,
                        existed ? "updated" : "created",
                        conflicts));
                }

                var conflictSyncActions = processedItems
                    .SelectMany(x => x.Conflicts)
                    .Where(x => x.Action is "demoted" or "archived")
                    .GroupBy(
                        x => x.MemoryId,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Last())
                    .ToList();

                QueueConflictSyncActions(
                    agentId,
                    conflictSyncActions,
                    now);

                // 整个批次仅提交一次数据库。
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var details = new MemoryBulkDetails
                {
                    Total = processedItems.Count,
                    Succeeded = processedItems.Count,
                    Failed = 0,
                    Conflicts = processedItems.Sum(x => x.Conflicts.Count)
                };

                // 在全部冲突处理完成后构造结果，确保返回最终置信度。
                foreach (var processed in processedItems)
                {
                    details.Items.Add(BuildSuccess(
                        Guid.NewGuid().ToString("N"),
                        new MemoryDetails
                        {
                            MemoryId = processed.Entity.ExternalMemoryId,
                            Confidence = processed.Entity.Confidence,
                            Source = processed.Entity.Source,
                            MemoryKey = processed.Entity.MemoryKey,
                            LastPositiveRef = processed.Entity.LastPositiveRef,
                            Action = processed.Action,
                            Conflicts = processed.Conflicts,
                            MemoryCreatedAt = processed.Entity.MemoryCreatedAt,
                        }));
                }

                return BuildSuccess(requestId, details);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                return BuildFailure<MemoryBulkDetails>(
                    requestId,
                    "部分记忆已被其他请求修改，请重新获取数据后再试。",
                    "memory_concurrency_conflict");
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                return BuildFailure<MemoryBulkDetails>(
                    requestId,
                    "批量记忆写入数据库失败，整个批次均未保存。",
                    "memory_batch_save_failed");
            }
        }

        // 按创建顺序拉取当前 Agent 的待同步动作。
        public async Task<List<MemorySyncActionItem>> GetPendingSyncActionsAsync(
            long agentId,
            MemorySyncActionQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.MemorySyncActions
                .AsNoTracking()
                .Where(x =>
                    x.AgentId == agentId
                    && x.Status == MemorySyncActionStatus.Pending)
                .OrderBy(x => x.Id)
                .Take(request.Limit)
                .Select(x => new MemorySyncActionItem
                {
                    ActionId = x.ActionId,
                    MemoryId = x.ExternalMemoryId,
                    ActionType = x.ActionType,
                    NewConfidence = x.NewConfidence,
                    Reason = x.Reason,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);
        }

        // 将同步动作标记为已确认；重复确认保持幂等。
        public async Task<GuardAlgorithmResult<MemorySyncActionResult>> ConfirmSyncActionAsync(
                long agentId,
                Guid actionId,
                CancellationToken cancellationToken = default
        )
        {
            var requestId = Guid.NewGuid().ToString("N");

            var action = await _dbContext.MemorySyncActions
                .SingleOrDefaultAsync(
                    x => x.AgentId == agentId
                         && x.ActionId == actionId,
                    cancellationToken);

            if (action == null)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "同步动作不存在。",
                    "sync_action_not_found");
            }

            // 已确认时直接返回成功，保证调用方可以安全重试。
            if (action.Status == MemorySyncActionStatus.Confirmed)
            {
                return BuildSuccess(
                    requestId,
                    new MemorySyncActionResult
                    {
                        ActionId = action.ActionId,
                        Status = action.Status,
                        UpdatedAt = action.ConfirmedAt ?? action.CreatedAt
                    },
                    "already_confirmed");
            }

            var now = DateTime.UtcNow;

            action.Status = MemorySyncActionStatus.Confirmed;
            action.ConfirmedAt = now;
            action.FailedAt = null;
            action.FailureMessage = null;


            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "同步动作已被其他请求修改，请重试。",
                    "sync_action_concurrency_conflict");
            }

            return BuildSuccess(
                requestId,
                new MemorySyncActionResult
                {
                    ActionId = action.ActionId,
                    Status = action.Status,
                    UpdatedAt = now
                });
        }

        // 将同步动作标记为失败，并保存调用方报告的错误原因。
        public async Task<GuardAlgorithmResult<MemorySyncActionResult>> FailSyncActionAsync(
                long agentId,
                Guid actionId,
                MemorySyncFailureRequest request,
                CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid().ToString("N");

            var action = await _dbContext.MemorySyncActions
                .SingleOrDefaultAsync(
                    x => x.AgentId == agentId
                         && x.ActionId == actionId,
                    cancellationToken);

            if (action == null)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "同步动作不存在。",
                    "sync_action_not_found");
            }

            // 已确认的动作不能再回退为失败。
            if (action.Status == MemorySyncActionStatus.Confirmed)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "已确认的同步动作不能标记为失败。",
                    "sync_action_already_confirmed");
            }

            if (action.Status == MemorySyncActionStatus.Failed
                && string.Equals(
                    action.FailureMessage,
                    request.FailureMessage.Trim(),
                    StringComparison.Ordinal))
            {
                return BuildSuccess(
                    requestId,
                    new MemorySyncActionResult
                    {
                        ActionId = action.ActionId,
                        Status = action.Status,
                        UpdatedAt = action.FailedAt ?? action.CreatedAt
                    },
                    "already_failed");
            }

            var now = DateTime.UtcNow;

            action.Status = MemorySyncActionStatus.Failed;
            action.ConfirmedAt = null;
            action.FailedAt = now;
            action.FailureMessage = request.FailureMessage.Trim();

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "同步动作已被其他请求修改，请重试。",
                    "sync_action_concurrency_conflict");
            }

            return BuildSuccess(
                requestId,
                new MemorySyncActionResult
                {
                    ActionId = action.ActionId,
                    Status = action.Status,
                    UpdatedAt = now
                });
        }

        // 将失败的同步动作重新置为待处理状态。
        public async Task<GuardAlgorithmResult<MemorySyncActionResult>>
            RetrySyncActionAsync(
                long agentId,
                Guid actionId,
                CancellationToken cancellationToken = default)
        {
            // 生成唯一请求 ID 用于日志追踪
            var requestId = Guid.NewGuid().ToString("N");

            // 根据 AgentId 和 ActionId 从数据库查询同步动作记录
            var action = await _dbContext.MemorySyncActions
                .SingleOrDefaultAsync(
                    x => x.AgentId == agentId
                         && x.ActionId == actionId,
                    cancellationToken);

            // 如果动作不存在，返回失败
            if (action == null)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "同步动作不存在。",
                    "sync_action_not_found");
            }

            // 已确认的动作无需重试，直接返回失败
            if (action.Status == MemorySyncActionStatus.Confirmed)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "已确认的同步动作不需要重试。",
                    "sync_action_already_confirmed");
            }

            // 如果动作已经是待处理状态，直接返回成功（幂等性）
            if (action.Status == MemorySyncActionStatus.Pending)
            {
                return BuildSuccess(
                    requestId,
                    new MemorySyncActionResult
                    {
                        ActionId = action.ActionId,
                        Status = action.Status,
                        UpdatedAt = action.CreatedAt
                    },
                    "already_pending");
            }

            // 将动作状态置为待处理，清空失败信息和失败时间
            action.Status = MemorySyncActionStatus.Pending;
            action.FailedAt = null;
            action.FailureMessage = null;

            // 保存变更，处理可能的并发冲突
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BuildFailure<MemorySyncActionResult>(
                    requestId,
                    "同步动作已被其他请求修改，请重试。",
                    "sync_action_concurrency_conflict");
            }

            // 返回成功结果，包含更新后的状态和当前时间
            return BuildSuccess(
                requestId,
                new MemorySyncActionResult
                {
                    ActionId = action.ActionId,
                    Status = action.Status,
                    UpdatedAt = DateTime.UtcNow
                });
        }

        // 查询指定记忆的元数据；为避免隐私扩散，接口不返回记忆正文。
        public async Task<GuardAlgorithmResult<MemoryDetails>> GetAsync(
            long agentId,
            string memoryId,
            CancellationToken cancellationToken = default
        )
        {
            var requestId = Guid.NewGuid().ToString("N");
            var memory = await _dbContext.MemoryRecords
                .AsNoTracking()
                .SingleOrDefaultAsync
                (
                    x => 
                    x.AgentId == agentId
                    && x.ExternalMemoryId == memoryId
                    && !x.IsArchived,
                    cancellationToken
                );

            if (memory == null)
            {
                return BuildFailure<MemoryDetails>(requestId, "记忆条目不存在。", "memory_not_found");
            }

            return BuildSuccess(requestId, new MemoryDetails
            {
                MemoryId = memory.ExternalMemoryId,
                Confidence = memory.Confidence,
                Source = memory.Source,
                MemoryKey = memory.MemoryKey,
                LastPositiveRef = memory.LastPositiveRef,
                Action = "read",
                MemoryCreatedAt = memory.MemoryCreatedAt
            });
        }

        // 软删除指定记忆，保留审计所需的状态和归档原因。
        public async Task<GuardAlgorithmResult<MemoryDetails>> DeleteAsync(
            long agentId,
            string memoryId,
            CancellationToken cancellationToken = default
        )
        {
            var requestId = Guid.NewGuid().ToString("N");
            var memory = await _dbContext.MemoryRecords
                .SingleOrDefaultAsync
                (
                    x => 
                    x.AgentId == agentId
                    && x.ExternalMemoryId == memoryId
                    && !x.IsArchived,
                    cancellationToken
                );

            if (memory == null)
            {
                return BuildFailure<MemoryDetails>(requestId, "记忆条目不存在。", "memory_not_found");
            }

            var now = DateTime.UtcNow;

            Archive(memory, "manual_delete", now);

            QueueSyncAction(
                agentId,
                memory.ExternalMemoryId,
                MemorySyncActionType.Archive,
                memory.Confidence,
                "manual_delete",
                now);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return BuildSuccess(requestId, ToDetails(memory, "archived"));
        }

        // 更新指定记忆的最后正向引用时间，不需要上传记忆正文
        public async Task<GuardAlgorithmResult<MemoryDetails>> UpdateReferenceAsync(
            long agentId,
            string memoryId,
            MemoryReferenceRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var requestId = Guid.NewGuid().ToString("N");

            if (string.IsNullOrEmpty(memoryId))
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    "记忆Id不能为空！",
                    "missing_memory_id");
            }

            var parseResult = ParseReferenceTime(request.ReferencedAt);

            if (parseResult.Error != null || !parseResult.ReferencedAt.HasValue)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    parseResult.Error ?? "正向引用时间无效！",
                    "invalid_reference_time");
            }

            var memory = await _dbContext.MemoryRecords
                .SingleOrDefaultAsync(
                x => x.AgentId == agentId && x.ExternalMemoryId == memoryId && !x.IsArchived, cancellationToken);

            if (memory == null)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    parseResult.Error ?? "该条记忆不存在！",
                    "memory_not_found");
            }

            var referencedAt = parseResult.ReferencedAt.Value;

            if (referencedAt < memory.MemoryCreatedAt)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    "正向引用时间不能早于记忆原始创建时间！",
                    "invalid_reference_time");
            }

            // 防止迟到或重复的同步请求把最后引用时间回退
            if (memory.LastPositiveRef.HasValue && referencedAt <= memory.LastPositiveRef.Value)
            {
                return BuildSuccess(requestId, ToDetails(memory, "reference_unchanged"));
            }

            memory.LastPositiveRef = referencedAt;
            memory.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    "记忆已被其他请求修改，请重试。",
                    "memory_concurrency_conflict");
            }

            return BuildSuccess(
                requestId,
                ToDetails(memory, "referenced"));
        }

        // 分批衰减当前 Agent 的未归档记忆，一次查询并统一提交。
        public async Task<GuardAlgorithmResult<MemoryBatchDecayDetails>> BatchDecayAsync(
            long agentId,
            MemoryBatchDecayRequest request,
            CancellationToken cancellationToken = default
        )
        {
            // 生成唯一请求 ID，便于日志追踪和问题定位
            var requestId = Guid.NewGuid().ToString("N");
            // 限制批次大小，防止单次处理过多数据导致性能问题或超时
            var batchSize = Math.Clamp(request.BatchSize, 1, 500);

            // 解析请求中的时间参数，若格式无效则直接返回错误
            var parseResult = ParseUpdatedBefore(request.UpdatedBefore);
            if (parseResult.Error != null)
            {
                return BuildFailure<MemoryBatchDecayDetails>(
                    requestId,
                    parseResult.Error,
                    "invalid_updated_before");
            }

            // 构建基础查询：只针对该 Agent 且未归档的记忆
            var query = _dbContext.MemoryRecords
                .Where(x =>
                    x.AgentId == agentId
                    && !x.IsArchived);

            // 如果指定了截止时间，则只处理更新时间早于该值的记忆
            if (parseResult.UpdatedBefore.HasValue)
            {
                var updatedBefore = parseResult.UpdatedBefore.Value;
                query = query.Where(x => x.UpdatedAt < updatedBefore);
            }

            // 执行查询并排序：
            // 1. 优先处理从未衰减过的记忆（LastDecayAt 为 null）
            // 2. 然后按 LastDecayAt 升序（最久未衰减的优先）
            // 3. 最后按创建时间升序（较旧的优先）
            var memories = await query
                .OrderBy(x => x.LastDecayAt.HasValue)
                .ThenBy(x => x.LastDecayAt)
                .ThenBy(x => x.MemoryCreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var details = new MemoryBatchDecayDetails
            {
                Processed = memories.Count
            };

            // 逐条执行衰减逻辑，统计衰减和归档数量
            foreach (var memory in memories)
            {
                var calculation = ApplyDecayToMemory(memory, now);

                var confidenceChanged =
                    Math.Abs(calculation.NewConfidence - calculation.OldConfidence) > 0.000001;

                if (calculation.Action == "archived" || confidenceChanged)
                {
                    QueueSyncAction(
                        agentId,
                        memory.ExternalMemoryId,
                        calculation.Action == "archived"
                            ? MemorySyncActionType.Archive
                            : MemorySyncActionType.UpdateConfidence,
                        memory.Confidence,
                        memory.ArchiveReason,
                        now);
                }

                if (calculation.Action == "archived")
                {
                    details.Archived++;
                }
                else
                {
                    details.Decayed++;
                }

                details.Items.Add(ToDetails(memory, calculation.Action));
            }

            // 保存所有更改，处理并发冲突（如其他任务同时修改了相同记录）
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // 发生并发冲突时，清除上下文追踪，并返回错误提示重试
                _dbContext.ChangeTracker.Clear();
                return BuildFailure<MemoryBatchDecayDetails>(
                    requestId,
                    "部分记忆已被其他衰减任务修改，请重新执行。",
                    "memory_concurrency_conflict");
            }

            // 返回成功结果，包含处理统计信息
            return BuildSuccess(requestId, details);
        }

        // 分页查询当前 Agent 的记忆安全索引，不返回正文、哈希或向量。
        public async Task<PagedResult<MemoryListItem>> SearchAsync(
            long agentId,
            MemorySearchRequest request,
            CancellationToken cancellationToken = default
        )
        {
            // 规范化分页参数：页码最小为1，页大小限制在1~100之间（校验在模型验证阶段完成）
            var pageIndex = request.PageIndex;
            var pageSize = request.PageSize;

            // 构建基础查询：只查指定 Agent，使用 AsNoTracking 提升只读性能
            var query = _dbContext.MemoryRecords
                .AsNoTracking()
                .Where(x => x.AgentId == agentId);

            // 关键词搜索：匹配 ExternalMemoryId 或 MemoryKey（忽略空值）
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                var keyword = request.Keyword.Trim();

                query = query.Where(x =>
                    x.ExternalMemoryId.Contains(keyword)
                    || (x.MemoryKey != null && x.MemoryKey.Contains(keyword)));
            }

            // 按来源过滤（精确匹配枚举值）
            if (request.Source.HasValue)
            {
                query = query.Where(x => x.Source == request.Source.Value);
            }

            // 按归档状态过滤
            if (request.IsArchived.HasValue)
            {
                query = query.Where(x => x.IsArchived == request.IsArchived.Value);
            }

            // 置信度区间过滤：下限（>=）
            if (request.MinConfidence.HasValue)
            {
                query = query.Where(x => x.Confidence >= request.MinConfidence.Value);
            }

            // 置信度区间过滤：上限（<=）
            if (request.MaxConfidence.HasValue)
            {
                query = query.Where(x => x.Confidence <= request.MaxConfidence.Value);
            }

            // 按归档原因精确匹配（忽略空字符串）
            if (!string.IsNullOrWhiteSpace(request.ArchiveReason))
            {
                var archiveReason = request.ArchiveReason.Trim();

                query = query.Where(x =>
                    x.ArchiveReason == archiveReason);
            }

            // 最近正面引用时间范围：起始（>=）
            if (request.LastPositiveRefFrom.HasValue)
            {
                var from = request.LastPositiveRefFrom.Value;

                query = query.Where(x =>
                    x.LastPositiveRef.HasValue
                    && x.LastPositiveRef.Value >= from);
            }

            // 最近正面引用时间范围：截止（<=）
            if (request.LastPositiveRefTo.HasValue)
            {
                var to = request.LastPositiveRefTo.Value;

                query = query.Where(x =>
                    x.LastPositiveRef.HasValue
                    && x.LastPositiveRef.Value <= to);
            }

            // 先获取总记录数（过滤条件已应用）
            var total = await query.CountAsync(cancellationToken);

            // 动态排序：根据 SortBy 字段和 Descending 标志选择排序方式
            query = (request.SortBy ?? "updatedAt").Trim().ToLowerInvariant() switch
            {
                "confidence" => request.Descending
                    ? query.OrderByDescending(x => x.Confidence)
                        .ThenBy(x => x.ExternalMemoryId)   // 辅助排序保证结果稳定
                    : query.OrderBy(x => x.Confidence)
                        .ThenBy(x => x.ExternalMemoryId),

                "createdat" => request.Descending
                    ? query.OrderByDescending(x => x.MemoryCreatedAt)
                        .ThenBy(x => x.ExternalMemoryId)
                    : query.OrderBy(x => x.MemoryCreatedAt)
                        .ThenBy(x => x.ExternalMemoryId),

                "lastpositiveref" => request.Descending
                    ? query.OrderByDescending(x => x.LastPositiveRef)
                        .ThenBy(x => x.ExternalMemoryId)
                    : query.OrderBy(x => x.LastPositiveRef)
                        .ThenBy(x => x.ExternalMemoryId),

                // 默认按 UpdatedAt 排序（降序或升序）
                _ => request.Descending
                    ? query.OrderByDescending(x => x.UpdatedAt)
                        .ThenBy(x => x.ExternalMemoryId)
                    : query.OrderBy(x => x.UpdatedAt)
                        .ThenBy(x => x.ExternalMemoryId)
            };

            // 执行分页查询，只选择索引字段，不包含正文、哈希、向量
            var items = await query
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new MemoryListItem
                {
                    MemoryId = x.ExternalMemoryId,
                    Source = x.Source,
                    MemoryKey = x.MemoryKey,
                    Confidence = x.Confidence,
                    MemoryCreatedAt = x.MemoryCreatedAt,
                    LastPositiveRef = x.LastPositiveRef,
                    LastDecayAt = x.LastDecayAt,
                    IsArchived = x.IsArchived,
                    ArchiveReason = x.ArchiveReason,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            // 返回分页结果，包含数据项和总数信息
            return new PagedResult<MemoryListItem>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        // 恢复当前 Agent 已归档的记忆，并将恢复操作视为一次正向引用。
        public async Task<GuardAlgorithmResult<MemoryDetails>> RestoreAsync(
            long agentId,
            string memoryId,
            MemoryRestoreRequest request,
            CancellationToken cancellationToken = default)
        {
            // 生成请求 ID 用于追踪
            var requestId = Guid.NewGuid().ToString("N");

            // 校验记忆 ID 不能为空
            if (string.IsNullOrWhiteSpace(memoryId))
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    "记忆 ID 不能为空。",
                    "missing_memory_id");
            }

            // 解析恢复引用时间（支持 ISO 8601 等格式）
            var parseResult = ParseReferenceTime(request.ReferencedAt);

            if (parseResult.Error != null || !parseResult.ReferencedAt.HasValue)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    parseResult.Error ?? "恢复确认时间无效。",
                    "invalid_reference_time");
            }

            // 从数据库中查找指定 Agent 下已归档且 ExternalMemoryId 匹配的记忆条目
            var memory = await _dbContext.MemoryRecords
                .SingleOrDefaultAsync(
                    x => x.AgentId == agentId
                         && x.ExternalMemoryId == memoryId
                         && x.IsArchived,
                    cancellationToken);

            // 如果没找到对应的已归档记忆，返回失败
            if (memory == null)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    "已归档的记忆条目不存在。",
                    "archived_memory_not_found");
            }

            var referencedAt = parseResult.ReferencedAt.Value;

            // 恢复引用时间不能早于记忆的原始创建时间，否则无意义
            if (referencedAt < memory.MemoryCreatedAt)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    "恢复确认时间不能早于记忆原始创建时间。",
                    "invalid_reference_time");
            }

            // 从配置中读取遗忘阈值，默认 0.1，并限制在 0~1 之间
            var forgetThreshold = Math.Clamp(
                _configuration.GetValue(
                    "GuardAlgorithms:Memory:ForgetThreshold",
                    0.1d),
                0,
                1);

            // 使用请求中指定的置信度，若未提供则保留原有置信度
            var restoredConfidence =
                request.Confidence ?? memory.Confidence;

            // 低于阈值的置信度即使恢复也会立刻再次被遗忘，因此此处拦截
            if (restoredConfidence < forgetThreshold)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    $"恢复后的置信度不能低于遗忘阈值 {forgetThreshold}。",
                    "restore_confidence_too_low");
            }

            var now = DateTime.UtcNow;

            // 更新记忆属性：置信度、最近正向引用时间、取消归档、清空归档原因、更新时间
            memory.Confidence = restoredConfidence;
            memory.LastPositiveRef = referencedAt;
            memory.IsArchived = false;
            memory.ArchiveReason = null;
            memory.UpdatedAt = now;

            // 保存更改，处理并发冲突
            try
            {
                QueueSyncAction(
                    agentId,
                    memory.ExternalMemoryId,
                    MemorySyncActionType.Restore,
                    memory.Confidence,
                    "manual_restore",
                    now
                );
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BuildFailure<MemoryDetails>(
                    requestId,
                    "记忆已被其他请求修改，请重试。",
                    "memory_concurrency_conflict");
            }

            // 返回成功结果，包含更新后的记忆详情
            return BuildSuccess(
                requestId,
                ToDetails(memory, "restored"));
        }

        // 按半衰期模型衰减记忆置信度，低于阈值时自动软归档。
        public async Task<GuardAlgorithmResult<MemoryDetails>> ApplyDecayAsync(
            long agentId,
            MemoryDecayRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var requestId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(request.MemoryId))
            {
                return BuildFailure<MemoryDetails>(requestId, "记忆 ID 不能为空。", "missing_memory_id");
            }

            var memory = await _dbContext.MemoryRecords
                .SingleOrDefaultAsync
                (
                    x => 
                    x.AgentId == agentId
                    && x.ExternalMemoryId == request.MemoryId
                    && !x.IsArchived,
                    cancellationToken
                );

            if (memory == null)
            {
                return BuildFailure<MemoryDetails>(requestId, "记忆条目不存在。", "memory_not_found");
            }

            var now = DateTime.UtcNow;
            var decayResult = ApplyDecayToMemory(memory, now);

            var confidenceChanged =
                Math.Abs(decayResult.NewConfidence - decayResult.OldConfidence) > 0.000001;

            if (decayResult.Action == "archived" || confidenceChanged)
            {
                QueueSyncAction(
                    agentId,
                    memory.ExternalMemoryId,
                    decayResult.Action == "archived"
                        ? MemorySyncActionType.Archive
                        : MemorySyncActionType.UpdateConfidence,
                    memory.Confidence,
                    memory.ArchiveReason,
                    now);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BuildFailure<MemoryDetails>(requestId, "记忆已被其他衰减任务修改，请重试。", "memory_concurrency_conflict");
            }

            return BuildSuccess(
                requestId,
                ToDetails(memory, decayResult.Action),
                decayResult.Action == "archived" ? "archived" : "ok");
        }

        // 检测确定冲突和潜在相似项；只有相同业务槽位才自动降权，避免误伤同主题补充信息。
        private async Task<List<MemoryConflictDetails>> DetectAndApplyConflictsAsync(
            long agentId,
            string memoryId,
            string content,
            string contentHash,
            MemorySource source,
            string? memoryKey,
            IReadOnlyList<float>? embedding,
            CancellationToken cancellationToken
        )
        {
            // 暂时注释化，避免伪造请求，跳过冲突检测产生记忆投毒的问题。后续加入鉴权字段——MemoryTrustLevel
            //if (source is MemorySource.System or MemorySource.Admin)
            //{
            //    return new List<MemoryConflictDetails>();   // 如果记忆来源为"system"或"admin"，直接返回一个冲突集合，不进行冲突检测的逻辑业务
            //}

            // 查询候选片段
            var candidates = await _dbContext.MemoryRecords
                .Where(x =>
                    x.AgentId == agentId
                    && !x.IsArchived
                    && x.ExternalMemoryId != memoryId
                    && x.Source == source)
                .ToListAsync(cancellationToken);

            var results = new List<MemoryConflictDetails>();    // 创建最终冲突内容集合
            var normalizedKey = NormalizeOptional(memoryKey);
            var newTokens = GuardTextAlgorithmHelper.Tokenize(content);    // 对于记忆内容做分词，返回去重后的token集合

            // 从环境变量json中读取相应参数，如果没有进行配置则使用默认编码值（编码值与开发环境json相同）
            var penaltyWeight = Math.Clamp(  // 冲突后就置信度减少 60%
                _configuration.GetValue("GuardAlgorithms:Memory:ConflictPenaltyWeight", 0.6d),    
                0,
                1);

            var forgetThreshold = Math.Clamp(  // 置信度低于0.1时对记忆执行归档处理
                _configuration.GetValue("GuardAlgorithms:Memory:ForgetThreshold", 0.1d),       
                0,
                1);

            var candidateThreshold = Math.Clamp(  // 当相似度达到0.75时标记为潜在相似项
                _configuration.GetValue("GuardAlgorithms:Memory:SimilarityCandidateThreshold", 0.75d), 
                0,
                1);

            // 遍历检索出的记忆记录，逐条与传入记忆做相似度检测
            foreach (var candidate in candidates)
            {
                if (candidate.ContentHash == contentHash)
                {
                    continue;   // 如果内容哈希相同则直接跳过
                }

                // 对记忆记录的向量切割json串做反序列化
                var oldEmbedding = DeserializeEmbedding(candidate.EmbeddingJson);

                // 计算检测记忆与记忆记录间的向量余弦相似度
                var vectorSimilarity = GuardTextAlgorithmHelper.CosineSimilarity(embedding, oldEmbedding);

                // 计算检测记忆与记忆记录间的token Jaccard相似度
                var lexicalSimilarity = GuardTextAlgorithmHelper.Jaccard(
                    newTokens,
                    GuardTextAlgorithmHelper.Tokenize(candidate.Content));

                // 最终相似度结果取向量余弦相似度和token Jaccard相似度的最大值
                var similarity = Math.Max(vectorSimilarity ?? 0, lexicalSimilarity);

                // 判断业务槽位是否相同，MemoryKey描述的是检测记忆的业务属性
                var sameKey = normalizedKey != null
                    && string.Equals(candidate.MemoryKey, normalizedKey, StringComparison.OrdinalIgnoreCase);

                if (sameKey)
                {
                    // 同一业务槽位出现新值时，旧值才被视为确定冲突并自动降权。
                    candidate.Confidence = Math.Clamp(candidate.Confidence * (1 - penaltyWeight), 0, 1);
                    candidate.UpdatedAt = DateTime.UtcNow;
                    var action = "demoted";

                    // 如果记忆记录置信度低于最低阈值，则对该记忆记录执行归档
                    if (candidate.Confidence < forgetThreshold)  
                    {
                        Archive(candidate, "slot_conflict", DateTime.UtcNow);
                        action = "archived";
                    }

                    results.Add(new MemoryConflictDetails
                    {
                        MemoryId = candidate.ExternalMemoryId,
                        Similarity = similarity,
                        Reason = "same_memory_key",
                        Action = action,
                        NewConfidence = candidate.Confidence
                    });
                }
                else if (similarity >= candidateThreshold)
                {
                    // 只有相似度而没有业务槽位证据时仅提示候选，不直接修改旧记忆。 todo:此时进入人工审核阶段
                    results.Add(new MemoryConflictDetails
                    {
                        MemoryId = candidate.ExternalMemoryId,
                        Similarity = similarity,
                        Reason = "similar_content_candidate",
                        Action = "candidate",
                        NewConfidence = candidate.Confidence
                    });
                }
            }

            return results;
        }

        // 对预加载到内存的记忆执行批量冲突检测，不访问或提交数据库。
        private List<MemoryConflictDetails> DetectAndApplyBulkConflicts(
            PreparedMemoryItem item,
            IEnumerable<MemoryRecord> candidates,
            ISet<string> penalizedMemoryIds,
            DateTime now)
        {
            var results = new List<MemoryConflictDetails>();
            var newTokens = GuardTextAlgorithmHelper.Tokenize(item.Content);

            // 从环境变量中读取相应参数
            var penaltyWeight = Math.Clamp(  // 冲突后记忆记录置信度减少0.6
                _configuration.GetValue(
                    "GuardAlgorithms:Memory:ConflictPenaltyWeight",
                    0.6d),
                0,
                1);

            var forgetThreshold = Math.Clamp(  // 置信度低于0.1时对该条记忆记录做归档处理
                _configuration.GetValue(
                    "GuardAlgorithms:Memory:ForgetThreshold",
                    0.1d),
                0,
                1);

            var candidateThreshold = Math.Clamp(  // 当检测记忆与记忆记录相似度达到0.75时标记为潜在相似项
                _configuration.GetValue(
                    "GuardAlgorithms:Memory:SimilarityCandidateThreshold",
                    0.75d),
                0,
                1);

            foreach (var candidate in candidates)
            {
                // 只比较同 Agent 预加载结果中的同来源、未归档记忆。
                if (candidate.IsArchived
                    || candidate.Source != item.Source
                    || string.Equals(
                        candidate.ExternalMemoryId,
                        item.MemoryId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 完全相同的正文不属于冲突。
                if (candidate.ContentHash == item.ContentHash)
                {
                    continue;
                }

                var oldEmbedding = DeserializeEmbedding(candidate.EmbeddingJson);

                var vectorSimilarity = GuardTextAlgorithmHelper.CosineSimilarity(
                    item.Embedding,
                    oldEmbedding);

                var lexicalSimilarity = GuardTextAlgorithmHelper.Jaccard(
                    newTokens,
                    GuardTextAlgorithmHelper.Tokenize(candidate.Content));

                var similarity = Math.Max(
                    vectorSimilarity ?? 0,
                    lexicalSimilarity);

                var sameKey = item.MemoryKey != null
                    && string.Equals(
                        candidate.MemoryKey,
                        item.MemoryKey,
                        StringComparison.OrdinalIgnoreCase);

                if (sameKey)
                {
                    // 同一批次中，同一条旧记忆最多自动降权一次。
                    if (!penalizedMemoryIds.Add(candidate.ExternalMemoryId))
                    {
                        continue;
                    }

                    candidate.Confidence = Math.Clamp(
                        candidate.Confidence * (1 - penaltyWeight),
                        0,
                        1);

                    candidate.UpdatedAt = now;

                    var action = "demoted";

                    if (candidate.Confidence < forgetThreshold)
                    {
                        Archive(candidate, "slot_conflict", now);
                        action = "archived";
                    }

                    results.Add(new MemoryConflictDetails
                    {
                        MemoryId = candidate.ExternalMemoryId,
                        Similarity = similarity,
                        Reason = "same_memory_key",
                        Action = action,
                        NewConfidence = candidate.Confidence
                    });

                    continue;
                }

                if (similarity >= candidateThreshold)
                {
                    results.Add(new MemoryConflictDetails
                    {
                        MemoryId = candidate.ExternalMemoryId,
                        Similarity = similarity,
                        Reason = "similar_content_candidate",
                        Action = "candidate",
                        NewConfidence = candidate.Confidence
                    });
                }
            }

            return results;
        }

        // 对已加载的记忆执行衰减计算，不查询或提交数据库。
        private DecayCalculationResult ApplyDecayToMemory(
            MemoryRecord memory,
            DateTime now
        )
        {
            var oldConfidence = memory.Confidence;
            var decayStart = GetDecayStart(memory);
            var elapsedDays = Math.Max(0, (now - decayStart).TotalDays);

            var halfLifeDays = Math.Max(
                0.01,
                _configuration.GetValue(
                    "GuardAlgorithms:Memory:HalfLifeDays",
                    30d));

            var forgetThreshold = Math.Clamp(
                _configuration.GetValue(
                    "GuardAlgorithms:Memory:ForgetThreshold",
                    0.1d),
                0,
                1);

            var decayFactor = Math.Pow(
                0.5,
                elapsedDays / halfLifeDays);

            memory.Confidence = Math.Clamp(
                memory.Confidence * decayFactor,
                0,
                1);

            memory.LastDecayAt = now;
            memory.UpdatedAt = now;

            var action = "decayed";

            if (memory.Confidence < forgetThreshold)
            {
                Archive(memory, "low_confidence", now);
                action = "archived";
            }

            return new DecayCalculationResult
            {
                OldConfidence = oldConfidence,
                NewConfidence = memory.Confidence,
                Action = action
            };
        }

        // 将记忆变更加入同步动作表；由外层 SaveChanges 和业务修改一起提交。
        private void QueueSyncAction(
            long agentId,
            string memoryId,
            MemorySyncActionType actionType,
            double? newConfidence,
            string? reason,
            DateTime now)
        {
            _dbContext.MemorySyncActions.Add(new MemorySyncAction
            {
                ActionId = Guid.NewGuid(),
                AgentId = agentId,
                ExternalMemoryId = memoryId,
                ActionType = actionType,
                NewConfidence = newConfidence,
                Reason = reason,
                Status = MemorySyncActionStatus.Pending,
                CreatedAt = now
            });
        }

        // 根据冲突处理结果创建同步动作。
        private void QueueConflictSyncActions(
            long agentId,
            IEnumerable<MemoryConflictDetails> conflicts,
            DateTime now)
        {
            foreach (var conflict in conflicts)
            {
                switch (conflict.Action)
                {
                    case "demoted":
                        QueueSyncAction(
                            agentId,
                            conflict.MemoryId,
                            MemorySyncActionType.UpdateConfidence,
                            conflict.NewConfidence,
                            "slot_conflict",
                            now);
                        break;

                    case "archived":
                        QueueSyncAction(
                            agentId,
                            conflict.MemoryId,
                            MemorySyncActionType.Archive,
                            conflict.NewConfidence,
                            "slot_conflict",
                            now);
                        break;
                }
            }
        }

        // 预处理MemoryItem内存项
        private sealed class PreparedMemoryItem
        {
            // 该项在批处理或列表中的索引位置
            public int Index { get; set; }

            // 内存项的唯一标识符
            public string MemoryId { get; set; } = string.Empty;

            // 内存项的文本内容
            public string Content { get; set; } = string.Empty;

            // 内容的哈希值，用于去重或快速比较
            public string ContentHash { get; set; } = string.Empty;

            // 内存来源（例如用户输入、外部知识库、系统生成等）
            public MemorySource Source { get; set; }

            // 可选的键值，用于分组或检索（例如对话ID、文档路径）
            public string? MemoryKey { get; set; }

            // 相关性置信度（0~1），用于排序或筛选
            public double Confidence { get; set; }

            // 向量嵌入的JSON序列化字符串（用于持久化或传输）
            public string? EmbeddingJson { get; set; }

            // 向量嵌入的只读列表（实际浮点数值）
            public IReadOnlyList<float>? Embedding { get; set; }

            // 最近一次被正面引用或匹配的时间（用于热度/新鲜度计算）
            public DateTime? LastPositiveRef { get; set; }

            // 记忆在Agent业务中实际被创建的时间
            public DateTime? MemoryCreatedAt { get; set; }
        }

        // 批量预加载状态类
        private sealed class BulkMemoryState
        {
            // 批量请求中已经存在于数据库的记录
            public Dictionary<string, MemoryRecord> ExistingByMemoryId { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            // 当前 Agent 下参与冲突检测的未归档记忆
            public List<MemoryRecord> ConflictCandidates { get; init; } = new();
        }

        // 置信度衰减计算结果
        private sealed class DecayCalculationResult
        {
            public double OldConfidence { get; init; }
            public double NewConfidence { get; init; }
            public string Action { get; init; } = string.Empty;
        }

        // 校验单条记忆写入参数，返回空值表示校验通过。
        private static string? ValidatePutRequest(MemoryPutRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MemoryId))
            {
                return "记忆 ID 不能为空。";
            }

            if (request.MemoryId.Length > 128)
            {
                return "记忆 ID 长度不能超过 128。";
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return "记忆内容不能为空。";
            }

            if (request.Content.Length > 20000)
            {
                return "记忆内容长度不能超过 20000。";
            }

            if (request.Confidence is < 0 or > 1)
            {
                return "记忆置信度必须处于 0 到 1 之间。";
            }

            var maximumAllowedTime = DateTimeOffset.UtcNow.AddMinutes(5);

            DateTimeOffset? memoryCreatedAt = null;
            DateTimeOffset? lastPositiveRef = null;

            if (!string.IsNullOrWhiteSpace(request.MemoryCreatedAt))
            {
                if (!DateTimeOffset.TryParse(
                        request.MemoryCreatedAt,
                        out var parsedCreatedAt))
                {
                    return "记忆原始创建时间必须使用 ISO8601 格式。";
                }

                if (parsedCreatedAt > maximumAllowedTime)
                {
                    return "记忆原始创建时间不能晚于当前时间。";
                }

                memoryCreatedAt = parsedCreatedAt;
            }

            if (!string.IsNullOrWhiteSpace(request.LastPositiveRef))
            {
                if (!DateTimeOffset.TryParse(
                        request.LastPositiveRef,
                        out var parsedLastPositiveRef))
                {
                    return "最后正向引用时间必须使用 ISO8601 格式。";
                }

                if (parsedLastPositiveRef > maximumAllowedTime)
                {
                    return "最后正向引用时间不能晚于当前时间。";
                }

                lastPositiveRef = parsedLastPositiveRef;
            }

            if (memoryCreatedAt.HasValue
                && lastPositiveRef.HasValue
                && lastPositiveRef.Value < memoryCreatedAt.Value)
            {
                return "最后正向引用时间不能早于记忆原始创建时间。";
            }

            return null;
        }

        // 对批量的记忆写入请求做整体校验
        private static string? ValidateBulkRequest(MemoryBulkPutRequest request)
        {
            if (request.Memories == null || request.Memories.Count == 0)
            {
                return "待写入记忆列表不能为空！";
            }

            if (request.Memories.Count > 500)
            {
                return "批量写入一次不能超过500条记忆！";
            }

            var duplicatedMemoryId = request.Memories
                .Where(x => !string.IsNullOrWhiteSpace(x.MemoryId))
                .GroupBy
                (
                    x =>
                    x.MemoryId.Trim(),
                    StringComparer.OrdinalIgnoreCase
                )
                .FirstOrDefault(x => x.Count() > 1);

            if (duplicatedMemoryId != null)
            {
                return $"批量请求中存在重复记忆！ ID：{duplicatedMemoryId.Key}";
            }

            return null;
        }

        // 对单条进行预处理
        private async Task<(PreparedMemoryItem? Item, string? Error)> PrepareBulkItemAsync(
            long agentId,
            MemoryPutRequest request,
            int index
        )
        {
            var validationError = ValidatePutRequest( request );
            if (validationError != null)
            {
                return (null, validationError);
            }

            var securityResult = await CheckContentAsync(agentId, request.Content);
            if (!securityResult.Allowed)
            {
                return (null, securityResult.Reason);
            }

            var content = (securityResult.ProcessedContent ?? request.Content).Trim();

            return (new PreparedMemoryItem
            {
                Index = index,
                MemoryId = request.MemoryId.Trim(),
                Content = content,
                ContentHash = ComputeSha256(content),
                Source = request.Source,
                MemoryKey = NormalizeOptional(request.MemoryKey),
                Confidence = Math.Clamp(request.Confidence, 0, 1),
                Embedding = request.Embedding,
                EmbeddingJson = SerializeEmbedding(request.Embedding),
                LastPositiveRef = ParseLastPositiveRef(request.LastPositiveRef),
                MemoryCreatedAt = ParseMemoryCreatedAt(request.MemoryCreatedAt)
            }, null);
        }

        // 对整个批次做预处理
        private async Task<(List<PreparedMemoryItem>? Items, string? Error)> PrepareBulkRequestAsync(
            long agentId,
            MemoryBulkPutRequest request
        )
        {
            // 先执行整体请求的校验（如 Memories 不能为空、总量限制等）
            var validationError = ValidateBulkRequest(request);
            if (validationError != null)
            {
                return (null, validationError);
            }

            // 初始化准备项列表，预分配容量
            var preparedItems = new List<PreparedMemoryItem>(request.Memories.Count);

            // 遍历每条记忆，进行独立的准备和校验
            for (var index = 0; index < request.Memories.Count; index++)
            {
                var result = await PrepareBulkItemAsync(
                    agentId,
                    request.Memories[index],
                    index);

                // 如果某条记忆准备失败，立即返回错误，停止后续处理
                if (result.Error != null || result.Item == null)
                {
                    return (
                        null,
                        $"第 {index + 1} 条记忆校验失败：{result.Error ?? "未知错误"}");
                }

                preparedItems.Add(result.Item);
            }

            // 全部准备成功，返回列表
            return (preparedItems, null);
        }

        // 加载批量内存操作的当前状态（数据库预加载）
        private async Task<BulkMemoryState> LoadBulkMemoryStateAsync(
            long agentId,
            IReadOnlyCollection<PreparedMemoryItem> preparedItems,
            CancellationToken cancellationToken)
        {
            // 提取所有待处理项的 ExternalMemoryId（不区分大小写）
            var incomingMemoryIds = preparedItems
                .Select(x => x.MemoryId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 提取所有待处理项的 Source
            var incomingSources = preparedItems
                .Select(x => x.Source)
                .ToHashSet();

            // 一次性查询所有可能相关的记录：
            // 1. 匹配 ExternalMemoryId 的记录（无论是否归档）
            // 2. 未归档且 Source 匹配的记录（用于检测同来源的冲突）
            var records = await _dbContext.MemoryRecords
                .Where(x =>
                    x.AgentId == agentId
                    && (
                        incomingMemoryIds.Contains(x.ExternalMemoryId)
                        || (!x.IsArchived && incomingSources.Contains(x.Source))
                    ))
                .ToListAsync(cancellationToken);

            // 按 ExternalMemoryId 建立字典，便于快速查找精确匹配项
            var existingByMemoryId = records
                .Where(x => incomingMemoryIds.Contains(x.ExternalMemoryId))
                .ToDictionary(
                    x => x.ExternalMemoryId,
                    StringComparer.OrdinalIgnoreCase);

            // 所有未归档的记录作为冲突候选（用于检测同一 Source 下的重复或覆盖）
            var conflictCandidates = records
                .Where(x => !x.IsArchived)
                .ToList();

            // 返回封装后的状态对象
            return new BulkMemoryState
            {
                ExistingByMemoryId = existingByMemoryId,
                ConflictCandidates = conflictCandidates
            };
        }

        // 调用 AIShield 本体输入安全服务检查记忆正文。
        private Task<SecurityCheckResponse> CheckContentAsync(long agentId, string content)
        {
            return _inputSecurityService.CheckInputAsync(agentId, new SecurityCheckRequest
            {
                Content = content
            });
        }

        // 将 ISO8601 时间转换为 UTC；空字符串表示没有正向引用。
        private static DateTime? ParseLastPositiveRef(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.Parse(value).UtcDateTime;
        }

        // 计算本次衰减的起点，优先使用最近发生的正向引用或上次衰减时间。
        private static DateTime GetDecayStart(MemoryRecord memory)
        {
            // 已经执行过衰减时，从上次衰减时间继续，避免重复计算同一时间段。
            if (memory.LastDecayAt.HasValue)
            {
                var start = memory.LastDecayAt.Value;

                if (memory.LastPositiveRef.HasValue
                    && memory.LastPositiveRef.Value > start)
                {
                    start = memory.LastPositiveRef.Value;
                }

                return DateTime.SpecifyKind(start, DateTimeKind.Utc);
            }

            // 首次衰减从原始创建时间开始；最近引用时间更晚时从引用时间开始。
            var initialStart = memory.MemoryCreatedAt;

            if (memory.LastPositiveRef.HasValue
                && memory.LastPositiveRef.Value > initialStart)
            {
                initialStart = memory.LastPositiveRef.Value;
            }

            return DateTime.SpecifyKind(initialStart, DateTimeKind.Utc);
        }

        // 解析记忆的创建时间为 UTC DateTime
        private static DateTime? ParseMemoryCreatedAt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.Parse(value).UtcDateTime;
        }

        // 对更新时间进行解析
        private static (DateTime? UpdatedBefore, string? Error) ParseUpdatedBefore(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, null);
            }

            if (!DateTimeOffset.TryParse(value, out var parsed))
            {
                return (null, "更新时间筛选值必须使用 ISO8601 格式。");
            }

            var maximumAllowedTime = DateTimeOffset.UtcNow.AddMinutes(5);

            if (parsed > maximumAllowedTime)
            {
                return (null, "更新时间筛选值不能晚于当前时间。");
            }

            return (parsed.UtcDateTime, null);
        }

        // 将记忆标记为软归档并记录原因。
        private static void Archive(MemoryRecord memory, string reason, DateTime now)
        {
            memory.IsArchived = true;
            memory.ArchiveReason = reason;
            memory.UpdatedAt = now;
        }

        // 将数据库实体转换为对外返回的元数据 DTO。
        private static MemoryDetails ToDetails(MemoryRecord memory, string action)
        {
            return new MemoryDetails
            {
                MemoryId = memory.ExternalMemoryId,
                Confidence = memory.Confidence,
                Source = memory.Source,
                MemoryKey = memory.MemoryKey,
                LastPositiveRef = memory.LastPositiveRef,
                MemoryCreatedAt = memory.MemoryCreatedAt,
                Action = action
            };
        }

        // 计算文本 SHA256 哈希，用于判定完全重复内容。
        private static string ComputeSha256(string content)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        }

        // 序列化可选向量，空向量不写入数据库。
        private static string? SerializeEmbedding(IReadOnlyList<float>? embedding)
        {
            return embedding is { Count: > 0 } ? JsonSerializer.Serialize(embedding) : null;
        }

        // 反序列化数据库中的可选向量，格式错误时安全降级为空。
        private static List<float>? DeserializeEmbedding(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<List<float>>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // 归一化可选文本字段，空白值统一保存为空。
        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        // 构造算法成功结果，保持与原 Guard 微服务相近的响应结构。
        private static GuardAlgorithmResult<T> BuildSuccess<T>(string requestId, T details, string message = "ok")
        {
            return new GuardAlgorithmResult<T>
            {
                Success = true,
                Message = message,
                Data = new GuardAlgorithmData<T>
                {
                    RequestId = requestId,
                    Blocked = false,
                    Details = details
                }
            };
        }

        // 解析正向引用时间，如果为空则使用服务器当前时间
        private static (DateTime? ReferencedAt, string? Error) ParseReferenceTime(string? referenceTime)
        {
            var maxAllowedTime = DateTimeOffset.UtcNow.AddMinutes(5);

            if (string.IsNullOrWhiteSpace(referenceTime))
            {
                return (DateTime.UtcNow, null);
            }

            if (!DateTimeOffset.TryParse(referenceTime, out var parsed)) 
            {
                return (null, "正向引用时间必须使用ISO8601格式！");
            }

            if(parsed > maxAllowedTime)
            {
                return (null, "正向引用时间不能晚于当前时间！");
            }

            return (parsed.UtcDateTime, null);
        }

        // 构造算法失败结果，统一返回阻断标记和风险标签。
        private static GuardAlgorithmResult<T> BuildFailure<T>(
            string requestId,
            string message,
            string riskLabel,
            T? details = default)
        {
            return new GuardAlgorithmResult<T>
            {
                Success = false,
                Message = message,
                Data = new GuardAlgorithmData<T>
                {
                    RequestId = requestId,
                    Blocked = true,
                    RiskLabel = riskLabel,
                    Details = details
                }
            };
        }
    }
}
