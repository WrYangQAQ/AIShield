"""
极简冲突检测测试 — 去掉千条噪音，只验证核心逻辑

用法：
  1. 启动服务: python start.py
  2. 跑测试: python test_conflict_simple.py
  3. 同时查看服务端终端的 📊 日志
"""

import requests
import time
import hashlib
import sys

BASE = "http://127.0.0.1:8900"
session = requests.Session()


def _skip(msg):
    """服务不可用时优雅退出：在 pytest 环境中 raise，直接运行时 exit。"""
    if "pytest" in sys.modules:
        import pytest
        pytest.skip(msg, allow_module_level=True)
    else:
        print(f"⏭ 跳过: {msg}")
        sys.exit(0)


def put_memory(mem_id, content, confidence=0.9, source="user"):
    r = session.post(f"{BASE}/manage/memory/put", json={
        "memoryId": mem_id,
        "content": content,
        "confidence": confidence,
        "source": source,
    }, timeout=10)
    return r.json()


def get_memory(mem_id):
    r = session.get(f"{BASE}/manage/memory/{mem_id}", timeout=5)
    return r.json()


def del_memory(mem_id):
    session.delete(f"{BASE}/manage/memory/{mem_id}", timeout=5)


# ── 检查服务 ──
print("=" * 60)
print("极简冲突检测测试")
print("=" * 60)

try:
    r = session.get(f"{BASE}/health", timeout=5)
    d = r.json()
    embedder_ok = d.get("dependencies", {}).get("embedder", False)
    print(f"服务: ✅  Embedder: {'✅' if embedder_ok else '❌'}")
    if not embedder_ok:
        print("❌ Embedder 未就绪，退出")
        _skip("Embedder 未就绪")
except Exception as e:
    print(f"❌ 服务未启动: {e}")
    _skip(f"服务未启动: {e}")

# ── 清理旧测试数据 ──
for prefix in ["s-anchor", "s-conflict"]:
    for i in range(1, 6):
        try:
            del_memory(f"{prefix}-{i:03d}")
        except:
            pass

# ── 用独立 source 避免被千条噪音干扰 ──
TEST_SOURCE = "conflict_test"

print(f"\n使用独立 source='{TEST_SOURCE}'，避免与已有记忆混合")

# ── Step 1: 写入 1 条锚点记忆 ──
print("\n── Step 1: 写入锚点记忆 ──")
anchor_content = "用户最近开始对法语产生了浓厚的兴趣，每天都会花至少一个小时来学习法语的基础语法和常用表达"
anchor_hash = hashlib.sha256(anchor_content.encode("utf-8")).hexdigest()
r = put_memory("s-anchor-001", anchor_content, 0.9, TEST_SOURCE)
print(f"  写入: {r.get('Data', {}).get('conflictCheck', '?')}")

# ── Step 2: 等待 embedding 完成 ──
print("\n── Step 2: 等待 embedding (5s) ──")
time.sleep(5)

# 验证锚点确实有 embedding 了（通过服务端日志确认）
d = get_memory("s-anchor-001")
print(f"  锚点状态: confidence={d.get('Data', {}).get('confidence', '?')}")

# ── Step 3: 写入语义冲突记忆 ──
print("\n── Step 3: 写入冲突记忆 ──")
conflict_content = "用户最近对法语学习产生了极大的热情，每天至少花一个小时研习法语的基本语法和日常用语"
r = put_memory("s-conflict-001", conflict_content, 0.9, TEST_SOURCE)
print(f"  写入: {r.get('Data', {}).get('conflictCheck', '?')}")

# ── Step 4: 等待冲突检测完成 ──
print("\n── Step 4: 等待冲突检测 (5s) ──")
time.sleep(5)

# ── Step 5: 检查锚点置信度 ──
print("\n── Step 5: 检查锚点置信度 ──")
d = get_memory("s-anchor-001")
conf = d.get("Data", {}).get("confidence", "N/A")
expected = 0.9 * (1 - 0.6)  # 0.36

if conf != "N/A" and conf < 0.9:
    print(f"  ✅ 冲突检测生效! confidence={conf} (原始=0.9, 预期≈{expected})")
else:
    print(f"  ❌ 冲突检测未生效! confidence={conf} (原始=0.9, 应降为≈{expected})")
    print(f"\n  ⚠️  请查看服务端终端的 📊 日志:")
    print(f"     - 📊 snapshot source={TEST_SOURCE}: total=?, with_embedding=?")
    print(f"     - 📊 compute_conflicts: max_sim=?.???? (threshold=0.75)")
    print(f"     如果 max_sim < 0.75 → 相似度不够")
    print(f"     如果 with_embedding=0 → embedding 未缓存")
    print(f"     如果没有任何 📊 日志 → worker 未处理此 source")

# ── 额外: 打印冲突记忆的状态 ──
d2 = get_memory("s-conflict-001")
conf2 = d2.get("Data", {}).get("confidence", "N/A")
print(f"\n  冲突记忆 confidence={conf2} (应保持 0.9，冲突检测只降权旧记忆)")

# ── 诊断: 如果冲突检测未生效，用 bulk 端点测试 ──
if conf != "N/A" and conf >= 0.9:
    print(f"\n── 额外诊断: 用 bulk 端点测试 ──")
    # 清理
    del_memory("s-anchor-001")
    del_memory("s-conflict-001")
    time.sleep(1)

    # 用 bulk 端点一次性写入（两阶段处理，确保 embedding 全部缓存后再冲突检测）
    r = session.post(f"{BASE}/manage/memory/bulk", json={
        "memories": [
            {"memoryId": "s-anchor-001", "content": anchor_content, "confidence": 0.9, "source": TEST_SOURCE},
            {"memoryId": "s-conflict-001", "content": conflict_content, "confidence": 0.9, "source": TEST_SOURCE},
        ],
        "skipConflictCheck": False,
    }, timeout=30)
    bulk_result = r.json()
    print(f"  bulk 结果: {bulk_result.get('Data', {})}")

    time.sleep(3)

    d = get_memory("s-anchor-001")
    conf = d.get("Data", {}).get("confidence", "N/A")
    if conf != "N/A" and conf < 0.9:
        print(f"  ✅ bulk 端点冲突检测生效! confidence={conf}")
    else:
        print(f"  ❌ bulk 端点也未生效! confidence={conf}")
        print(f"  → 问题可能在 compute_conflicts_batch_sync 或 apply 阶段")

print("\n" + "=" * 60)
