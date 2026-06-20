"""
Embedding 千条级压力测试 v2
- 修复：localhost → 127.0.0.1（避免 Windows DNS 解析延迟）
- 修复：用 requests.Session() 复用连接
- 1000+ 条记忆，每条 50+ 字符
- 批量写入（测写入吞吐），冲突检测验证
用法：python test_embedding_stress.py [条数]
默认 1000 条
"""

import requests
import time
import random
import sys

BASE = "http://127.0.0.1:8900"
DEFAULT_COUNT = 1000

# ── 生成 50+ 字符的语料 ──

TEMPLATES = {
    "lang": [
        "用户最近开始对{lang}产生了浓厚的兴趣，每天都会花至少一个小时来学习{lang}的基础语法和常用表达",
        "用户在准备{lang}等级考试，目前正在进行阅读理解和听力部分的专项训练，计划在下个月参加考试",
        "用户希望通过系统学习{lang}来提升自己的职业竞争力，已经在在线平台注册了{lang}课程并坚持打卡",
        "用户对{lang}文化非常着迷，不仅在学习语言本身，还在深入了解{lang}国家的历史和传统习俗",
        "用户之前学过一些{lang}基础，现在想要重新拾起来，正在寻找合适的{lang}进阶教材和学习方法",
    ],
    "lang_val": ["英语", "德语", "法语", "日语", "韩语", "西班牙语", "俄语", "意大利语", "葡萄牙语", "阿拉伯语"],
    "code": [
        "用户最近在深入学习{lang}编程语言，已经完成了基础语法部分的学习，正在尝试用{lang}开发一个小型项目",
        "用户希望通过掌握{lang}来拓展自己的技术栈，目前正在跟着官方文档逐步学习{lang}的核心特性",
        "用户在工作中需要使用{lang}进行开发，正在通过阅读源码和编写练习来快速提升{lang}的实战能力",
        "用户对{lang}的并发模型和性能优势很感兴趣，正在研究如何用{lang}重构现有的后端服务",
        "用户正在准备{lang}相关的技术面试，每天刷算法题并整理{lang}常见面试知识点",
    ],
    "code_val": ["Python", "Java", "Go", "Rust", "C++", "TypeScript", "Kotlin", "Swift"],
    "ai": [
        "用户正在系统学习{topic}，从理论基础开始逐步深入到实际应用，希望能在工作中落地相关技术方案",
        "用户对{topic}领域有强烈的研究兴趣，目前正在阅读相关论文并尝试复现经典的实验结果",
        "用户希望通过学习{topic}来解决当前项目中的具体问题，正在评估不同的技术路线和实现方案",
        "用户已经掌握了{topic}的基础知识，现在想要进一步提升，正在研究该领域的前沿进展和最新方法",
        "用户在参加{topic}相关的在线课程，每周完成作业和项目实践，目标是获得课程认证证书",
    ],
    "ai_val": ["机器学习", "深度学习", "自然语言处理", "计算机视觉", "强化学习", "大语言模型", "数据分析", "数据可视化"],
    "sport": [
        "用户每周坚持进行{sport}训练，已经养成了规律的运动习惯，体能和技巧都有了明显提升",
        "用户最近加入了{sport}俱乐部，每周参加两次集体训练，希望通过专业指导来提高自己的水平",
        "用户为了保持健康开始坚持{sport}，从最初的不适应到现在已经能够轻松完成中等强度的训练",
        "用户在{sport}方面有一定基础，现在想要突破瓶颈，正在制定更系统的训练计划和营养方案",
        "用户通过{sport}来缓解工作压力，已经坚持了三个月，感觉身体素质和心情都有了明显改善",
    ],
    "sport_val": ["篮球", "足球", "游泳", "跑步", "羽毛球", "乒乓球", "瑜伽", "力量训练"],
    "food": [
        "用户对{food}非常感兴趣，已经尝试制作了多种经典菜品，正在研究更地道的做法和食材搭配",
        "用户最近开始学习{food}，从基础的刀工和调味开始练起，每天都会尝试一道新菜并记录心得",
        "用户希望掌握{food}的核心技巧，正在跟随专业厨师的视频教程系统学习，目标是能独立完成宴席",
        "用户在{food}方面已经积累了不少经验，现在开始尝试创新菜品，融合不同菜系的特色和风味",
        "用户为了健康饮食开始研究{food}，注重食材的新鲜和营养搭配，每周制定详细的食谱计划",
    ],
    "food_val": ["川菜", "粤菜", "烘焙", "日料", "意大利菜", "法餐", "东南亚菜", "素食料理"],
    "music": [
        "用户最近迷上了{music}，每天通勤路上都会听，还在研究这种音乐风格的发展历史和代表艺术家",
        "用户正在学习欣赏{music}，通过参加音乐会和阅读乐评来培养自己的音乐审美和鉴赏能力",
        "用户对{music}有浓厚兴趣，已经开始学习相关的乐器演奏，希望有一天能自己演奏喜欢的曲子",
        "用户收藏了大量{music}的唱片和数字专辑，经常和朋友交流推荐，还组织过小型的音乐分享会",
        "用户通过{music}来放松心情和激发灵感，已经形成了自己独特的播放列表和聆听习惯",
    ],
    "music_val": ["古典音乐", "摇滚乐", "爵士乐", "电子音乐", "民谣", "嘻哈音乐", "蓝调"],
    "independent": [
        "用户正在准备研究生入学考试，每天复习八小时以上，重点攻克数学和专业课的薄弱环节",
        "用户最近在装修新房，正在比较不同的设计方案和装修材料，希望打造一个温馨舒适的居住空间",
        "用户养了一只布偶猫，每天花时间照顾它的饮食和健康，还在学习猫的行为心理学知识",
        "用户正在准备技术面试，系统复习算法和数据结构，同时整理项目经验和设计方案",
        "用户喜欢摄影，周末经常带着相机去城市各个角落拍摄，正在学习后期修图和构图技巧",
        "用户在学画画，从素描基础开始练习，每周完成两幅作品，希望能在年底举办个人画展",
        "用户喜欢下围棋，每天在线上平台对弈两三盘，正在研究布局理论和官子计算方法",
        "用户热爱阅读，每个月至少读四本书，涉猎范围包括历史、哲学、科学和文学作品",
        "用户最近在研究个人理财，学习基金定投和资产配置的知识，希望制定长期的财务规划",
        "用户对天文学很感兴趣，购买了入门级望远镜，每个晴朗的夜晚都会观测星空和行星",
        "用户在学习园艺知识，在阳台上种了多种香草和蔬菜，正在研究不同植物的养护方法",
        "用户正在学习时间管理方法，尝试番茄工作法和GTD系统，希望提高日常工作效率",
        "用户对心理学很感兴趣，正在阅读认知心理学的教材，希望更好地理解人类的思维模式",
        "用户在练习冥想，每天早起冥想二十分钟，已经坚持了两个月，感觉注意力和情绪都有改善",
        "用户最近开始学习手工皮具制作，已经完成了钱包和卡套，正在挑战更复杂的包型",
    ],
}


def generate_memories(count: int) -> list[tuple[str, str]]:
    """生成 count 条记忆，每条 50+ 字符，返回 [(content, source), ...]"""
    memories = []
    categories = ["lang", "code", "ai", "sport", "food", "music"]
    while len(memories) < count:
        if random.random() < 0.2:
            content = random.choice(TEMPLATES["independent"])
            source = random.choice(["user", "user", "user", "system", "admin"])
        else:
            cat = random.choice(categories)
            template = random.choice(TEMPLATES[cat])
            val = random.choice(TEMPLATES[f"{cat}_val"])
            placeholder = {"code": "lang", "ai": "topic"}.get(cat, cat)
            content = template.format(**{placeholder: val})
            source = "user"
        memories.append((content, source))
    return memories


def main():
    count = DEFAULT_COUNT
    if len(sys.argv) > 1:
        try:
            count = int(sys.argv[1])
        except ValueError:
            print(f"参数无效，使用默认 {DEFAULT_COUNT} 条")

    print("╔══════════════════════════════════════════════════════════╗")
    print(f"║  压力测试 v2 — {count}条 (127.0.0.1 + Session复用)        ║")
    print("╚══════════════════════════════════════════════════════════╝")

    session = requests.Session()

    # 检查服务
    try:
        r = session.get(f"{BASE}/health", timeout=5)
        d = r.json()
        embedder_ok = d.get("dependencies", {}).get("embedder", False)
        print(f"  服务状态: ✅")
        print(f"  Embedder: {'✅' if embedder_ok else '❌'}")
        if not embedder_ok:
            print("  ❌ Embedder 未加载，无法测试。")
            return
    except Exception:
        print("  ❌ 服务未启动！请先运行: python start.py")
        return

    # 生成语料
    print(f"\n  生成 {count} 条语料...")
    pool = generate_memories(count)
    avg_len = sum(len(c) for c, _ in pool) / len(pool)
    print(f"  字符数: 平均={avg_len:.0f}, 最小={min(len(c) for c, _ in pool)}, 最大={max(len(c) for c, _ in pool)}")

    # ── 阶段 1：批量写入 ──
    print(f"\n{'='*60}")
    print(f"  阶段 1：批量写入 {count} 条记忆")
    print(f"{'='*60}")

    write_latencies = []
    write_errors = 0
    batch_size = 50
    total_batches = (count + batch_size - 1) // batch_size

    t_write_start = time.perf_counter()

    for b in range(total_batches):
        batch_pool = pool[b * batch_size:(b + 1) * batch_size]
        t_batch = time.perf_counter()

        for i, (content, source) in enumerate(batch_pool):
            mem_id = f"stress-{b * batch_size + i:05d}"
            try:
                t1 = time.perf_counter()
                r = session.post(f"{BASE}/manage/memory/put", json={
                    "memoryId": mem_id,
                    "content": content,
                    "confidence": round(random.uniform(0.7, 1.0), 2),
                    "source": source,
                }, timeout=10)
                elapsed = (time.perf_counter() - t1) * 1000
                write_latencies.append(elapsed)
                if r.status_code != 200:
                    write_errors += 1
            except Exception:
                write_errors += 1

        batch_elapsed = time.perf_counter() - t_batch
        done = min((b + 1) * batch_size, count)
        recent = write_latencies[-batch_size:]
        avg_recent = sum(recent) / len(recent) if recent else 0
        print(f"  批次 {b + 1}/{total_batches}: {done}/{count} 条, "
              f"本批 {batch_elapsed:.1f}s, 均值 {avg_recent:.0f}ms/条")

    total_write_time = time.perf_counter() - t_write_start

    wls = sorted(write_latencies) if write_latencies else [0]
    print(f"\n  ── 写入结果 ──")
    print(f"  总条数: {count}")
    print(f"  总耗时: {total_write_time:.1f}s")
    print(f"  吞吐量: {count / total_write_time:.1f} 条/秒")
    print(f"  错误数: {write_errors}")
    print(f"  延迟: 平均={sum(wls) / len(wls):.0f}ms, "
          f"最小={wls[0]:.0f}ms, 最大={wls[-1]:.0f}ms, "
          f"P50={wls[len(wls) // 2]:.0f}ms, P95={wls[int(len(wls) * 0.95)]:.0f}ms, P99={wls[int(len(wls) * 0.99)]:.0f}ms")

    # ── 阶段 2：冲突检测验证 ──
    #   关键：冲突检测是「降权旧记忆」，不是降权新记忆。
    #   所以测试流程：
    #     1. 先写 5 条锚点记忆（原始置信度 0.9）
    #     2. 等 embedding 完成
    #     3. 再写 5 条语义冲突记忆（相似但不同内容）
    #     4. 等冲突检测完成
    #     5. 检查锚点记忆的置信度是否被降权
    print(f"\n{'='*60}")
    print(f"  阶段 2：冲突检测验证")
    print(f"{'='*60}")

    print("  等待阶段1后台冲突检测完成（10s）...")
    time.sleep(10)

    # Step 1: 写入 5 条锚点记忆（原始置信度 0.9）
    anchor_cases = [
        ("anchor-001", "用户最近开始对法语产生了浓厚的兴趣，每天都会花至少一个小时来学习法语的基础语法和常用表达", 0.9, "user"),
        ("anchor-002", "用户正在深入学习Rust编程语言，已经完成了基础语法部分的学习，正在尝试用Rust开发一个小型项目", 0.9, "user"),
        ("anchor-003", "用户正在系统学习计算机视觉，从理论基础开始逐步深入到实际应用，希望能在工作中落地相关技术方案", 0.9, "user"),
        ("anchor-004", "用户每周坚持进行游泳训练，已经养成了规律的运动习惯，体能和技巧都有了明显提升", 0.9, "user"),
        ("anchor-005", "用户对粤菜非常感兴趣，已经尝试制作了多种经典菜品，正在研究更地道的做法和食材搭配", 0.9, "user"),
    ]

    print("\n  Step 1: 写入锚点记忆（原始置信度 0.9）：")
    for mem_id, content, conf, source in anchor_cases:
        try:
            t1 = time.perf_counter()
            r = session.post(f"{BASE}/manage/memory/put", json={
                "memoryId": mem_id,
                "content": content,
                "confidence": conf,
                "source": source,
            }, timeout=10)
            elapsed = (time.perf_counter() - t1) * 1000
            d = r.json()
            check = d.get("Data", {}).get("conflictCheck", "")
            print(f"    {mem_id}: latency={elapsed:.0f}ms, conflictCheck={check}")
        except Exception as e:
            print(f"    {mem_id}: 写入失败 - {e}")

    # Step 2: 等待锚点记忆的 embedding 完成
    print("\n  等待锚点记忆 embedding 完成（8s）...")
    time.sleep(8)

    # Step 3: 写入 5 条语义冲突记忆（语义相似但文本不同，触发冲突检测）
    conflict_cases = [
        ("conflict-001", "用户最近对法语学习产生了极大的热情，每天至少花一个小时研习法语的基本语法和日常用语", "user"),
        ("conflict-002", "用户最近在系统学习Rust编程，已经搞定了基础语法，正准备用Rust来构建一个小项目练手", "user"),
        ("conflict-003", "用户正在从理论基础入手学习计算机视觉技术，希望能将相关技术方案在实际工作中落地", "user"),
        ("conflict-004", "用户一直坚持游泳锻炼，已经形成了规律的运动习惯，体力和游泳技巧都得到了显著提高", "user"),
        ("conflict-005", "用户对粤式料理非常着迷，已经尝试做了不少经典粤菜，正在探索更正宗的做法和食材组合", "user"),
    ]

    print("\n  Step 3: 写入语义冲突记忆（相似内容，应触发对锚点记忆的降权）：")
    for mem_id, content, source in conflict_cases:
        try:
            t1 = time.perf_counter()
            r = session.post(f"{BASE}/manage/memory/put", json={
                "memoryId": mem_id,
                "content": content,
                "confidence": 0.9,
                "source": source,
            }, timeout=10)
            elapsed = (time.perf_counter() - t1) * 1000
            d = r.json()
            check = d.get("Data", {}).get("conflictCheck", "")
            print(f"    {mem_id}: latency={elapsed:.0f}ms, conflictCheck={check}")
        except Exception as e:
            print(f"    {mem_id}: 写入失败 - {e}")

    # Step 4: 等待冲突检测完成
    print("\n  等待冲突检测完成（10s）...")
    time.sleep(10)

    # Step 5: 检查锚点记忆的置信度（应被降权：0.9 * (1-0.6) = 0.36）
    print("\n  Step 5: 检查锚点记忆置信度（原始=0.9, 若降权→0.36 说明冲突检测生效）：")
    demoted_count = 0
    for mem_id, _, _, _ in anchor_cases:
        try:
            r = session.get(f"{BASE}/manage/memory/{mem_id}", timeout=5)
            d = r.json()
            data = d.get("Data", {})
            conf = data.get("confidence", "N/A")
            is_demoted = conf != "N/A" and conf < 0.9
            demoted_count += 1 if is_demoted else 0
            status = "✅ 已降权" if is_demoted else "❌ 未降权"
            print(f"    {mem_id}: confidence={conf} {status}")
        except:
            print(f"    {mem_id}: 查询失败")

    print(f"\n  冲突检测生效: {demoted_count}/5 条锚点记忆被降权")

    print(f"\n{'='*60}")
    print(f"  测试完成")
    print(f"{'='*60}")


if __name__ == "__main__":
    main()
