# 03 - Processos e Fluxos Operacionais

## Macroprocesso Sentinel

| Etapa | Entrada | Atividade | Saida | Evidencia |
|---|---|---|---|---|
| 1. Acesso | Usuario e navegador | Abrir frontend servido pelo backend | Tela Sentinel | `Backend/wwwroot/index.html`, `js/app.js` |
| 2. Selecao de fluxo | Audio, foto, video, salvos ou merge | Usuario seleciona modo | Estado de UI atualizado | `ui/navigation.js`, `core/state.js` |
| 3. Upload | Arquivo e contexto | Validar selecao e enviar ao endpoint | Request multipart/form-data | `ui/upload.js`, `api/sinistroApi.js` |
| 4. Validacao backend | Request | Validar tipo, tamanho e configuracao | Rejeicao ou processamento | Controllers e `UploadLimitsOptions` |
| 5. Processamento | Bytes e contexto | IA, media processor, heuristicas e prompts | Transcricao ou laudo | Services em `Backend/Services` |
| 6. Revisao | Markdown/transcricao | Exibir, editar, copiar, exportar | Conteudo revisado | `services/analise`, `services/export.js` |
| 7. Persistencia | Conteudo final | Salvar em SQLite | Registro em `Analises` | `AnalisesController`, `AppDbContext` |
| 8. Monitoramento | Health e dashboard | Verificar status e KPIs | Indicadores e alertas | `HealthController`, `dashboard-kpi` |

## Procedimento - Transcricao de oitiva

1. Usuario seleciona arquivo de audio ou video curto na aba de audio.
2. Frontend chama `gerarTranscricao()`.
3. Frontend inicializa SignalR e obtem `connectionId`.
4. Frontend envia `POST /api/transcrever` com `Arquivo` e header `X-Connection-Id`.
5. Backend valida:
   - arquivo existente;
   - content type `audio/*`, `video/webm` ou `video/mp4`;
   - tamanho abaixo de `MaxAudioUploadMB`;
   - servico de transcricao configurado.
6. Backend pode pre-processar audio no `sentinel-cortex` quando aplicavel.
7. Orquestrador tenta Azure Speech-to-Text.
8. Se a transcricao vier fraca ou falhar, tenta Azure Whisper.
9. Backend retorna `TranscricaoResponse(transcricao, "azure")`.
10. Frontend renderiza transcricao, salva draft local e habilita edicao/exportacao.
11. Em background, backend tenta analise de sentimento por Azure Text Analytics e fallback acustico Python.

### Controles de qualidade

- Validacao de tipo e tamanho antes de processar.
- Heuristica `TranscricaoPareceValida` contra respostas vazias ou repetitivas.
- Correcao de termos por regex em `text_corrections.json`.
- Speaker detection para padronizar `Operador BAS` e `Motorista`.
- Progresso por SignalR para transparência de execucao.

## Procedimento - Geracao de laudo a partir de transcricao

1. Usuario gera ou edita transcricao.
2. Frontend chama `gerarLaudoTecnicoAPI`.
3. Backend recebe `POST /api/analisar/laudo` com `Transcricao`, `Duracao` e `Contexto`.
4. Backend valida transcricao nao vazia e provider configurado.
5. `DescricaoAnaliseService` monta prompt pericial.
6. `AzureOpenAIService` chama deployment configurado com `temperature = 0.0`.
7. Backend retorna Markdown do laudo.
8. Frontend separa tabela `DADOS IDENTIFICADOS` quando o separador existe.
9. Usuario revisa, edita, exporta e salva.

### Controles de qualidade

- Prompt obriga citacao de trecho fonte ou "Nao mencionado".
- Temperatura zero para reduzir variacao.
- Pos-processamento padroniza nomenclatura de interlocutores.
- Separacao de dados identificados facilita revisao.

## Procedimento - Analise de imagem

1. Usuario seleciona imagem e contexto.
2. Frontend envia `POST /api/analisar/imagem`.
3. Backend valida arquivo e `ContentType` iniciando com `image/`.
4. Backend valida limite `MaxImageUploadMB`.
5. `ImagemAnaliseService` seleciona provider:
   - Azure OpenAI Vision, se configurado;
   - Vertex AI, se configurado;
   - Gemini API, se houver chave.
6. Prompt orienta laudo de vistoria apenas com o que e visivel.
7. Frontend renderiza Markdown e habilita acoes.

## Procedimento - Analise de video

1. Usuario seleciona video e contexto.
2. Frontend calcula duracao quando possivel.
3. Frontend envia `POST /api/analisar/video`.
4. Backend valida arquivo e `ContentType` `video/*`.
5. `VideoAnaliseService` escolhe:
   - Vertex inline para videos pequenos;
   - Gemini File API para arquivos maiores quando configurada.
6. Gemini File API usa upload resumable, aguarda estado `ACTIVE` e analisa via `file_uri`.
7. Resultado e devolvido como laudo tecnico de video.

## Procedimento - Merge de audio

1. Usuario seleciona dois ou mais arquivos de audio/MPEG.
2. Frontend envia `POST /api/tools/merge-audio` com campo `files`.
3. Backend valida quantidade minima.
4. Backend encaminha bytes para `MediaProcessorService.MergeAudiosAsync`.
5. `sentinel-cortex` concatena segmentos, normaliza parametros e retorna MP3.
6. Frontend cria um `File` local `audio_completo_merged.mp3` e define como audio atual.
7. Usuario executa transcricao normal sobre o audio mergeado.

## Procedimento - Salvamento e consulta de analises

1. Usuario clica salvar em transcricao, laudo, foto ou video.
2. Frontend monta objeto com `tipo`, `conteudo`, `arquivo`, `dataAnalise`.
3. Frontend chama `POST /api/salvar`.
4. Backend sobrescreve `Data` com `DateTime.Now`.
5. Entity Framework salva em SQLite.
6. Usuario pode consultar ate 50 ultimas analises por `GET /api/analises`.
7. Usuario pode buscar ou excluir por ID.

## Procedimento - Autenticacao

### Estado atual

O backend possui login simples em `AuthController`:

- Busca usuario por `Username` e `Password`.
- Retorna sucesso, username e role.
- Registro cria usuario com role `Membro`.
- Nao ha JWT, hash de senha, middleware de autorizacao ou protecao de endpoints principais.

### Estado recomendado

Usar `docs/PLANO_AUTENTICACAO.md` como base para:

- JWT ou cookie seguro.
- Hash de senha com algoritmo apropriado.
- Role-based access control.
- Audit log de login e acoes sensiveis.
- Rate limiting para login.

## Procedimento - Monitoramento

| Monitor | Como validar | Criterio |
|---|---|---|
| Liveness | `GET /api/health/live` | Deve retornar `alive`. |
| Readiness | `GET /api/health/ready` | Deve retornar `ready` se DB conecta. |
| Health completo | `GET /api/health` | `healthy`, `degraded` ou `unhealthy`. |
| Media processor | Health interno via backend | Se indisponivel, status degradado quando habilitado. |
| Dashboard | Login e carregamento de graficos | Deve restringir por role permitida. |

## Registros obrigatorios por execucao critica

- Nome do arquivo processado.
- Tipo de midia.
- Data/hora.
- Provider usado.
- Resultado ou erro.
- Usuario responsavel.
- Versao do sistema.
- Evidencia da revisao humana.

Atualmente nem todos esses registros existem no banco. A lacuna deve ser tratada por um `AuditLog`.

