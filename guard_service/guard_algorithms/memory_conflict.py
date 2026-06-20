"""
记忆冲突检测 —— 写入新记忆时检测语义冲突，对同 source 旧记忆降权。

核心逻辑：
  1. 遍历同 source 的已有记忆
  2. 跳过 system/admin 来源（不被用户意图覆盖）
  3. 跳过内容完全一致的记忆（SHA256 比对）
  4. 计算新旧记忆的余弦相似度（仅使用缓存的 embedding，跳过未处理的条目）
  5. 相似度 >= conflict_similarity_threshold 且内容不同 → 视为意图替换
  6. 对旧记忆降权：new_confidence = old_confidence * (1 - penalty_weight)
  7. 降权后低于 forget_threshold → 归档

使用方式：
  conflicts = await detect_and_demote_conflicts(
      new_content="用户偏好：喜欢简洁的回答",
      new_source="user",
      new_content_hash="sha256...",
      memory_store=_memory,
      embedder=_embedder,
      config=config,
  )

设计约束：
  - 所有操作有 try/except 保护，异常时不影响正常写入
  - MemoryStore 接口只有 get/update/archive，同 source 遍历依赖
    InMemoryMemoryStore.list_by_source() 方法
  - 余弦相似度使用 numpy 向量化矩阵运算（BLAS 释放 GIL，不阻塞事件循环）
  - 单条路径保留纯 Python _cosine_similarity 作为后备
"""

from __future__ import annotations

import math
from typing import Optional

from .config import GuardConfig
from .interfaces import Embedder, MemoryStore
from .models import MemoryEntry


def _cosine_similarity(a: list[float], b: list[float]) -> float:
    """
    计算两个向量的余弦相似度（纯 Python 实现，不依赖 numpy）。

    Args:
        a: 向量 A。
        b: 向量 B。

    Returns:
        余弦相似度，范围 [-1, 1]。向量长度不匹配或零向量时返回 0.0。
    """
    if not a or not b or len(a) != len(b):
        return 0.0

    dot = sum(x * y for x, y in zip(a, b))
    na = sum(x * x for x in a)
    nb = sum(y * y for y in b)

    if na <= 0 or nb <= 0:
        return 0.0

    return dot / (math.sqrt(na) * math.sqrt(nb))


async def detect_and_demote_conflicts(
    new_content: str,
    new_source: str,
    new_content_hash: str,
    memory_store: MemoryStore,
    embedder: Embedder,
    config: GuardConfig,
    new_embedding: Optional[list[float]] = None,
) -> list[dict]:
    """
    写入新记忆时检测语义冲突，对同 source 旧记忆降权。

    冲突判定条件（全部满足）：
      - 新旧记忆属于同一 source
      - 旧记忆的 source 不是 system 或 admin（不被用户意图覆盖）
      - 新旧记忆内容不同（SHA256 不一致）
      - 新旧记忆的余弦相似度 >= conflict_similarity_threshold

    降权策略：
      - new_confidence = old_confidence * (1 - penalty_weight)
      - 降权后低于 forget_threshold → 归档

    Args:
        new_content: 新记忆的内容文本。
        new_source: 新记忆的来源标签。
        new_content_hash: 新记忆内容的 SHA256 哈希。
        memory_store: 记忆存储实例（需要支持 list_by_source 方法）。
        embedder: 向量嵌入服务实例。
        config: 护栏配置（使用 conflict_similarity_threshold、
                conflict_penalty_weight、forget_threshold）。
        new_embedding: 新记忆的预计算 embedding（可选，传入后跳过重复计算）。

    Returns:
        被降权/归档的记忆列表，每条记录包含：
        - memoryId: 记忆 ID
        - oldConfidence: 原始置信度
        - newConfidence: 降权后置信度
        - action: "demoted"（降权）或 "archived"（归档）
    """
    # system/admin 来源的记忆不被用户意图覆盖，跳过冲突检测
    if new_source in ("system", "admin"):
        return []

    # 获取同 source 的所有记忆
    # InMemoryMemoryStore 提供了 list_by_source 方法
    list_method = getattr(memory_store, "list_by_source", None)
    if list_method is None:
        # 存储实现不支持按 source 列出，无法检测冲突
        return []

    same_source_entries: list[MemoryEntry] = list_method(new_source)
    if not same_source_entries:
        return []

    # 计算新记忆的向量嵌入（优先使用传入的，避免重复计算）
    if new_embedding is not None:
        pass  # 已有，直接用
    else:
        try:
            new_embedding = await embedder.embed(new_content)
        except Exception:
            # 嵌入失败，无法计算相似度，跳过冲突检测
            return []

    # 获取配置阈值
    similarity_threshold = config.conflict_similarity_threshold
    penalty_weight = config.conflict_penalty_weight
    forget_threshold = config.forget_threshold

    conflicts: list[dict] = []

    for entry in same_source_entries:
        # 跳过 system/admin 来源的旧记忆
        if entry.source in ("system", "admin"):
            continue

        # 跳过内容完全一致的记忆（SHA256 比对）
        import hashlib
        entry_hash = hashlib.sha256(entry.content.encode("utf-8")).hexdigest()
        if entry_hash == new_content_hash:
            continue

        # 计算语义相似度：只用缓存的 embedding，跳过未处理的条目
        if entry.embedding is not None:
            old_embedding = entry.embedding
        else:
            # 该条目的 embedding 尚未由 worker 计算缓存，跳过
            # 等该条目的 worker 处理完后，后续写入自然会比对到
            continue

        similarity = _cosine_similarity(new_embedding, old_embedding)

        # 相似度低于阈值，不视为冲突
        if similarity < similarity_threshold:
            continue

        # 冲突确认：降权处理
        old_confidence = entry.confidence
        new_confidence = old_confidence * (1 - penalty_weight)

        # 降权后低于遗忘阈值 → 归档
        if new_confidence < forget_threshold:
            try:
                await memory_store.archive(entry.id, "conflict_demoted_and_archived")
                conflicts.append({
                    "memoryId": entry.id,
                    "content": entry.content,
                    "similarity": similarity,
                    "oldConfidence": old_confidence,
                    "newConfidence": new_confidence,
                    "action": "archived",
                })
            except Exception:
                # 归档失败，记录但跳过
                pass
        else:
            # 降权但未归档 → 更新置信度
            try:
                await memory_store.update(entry.id, new_confidence)
                conflicts.append({
                    "memoryId": entry.id,
                    "content": entry.content,
                    "similarity": similarity,
                    "oldConfidence": old_confidence,
                    "newConfidence": new_confidence,
                    "action": "demoted",
                })
            except Exception:
                # 更新失败，记录但跳过
                pass

    return conflicts


def compute_conflicts_sync(
    new_embedding: list[float],
    same_source_entries: list[MemoryEntry],
    new_content_hash: str,
    similarity_threshold: float,
    penalty_weight: float,
    forget_threshold: float,
) -> list[dict]:
    """
    纯同步冲突检测计算（CPU密集），可安全在线程池中运行。

    不执行任何 I/O 或状态修改，仅计算需要降权/归档的条目列表。
    调用方在事件循环中根据返回结果执行实际的 update/archive 操作。

    Args:
        new_embedding: 新记忆的向量嵌入。
        same_source_entries: 同来源的已有记忆条目列表。
        new_content_hash: 新记忆内容的 SHA256 哈希。
        similarity_threshold: 冲突相似度阈值。
        penalty_weight: 冲突降权权重。
        forget_threshold: 遗忘阈值。

    Returns:
        冲突条目列表，每条包含 memoryId, similarity, oldConfidence, newConfidence, action。
    """
    import hashlib

    conflicts: list[dict] = []

    for entry in same_source_entries:
        if entry.source in ("system", "admin"):
            continue

        entry_hash = hashlib.sha256(entry.content.encode("utf-8")).hexdigest()
        if entry_hash == new_content_hash:
            continue

        if entry.embedding is None:
            continue

        similarity = _cosine_similarity(new_embedding, entry.embedding)
        if similarity < similarity_threshold:
            continue

        old_confidence = entry.confidence
        new_confidence = old_confidence * (1 - penalty_weight)

        action = "archived" if new_confidence < forget_threshold else "demoted"
        conflicts.append({
            "memoryId": entry.id,
            "similarity": similarity,
            "oldConfidence": old_confidence,
            "newConfidence": new_confidence,
            "action": action,
        })

    return conflicts


def compute_conflicts_batch_sync(
    batch_items: list[dict],
    memory_store_snapshot: dict[str, list[MemoryEntry]],
    similarity_threshold: float,
    penalty_weight: float,
    forget_threshold: float,
) -> list[dict]:
    """
    批量冲突检测（numpy 向量化，释放 GIL），一次线程池调用处理整个 batch。

    关键优化：用 numpy 矩阵运算替代逐条纯 Python 余弦相似度循环。
    - 纯 Python 版本：64条×1000已有 = 64000次512维点积，~3s，全程持有GIL阻塞事件循环
    - numpy 版本：单次 BLAS 矩阵乘法 (N,D)@(D,M)→(N,M)，~1ms，C层释放GIL零阻塞

    流程（按 source 分组处理）：
      1. 将同 source 的已有 embedding 构建 numpy 矩阵，一次性 L2 归一化
      2. 将新条目的 embedding 构建 numpy 矩阵，一次性 L2 归一化
      3. 单次矩阵乘法计算全部相似度 sim_matrix = new_norm @ exist_norm.T
      4. numpy.where 筛选 >= 阈值的位置，再逐条检查 content_hash 去重

    Args:
        batch_items: 批量条目列表，每项含 embedding, content_hash, source, memory_id。
        memory_store_snapshot: source → [MemoryEntry] 的快照（调用方一次性构建）。
        similarity_threshold / penalty_weight / forget_threshold: 同 compute_conflicts_sync。

    Returns:
        所有冲突的扁平列表，每条额外带 source_memory_id 标识来源条目。
    """
    import hashlib
    import numpy as np

    all_conflicts: list[dict] = []

    if not batch_items:
        return all_conflicts

    # 按 source 分组，同 source 的条目一起做矩阵运算
    by_source: dict[str, list[dict]] = {}
    for item in batch_items:
        by_source.setdefault(item["source"], []).append(item)

    for source, items in by_source.items():
        same_source = memory_store_snapshot.get(source, [])
        if not same_source:
            continue

        # 筛选有 embedding 的已有条目，跳过 system/admin
        existing_entries: list[MemoryEntry] = []
        existing_vectors: list[list[float]] = []
        for entry in same_source:
            if entry.source in ("system", "admin"):
                continue
            if entry.embedding is None:
                continue
            existing_entries.append(entry)
            existing_vectors.append(entry.embedding)

        if not existing_entries:
            continue

        # ── 构建已有条目的归一化矩阵（一次构建，整批复用） ──
        existing_matrix = np.asarray(existing_vectors, dtype=np.float32)  # (M, D)
        existing_norms = np.linalg.norm(existing_matrix, axis=1, keepdims=True)  # (M, 1)
        existing_norms = np.maximum(existing_norms, 1e-10)  # 防止除零
        existing_normalized = existing_matrix / existing_norms  # (M, D)

        # ── 构建新条目的归一化矩阵 ──
        new_vectors = [item["embedding"] for item in items]
        new_matrix = np.asarray(new_vectors, dtype=np.float32)  # (N, D)
        new_norms = np.linalg.norm(new_matrix, axis=1, keepdims=True)  # (N, 1)
        new_norms = np.maximum(new_norms, 1e-10)
        new_normalized = new_matrix / new_norms  # (N, D)

        # ── 单次 BLAS 矩阵乘法计算全部相似度（C 层释放 GIL） ──
        sim_matrix = new_normalized @ existing_normalized.T  # (N, M)

        # 🔍 调试：打印最大相似度和对应的条目对
        max_idx = np.unravel_index(np.argmax(sim_matrix), sim_matrix.shape)
        max_sim = float(sim_matrix[max_idx])
        import logging as _logging
        _logging.getLogger("guard_service").info(
            "📊 compute_conflicts: source=%s, new=%d, existing=%d, max_sim=%.4f (threshold=%.2f) new_id=%s vs exist_id=%s",
            source, len(items), len(existing_entries),
            max_sim, similarity_threshold,
            items[int(max_idx[0])]["memory_id"],
            existing_entries[int(max_idx[1])].id,
        )

        # 预计算已有条目的内容哈希（避免在循环中重复计算）
        existing_hashes = [
            hashlib.sha256(e.content.encode("utf-8")).hexdigest()
            for e in existing_entries
        ]

        # ── 筛选冲突 ──
        for i, item in enumerate(items):
            memory_id = item["memory_id"]
            new_content_hash = item["content_hash"]
            sims = sim_matrix[i]  # (M,) 该新条目与所有已有条目的相似度

            # 找出相似度 >= 阈值的位置
            conflict_indices = np.where(sims >= similarity_threshold)[0]

            for j in conflict_indices:
                j_int = int(j)
                # 跳过内容完全一致的条目（自身或重复内容）
                if existing_hashes[j_int] == new_content_hash:
                    continue

                old_confidence = existing_entries[j_int].confidence
                new_confidence = old_confidence * (1 - penalty_weight)
                action = "archived" if new_confidence < forget_threshold else "demoted"

                all_conflicts.append({
                    "source_memory_id": memory_id,
                    "memoryId": existing_entries[j_int].id,
                    "similarity": float(sims[j_int]),
                    "oldConfidence": old_confidence,
                    "newConfidence": new_confidence,
                    "action": action,
                })

    return all_conflicts
