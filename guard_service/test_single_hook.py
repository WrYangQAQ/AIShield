"""单条记忆写入计时测试 v2 — 定位 handler 外的 ~2070ms 黑洞

改动：
1. 用 127.0.0.1 替代 localhost（避免 Windows DNS 解析 IPv6 回退）
2. 先发 warmup 请求排除首次延迟
3. 分别计时：连接建立 / 请求发送 / 响应接收
"""
import requests, time

BASE = "http://127.0.0.1:8900"
session = requests.Session()  # 连接池复用

# ── 1. 健康检查 ──
print("╔══════════════════════════════════════════════╗")
print("║  单条记忆写入瓶颈定位 v2                      ║")
print("╚══════════════════════════════════════════════╝")

t0 = time.perf_counter()
r = session.get(f"{BASE}/health", timeout=5)
t1 = time.perf_counter()
print(f"健康检查: {r.status_code}, 耗时: {(t1-t0)*1000:.1f}ms")
deps = r.json().get("dependencies", {})
print(f"Embedder: {'✅' if deps.get('embedder') else '❌'}")
print()

# ── 2. Warmup 请求（排除首次 Pydantic/路由编译延迟） ──
warmup = {
    "memoryId": "warmup-000",
    "content": "warmup warmup",
    "confidence": 0.5,
    "source": "system",
}
t0 = time.perf_counter()
r = session.post(f"{BASE}/manage/memory/put", json=warmup, timeout=30)
t1 = time.perf_counter()
print(f"Warmup: {r.status_code}, 耗时: {(t1-t0)*1000:.1f}ms")
print()

# ── 3. 正式测试 ──
payload = {
    "memoryId": "hook-test-001",
    "content": "用户最近开始对法语产生了浓厚的兴趣，每天都会花至少一个小时来学习法语的基础语法和常用表达",
    "confidence": 0.9,
    "source": "user",
}

print("写入 1 条记忆 (127.0.0.1, 连接池复用)...")
t0 = time.perf_counter()
r = session.post(f"{BASE}/manage/memory/put", json=payload, timeout=30)
t1 = time.perf_counter()
elapsed = (t1 - t0) * 1000
print(f"HTTP 响应: {r.status_code}, 耗时: {elapsed:.1f}ms")
print(f"返回: {r.json()}")
print()

print("等待 3 秒看后台冲突检测 HOOK 日志...")
time.sleep(3)
print("完成。请查看服务终端的 ⏱ HOOK 日志。")
print()
print("关键看 [middleware] HOOK 的 total —— 如果 ~2ms 说明瓶颈在客户端/DNS，如果 ~2070ms 说明瓶颈在服务端框架层。")
