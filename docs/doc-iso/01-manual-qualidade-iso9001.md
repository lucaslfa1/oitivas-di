# 01 - Manual da Qualidade ISO 9001

## Politica da qualidade do projeto

O projeto Sentinel deve entregar analises forenses de sinistros veiculares com confiabilidade, rastreabilidade, controle de evidencias e linguagem tecnica. O sistema deve preservar o conteudo original recebido, aplicar processamento verificavel, reduzir alucinacoes de IA e permitir revisao humana antes do uso do laudo como evidencia operacional.

## Objetivos da qualidade

| Objetivo | Indicador sugerido | Evidencia |
|---|---|---|
| Transcrever oitivas com formato consistente | Percentual de transcricoes com timestamp e falante identificado | `TranscricaoOrquestradorService`, `AzureFastTranscricaoService`, `AzureWhisperService` |
| Gerar laudos com base em evidencias | Percentual de laudos que citam fonte ou informam "Nao mencionado" | `DescricaoAnaliseService`, `Prompts/SinistroPrompts.cs`, `Configuration/prompts.json` |
| Controlar entrada de arquivos | Rejeicao por tipo MIME e tamanho maximo | `TranscricaoController`, `AnaliseController`, `UploadLimitsOptions` |
| Manter rastreabilidade de analises | Registro salvo com tipo, conteudo, arquivo e data | `AnalisesController`, `AnaliseModel`, SQLite |
| Detectar degradacao operacional | Health checks de banco e media processor | `HealthController`, `/api/health` |
| Melhorar continuamente | Registro de riscos, falhas de teste e acoes CAPA | `06-riscos-controles-acoes.md`, `07-validacao-evidencias.md` |

## Contexto da organizacao - ISO 9001, clausula 4

O Sentinel opera em um contexto de analise de sinistros, regulacao, combate a fraudes, vistoria veicular e atendimento por oitiva. As partes interessadas incluem:

- Operadores BAS que conduzem oitivas.
- Motoristas ou declarantes que fornecem relatos.
- Analistas e peritos que revisam laudos.
- Supervisores e coordenadores que acompanham KPIs.
- Area de seguranca da informacao.
- Clientes internos/externos que auditam qualidade do processo.

### Escopo do SGQ para este projeto

O escopo recomendado do Sistema de Gestao da Qualidade aplicado ao Sentinel e:

> Projeto, desenvolvimento, operacao assistida e melhoria continua de sistema web para transcricao, analise e geracao de laudos tecnicos de sinistros veiculares, com suporte a audio, imagem, video, persistencia de analises e indicadores operacionais.

## Lideranca - ISO 9001, clausula 5

Responsabilidades recomendadas:

| Papel | Responsabilidades |
|---|---|
| Dono do processo | Definir fluxo operacional, aprovar criterios de laudo e aceitar riscos. |
| Responsavel tecnico | Manter arquitetura, codigo, testes, deploy e documentacao tecnica. |
| Qualidade/Auditoria | Revisar aderencia aos procedimentos, registrar nao conformidades e acompanhar CAPA. |
| Operacao | Executar oitivas, validar entradas e reportar falhas. |
| Seguranca | Controlar credenciais, acesso, hardening e resposta a incidentes. |

## Planejamento - ISO 9001, clausula 6

O planejamento de qualidade deve considerar riscos e oportunidades:

- Risco de alucinacao de IA: mitigado por temperatura `0.0`, prompts anti-alucinacao e exigencia de citacao.
- Risco de transcricao incorreta: mitigado por fallback Azure Speech-to-Text -> Whisper e heuristicas de falante.
- Risco de exposicao de credenciais: atualmente nao mitigado de forma suficiente, requer acao corretiva.
- Risco de indisponibilidade do microservico Python: mitigado por fallback para audio original e health check degradado.
- Risco de perda de persistencia em Cloud Run com SQLite local: requer definicao de armazenamento persistente ou banco gerenciado.
- Risco de testes quebrados: requer alinhamento de versoes de pacotes de teste e runtime.

## Suporte - ISO 9001, clausula 7

### Recursos

- Runtime .NET 8 para backend.
- Python/FastAPI para processamento de midia.
- SQLite para persistencia local.
- Azure Speech, Azure OpenAI, Azure Text Analytics, Gemini API e Vertex AI como integracoes configuraveis.
- Docker/Cloud Run para implantacao.

### Competencia

Perfis envolvidos devem dominar:

- ASP.NET Core, Entity Framework, SignalR e HTTP multipart.
- Processamento de midia com Python, pydub, ffmpeg e OpenCV.
- Operacao de providers de IA e limites de uso.
- Boas praticas de seguranca para segredos e autenticacao.
- Procedimentos de auditoria, evidencias e CAPA.

### Informacao documentada

Documentos requeridos:

- Manual da qualidade do projeto.
- Procedimentos operacionais.
- Fluxogramas.
- Matriz de rastreabilidade ISO.
- Registro de riscos e acoes.
- Evidencias de validacao.
- Registro de alteracoes e aprovacoes.

## Operacao - ISO 9001, clausula 8

O processo operacional principal e:

1. Usuario acessa frontend.
2. Usuario seleciona tipo de fluxo: audio, merge de audio, foto, video ou salvos.
3. Frontend valida presenca de arquivo e envia para endpoint correto.
4. Backend valida tipo, tamanho e configuracao do servico.
5. Backend orquestra processamento de IA e/ou media processor.
6. Resultado e exibido como transcricao ou laudo em Markdown.
7. Usuario pode editar, copiar, exportar e salvar.
8. Backend persiste analise no SQLite.
9. Dashboard e health checks apoiam monitoramento.

## Avaliacao de desempenho - ISO 9001, clausula 9

Indicadores recomendados:

- Taxa de sucesso de transcricao.
- Tempo medio de transcricao por tamanho de arquivo.
- Taxa de fallback para Whisper.
- Taxa de laudos gerados com erro.
- Taxa de arquivos rejeitados por tipo/tamanho.
- Disponibilidade de `/api/health`.
- Resultado de testes automatizados por commit.
- Quantidade de nao conformidades abertas e encerradas.

## Melhoria - ISO 9001, clausula 10

Melhorias prioritarias identificadas:

1. Remover credenciais reais do repositorio e rotacionar chaves.
2. Implementar hash de senha e autorizacao por role no backend.
3. Corrigir incompatibilidade dos testes C#.
4. Corrigir risco funcional em `sentinel-cortex/services/audio_processor.py`.
5. Consolidar decisao arquitetural de infraestrutura: Cloud Run, Azure Web App, banco persistente e providers de IA.
6. Formalizar revisao humana de laudos antes de uso externo.
7. Criar trilha de auditoria para login, transcricao, laudo, edicao, exclusao e exportacao.

