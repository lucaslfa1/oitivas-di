# 00 - Controle Documental

## Identificacao

| Campo | Valor |
|---|---|
| Documento | Documentacao ISO 9001 do Projeto Sentinel |
| Pasta | `docs/doc-iso` |
| Sistema | Sentinel - Analise forense de sinistros veiculares |
| Repositorio | `https://github.com/lucaslfa1/oitivas-di` |
| Base inspecionada | Branch `main`, commit `cd4e0e5` |
| Data de elaboracao | 2026-05-18 |
| Responsavel tecnico | Equipe do projeto Sentinel |
| Status | Documento tecnico auditavel, pendente de aprovacao formal |

## Objetivo

Estabelecer uma documentacao completa do projeto Sentinel para auditoria de qualidade, com foco em:

- Processo de desenvolvimento, operacao, verificacao e melhoria.
- Evidencias rastreaveis no codigo-fonte.
- Fluxo ponta a ponta de atendimento, processamento, laudo, salvamento e monitoramento.
- Matriz de aderencia aos requisitos da ISO 9001:2015.
- Registro de riscos, nao conformidades, controles e acoes corretivas.

## Escopo

Incluido no escopo:

- Backend ASP.NET Core 8 em `Backend/`.
- Frontend estatico servido por `Backend/wwwroot/`.
- Microservico Python `sentinel-cortex/`.
- Dashboard KPI `dashboard-kpi/`.
- Banco SQLite usado pelo backend.
- Scripts de teste, deploy e verificacao presentes no repositorio.
- Documentos existentes em `docs/` usados como referencia.

Fora do escopo:

- Infraestrutura real em nuvem que nao esteja evidenciada por codigo, template ou script.
- Politicas corporativas externas da nstech/Opentech nao versionadas neste repositorio.
- Evidencias de operacao em producao nao presentes localmente, como logs de Cloud Run, registros IAM, tickets, incidentes e aprovacoes formais.

## Criterios de controle documental

| Controle | Regra |
|---|---|
| Codigo do documento | `SENTINEL-QMS-ISO9001` |
| Revisao | Deve ser incrementada a cada alteracao material. |
| Aprovacao | Requer aprovacao do responsavel tecnico e dono do processo. |
| Retencao | Manter historico no Git por tempo indeterminado ou conforme politica corporativa. |
| Publicacao | Exportar copia consolidada em DOCX/PDF quando for usada em auditoria. |
| Distribuicao | Somente por canais controlados. Nao distribuir com segredos ou credenciais. |

## Historico de revisoes

| Revisao | Data | Autor | Alteracao | Aprovacao |
|---|---:|---|---|---|
| 0.1 | 2026-05-18 | Codex | Criacao inicial baseada na inspecao do repositorio. | Pendente |

## Referencias internas

- `README.md`
- `ARQUITETURA.md`
- `docs/ARQUITETURA_FRONTEND.md`
- `docs/PLANO_AUTENTICACAO.md`
- `docs/RELATORIO_MELHORIAS.md`
- `docs/referencia-audio/README_IMPLEMENTACAO_AUDIO_AZURE.md`
- `docker-compose.yml`
- `deploy.ps1`
- `setup_gcp.ps1`

## Premissas adotadas

- A documentacao descreve o estado atual do codigo versionado, nao necessariamente o ambiente produtivo em execucao.
- Quando documentos antigos divergem do codigo atual, a evidencia primaria considerada e o codigo.
- Segredos foram mascarados nesta documentacao. A presenca de segredos no repositorio e tratada como nao conformidade.
- O termo "ISO 9001" e usado como referencia de alinhamento a requisitos de gestao da qualidade, nao como declaracao de certificacao.

## Regras para futuras alteracoes

1. Toda mudanca em endpoint, fluxo, prompt, provider de IA, autenticacao, persistencia ou deploy deve atualizar esta pasta.
2. Todo risco novo deve ser registrado em `06-riscos-controles-acoes.md`.
3. Toda evidencia de teste deve ser registrada em `07-validacao-evidencias.md`.
4. Nenhum arquivo desta pasta deve conter chave real, senha real, token ou segredo operacional.
5. Artefatos exportados em `exports/` devem ser regenerados apos mudancas relevantes.

