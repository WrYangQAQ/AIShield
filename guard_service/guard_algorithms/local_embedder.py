"""
本地 Embedder — 子进程隔离版（Windows 兼容）

用 multiprocessing.Process + Queue 将 fastembed 推理完全移出主进程，
彻底消除 GIL 对事件循环的阻塞。

架构：
  主进程 (FastAPI/uvicorn)          子进程 (fastembed)
  ┌─────────────────────┐          ┌─────────────────────┐
  │ embed_batch()       │──req_q──▶│ _worker()           │
  │   → 分配 req_id     │          │   model.embed()     │
  │   → 创建 Future     │          │   → 结果放 resp_q   │
  │ _listen_thread      │◀─resp_q──│                     │
  │   → 匹配 req_id     │          └─────────────────────┘
  │   → set_result()    │
  └─────────────────────┘

启动同步机制：
  使用 threading.Event 代替双消费者抢队列。
  监听线程独占 resp_queue，收到 READY 时 set event，start() 等 event 即可。
"""

import multiprocessing as mp
import asyncio
import logging
import os
import sys
import threading
import traceback
import uuid
from typing import Optional

logger = logging.getLogger("guard_service")


class LocalEmbedder:
    """子进程隔离的本地 Embedder，GIL 零干扰。"""

    def __init__(self, model_name: str = "BAAI/bge-small-zh-v1.5", startup_timeout: int = 180):
        self.model_name = model_name
        self.startup_timeout = startup_timeout
        self._process: Optional[mp.Process] = None
        self._req_queue: Optional[mp.Queue] = None
        self._resp_queue: Optional[mp.Queue] = None
        self._ready = False
        self._pending: dict[str, asyncio.Future] = {}
        self._listener_thread: Optional[threading.Thread] = None
        self._lock = threading.Lock()
        # 启动同步：监听线程设置 event，start() 等待
        self._ready_event = threading.Event()
        self._startup_error: Optional[str] = None

    # ── 子进程入口 ──────────────────────────────────────────

    @staticmethod
    def _worker(model_name: str, req_queue: mp.Queue, resp_queue: mp.Queue):
        """子进程：加载模型，循环处理 embedding 请求。"""
        try:
            resp_queue.put(("LOG", "子进程启动, pid=%d, python=%s" % (os.getpid(), sys.executable)))

            resp_queue.put(("LOG", "正在 import fastembed ..."))
            from fastembed import TextEmbedding
            resp_queue.put(("LOG", "fastembed import 成功"))

            resp_queue.put(("LOG", "正在加载模型 %s ..." % model_name))
            model = TextEmbedding(model_name)
            resp_queue.put(("LOG", "模型加载成功"))

            # 快速自检：试 embed 一条文本
            resp_queue.put(("LOG", "自检：试 embed 一条文本 ..."))
            test_result = list(model.embed(["测试"]))
            dim = len(test_result[0].tolist()) if test_result else 0
            resp_queue.put(("LOG", "自检通过, dim=%d" % dim))

            resp_queue.put(("READY", os.getpid()))
        except Exception as e:
            tb = traceback.format_exc()
            resp_queue.put(("STARTUP_ERROR", str(e), tb))
            return  # 子进程退出

        while True:
            try:
                item = req_queue.get()
            except Exception:
                break
            if item is None:  # SHUTDOWN
                resp_queue.put(("DONE", None))
                break
            req_id, texts = item
            try:
                # fastembed embed() 返回生成器，转 list
                results = list(model.embed(texts))
                resp_queue.put((req_id, "OK", [r.tolist() for r in results]))
            except Exception as e:
                resp_queue.put((req_id, "ERROR", str(e)))

    # ── 后台响应分发线程 ────────────────────────────────────

    def _listen_responses(self):
        """
        后台线程：独占 resp_queue，负责所有消息的读取和分发。

        消息类型：
          ("READY", pid)          — 子进程就绪，set _ready_event
          ("STARTUP_ERROR", msg, tb) — 子进程启动失败，set _ready_event
          ("LOG", msg)            — 子进程日志，打印到主进程 logger
          ("DONE", None)          — 关闭信号
          (req_id, "OK", data)    — embedding 结果，set Future
          (req_id, "ERROR", msg)  — embedding 错误，set Future exception
        """
        while True:
            try:
                item = self._resp_queue.get(timeout=1.0)
            except Exception:
                # 超时：检查子进程是否还活着
                if self._process is not None and not self._process.is_alive():
                    # 子进程已死
                    if not self._ready_event.is_set():
                        self._startup_error = "子进程异常退出, exitcode=%d" % (self._process.exitcode or -1)
                        self._ready_event.set()
                    break
                # 正常运行中，继续等
                continue

            if not isinstance(item, tuple) or len(item) == 0:
                continue

            tag = item[0]

            if tag == "READY":
                self._ready = True
                self._ready_event.set()  # 通知 start() 子进程就绪
                logger.info("🔧 Embedder子进程: 就绪, pid=%d", item[1] if len(item) > 1 else "?")
                continue

            if tag == "STARTUP_ERROR":
                self._startup_error = "%s\n%s" % (item[1], item[2] if len(item) > 2 else "")
                self._ready_event.set()  # 通知 start() 子进程失败
                logger.error("🔧 Embedder子进程启动失败: %s", item[1])
                continue

            if tag == "LOG":
                logger.info("🔧 Embedder子进程: %s", item[1])
                continue

            if tag == "DONE":
                break

            # 正常响应: (req_id, status, data)
            if len(item) == 3:
                req_id, status, data = item
                with self._lock:
                    future = self._pending.pop(req_id, None)
                if future is not None and not future.done():
                    if status == "OK":
                        future.set_result(data)
                    else:
                        future.set_exception(RuntimeError(f"Embedding 子进程错误: {data}"))

    # ── 公开接口 ────────────────────────────────────────────

    async def start(self):
        """启动子进程和响应监听线程。必须在 lifespan 中调用。"""
        self._req_queue = mp.Queue()
        self._resp_queue = mp.Queue()
        self._ready_event.clear()
        self._startup_error = None

        self._process = mp.Process(
            target=LocalEmbedder._worker,
            args=(self.model_name, self._req_queue, self._resp_queue),
            daemon=True,
        )
        self._process.start()
        logger.info("Embedder 子进程已 fork, pid=%d, 等待就绪 (timeout=%ds) ...", self._process.pid, self.startup_timeout)

        # 先启动监听线程（它独占 resp_queue，处理所有消息包括 READY）
        self._listener_thread = threading.Thread(
            target=self._listen_responses, daemon=True
        )
        self._listener_thread.start()

        # 等待就绪事件（由监听线程设置 _ready_event）
        loop = asyncio.get_event_loop()
        try:
            await asyncio.wait_for(
                loop.run_in_executor(None, self._ready_event.wait),
                timeout=self.startup_timeout,
            )
        except asyncio.TimeoutError:
            alive = self._process.is_alive() if self._process else False
            exitcode = self._process.exitcode if self._process else None
            self._ready = False
            if self._process and self._process.is_alive():
                self._process.kill()
            raise RuntimeError(
                f"Embedding 子进程启动超时 ({self.startup_timeout}s). "
                f"子进程 alive={alive}, exitcode={exitcode}"
            )

        # 检查是成功还是失败
        if self._startup_error is not None:
            self._ready = False
            raise RuntimeError(f"Embedding 子进程启动失败: {self._startup_error}")

        logger.info("Embedding 子进程就绪 pid=%d", self._process.pid)

    async def embed(self, text: str) -> list[float]:
        """单条文本 embedding。"""
        return (await self.embed_batch([text]))[0]

    async def embed_batch(self, texts: list[str]) -> list[list[float]]:
        """批量文本 embedding，不阻塞事件循环。"""
        if not self._ready:
            raise RuntimeError("Embedder 未就绪，请先调用 start()")

        import time as _time
        t0 = _time.perf_counter()

        req_id = uuid.uuid4().hex[:12]
        loop = asyncio.get_event_loop()
        future = loop.create_future()

        with self._lock:
            self._pending[req_id] = future

        t1 = _time.perf_counter()
        self._req_queue.put((req_id, texts))
        t2 = _time.perf_counter()

        try:
            result = await future
            t3 = _time.perf_counter()
            logger.info(
                "⏱ HOOK [embed_batch] n=%d: alloc_future=%.2fms put_queue=%.2fms await_result=%.2fms total=%.2fms",
                len(texts),
                (t1 - t0) * 1000,
                (t2 - t1) * 1000,
                (t3 - t2) * 1000,
                (t3 - t0) * 1000,
            )
            return result
        except asyncio.CancelledError:
            with self._lock:
                self._pending.pop(req_id, None)
            raise

    async def close(self):
        """关闭子进程，清理资源。"""
        self._ready = False
        if self._process and self._process.is_alive():
            self._req_queue.put(None)  # SHUTDOWN signal
            self._process.join(timeout=10)
            if self._process.is_alive():
                logger.warning("Embedding 子进程未正常退出，强制终止")
                self._process.kill()
                self._process.join(timeout=3)
        logger.info("Embedding 子进程已关闭")

    def is_ready(self) -> bool:
        return self._ready and self._process is not None and self._process.is_alive()
