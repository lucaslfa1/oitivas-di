FROM python:3.11-slim

# Instalar ffmpeg (necessário para pydub)
RUN apt-get update && apt-get install -y \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copiar requirements e instalar dependências
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copiar código
COPY . .

# Porta padrão 8000 (dev local). Cloud Run sobrescreve via env var PORT.
ENV PORT=8000
EXPOSE 8000

# Comando de entrada - usa shell form para expandir $PORT
CMD uvicorn main:app --host 0.0.0.0 --port $PORT
