# 09 - Checklists de Auditoria

## Checklist de auditoria interna ISO 9001

| Item | Pergunta | Evidencia esperada | Status |
|---|---|---|---|
| 1 | O escopo do sistema esta definido e aprovado? | `00-controle-documental.md`, assinatura/aprovacao. | Pendente |
| 2 | As partes interessadas e requisitos foram identificados? | Manual da qualidade e matriz ISO. | Parcial |
| 3 | Os processos principais possuem entradas, saidas e responsaveis? | `03-processos-fluxos.md`. | Criado |
| 4 | Existe controle de documentos e revisoes? | Historico Git e controle documental. | Parcial |
| 5 | Existem objetivos da qualidade mensuraveis? | Indicadores definidos e metricas coletadas. | Parcial |
| 6 | Riscos e oportunidades foram tratados? | `06-riscos-controles-acoes.md`. | Criado |
| 7 | As competencias por papel foram definidas? | Matriz de treinamento. | Pendente |
| 8 | A producao do servico e controlada? | Controllers, services, validações e logs. | Parcial |
| 9 | Saidas nao conformes sao identificadas e tratadas? | Erros padronizados, CAPA e registros. | Parcial |
| 10 | Testes automatizados estao passando? | `dotnet test`, Python tests, smoke tests. | Nao conforme |
| 11 | Ha auditoria interna planejada e registrada? | Checklist preenchido, evidencias e responsaveis. | Pendente |
| 12 | Existe melhoria continua e analise critica? | Backlog CAPA e atas de revisao. | Parcial |

## Checklist de seguranca

| Item | Criterio | Resultado atual | Acao |
|---|---|---|---|
| Segredos versionados | Nenhum segredo real no Git | Nao conforme | Remover e rotacionar. |
| Senhas | Hash forte, nunca texto claro | Nao conforme | Implementar hash e migracao. |
| Autorizacao | Endpoints protegidos por role | Nao conforme | `[Authorize]` e RBAC. |
| CORS | Origens produtivas explicitas | Parcial | Validar ambientes. |
| Upload | Validar MIME e tamanho | Parcial | Adicionar verificacao de extensao/conteudo. |
| Logs | Sem segredo em log | Parcial | Revisar logs de configuracao. |
| Audit log | Login e acoes sensiveis rastreados | Nao conforme | Criar tabela/eventos. |
| Retencao | Politica de arquivos enviados | Pendente | Definir e implementar limpeza. |

## Checklist de release

Antes de deploy:

- [ ] `git status` sem alteracoes inesperadas.
- [ ] Segredos ausentes do diff.
- [ ] `dotnet build Backend\SinistroAPI.csproj` sem erro.
- [ ] `dotnet test` 100% aprovado ou excecao formal aprovada.
- [ ] Teste de `sentinel-cortex` com `/health`.
- [ ] Teste de audio curto.
- [ ] Teste de laudo.
- [ ] Teste de imagem.
- [ ] Teste de video ou mock controlado.
- [ ] Teste de salvar e listar.
- [ ] Health check pos-deploy.
- [ ] Rollback conhecido.
- [ ] Documentacao atualizada.

## Checklist operacional de atendimento

| Etapa | Verificacao |
|---|---|
| Antes da oitiva | Confirmar arquivo, contexto, autorizacao e finalidade. |
| Durante processamento | Acompanhar progresso, erros e tempo. |
| Apos transcricao | Revisar falantes, timestamps e trechos inaudiveis. |
| Apos laudo | Verificar se cada afirmacao tem suporte na fonte. |
| Antes de salvar | Conferir tipo, arquivo, conteudo e revisao humana. |
| Antes de exportar | Confirmar que nao ha dados indevidos ou texto placeholder. |
| Em falha | Registrar erro, horario, arquivo, endpoint e responsavel. |

## Checklist de dados e privacidade

- [ ] Arquivos de audio/video/foto nao ficam versionados no Git.
- [ ] Dados pessoais sao tratados conforme finalidade do processo.
- [ ] Exports sao armazenados em local seguro.
- [ ] Logs nao contem credenciais ou dados sensiveis desnecessarios.
- [ ] Existe prazo de retencao de arquivos enviados e laudos.
- [ ] Exclusao de analises exige autorizacao e trilha de auditoria.

## Checklist de evidencias para auditor externo

Separar antes da auditoria:

- Copia consolidada desta documentacao em PDF/DOCX.
- Commit/tag da versao auditada.
- Resultado de testes.
- Lista de endpoints.
- Evidencia de deploy e variaveis sem segredos expostos.
- Amostra de laudo revisado e aprovado.
- Registro de usuario/role responsavel pela amostra.
- Registro de health check.
- Registro de riscos e CAPA atualizado.
- Evidencia de treinamento dos usuarios envolvidos.

