FROM python:3.12-slim
RUN apt-get update && apt-get install -y --no-install-recommends git curl ca-certificates && \
    curl -fsSL https://deb.nodesource.com/setup_22.x | bash - && \
    apt-get install -y --no-install-recommends nodejs && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY engine/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY engine/ ./engine/
COPY attacks/ ./attacks/
COPY site/package.json ./site/
RUN cd site && npm install --omit=dev
WORKDIR /app/engine
CMD ["uvicorn", "breach_engine.api:app", "--host", "0.0.0.0", "--port", "8000"]
