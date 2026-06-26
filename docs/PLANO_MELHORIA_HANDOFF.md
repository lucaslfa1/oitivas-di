# Plano de Melhoria para Handoff — Sentinel (oitivas-di)

> Documento de preparação do repositório para entrega ao time de desenvolvimento.
> Objetivos: (1) documentar cada função explicando **como** ela funciona e (2) reorganizar
> os diretórios para facilitar a implantação.
>
> Gerado a partir de análise de todos os componentes em 2026-06-26.

---

## Sumário executivo

O projeto é um **monorepo poliglota** com 3 aplicações deployáveis:

| App | Stack | Pasta atual |
|---|---|---|
| **API principal** (`SinistroAPI`) | C# / ASP.NET Core 8 + EF Core (SQLite) + SignalR + SPA vanilla JS | `Backend/` (frontend em `Backend/wwwroot/`) |
| **Media Processor** (`sentinel-cortex`) | Python 3.11 / FastAPI (ffmpeg, pydub, opencv) | `sentinel-cortex/` |
| **Dashboard KPI** | Python 3.9 / Dash + Flask + gunicorn | `dashboard-kpi/` |

**Arquitetura real = Azure-only**: Azure Speech-to-Text + Azure OpenAI (GPT-4o / Whisper) + Azure Text Analytics. Deploy real = Google Cloud Run via `gcloud` (`deploy.ps1`).

**Os 5 problemas que bloqueiam o handoff:**

1. 🔴 **Segredos versionados em repo público** (chaves Azure, senhas) — Fase 0.
2. 🟠 **Raiz poluída** com ~40 arquivos de lixo/scratch — Fase 1.
3. 🟡 **Estrutura não modular** (apps espalhados, `Backend/` confunde) — Fase 2.
4. 🟢 **Comentários parciais e desiguais** — Fase 3 (objetivo central).
5. 🟡 **Docs desatualizadas + código morto + 1 bug latente** — Fase 4.

A ordem recomendada é **0 → 1 → 4 → 2 → 3** (segurança, limpeza, correções baratas, reorganização e, por fim, documentação sobre a base já limpa — para não documentar código morto).

---

## Fase 0 — Segurança (BLOQUEADOR)

O repositório `github.com/lucaslfa1/oitivas-di` é **público** e contém credenciais reais versionadas no HEAD **e no histórico**.

| Arquivo | Segredo exposto |
|---|---|
| `Backend/appsettings.json` | Chaves Azure Speech (`SpeechToTextKey`, `SubscriptionKey`), Azure OpenAI (`ApiKey`) e Azure Text Analytics (`Key`), incluindo blocos `Fallback` |
| `test_payload.cs` (raiz) | Chave Azure Cognitive Speech hardcoded (região eastus2) |
| `dashboard-kpi/users.json` | Senhas de usuários em texto plano (arquivo morto — auth real é via `BACKEND_AUTH_URL`) |
| `Backend/Data/AppDbContext.cs` | Seed com ~8 usuários e senhas em texto plano; `Program.cs` recria usuário com senha fixa |
| `sentinel-cortex/core/config.py` | `secret_key` JWT hardcoded |
| `sentinel-cortex/routers/v1/auth.py` | `FAKE_USERS_DB` com nomes reais e senhas em claro |

### Ações (nesta ordem)

1. **Rotacionar TODAS as chaves Azure** no portal (Speech, OpenAI, Text Analytics) — trate-as como comprometidas.
2. **Trocar todas as senhas** dos usuários reais.
3. **Hashear senhas** no código (ASP.NET Core `PasswordHasher` / `passlib` no cortex). Hoje `AuthController.Login/Register` compara senha em texto puro.
4. **Remover usuários de teste** do seed (`admin/admin`, `teste/teste123`) e o `FAKE_USERS_DB`.
5. **Tirar segredos do código**: `appsettings.json` fica só com placeholders; segredos vão para `appsettings.Local.json` (dev) e variáveis de ambiente / Secret Manager (prod). O `cortex` já usa `env_prefix SENTINEL_`, então basta injetar `SENTINEL_SECRET_KEY`.
6. **Expurgar do histórico** com `git filter-repo` ou BFG (remover do HEAD **não basta** — o histórico público continua acessível). Considere deixar o repo **privado** durante a limpeza.

> **Importante:** mesmo após a limpeza do HEAD, as chaves antigas devem ser consideradas vazadas para sempre. A rotação (passo 1) é o que realmente protege.

---

## Fase 1 — Limpeza da raiz (baixo risco, ganho imediato)

A raiz tem ~60 itens; ~40 são lixo. Após esta fase ela cai para ~10 itens legíveis.

### Remover (lixo de runtime / dumps / scratch)

**Logs e dumps:**
`backend_logs.txt`, `fresh_logs.txt`, `fresh_errors.txt`, `fresh_cortex.txt`, `sentinel_cortex_logs.txt`, `postgres_search.txt`, `report.txt`, `dotnet-test-output.txt`, `dummy.txt`, `merged_python.json`, `models_full.json`, `payload_output.json`, `package-lock.json` (lockfile npm órfão, sem `package.json`).

**Scripts scratch (PowerShell):**
`test_final_logic.ps1` + `_fix/_v3/_v4/_v5`, `test_metrics.ps1` + `_debug/_found/_found_fix/_final`, `test_audit.ps1`, `test_extraction.ps1`, `test_model.ps1`, `test_python_endpoint.ps1`, `run_audit_test.ps1` + `_debug`, `run_python.ps1`, `reproduce_crash.ps1`.

**Scripts mortos de Gemini** (integração já removida no commit `e885a6b`):
`list_models.ps1`, `list_models_names.ps1`, `list_models_full.ps1`, `list_models_to_file.ps1`, `test_transcription.ps1`, `test_google_speech.ps1`.

**Scripts scratch (Python):**
`test_request.py`, `test_payload.py`, `test_merge.py`, `test_integration.py`.

**Segredo (já coberto na Fase 0):** `test_payload.cs`.

**Pastas de memorandos internos** (não são código; expõem IP de rede interna):
`compartilhamento/` (instruções de firewall com `172.16.138.111:5252`), `defesa_tecnica/` (pedido de orçamento à gestão).

**Em `Backend/`:** `build_errors.txt`, `build_output.txt`, `log.txt`, `Interfaces/IAnaliseService.cs` (vazio, 0 bytes).

**Em `Backend/wwwroot/`:** `assets/index-Dv96PTBn.js` (bundle órfão de ~911 KB, não referenciado), `index.html.backup-original`.

### Mover

| De | Para |
|---|---|
| `verify_install.ps1`, `generate_docx.py`, `gerar_doc_apis.py`, `converter.html` | `scripts/tools/` |
| Poucos `test_*.ps1` que valha guardar | `scripts/manual-tests/` |
| `deploy.ps1`, `deploy_test.ps1`, `setup_gcp.ps1` | `deploy/` |
| `ARQUITETURA.md`, `deploy_gcp.md` | `docs/` |

> Ao mover `converter.html`, atualizar/remover o endpoint `/converter` em `sentinel-cortex/main.py` (lê `../converter.html`). Recomendado **remover** o endpoint — acopla o microserviço ao repo pai.

### Estender o `.gitignore` (impedir reincidência)

```gitignore
# Logs e dumps de runtime
*_logs.txt
fresh_*.txt
backend_logs.txt
report.txt
dotnet-test-output.txt
postgres_search.txt
payload_output.json
merged_python.json
models_full.json
# Build do backend (.NET)
Backend/build_*.txt
Backend/log.txt
```

---

## Fase 2 — Reorganização de diretórios (risco médio — fazer em PR isolado)

Cada app vira uma pasta **autossuficiente** dentro de `services/`. Como o conteúdo interno de cada app não muda de posição relativa, **nenhum caminho interno quebra** — só muda o prefixo.

### Estrutura-alvo

```
oitivas-di/                         # raiz limpa: só entrypoints de topo
├─ README.md                        # visão geral + ordem de subida (atualizado Azure-only)
├─ docker-compose.yml               # orquestra os 3 services (contexts → services/*)
├─ .gitignore  .dockerignore        # estendidos
│
├─ services/                        # os 3 apps deployáveis
│  ├─ api/                          # ex-Backend (.NET SinistroAPI)
│  │  ├─ SinistroAPI.csproj
│  │  ├─ Dockerfile                 # CANÔNICO (usado por compose e deploy --source)
│  │  ├─ Program.cs
│  │  ├─ Controllers/ Services/ Models/ Data/ Hubs/ Configuration/ Interfaces/ Prompts/
│  │  ├─ appsettings.json           # SÓ placeholders
│  │  ├─ appsettings.Production.template.json
│  │  ├─ wwwroot/                   # SPA (servido cru pelo ASP.NET)
│  │  │  ├─ index.html  js/  layout/  assets/   # style.css → layout/global.css; assets só imagens
│  │  └─ tests/                     # ex-Backend.Tests (TFM alinhado a net8.0)
│  │
│  ├─ cortex/                       # ex-sentinel-cortex (FastAPI)
│  │  ├─ Dockerfile  cloudbuild.yaml  requirements.txt
│  │  ├─ main.py  core/ models/ routers/ services/
│  │  └─ scripts/inspect_routes.py  # debug movido p/ dentro do serviço
│  │
│  └─ dashboard/                    # ex-dashboard-kpi (Dash/Flask)
│     ├─ Dockerfile  Procfile  requirements.txt
│     ├─ app_dashboard.py  assets/
│     ├─ data/                      # Dim_Operador_Inferida.csv + Base_BI...xlsx (PII; idealmente fora do git)
│     └─ tests/                     # test_login.py, test_time_metrics.py → pytest
│
├─ deploy/                          # tudo de infra/entrega
│  ├─ deploy.ps1                    # prod (paths ajustados p/ services/api e services/cortex)
│  ├─ deploy_test.ps1               # sandbox (idealmente fundir em deploy.ps1 -Environment)
│  └─ setup_gcp.ps1                 # provisionamento GCP (marcar 'planejado/legado' se não reflete o real)
│
├─ scripts/
│  ├─ tools/                        # verify_install.ps1, generate_docx.py, gerar_doc_apis.py, converter.html
│  └─ manual-tests/                 # poucos test_*.ps1 que valham guardar
│
└─ docs/                            # documentação consolidada
   ├─ ARQUITETURA.md (atualizado Azure-only)  deploy_gcp.md (atualizar ou remover)
   ├─ doc-iso/  referencia-audio/  ARQUITETURA_FRONTEND.md  PLANO_AUTENTICACAO.md
   └─ .env.example                  # contrato único de TODAS as vars
```

### Movimentações principais (from → to)

| De | Para | Nota |
|---|---|---|
| `Backend/` | `services/api/` | Atualizar `deploy.ps1` (`Set-Location Backend`, `Backend\wwwroot\index.html`), `docker-compose` (`context: ./Backend`) e cloudbuild |
| `Backend/Backend.Tests/` | `services/api/tests/` | Reescrever `<ProjectReference>` e o `.sln`; alinhar TFM `net10`→`net8` |
| `Backend/wwwroot/style.css` | `services/api/wwwroot/layout/global.css` | Atualizar `<link>` em `index.html` |
| `sentinel-cortex/` | `services/cortex/` | Atualizar `deploy.ps1` e `docker-compose` (`context`) |
| `dashboard-kpi/` | `services/dashboard/` | Entrypoint `gunicorn app_dashboard:server` inalterado |
| `dashboard-kpi/*.csv` + `.xlsx` | `services/dashboard/data/` | Tornar `EXCEL_PATH`/`DIM_OPERADOR_PATH` configuráveis por env |
| `Dockerfile` (raiz) | `deploy/root.Dockerfile.legacy` ou **remover** | Órfão — `compose` usa `Backend/Dockerfile` e deploy usa `--source Backend` |

### Riscos e mitigação

- **Renomear `Backend/` quebra todo caminho com prefixo `Backend`** — corrigir `deploy.ps1`, `docker-compose.yml`, Dockerfile da raiz e cloudbuild **no mesmo commit**. Validar com `docker-compose build` + `gcloud run deploy --source` (dry-run) antes do merge.
- **`.sln` e `<ProjectReference>`** usam caminhos relativos — ajustar e rodar `dotnet build`/`dotnet test`.
- **`sentinel-cortex/main.py` lê `../converter.html`** — ao mover ambos, o path quebra. Remover o endpoint `/converter` (recomendado) ou levar o arquivo para dentro do serviço.
- **Dashboard carrega dados por caminho relativo ao CWD** — se rodar gunicorn de outro diretório, sobe com DataFrames vazios silenciosamente. Tornar paths configuráveis por env e **logar** (não `print`) quando faltar dado.
- **`.dockerignore` por serviço** — `dashboard-kpi/.dockerignore` ignora `check_*.py`/`test_*.py` mas **não** `debug_*.py` nem `users.json`. Revisar após o move para `data/`/segredos não entrarem na imagem.

---

## Fase 3 — Padrão de comentários e documentação das funções (objetivo central)

### Princípios

1. Comente o **PORQUÊ e o COMO**, não narre o óbvio. O código já diz o "o quê".
2. Toda função recebe **resumo de 1 linha + seção "COMO funciona"** (pipeline passo a passo). Obrigatório para heurísticas, scoring, orquestração e integração externa.
3. Documente o **contrato**: parâmetros (tipo + significado), retorno e exceções. Em JS sem TypeScript, o JSDoc é a única fonte de tipos.
4. Explicite **efeitos colaterais**: mutação de estado global, `Task.Run` fire-and-forget, reset de estado, listeners/observers que precisam de cleanup.
5. Justifique os **números mágicos** no ponto onde aparecem (35 MB, 70%, 1.6 s, pesos +1/+2, `no_speech_prob`, `avg_logprob`, `compression_ratio`).
6. Mantenha o comentário **atualizado** — comentário divergente é pior que nenhum. Remova resíduos de Gemini/Vertex/Central.
7. **Não comente código morto**: remova (ou marque `@deprecated`/`[Obsolete]`) duplicatas e funções não usadas **antes** de documentar.
8. Documente decisões de **"desligado de propósito"** (noise reduction, trim de silêncio) com o motivo — senão alguém reativa e quebra.
9. Padronize por linguagem (abaixo), tudo em português.
10. Ative o **feedback automático de cobertura**: `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (CS1591) no C#; lint de docstring/JSDoc no Python/JS.

### Convenção por linguagem

| Linguagem | Convenção | Documentar |
|---|---|---|
| **C# / .NET** | XML doc `///`: `<summary>` (1 linha) + `<remarks>` (COMO funciona + limiares) + `<param>`/`<returns>`/`<exception>` | Métodos públicos de Services, Controllers, Hubs e records. Prioridade às heurísticas |
| **Python** | Docstrings PEP257 estilo Google: resumo + `Args:`/`Returns:`/`Raises:` + seção "Como funciona:" | Processamento de mídia, segurança (algoritmo/exp/payload), endpoints de auth, heurísticas |
| **JavaScript** | JSDoc `/** */`: descrição + `@param {tipo}` + `@returns` + `@throws`; `@deprecated` p/ remover | Funções exportadas, handlers e funções em `window.*` |

### Exemplo antes/depois — `TranscricaoOrquestradorService.cs`

**Antes:**

```csharp
private static bool TranscricaoPareceValida(string transcricao)
{
    if (string.IsNullOrWhiteSpace(transcricao)) return false;

    var linhas = transcricao
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    if (linhas.Count <= 2 && transcricao.Length < 180) return false;

    var frases = new List<string>(linhas.Count);
    foreach (var linha in linhas)
    {
        var semMeta = Regex.Replace(linha, @"^\[\d{2}:\d{2}\]\s*[^:]+:\s*", string.Empty);
        var norm = Regex.Replace(semMeta.ToLowerInvariant(), @"\s+", " ").Trim();
        if (!string.IsNullOrWhiteSpace(norm)) frases.Add(norm);
    }

    if (frases.Count == 0) return false;

    var dominante = frases.GroupBy(f => f)
        .Select(g => new { Texto = g.Key, Count = g.Count() })
        .OrderByDescending(g => g.Count).First();

    var repeticaoDominante = (double)dominante.Count / frases.Count;
    if (frases.Count >= 8 && repeticaoDominante >= 0.70) return false;

    return true;
}
```

**Depois:**

```csharp
/// <summary>
/// Heurística anti-alucinação: decide se o resultado do Azure Speech-to-Text
/// é bom o suficiente para ser aceito, ou se vale a pena cair para o Whisper.
/// </summary>
/// <remarks>
/// COMO funciona (em ordem; qualquer reprovação retorna false):
/// 1. Texto vazio/em branco → inválido.
/// 2. Densidade mínima: até 2 linhas E menos de 180 chars → inválido
///    (transcrição curta demais para uma oitiva real).
/// 3. Normaliza cada linha removendo o prefixo de metadados "[mm:ss] Falante:"
///    (regex), baixando para minúsculas e colapsando espaços, para comparar
///    apenas o conteúdo falado.
/// 4. Detecção de loop/alucinação: agrupa as frases normalizadas e mede a
///    fração da frase mais repetida. Se houver ≥ 8 frases E a frase dominante
///    representar ≥ 70% do total, considera-se que o modelo "travou" repetindo
///    a mesma frase → inválido. O piso de 8 frases evita falso-positivo em
///    áudios legitimamente curtos; 0.70 foi calibrado para não reprovar
///    repetições naturais ("sim", "certo") em diálogos reais.
/// </remarks>
/// <param name="transcricao">Texto formatado retornado pelo provedor (linhas "[mm:ss] Falante: fala").</param>
/// <returns>true se a transcrição parece válida; false se deve ser descartada e acionado o fallback.</returns>
private static bool TranscricaoPareceValida(string transcricao)
{
    // Guard: sem texto não há o que validar.
    if (string.IsNullOrWhiteSpace(transcricao)) return false;

    var linhas = transcricao
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    // Densidade mínima: oitiva real não cabe em ≤2 linhas / <180 chars.
    if (linhas.Count <= 2 && transcricao.Length < 180) return false;

    // Remove "[mm:ss] Falante:" e normaliza, isolando só o conteúdo falado.
    var frases = new List<string>(linhas.Count);
    foreach (var linha in linhas)
    {
        var semMeta = Regex.Replace(linha, @"^\[\d{2}:\d{2}\]\s*[^:]+:\s*", string.Empty);
        var norm = Regex.Replace(semMeta.ToLowerInvariant(), @"\s+", " ").Trim();
        if (!string.IsNullOrWhiteSpace(norm)) frases.Add(norm);
    }

    if (frases.Count == 0) return false;

    // Frase mais repetida do conjunto.
    var dominante = frases.GroupBy(f => f)
        .Select(g => new { Texto = g.Key, Count = g.Count() })
        .OrderByDescending(g => g.Count).First();

    // Loop de alucinação: a partir de 8 frases, ≥70% iguais indica modelo travado.
    var repeticaoDominante = (double)dominante.Count / frases.Count;
    if (frases.Count >= 8 && repeticaoDominante >= 0.70) return false;

    return true;
}
```

### Backlog priorizado de documentação

Esforço: **P** (pequeno) · **M** (médio) · **G** (grande).

#### Prioridade ALTA

| Arquivo | Lang | Esforço | Por quê |
|---|---|---|---|
| `Backend/Services/SpeakerDetectionService.cs` | C# | G | Centraliza a heurística Operador BAS vs Motorista; pesos (+1/+2) e limiares (≥3, 30 chars, 2.2s) sem explicação = núcleo do domínio |
| `Backend/Services/AzureFastTranscricaoService.cs` | C# | G | ~640 linhas: diarização, mapeamento de speaker (score+perguntas+intro+1ª fala≤25s), merge (gap 1.6s) |
| `Backend/Services/AzureWhisperService.cs` | C# | M | Fallback com filtros de alucinação (`no_speech_prob`/`avg_logprob`/`compression_ratio`) + código morto (`TemRepeticaoExcessiva`) |
| `Backend/Services/TranscricaoOrquestradorService.cs` | C# | M | Orquestrador central: pré-processar (35MB), ordem de fallback, validação anti-alucinação |
| `sentinel-cortex/services/audio_processor.py` | Py | M | Pipeline de áudio; decisões não óbvias (normalização, noise reduction/trim desligados); expõe o bug `start_trim` |
| `sentinel-cortex/core/security.py` | Py | P | Camada JWT/bcrypt sem nenhuma docstring (`verify_password`, `get_password_hash`, `create_access_token`) |
| `sentinel-cortex/routers/v1/auth.py` | Py | P | Endpoint de auth sem docstring do fluxo OAuth2; documentar (e remover) `FAKE_USERS_DB` |
| `Backend/wwwroot/js/services/signalrService.js` | JS | M | Zero JSDoc; reuso de conexão via `connectionStartPromise` e `Set` de listeners (propenso a regressão) |
| `Backend/wwwroot/js/core/state.js` | JS | M | Store global; efeitos colaterais (reset ao trocar áudio, captura de backup) causam bugs sem doc |
| `Backend/wwwroot/js/services/analise/transcricao.js` | JS | M | Fluxo central (SignalR+API+progresso duplo+insights). Remover `gerarRelatorioPericial` duplicada antes |
| `Backend/wwwroot/js/services/analise/relatorio.js` | JS | P | Regra do separador `---SEPARADOR_DADOS---` em `renderizarLaudoComDados` |
| `dashboard-kpi/app_dashboard.py` | Py | G | Monolito 0% docstrings: `update_dashboard` (5 outputs, pizza ignora filtro), `login_route` (RBAC), `display_page` |

#### Prioridade MÉDIA

| Arquivo | Lang | Esforço | Por quê |
|---|---|---|---|
| `Backend/Controllers/TranscricaoController.cs` | C# | M | Armadilha do `Task.Run` fire-and-forget de sentimento (sem await, sem escopo de DI) |
| `Backend/Services/DescricaoAnaliseService.cs` | C# | M | Regras em `PosProcessarTranscricao` (Central/Atendente/Vítima → Operador BAS/Motorista) |
| `Backend/Services/AzureOpenAIService.cs` | C# | P | Payload multimodal (captions+imagens base64, temperature=0), api-version hardcoded |
| `Backend/wwwroot/js/services/analise/edicaoInline.js` | JS | M | 12 funções toggle/save/cancel paralelas + variáveis de backup |
| `Backend/wwwroot/js/ui/waveform.js` | JS | P | Ciclo de vida WaveSurfer + MutationObserver; `destroyWaveform` evita leak |
| `Backend/wwwroot/js/services/export.js` | JS | G | 517 linhas montando docx sem `@param`/`@returns` |
| `sentinel-cortex/services/sentiment_analyzer.py` | Py | P | Limiares mágicos (silence>0.6 hesitante, rms>0.15 agitado) — núcleo da feature |
| `sentinel-cortex/services/video_processor.py` | Py | P | Detalhar o COMO em `process()` (interval, resize >1280, encode JPEG) |
| `deploy.ps1` | PS | P | Cabeçalho de uso/pré-requisitos + remover injeção morta de `GEMINI_API_KEY` |
| `Backend/Controllers/AuthController.cs` | C# | P | Comparação de senha em texto puro (documentar **e** corrigir) |

#### Prioridade BAIXA

| Arquivo | Lang | Esforço | Por quê |
|---|---|---|---|
| `Backend/wwwroot/js/app.js` | JS | M | ~40 funções em `window.*` sem contrato; `limparResultado` condicional |
| `sentinel-cortex/main.py` | Py | P | `get_converter()` serve HTML de fora do serviço (documentar ou remover) |
| `sentinel-cortex/core/config.py` | Py | P | Documentar cada grupo de config e o efeito do `env_prefix SENTINEL_` |

---

## Fase 4 — Correções de qualidade adjacentes

Resolver **antes** de documentar (Fase 3), para não documentar código morto.

### Código morto e duplicatas

- `Backend/Interfaces/IAnaliseService.cs` — vazio, remover.
- `Backend/Services/OpenAITranscricaoService.cs` — registrado mas nunca injetado (sempre resolve `TranscricaoOrquestradorService`). Decidir: manter como fallback documentado ou remover com a dependência OpenAI.
- Google Speech (`GoogleCloudSpeechSettings` + pacote `Google.Cloud.Speech.V1`) — comentado/não implementado. Remover ou implementar.
- `AzureWhisperService.TemRepeticaoExcessiva` e `NormalizarTexto` (só delega) — aparentam não ter uso.
- Frontend: `gerarRelatorioPericial` duplicada (`analise/transcricao.js` **e** `relatorio.js` — canônica é a de `relatorio.js`); dois `transcricao.js` (renomear para `transcricaoFormatter.js` vs `transcricaoFlow.js`); bundle órfão `index-Dv96PTBn.js`; `index.html.backup-original`.
- `sentinel-cortex`: `_reduce_noise` (desligado de propósito); `openai` no `requirements.txt` (não usado pós Azure-only); cache implementado mas **nunca usado** (`use_cache=True` sem `cache.get/set` — sempre retorna `cached=False`).

### Bug latente

- `sentinel-cortex/services/audio_processor.py:46` — `process()` referencia `start_trim` (em `metadata['start_trim_seconds']`) que **nunca é definido** (lógica de trim foi desabilitada) → `NameError` em runtime, pode derrubar `/process/audio`. Definir `start_trim = 0` ou remover a chave. **Adicionar teste cobrindo o endpoint.**

### Docs desatualizadas (Azure-only)

- `README.md` e `ARQUITETURA.md` descrevem Gemini/Vertex/Google Cloud Run/Azure Web App. Reescrever usando `docs/doc-iso/02-arquitetura-e-inventario.md` como fonte de verdade.
- `deploy_gcp.md` — referencia `d:\sentinel-open`, `GEMINI_API_KEY` e SQLite obsoletos. Atualizar ou remover.
- Renomear config residual `ResilienceOptions.GeminiTimeoutMinutes` para algo provider-agnóstico.
- Limpar comentário "CORS (Google Cloud Run)" em `Program.cs`.

### Build / deploy

- **TFM divergente**: `SinistroAPI.csproj` é `net8.0` mas `Backend.Tests.csproj` é `net10.0` (pacotes EFCore 10.x). Alinhar para `net8.0` para `dotnet test` rodar sem SDK 10.
- **Dockerfile canônico**: existem 3 (`Dockerfile` raiz, `Backend/Dockerfile`, + os de cortex/dashboard). `compose` usa `Backend/Dockerfile` e o deploy usa `--source Backend` → o da raiz é **órfão**. Eleger `Backend/Dockerfile` (→ `services/api/Dockerfile`) como único e remover/arquivar o da raiz.
- **`docker-compose` incompleto**: backend não recebe credenciais Azure nem `MediaProcessor__BaseUrl=http://sentinel-cortex:8000`; dashboard precisa de `BACKEND_AUTH_URL=http://backend:8080/api/auth/login`; falta `depends_on`/healthcheck; CORS libera só `localhost:5150/3000`. Adicionar `env_file` (.env) e ordem de subida.
- **Criar `.env.example`** cobrindo TODAS as chaves (Speech/OpenAI/Text Analytics), `DB_PATH`, `MediaProcessor__BaseUrl`, `BACKEND_AUTH_URL` + um README de "como receber e fazer deploy" com ordem **cortex → backend → dashboard**.
- **Persistência**: SQLite em `/data` no Cloud Run não sobrevive a restart/escala (filesystem efêmero). Avaliar banco gerenciado para piloto real.
- **Decisão de arquitetura pendente**: plataforma-alvo é **Cloud Run** (scripts/cloudbuild) ou **Azure** (ARQUITETURA.md)? Definir e alinhar todos os artefatos.
- **Renomear controllers** quase idênticos: `AnaliseController` (api/analisar) vs `AnalisesController` (CRUD) → `AnaliseMidiaController` e `AnalisesCrudController`.

### Outros (segurança aplicacional, além da Fase 0)

- `cortex`: CORS `['*']` + `allow_credentials=True` (combinação inválida/insegura); JWT emitido mas **nenhum endpoint valida o token** (sem `get_current_user`); `--allow-unauthenticated` no Cloud Run.
- Frontend: gate de auth via `localStorage` em `<script>` inline não é barreira de segurança — enforcement tem que estar no backend.
- `@app.on_event('startup'/'shutdown')` está deprecado no FastAPI — migrar para lifespan handlers.

---

## Checklist de handoff

- [ ] **Fase 0** — chaves rotacionadas, senhas trocadas/hasheadas, segredos fora do código, histórico expurgado
- [ ] **Fase 1** — raiz limpa, scripts movidos, `.gitignore` estendido
- [ ] **Fase 4** — código morto removido, bug `start_trim` corrigido, docs Azure-only, TFM alinhado, Dockerfile canônico, `.env.example`, `docker-compose` funcional ponta a ponta
- [ ] **Fase 2** — reorganização em `services/`, validada com `docker-compose build` + deploy dry-run
- [ ] **Fase 3** — backlog de documentação percorrido (ALTA → MÉDIA → BAIXA), `GenerateDocumentationFile` ativo
- [ ] README reescrito com setup, ordem de subida e variáveis necessárias
- [ ] `docker-compose up` sobe o sistema completo localmente
```
