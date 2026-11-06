FROM python:3.12-slim
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY engine/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY engine/ ./engine/
COPY attacks/ ./attacks/
WORKDIR /app/engine
CMD ["uvicorn", "breach_engine.api:app", "--host", "0.0.0.0", "--port", "8000"]
