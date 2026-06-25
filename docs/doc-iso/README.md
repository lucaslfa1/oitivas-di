# Documentacao ISO 9001 - Projeto Sentinel

Esta pasta contem a documentacao auditavel do projeto Sentinel, estruturada para apoiar auditorias internas, auditorias de cliente e preparacao para um Sistema de Gestao da Qualidade alinhado a ISO 9001:2015.

> Observacao: esta documentacao nao declara certificacao ISO 9001. Ela organiza evidencias, processos, riscos e controles do projeto para sustentar uma auditoria.

## Como navegar

| Arquivo | Finalidade |
|---|---|
| `00-controle-documental.md` | Controle do documento, escopo, revisoes e criterios de aprovacao. |
| `01-manual-qualidade-iso9001.md` | Manual da qualidade do projeto e aderencia aos requisitos ISO 9001. |
| `02-arquitetura-e-inventario.md` | Arquitetura, componentes, tecnologias, dados, endpoints e inventario tecnico. |
| `03-processos-fluxos.md` | Procedimentos operacionais ponta a ponta, da entrada ate o laudo e salvamento. |
| `04-fluxogramas.md` | Fluxogramas em Mermaid para processo principal, transcricao, midia e deploy. |
| `05-matriz-rastreabilidade-iso9001.md` | Matriz de rastreabilidade entre requisitos ISO, controles e evidencias do repo. |
| `06-riscos-controles-acoes.md` | Registro de riscos, nao conformidades, controles atuais e plano CAPA. |
| `07-validacao-evidencias.md` | Evidencias de build, testes, verificacoes e lacunas de validacao. |
| `08-snippets-codigo.md` | Snippets auditaveis de codigo com explicacao de finalidade e controle associado. |
| `09-checklists-auditoria.md` | Checklists para auditoria interna, go-live, seguranca, qualidade e operacao. |
| `assets/fluxo-principal.mmd` | Fonte Mermaid do fluxograma principal. |
| `exports/` | Saidas consolidadas em DOCX/PDF geradas pelo script de publicacao. |

## Resumo executivo

O Sentinel e uma solucao para analise forense de sinistros veiculares. O sistema recebe audios de oitiva, fotos de vistoria e videos relacionados ao sinistro; processa midias por servicos de IA e componentes auxiliares; gera transcricoes e laudos tecnicos; permite revisao/exportacao; e persiste analises em banco SQLite.

O repositorio contem:

- Backend ASP.NET Core 8 em `Backend/`.
- Frontend estatico modular em `Backend/wwwroot/`.
- Microservico Python FastAPI para processamento de midia em `sentinel-cortex/`.
- Dashboard KPI em Dash/Flask em `dashboard-kpi/`.
- Scripts de deploy, verificacao, teste e guias de arquitetura.

## Evidencias principais

- Codigo-fonte versionado no Git.
- Endpoints REST documentados em controllers ASP.NET Core.
- Configuracoes centralizadas em `Backend/appsettings.json` e `Backend/Configuration/*.json`.
- Testes automatizados em `Backend/Backend.Tests/`.
- Scripts de teste e integracao em `test_*.ps1`, `test_integration.py` e `verify_install.ps1`.
- Dockerfiles e `docker-compose.yml` para reproducibilidade de ambiente.

## Pontos criticos para auditoria

Foram identificadas lacunas que devem ser tratadas antes de qualquer auditoria externa formal:

- Segredos e credenciais reais aparecem versionados em arquivos de configuracao e usuarios seed.
- Autenticacao do backend usa senha em texto claro, sem hash e sem token/JWT.
- `dotnet test` executa 6 testes, com 4 aprovados e 2 falhando por incompatibilidade de versoes do Entity Framework nos testes.
- Existem divergencias historicas nas docs entre Cloud Run, Azure Web App, Gemini, Azure OpenAI e Vertex AI. Esta documentacao diferencia o que esta implementado no codigo do que esta citado em documentos antigos.
- O microservico Python compila sintaticamente, mas ha risco funcional em `audio_processor.py` por referencia a variavel `start_trim` sem definicao no retorno de metadados.

## Como publicar os artefatos consolidados

Use o script abaixo depois de alterar qualquer arquivo Markdown desta pasta:

```powershell
python docs/doc-iso/tools/build_doc_iso.py
```

Saidas esperadas:

- `docs/doc-iso/exports/Sentinel_Documentacao_ISO9001.md`
- `docs/doc-iso/exports/Sentinel_Documentacao_ISO9001.docx`
- `docs/doc-iso/exports/Sentinel_Documentacao_ISO9001.pdf`

## Artefato externo de fluxograma

Uma copia navegavel do fluxograma principal foi gerada no FigJam:

- `https://www.figma.com/board/tgI3bEdT50AyALl519fUaz?utm_source=codex&utm_content=edit_in_figjam&oai_id=&request_id=4df7c58b-0940-44d5-89a0-f687c7f6f03e`

## Politica de manutencao

Atualize esta pasta sempre que houver:

- Mudanca de arquitetura, provider de IA, banco de dados ou infraestrutura.
- Novo endpoint, novo fluxo operacional ou mudanca de regra de negocio.
- Correcao de risco, nao conformidade, vulnerabilidade ou falha de teste.
- Alteracao em prompts, limites de upload, politicas de CORS ou credenciais.
