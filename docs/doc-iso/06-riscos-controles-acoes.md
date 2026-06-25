# 06 - Riscos, Controles e Acoes Corretivas

## Criterio de classificacao

| Campo | Escala |
|---|---|
| Probabilidade | Baixa, Media, Alta |
| Impacto | Baixo, Medio, Alto, Critico |
| Prioridade | P1 critica, P2 alta, P3 media, P4 baixa |

## Registro de riscos e nao conformidades

| ID | Risco ou nao conformidade | Evidencia | Prob. | Impacto | Prioridade | Controle atual | Acao recomendada |
|---|---|---|---|---|---|---|---|
| NC-001 | Credenciais reais versionadas em configuracao. | `Backend/appsettings.json`, `dashboard-kpi/users.json`, seeds em `AppDbContext`. | Alta | Critico | P1 | Nenhum suficiente. | Remover segredos, rotacionar chaves, usar Secret Manager/env vars, reescrever historico se necessario. |
| NC-002 | Senhas em texto claro no backend. | `UserModel.Password`, `AuthController`, seeds. | Alta | Critico | P1 | Login simples. | Implementar hash, salt, politica de senha, JWT/session segura e migracao de usuarios. |
| NC-003 | Endpoints principais nao exigem autenticacao/autorizacao. | Controllers sem `[Authorize]`. | Alta | Alto | P1 | CORS parcial. | Implementar RBAC por role e middleware de autorizacao. |
| NC-004 | Testes C# falham parcialmente. | `dotnet test`: 4 aprovados, 2 falharam. | Alta | Alto | P1 | Build compila, mas teste falha. | Alinhar `Backend.Tests` para `net8.0` e EF Core 8 ou atualizar projeto inteiro com coerencia. |
| NC-005 | Variavel `start_trim` referenciada sem definicao no `audio_processor.py`. | `metadata["start_trim_seconds"] = start_trim/1000`. | Media | Alto | P2 | Py compile nao detecta. | Definir `start_trim = 0` ou remover campo se trim esta desabilitado; adicionar teste de `/process/audio`. |
| NC-006 | SQLite local pode perder dados em container sem volume persistente. | `DB_PATH=/data/sinistros.db`, docs de Cloud Run. | Media | Alto | P2 | Volume local em compose. | Usar Cloud SQL/Firestore/volume persistente; documentar backup/restore. |
| NC-007 | Divergencia entre documentacao antiga e codigo atual. | `README.md`, `ARQUITETURA.md`, docs Azure audio. | Alta | Medio | P2 | Esta documentacao registra divergencia. | Aprovar arquitetura alvo e atualizar docs antigas. |
| NC-008 | Falta audit log de acoes criticas. | Nao ha tabela `AuditLog`. | Alta | Alto | P2 | Logs de aplicacao. | Criar entidade de auditoria para login, laudo, edicao, exclusao e exportacao. |
| NC-009 | Deploy script faz `git add -A`, commit e push automaticamente. | `deploy.ps1`. | Media | Alto | P2 | Parametro `DryRun`. | Separar build/deploy de commit/push; exigir PR/aprovacao. |
| NC-010 | CORS de producao depende de lista e comentario historico fala allow any para piloto. | `Program.cs`, `appsettings.Production.template.json`. | Media | Medio | P3 | `WithOrigins` quando configurado. | Confirmar origens produtivas e bloquear wildcard. |
| NC-011 | Provider de IA externo sem registro formal de fornecedor/SLA. | Azure/Gemini/Vertex services. | Media | Alto | P3 | Health/fallback parcial. | Criar avaliacao de fornecedor, contrato, SLA, regiao e contingencia. |
| NC-012 | Saidas de IA dependem de revisao humana, mas aprovacao nao e registrada. | Frontend permite edicao/exportacao; DB salva conteudo final sem aprovador. | Media | Alto | P2 | Edicao manual. | Adicionar status, aprovador, data de revisao e historico de versoes. |
| NC-013 | Arquivos de audio em `wwwroot/uploads/audio` estao versionados. | `Backend/wwwroot/uploads/audio/*.wav`. | Media | Alto | P2 | `.gitignore` local parcial. | Remover midias sensiveis do repositorio e ajustar ignore/retencao. |
| NC-014 | Dashboard usa `server.secret_key = os.urandom(24)`, invalidando sessoes a cada restart. | `dashboard-kpi/app_dashboard.py`. | Media | Medio | P3 | Sessao Flask. | Usar segredo persistente via variavel de ambiente. |
| NC-015 | `dashboard-kpi/users.json` contem usuarios/senhas em texto claro, ainda que app atual consulte backend. | `dashboard-kpi/users.json`. | Media | Alto | P2 | Nao usado diretamente no login atual. | Remover arquivo ou trocar por template sem segredos. |

## Plano CAPA prioritario

| CAPA | Causa raiz provavel | Correcao | Prevencao | Dono sugerido | Prazo sugerido |
|---|---|---|---|---|---|
| CAPA-001 | Uso de configuracao local versionada como producao. | Remover segredos e rotacionar chaves. | Pipeline com secret scan e template seguro. | Seguranca + Dev | Imediato |
| CAPA-002 | Autenticacao criada como MVP. | Implementar hash/JWT/RBAC. | Gate de seguranca antes de release. | Dev backend | 1 sprint |
| CAPA-003 | Dependencias de teste desalinhadas. | Alinhar target framework e EF. | Renovate/dependabot e build CI obrigatorio. | Dev backend | 1 sprint |
| CAPA-004 | Codigo Python alterado com trecho comentado sem teste de endpoint. | Corrigir `start_trim`. | Teste automatizado para `/process/audio`. | Dev Python | Imediato |
| CAPA-005 | Falta trilha de auditoria de negocio. | Criar `AuditLog`. | Requisito obrigatorio para operacoes criticas. | Dev backend + Qualidade | 2 sprints |

## Controles compensatorios temporarios

Enquanto as acoes definitivas nao forem concluidas:

- Executar o sistema somente em ambiente controlado.
- Restringir rede e origens CORS.
- Usar credenciais rotacionadas e de menor privilegio.
- Manter logs de aplicacao e evidencias de execucao.
- Exigir revisao humana externa ao sistema antes de uso do laudo.
- Nao expor banco SQLite, arquivos enviados ou exports fora de canal seguro.

## Registro de aceitacao de risco

| Risco | Aceito por | Data | Justificativa | Validade |
|---|---|---|---|---|
| Pendente | Pendente | Pendente | Nenhum risco critico deve ser aceito sem aprovacao formal. | Pendente |

