#!/usr/bin/env python3
"""
CuteBlogGuard 启动入口。

用法：
  python start.py                    # 默认 0.0.0.0:8900
  python start.py --port 9000        # 自定义端口
  python start.py --host 127.0.0.1   # 自定义绑定地址
  python start.py --reload           # 开发模式（自动重载）
  python start.py --workers 4        # 生产多 worker

环境变量：
  见 .env.example
"""

import argparse
import sys


def main():
    parser = argparse.ArgumentParser(description="CuteBlogGuard 安全护栏微服务")
    parser.add_argument("--host", default="0.0.0.0", help="绑定地址 (默认 0.0.0.0)")
    parser.add_argument("--port", type=int, default=8900, help="端口 (默认 8900)")
    parser.add_argument("--workers", type=int, default=1, help="Worker 数量 (生产建议 4)")
    parser.add_argument("--reload", action="store_true", help="开发模式（文件变更自动重载）")
    parser.add_argument("--log-level", default="info", choices=["debug", "info", "warning", "error"], help="日志级别")
    args = parser.parse_args()

    try:
        import uvicorn
    except ImportError:
        print("❌ 缺少 uvicorn，请先安装：pip install uvicorn", file=sys.stderr)
        sys.exit(1)

    # 尝试加载 .env
    try:
        from dotenv import load_dotenv
        load_dotenv()
    except ImportError:
        pass

    print(f"🚀 CuteBlogGuard 启动中... {args.host}:{args.port} (workers={args.workers})")

    uvicorn.run(
        "guard_service:app",
        host=args.host,
        port=args.port,
        workers=1 if args.reload else args.workers,
        reload=args.reload,
        log_level=args.log_level,
    )


if __name__ == "__main__":
    main()
