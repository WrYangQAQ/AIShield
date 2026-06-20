"""
Embedding 长时间持续测试 — 单一循环跑满指定时长
持续写入记忆 → 触发冲突检测 → 降权验证 → RAG重排 → 循环
用法：cd guard_service && python test_embedding_full.py [分钟数]
默认跑5分钟
"""

import os
import sys
# 自动定位项目根目录（包含 guard_algorithms/ 的目录），无论测试文件放在哪层都能找到
_current = os.path.dirname(os.path.abspath(__file__))
while not os.path.exists(os.path.join(_current, "guard_algorithms", "__init__.py")):
    _parent = os.path.dirname(_current)
    if _parent == _current:
        break
    _current = _parent
sys.path.insert(0, _current)

import requests
import time
import json
import random

BASE = "http://127.0.0.1:8900"
DEFAULT_MINUTES = 5

# 测试语料库
MEMORY_POOL = [
    # ── 语言学习（同一领域，应互相冲突）──
    ("用户想学英语", "user"),
    ("用户想学德语", "user"),
    ("用户想学法语", "user"),
    ("用户想学日语", "user"),
    ("用户想学韩语", "user"),
    ("用户想学西班牙语", "user"),
    ("用户想学俄语", "user"),
    ("用户想学意大利语", "user"),
    ("用户想学葡萄牙语", "user"),
    ("用户想学阿拉伯语", "user"),
    ("我想学习英语口语", "user"),
    ("英语四级考试怎么准备", "user"),
    ("用户正在备考雅思", "user"),
    ("用户想考托福", "user"),
    ("用户想提高英语听力", "user"),
    # ── 编程（同一领域，应互相冲突）──
    ("用户喜欢用Python写代码", "user"),
    ("用户最近在学习Java", "user"),
    ("用户对Go语言感兴趣", "user"),
    ("用户想学Rust编程", "user"),
    ("用户正在学C++", "user"),
    ("用户最近在学习数据库", "user"),
    ("用户对前端开发感兴趣", "user"),
    ("用户想学React框架", "user"),
    ("用户想学Vue框架", "user"),
    ("用户想学后端开发", "user"),
    ("用户想学Spring Boot", "user"),
    ("用户想学Docker容器", "user"),
    # ── AI/数据（同一领域，应互相冲突）──
    ("机器学习入门教程", "user"),
    ("深度学习与神经网络", "user"),
    ("统计学基础概念", "user"),
    ("用户想学自然语言处理", "user"),
    ("用户在学计算机视觉", "user"),
    ("用户想了解大语言模型", "user"),
    ("用户在研究强化学习", "user"),
    ("用户想学数据可视化", "user"),
    ("用户想学数据分析", "user"),
    # ── 运动/健身（同一领域）──
    ("用户喜欢打篮球", "user"),
    ("用户喜欢踢足球", "user"),
    ("用户喜欢打乒乓球", "user"),
    ("用户喜欢打羽毛球", "user"),
    ("用户喜欢游泳", "user"),
    ("用户喜欢跑步", "user"),
    ("用户喜欢瑜伽", "user"),
    ("用户每天做力量训练", "user"),
    # ── 饮食/烹饪（同一领域）──
    ("如何制作红烧肉", "user"),
    ("用户想学做川菜", "user"),
    ("用户想学做粤菜", "user"),
    ("用户想学烘焙", "user"),
    ("用户想学做甜品", "user"),
    ("用户是素食主义者", "user"),
    ("用户对日料感兴趣", "user"),
    # ── 音乐/影视（同一领域）──
    ("用户喜欢听古典音乐", "user"),
    ("用户喜欢听摇滚乐", "user"),
    ("用户喜欢听爵士乐", "user"),
    ("用户喜欢看科幻电影", "user"),
    ("用户喜欢看悬疑片", "user"),
    ("用户喜欢看纪录片", "user"),
    ("用户喜欢看动漫", "user"),
    # ── 独立领域（互不应冲突）──
    ("今天北京天气晴朗", "user"),
    ("用户偏好深色主题", "user"),
    ("用户正在准备考研", "user"),
    ("用户想去西藏旅游", "user"),
    ("用户养了一只猫", "user"),
    ("用户最近在装修房子", "user"),
    ("用户想学开车", "user"),
    ("用户喜欢摄影", "user"),
    ("用户在学画画", "user"),
    ("用户想学弹吉他", "user"),
    ("用户想学钢琴", "user"),
    ("用户喜欢下棋", "user"),
    ("用户喜欢读书", "user"),
    ("用户在准备面试", "user"),
    # ── 非user来源（不同source不检测冲突）──
    ("系统默认语言为中文", "system"),
    ("管理员设置了严格模式", "admin"),
    ("系统已启用审计日志", "system"),
    ("管理员配置了信任等级", "admin"),
]

# 统计
stats = {
    "total_writes": 0,
    "conflicts_triggered": 0,
    "conflicts_missed": 0,       # 应触发但没触发
    "false_conflicts": 0,        # 不应触发但触发了
    "rag_requests": 0,
    "rag_filtered": 0,
    "errors": 0,
    "conflict_latencies": [],
    "rag_latencies": [],
    "write_latencies": [],
}

# 同领域分组（用于判断是否应该触发冲突）
# 注意：分组只包含语义上高度重叠的条目，边界模糊的不放进来避免误判
DOMAIN_GROUPS = [
    {"英语", "德语", "法语", "日语", "韩语", "西班牙语", "俄语", "意大利语", "葡萄牙语", "阿拉伯语", "口语", "四级", "雅思", "托福", "听力"},  # 语言学习
    {"Python", "Java", "Go语言", "Rust", "C++"},  # 编程语言（框架/工具不加入，语义距离大）
    {"机器学习", "深度学习", "自然语言", "计算机视觉", "大语言", "强化学习"},  # AI核心
    {"篮球", "足球", "乒乓球", "羽毛球", "游泳", "跑步"},  # 球类+有氧运动
    {"川菜", "粤菜", "烘焙", "甜品", "日料"},  # 中外菜系
    {"古典音乐", "摇滚乐", "爵士乐"},  # 音乐类型
    {"科幻电影", "悬疑", "纪录片", "动漫"},  # 影视类型
    {"吉他", "钢琴"},  # 乐器
]

def get_domain(content):
    """判断内容属于哪个领域组"""
    groups = []
    for i, group in enumerate(DOMAIN_GROUPS):
        for keyword in group:
            if keyword in content:
                groups.append(i)
                break
    return groups

def should_conflict(content_a, content_b):
    """判断两条记忆是否应该触发冲突（同领域不同内容）"""
    domains_a = get_domain(content_a)
    domains_b = get_domain(content_b)
    same_domain = bool(set(domains_a) & set(domains_b))
    same_content = (content_a == content_b)
    return same_domain and not same_content


def write_memory(mem_id, content, source="user"):
    """写入一条记忆，返回 (耗时ms, conflicts列表)"""
    start = time.perf_counter()
    try:
        r = requests.post(f"{BASE}/manage/memory/put", json={
            "memoryId": mem_id, "content": content, "confidence": 0.9, "source": source
        }, timeout=30)
        elapsed = (time.perf_counter() - start) * 1000
        d = r.json()
        conflicts = d.get("Data", {}).get("conflicts", [])
        stats["write_latencies"].append(elapsed)
        return elapsed, conflicts
    except Exception as e:
        stats["errors"] += 1
        return -1, []


def get_memory(mem_id):
    """读取一条记忆"""
    try:
        r = requests.get(f"{BASE}/manage/memory/{mem_id}", timeout=10)
        d = r.json()
        return d.get("Data", {})
    except:
        return {}


def run_rag_rerank(query, candidates_with_emb):
    """RAG 重排"""
    start = time.perf_counter()
    try:
        r = requests.post(f"{BASE}/guard/rag-rerank", json={
            "query": query, "candidates": candidates_with_emb, "agentKey": "long-test"
        }, timeout=30)
        elapsed = (time.perf_counter() - start) * 1000
        d = r.json()
        kept = d.get("Data", {}).get("details", {}).get("kept", [])
        stats["rag_requests"] += 1
        stats["rag_latencies"].append(elapsed)
        stats["rag_filtered"] += len(candidates_with_emb) - len(kept)
        return elapsed, kept
    except Exception as e:
        stats["errors"] += 1
        return -1, []


def main():
    minutes = DEFAULT_MINUTES
    if len(sys.argv) > 1:
        try:
            minutes = float(sys.argv[1])
        except ValueError:
            print(f"参数无效，使用默认 {DEFAULT_MINUTES} 分钟")

    duration = minutes * 60

    print("╔══════════════════════════════════════════════════════════╗")
    print(f"║  Embedding 长时间持续测试 — {minutes}分钟                   ║")
    print("╚══════════════════════════════════════════════════════════╝")

    # 检查服务
    try:
        r = requests.get(f"{BASE}/health", timeout=3)
        d = r.json()
        embedder_ok = d.get("dependencies", {}).get("embedder", False)
        print(f"  服务状态: ✅ 运行中")
        print(f"  Embedder: {'✅ 已加载' if embedder_ok else '❌ 未加载'}")
        if not embedder_ok:
            print("  ⚠️ 无 Embedder，测试结果不完整")
    except:
        print("  ❌ 服务未启动！请先运行: python start.py")
        return

    print(f"\n  开始时间: {time.strftime('%H:%M:%S')}")
    print(f"  预计结束: {time.strftime('%H:%M:%S', time.localtime(time.time() + duration))}")
    print(f"  语料数: {len(MEMORY_POOL)} 条")
    print(f"\n  运行中... (Ctrl+C 提前结束)\n")

    start_time = time.perf_counter()
    round_num = 0

    # 预生成 embedding（RAG重排用）
    from guard_algorithms.local_embedder import LocalEmbedder
    import asyncio
    emb = LocalEmbedder()

    async def precompute_embeddings():
        cache = {}
        for content, _ in MEMORY_POOL:
            if content not in cache:
                cache[content] = await emb.embed(content)
        return cache

    print("  预计算 embedding 缓存...")
    emb_cache = asyncio.run(precompute_embeddings())
    print(f"  缓存完成: {len(emb_cache)} 条\n")

    try:
        while True:
            elapsed_total = time.perf_counter() - start_time
            if elapsed_total >= duration:
                break

            round_num += 1
            remaining = duration - elapsed_total
            print(f"── 轮次 {round_num} (剩余 {remaining:.0f}s) ──")

            # 每轮：批量写入 5~8 条记忆
            batch_size = random.randint(5, 8)
            written = []  # (mem_id, content, source)

            for i in range(batch_size):
                mem_id = f"rnd{round_num}-m{i:03d}"
                content, source = random.choice(MEMORY_POOL)
                latency, conflicts = write_memory(mem_id, content, source)
                stats["total_writes"] += 1

                if latency >= 0:
                    # 判断冲突是否正确
                    if conflicts:
                        stats["conflicts_triggered"] += len(conflicts)
                        # 检查是否误报
                        for c in conflicts:
                            old_content = c.get("content", "")
                            if not should_conflict(content, old_content):
                                stats["false_conflicts"] += 1
                                print(f"    ⚠️ 误报: '{content}' vs '{old_content}' 不应冲突但触发了")
                            else:
                                print(f"    ✓ 冲突: '{content[:15]}...' vs '{old_content[:15]}...' (sim={c.get('similarity', 0):.4f})")
                    else:
                        # 检查是否漏报
                        for prev_id, prev_content, prev_source in written:
                            if prev_source == source and should_conflict(content, prev_content):
                                stats["conflicts_missed"] += 1
                                print(f"    ⚠️ 漏报: '{content}' vs '{prev_content}' 应冲突但未触发")
                                break

                    avg_lat = sum(stats["write_latencies"][-10:]) / min(10, len(stats["write_latencies"]))
                    print(f"    写入 {mem_id}: '{content[:20]}...' latency={latency:.0f}ms (近10次均值={avg_lat:.0f}ms)")

                written.append((mem_id, content, source))

            # 每3轮跑一次 RAG 重排
            if round_num % 3 == 0:
                query = random.choice(["如何学习机器学习", "用户想学英语", "Python编程入门", "今天天气怎么样"])
                # 从缓存构建候选
                cands = []
                sample = random.sample(MEMORY_POOL, min(6, len(MEMORY_POOL)))
                for content, source in sample:
                    cands.append({
                        "id": f"rag-{content[:6]}",
                        "content": content,
                        "embedding": emb_cache[content],
                        "source": source
                    })
                rag_lat, kept = run_rag_rerank(query, cands)
                if rag_lat >= 0:
                    print(f"    RAG重排: query='{query}', 保留{len(kept)}/{len(cands)}, latency={rag_lat:.0f}ms")

            # 进度条
            pct = elapsed_total / duration * 100
            bar_len = 30
            filled = int(bar_len * pct / 100)
            bar = "█" * filled + "░" * (bar_len - filled)
            print(f"    [{bar}] {pct:.0f}%")
            print()

    except KeyboardInterrupt:
        print("\n\n  ⏹ 用户中断测试")

    # ──────────────────────────────────────────
    # 汇总报告
    # ──────────────────────────────────────────
    total_time = time.perf_counter() - start_time

    print("\n" + "=" * 60)
    print("  汇总报告")
    print("=" * 60)

    print(f"\n  运行时长: {total_time:.1f}s ({total_time/60:.1f}min)")
    print(f"  完成轮次: {round_num}")

    print(f"\n  ── 写入统计 ──")
    print(f"  总写入: {stats['total_writes']}")
    print(f"  错误: {stats['errors']}")
    if stats['write_latencies']:
        wls = stats['write_latencies']
        print(f"  写入延迟: 平均={sum(wls)/len(wls):.0f}ms, "
              f"最小={min(wls):.0f}ms, 最大={max(wls):.0f}ms, "
              f"P95={sorted(wls)[int(len(wls)*0.95)]:.0f}ms")

    print(f"\n  ── 冲突检测统计 ──")
    print(f"  冲突触发: {stats['conflicts_triggered']}")
    print(f"  漏报(应触发未触发): {stats['conflicts_missed']}")
    print(f"  误报(不应触发但触发): {stats['false_conflicts']}")
    precision = stats['conflicts_triggered'] / (stats['conflicts_triggered'] + stats['false_conflicts']) if (stats['conflicts_triggered'] + stats['false_conflicts']) > 0 else 0
    recall = stats['conflicts_triggered'] / (stats['conflicts_triggered'] + stats['conflicts_missed']) if (stats['conflicts_triggered'] + stats['conflicts_missed']) > 0 else 0
    print(f"  精确率: {precision:.1%}")
    print(f"  召回率: {recall:.1%}")

    print(f"\n  ── RAG重排统计 ──")
    print(f"  请求次数: {stats['rag_requests']}")
    print(f"  过滤文档数: {stats['rag_filtered']}")
    if stats['rag_latencies']:
        rls = stats['rag_latencies']
        print(f"  重排延迟: 平均={sum(rls)/len(rls):.0f}ms, "
              f"最小={min(rls):.0f}ms, 最大={max(rls):.0f}ms")

    print(f"\n  ── 吞吐量 ──")
    print(f"  写入QPS: {stats['total_writes'] / total_time:.1f}")
    if stats['rag_requests'] > 0:
        print(f"  RAG QPS: {stats['rag_requests'] / total_time:.2f}")

    print(f"\n{'=' * 60}")
    print("  测试结束")


if __name__ == "__main__":
    main()
