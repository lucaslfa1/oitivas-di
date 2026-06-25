# 05 - Matriz de Rastreabilidade ISO 9001

Esta matriz relaciona requisitos da ISO 9001:2015 com controles, evidencias e lacunas encontradas no projeto Sentinel.

## Matriz por clausula

| Clausula | Tema | Controle ou pratica no Sentinel | Evidencia no repositorio | Status |
|---|---|---|---|---|
| 4.1 | Contexto da organizacao | Sistema definido para sinistros, oitivas, vistorias, videos e combate a fraudes. | `README.md`, `ARQUITETURA.md`, `Configuration/prompts.json` | Parcial |
| 4.2 | Partes interessadas | Operador, motorista, analista, supervisor, admin, cliente e auditor identificados. | `docs/PLANO_AUTENTICACAO.md`, `dashboard-kpi/app_dashboard.py` | Parcial |
| 4.3 | Escopo do SGQ | Escopo proposto nesta documentacao. | `docs/doc-iso/01-manual-qualidade-iso9001.md` | Criado |
| 4.4 | Processos do SGQ | Fluxos de transcricao, laudo, midia, merge e salvamento mapeados. | `03-processos-fluxos.md`, `04-fluxogramas.md` | Criado |
| 5.1 | Lideranca | Responsabilidades propostas, mas sem aprovacao formal versionada. | `01-manual-qualidade-iso9001.md` | Lacuna |
| 5.2 | Politica da qualidade | Politica de qualidade proposta. | `01-manual-qualidade-iso9001.md` | Criado |
| 5.3 | Papeis e responsabilidades | RACI recomendado. | `09-checklists-auditoria.md` | Parcial |
| 6.1 | Riscos e oportunidades | Registro de riscos e acoes CAPA. | `06-riscos-controles-acoes.md` | Criado |
| 6.2 | Objetivos da qualidade | Indicadores recomendados. | `01-manual-qualidade-iso9001.md` | Criado |
| 6.3 | Planejamento de mudancas | Existe deploy script, mas sem procedimento formal de change control. | `deploy.ps1`, `06-riscos-controles-acoes.md` | Parcial |
| 7.1 | Recursos | Runtimes, servicos de IA, Docker e banco documentados. | `02-arquitetura-e-inventario.md` | Parcial |
| 7.2 | Competencia | Competencias recomendadas descritas, sem matriz de treinamento. | `01-manual-qualidade-iso9001.md` | Lacuna |
| 7.3 | Conscientizacao | Nao ha evidencia de treinamento formal. | Nao identificado | Lacuna |
| 7.4 | Comunicacao | SignalR comunica progresso; logs registram eventos. | `AnalysisHub`, `signalrService.js`, controllers | Parcial |
| 7.5 | Informacao documentada | Esta pasta estabelece controle documental. | `00-controle-documental.md` | Criado |
| 8.1 | Planejamento operacional | Processos descritos e endpoints implementados. | Controllers, services, `03-processos-fluxos.md` | Parcial |
| 8.2 | Requisitos para produtos e servicos | Requisitos de entrada: arquivos, contexto, tamanho, formato. | DTOs e controllers | Parcial |
| 8.3 | Projeto e desenvolvimento | Arquitetura, prompts, testes e modularizacao existem; falta registro formal de revisao/aprovacao. | `ARQUITETURA.md`, `docs/*`, testes | Parcial |
| 8.4 | Controle de provedores externos | Providers Azure/Gemini/Vertex identificados; falta SLA, avaliacao e aprovacao de fornecedores. | `appsettings.json`, services | Lacuna |
| 8.5 | Producao e provisao de servico | Fluxos de execucao e salvamento implementados. | Frontend, controllers, services | Parcial |
| 8.6 | Liberacao de produtos e servicos | Nao ha gate formal de release; `deploy.ps1` faz commit/push/deploy. | `deploy.ps1` | Lacuna |
| 8.7 | Saidas nao conformes | Erros retornam `ErrorResponse`/`Problem`; falta processo formal para laudos rejeitados. | Controllers, `06-riscos-controles-acoes.md` | Parcial |
| 9.1 | Monitoramento e medicao | Health check e dashboard KPI existem. | `HealthController`, `dashboard-kpi` | Parcial |
| 9.2 | Auditoria interna | Checklist criado; falta rotina executada e registros assinados. | `09-checklists-auditoria.md` | Criado |
| 9.3 | Analise critica pela direcao | Nao ha evidencia formal no repo. | Nao identificado | Lacuna |
| 10.1 | Melhoria | Relatorio de melhorias e riscos documentados. | `docs/RELATORIO_MELHORIAS.md`, `06-riscos-controles-acoes.md` | Parcial |
| 10.2 | Nao conformidade e acao corretiva | Registro CAPA criado nesta pasta. | `06-riscos-controles-acoes.md` | Criado |
| 10.3 | Melhoria continua | Backlog de melhorias priorizadas. | `06-riscos-controles-acoes.md` | Parcial |

## Rastreabilidade de requisitos funcionais

| Requisito | Implementacao | Evidencia | Criterio de aceite |
|---|---|---|---|
| Transcrever audio de oitiva | `POST /api/transcrever` | `TranscricaoController`, `TranscricaoOrquestradorService` | Retornar texto com timestamps e falantes. |
| Gerar laudo de oitiva | `POST /api/analisar/laudo` | `DescricaoAnaliseService`, `AzureOpenAIService` | Retornar Markdown com dados identificados e laudo. |
| Auditar conformidade | `POST /api/auditar` | `AuditarConformidade` | Relatorio de nao conformidades e score. |
| Analisar imagem | `POST /api/analisar/imagem` | `ImagemAnaliseService` | Laudo descreve apenas elementos visiveis. |
| Analisar video | `POST /api/analisar/video` | `VideoAnaliseService` | Laudo com dados tecnicos, observacoes e parecer. |
| Mesclar audios | `POST /api/tools/merge-audio` | `ToolsController`, `MediaProcessorService`, `audio_processor.py` | Retornar MP3 combinado. |
| Salvar analise | `POST /api/salvar` | `AnalisesController`, `AnaliseModel` | Registro persistido com ID. |
| Listar salvos | `GET /api/analises` | `AnalisesController` | Retornar ultimos 50 registros. |
| Health check | `GET /api/health` | `HealthController` | Informar DB e media processor. |
| Dashboard KPI | App Dash | `dashboard-kpi/app_dashboard.py` | Exibir KPIs apos login autorizado. |

## Rastreabilidade de controles tecnicos

| Controle | Codigo | Como auditar |
|---|---|---|
| Limite de tamanho de request | `Program.cs`, `UploadLimitsOptions.cs` | Conferir `MaxRequestBodySize` e `MultipartBodyLengthLimit`. |
| Validacao MIME | `TranscricaoController`, `AnaliseController` | Enviar arquivos invalidos e confirmar `400`. |
| Fallback de transcricao | `TranscricaoOrquestradorService` | Simular falha/baixa qualidade do STT e verificar Whisper. |
| Prompt anti-alucinacao | `DescricaoAnaliseService`, `prompts.json` | Revisar temperatura, instrucoes e saida esperada. |
| Health de DB | `HealthController` | Derrubar DB/caminho e verificar `503`. |
| Fallback local de salvamento no frontend | `services/salvar.js` | Simular API indisponivel e verificar localStorage. |
| Cache do Cortex | `cache_service.py` | Consultar `/cache/stats` e executar limpeza. |

## Lacunas de evidencia documental

| Lacuna | Impacto | Acao recomendada |
|---|---|---|
| Aprovacao formal do manual | Auditor pode nao aceitar documento sem dono/aprovacao. | Criar registro de aprovacao e responsaveis. |
| Change control | Deploy direto por script pode nao demonstrar segregacao. | Criar procedimento de release, aprovacao e rollback. |
| Registros de treinamento | ISO exige competencia/conscientizacao. | Criar matriz de treinamento por papel. |
| Audit log | Dificulta rastrear quem gerou/editou/excluiu laudos. | Implementar tabela `AuditLog` e middleware. |
| Controle de fornecedores de IA | Falta avaliacao de dependencia externa. | Registrar fornecedores, regioes, SLA, riscos e planos de contingencia. |

