"""
快速诊断：测试 fastembed 本地 embedding 是否正常工作。
不依赖 guard_service，直接 import fastembed 跑一遍。

用法：
  python diag_embedder.py
"""

import os
import sys
import time

print("=" * 60)
print("fastembed 诊断脚本")
print("=" * 60)

# 1. Python 版本
print(f"\n[1] Python: {sys.version}")
print(f"    路径: {sys.executable}")

# 2. fastembed 是否已安装
print("\n[2] 检查 fastembed ...")
try:
    import fastembed
    print(f"    ✅ fastembed 版本: {fastembed.__version__}")
except ImportError as e:
    print(f"    ❌ fastembed 未安装: {e}")
    print("    修复: pip install fastembed")
    sys.exit(1)

# 3. onnxruntime 是否可用
print("\n[3] 检查 onnxruntime ...")
try:
    import onnxruntime
    print(f"    ✅ onnxruntime 版本: {onnxruntime.__version__}")
    providers = onnxruntime.get_available_providers()
    print(f"    可用 Providers: {providers}")
except ImportError as e:
    print(f"    ❌ onnxruntime 未安装: {e}")
    sys.exit(1)

# 4. 模型加载
print("\n[4] 加载模型 BAAI/bge-small-zh-v1.5 ...")
t0 = time.perf_counter()
try:
    from fastembed import TextEmbedding
    model = TextEmbedding("BAAI/bge-small-zh-v1.5")
    t1 = time.perf_counter()
    print(f"    ✅ 模型加载成功，耗时 {t1 - t0:.1f}s")
except Exception as e:
    t1 = time.perf_counter()
    print(f"    ❌ 模型加载失败 ({t1 - t0:.1f}s): {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

# 5. 单条 embed
print("\n[5] 单条 embedding 测试 ...")
try:
    t0 = time.perf_counter()
    result = list(model.embed(["用户最近开始对法语产生了浓厚的兴趣"]))
    t1 = time.perf_counter()
    vec = result[0].tolist()
    print(f"    ✅ 单条 embed 成功，耗时 {t1 - t0:.3f}s，维度 {len(vec)}")
    print(f"    前5维: {vec[:5]}")
except Exception as e:
    print(f"    ❌ embed 失败: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

# 6. 批量 embed
print("\n[6] 批量 embedding 测试 (4条) ...")
texts = [
    "用户最近开始对法语产生了浓厚的兴趣",
    "用户最近对法语学习产生了极大的热情",  # 语义相似
    "今天天气晴朗适合户外运动",
    "Rust 语言的所有权机制确保内存安全",
]
try:
    t0 = time.perf_counter()
    results = list(model.embed(texts))
    t1 = time.perf_counter()
    print(f"    ✅ 批量 embed 成功，耗时 {t1 - t0:.3f}s，{len(results)} 条结果")
except Exception as e:
    print(f"    ❌ 批量 embed 失败: {e}")
    sys.exit(1)

# 7. 余弦相似度计算
print("\n[7] 余弦相似度验证 ...")
import numpy as np
vecs = np.array([r.tolist() for r in results])
norms = np.linalg.norm(vecs, axis=1, keepdims=True)
vecs_n = vecs / norms
sim_matrix = vecs_n @ vecs_n.T
labels = ["法语兴趣", "法语热情", "天气", "Rust"]
print("     " + "  ".join(f"{l:>8}" for l in labels))
for i, label in enumerate(labels):
    row = "  ".join(f"{sim_matrix[i][j]:8.4f}" for j in range(len(labels)))
    print(f"  {label:>6}  {row}")

# 8. 关键结论
sim_01 = sim_matrix[0][1]  # 法语兴趣 vs 法语热情
print(f"\n[8] 结论：")
print(f"    '法语兴趣' vs '法语热情' 相似度 = {sim_01:.4f}")
print(f"    当前冲突阈值 = 0.75")
if sim_01 >= 0.75:
    print(f"    ✅ 高于阈值，冲突检测应该能触发")
else:
    print(f"    ⚠️  低于阈值！bge-small-zh-v1.5 对改写文本的相似度不够高")
    print(f"    建议：降低 conflict_similarity_threshold 到 {sim_01 - 0.05:.2f}")

# 9. 模型缓存位置
print(f"\n[9] 模型缓存位置 ...")
try:
    from pathlib import Path
    # fastembed 默认缓存在 ~/.cache/fastembed 或 FASTEMBED_CACHE_PATH
    cache_dir = os.environ.get("FASTEMBED_CACHE_PATH", "")
    if not cache_dir:
        home = Path.home()
        cache_dir = str(home / ".cache" / "fastembed")
    print(f"    FASTEMBED_CACHE_PATH: {cache_dir}")
    if Path(cache_dir).exists():
        size = sum(f.stat().st_size for f in Path(cache_dir).rglob("*") if f.is_file())
        print(f"    缓存大小: {size / 1024 / 1024:.1f} MB")
    else:
        print(f"    ⚠️  缓存目录不存在")
except Exception as e:
    print(f"    检查缓存位置失败: {e}")

print("\n" + "=" * 60)
print("诊断完成")
print("=" * 60)
