"""快速验证：子进程 embedding 是否能独立运行"""
import multiprocessing as mp
import time
import os

def worker(model_name, req_q, resp_q):
    from fastembed import TextEmbedding
    model = TextEmbedding(model_name)
    resp_q.put("READY")
    print(f"[子进程] 就绪 pid={os.getpid()}", flush=True)
    while True:
        item = req_q.get()
        if item == "SHUTDOWN":
            break
        texts = item
        results = list(model.embed(texts))
        resp_q.put([r.tolist() for r in results])

if __name__ == "__main__":
    mp.freeze_support()  # Windows 必需
    
    print(f"[主进程] pid={os.getpid()}", flush=True)
    
    req_q = mp.Queue()
    resp_q = mp.Queue()
    
    p = mp.Process(target=worker, args=("BAAI/bge-small-zh-v1.5", req_q, resp_q), daemon=True)
    p.start()
    
    signal = resp_q.get(timeout=120)
    print(f"[主进程] 收到信号: {signal}", flush=True)
    print(f"[主进程] 子进程存活: {p.is_alive()}, pid={p.pid}", flush=True)
    
    # 测试推理
    t0 = time.perf_counter()
    req_q.put(["你好世界", "测试文本"])
    result = resp_q.get(timeout=30)
    t1 = time.perf_counter()
    print(f"[主进程] 推理耗时: {t1-t0:.3f}s, 结果维度: {len(result)}x{len(result[0])}", flush=True)
    
    req_q.put("SHUTDOWN")
    p.join(timeout=5)
    print(f"[主进程] 子进程已退出", flush=True)
