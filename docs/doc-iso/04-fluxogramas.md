# 04 - Fluxogramas

## Fluxograma principal do processo

```mermaid
flowchart TD
  A["Usuario acessa Sentinel"] --> B{"Escolhe fluxo"}
  B --> C["Audio ou merge de audio"]
  B --> D["Foto de vistoria"]
  B --> E["Video de sinistro"]
  B --> F["Analises salvas"]

  C --> C1["Frontend valida arquivo"]
  C1 --> C2["POST /api/transcrever"]
  C2 --> C3{"Arquivo valido e servico configurado?"}
  C3 -->|Nao| C4["Retorna erro controlado"]
  C3 -->|Sim| C5["Pre-processamento opcional no sentinel-cortex"]
  C5 --> C6["Azure Speech-to-Text"]
  C6 --> C7{"Transcricao valida?"}
  C7 -->|Nao| C8["Fallback Azure Whisper"]
  C7 -->|Sim| C9["Transcricao formatada"]
  C8 --> C9
  C9 --> C10["Usuario revisa ou edita"]
  C10 --> C11["POST /api/analisar/laudo"]
  C11 --> C12["Azure OpenAI gera laudo"]
  C12 --> G["Usuario revisa resultado"]

  D --> D1["POST /api/analisar/imagem"]
  D1 --> D2["Validar imagem e limite"]
  D2 --> D3["Provider vision analisa somente evidencias visiveis"]
  D3 --> G

  E --> E1["POST /api/analisar/video"]
  E1 --> E2["Validar video"]
  E2 --> E3{"Video pequeno para Vertex inline?"}
  E3 -->|Sim| E4["Vertex AI inline"]
  E3 -->|Nao| E5["Gemini File API resumable upload"]
  E4 --> G
  E5 --> G

  F --> F1["GET /api/analises"]
  F1 --> G

  G --> H{"Salvar?"}
  H -->|Nao| I["Exportar, copiar ou descartar"]
  H -->|Sim| J["POST /api/salvar"]
  J --> K["SQLite registra analise"]
  K --> L["Auditoria consulta evidencias"]
```

## Fluxo de transcricao

```mermaid
flowchart TD
  A["Receber arquivo"] --> B["Validar content type"]
  B --> C["Validar limite MaxAudioUploadMB"]
  C --> D["Copiar bytes para memoria"]
  D --> E{"Pre-processar?"}
  E -->|Sim| F["sentinel-cortex /process/audio"]
  E -->|Nao| G["Preservar arquivo original"]
  F --> H["Audio otimizado ou fallback"]
  G --> H
  H --> I["Enviar progresso SignalR"]
  I --> J["Tentar Azure Speech-to-Text"]
  J --> K{"Resultado tem qualidade?"}
  K -->|Sim| L["Aplicar correcoes e speaker detection"]
  K -->|Nao| M["Tentar Azure Whisper"]
  M --> L
  L --> N["Retornar TranscricaoResponse"]
```

## Fluxo de laudo pericial

```mermaid
flowchart TD
  A["Transcricao revisada"] --> B["POST /api/analisar/laudo"]
  B --> C["Validar texto nao vazio"]
  C --> D["Montar prompt pericial"]
  D --> E["Adicionar contexto do usuario"]
  E --> F["AzureOpenAIService"]
  F --> G["Temperature 0.0"]
  G --> H["Markdown do laudo"]
  H --> I["Pos-processar interlocutores"]
  I --> J["Frontend renderiza dados identificados e corpo"]
  J --> K["Revisao humana"]
```

## Fluxo de controle de qualidade e melhoria

```mermaid
flowchart LR
  A["Alteracao no codigo ou processo"] --> B["Revisao tecnica"]
  B --> C["Build e testes"]
  C --> D{"Conforme?"}
  D -->|Sim| E["Atualizar documentacao"]
  D -->|Nao| F["Registrar nao conformidade"]
  F --> G["Analisar causa raiz"]
  G --> H["Plano CAPA"]
  H --> I["Implementar correcao"]
  I --> C
  E --> J["Deploy controlado"]
  J --> K["Monitoramento e evidencias"]
```

## Fluxo de deploy atual

```mermaid
flowchart TD
  A["Executar deploy.ps1"] --> B["Incrementar versao app.js no index.html"]
  B --> C["git add -A"]
  C --> D["git commit"]
  D --> E["git push origin main"]
  E --> F{"DeployCortex informado?"}
  F -->|Sim| G["gcloud run deploy sentinel-cortex"]
  F -->|Nao| H["Pular cortex"]
  G --> I["Deploy backend"]
  H --> I
  I --> J["gcloud run deploy sentinel-nstech"]
  J --> K["Cloud Run publica URL"]
```

## Fonte do fluxograma

O arquivo `assets/fluxo-principal.mmd` contem o fluxo principal em formato Mermaid para uso em ferramentas externas.

## Copia em FigJam

Fluxograma principal publicado para navegacao visual:

`https://www.figma.com/board/tgI3bEdT50AyALl519fUaz?utm_source=codex&utm_content=edit_in_figjam&oai_id=&request_id=4df7c58b-0940-44d5-89a0-f687c7f6f03e`
