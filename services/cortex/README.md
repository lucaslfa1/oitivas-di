# Sentinel Media Processor

Microserviço Python para pré-processamento de mídia antes da análise com Gemini AI.

## Funcionalidades

### Processamento de Áudio
- Normalização de volume (target -16 dBFS)
- Redução de ruído (noisereduce)
- Compressão dinâmica
- Conversão para MP3 otimizado

### Processamento de Vídeo
- Extração de keyframes
- Análise de qualidade

### Cache Inteligente
- Cache de resultados baseado em hash SHA-256
- TTL configurável (default: 24h)
- LRU eviction para controle de memória
- Endpoints para gerenciamento do cache

## Endpoints

| Método | Endpoint | Descrição |
|--------|----------|-----------|
| GET | `/health` | Health check |
| GET | `/cache/stats` | Estatísticas do cache |
| POST | `/cache/clear` | Limpa todo o cache |
| POST | `/cache/cleanup` | Remove entradas expiradas |
| POST | `/process/audio` | Processa áudio |
| POST | `/process/video` | Processa vídeo (keyframes) |

## Instalação

```bash
# Criar ambiente virtual
python -m venv .venv

# Ativar (Windows)
.venv\Scripts\activate

# Ativar (Linux/Mac)
source .venv/bin/activate

# Instalar dependências
pip install -r requirements.txt
```

## Execução

```bash
# Desenvolvimento
python main.py

# Ou com uvicorn
uvicorn main:app --reload --host 0.0.0.0 --port 8000
```

## Docker

```bash
# Build
docker build -t sinistro-processor .

# Run
docker run -p 8000:8000 sinistro-processor
```

## Uso do Cache

O cache é automático para processamento de áudio:

```python
# Com cache (padrão)
POST /process/audio

# Sem cache
POST /process/audio?use_cache=false
```

Resposta inclui campo `cached: true/false` indicando se veio do cache.

## Dependências

- **FastAPI**: Framework web async
- **Uvicorn**: Servidor ASGI
- **Pydub**: Manipulação de áudio
- **Noisereduce**: Redução de ruído
- **NumPy**: Processamento numérico

## Integração com Backend .NET

O backend .NET pode chamar este serviço via HTTP:

```csharp
var response = await httpClient.PostAsync(
    "http://localhost:8000/process/audio",
    new MultipartFormDataContent { { audioContent, "file", filename } }
);
```

