FROM python:3.13-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

EXPOSE 8900

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8900/health')" || exit 1

# ⚠️ 默认单 worker：无 OPENAI_API_KEY 时使用 LocalEmbedder（子进程 ~300MB/个），
#    多 worker 会倍增内存。有远程 Embedding API 时可改为 --workers 4。
CMD ["python", "start.py", "--host", "0.0.0.0", "--port", "8900", "--workers", "1"]
