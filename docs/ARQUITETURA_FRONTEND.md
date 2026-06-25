# 📁 Arquitetura Frontend - Sentinel

## Visão Geral

O código JavaScript foi organizado em uma **arquitetura modular** com separação clara de responsabilidades.

```
wwwroot/js/
├── app.js              ← PONTO DE ENTRADA (orquestrador)
├── main.js             ← Código legado (não usado, backup)
│
├── api/                ← Comunicação com Backend
│   └── sinistroApi.js  → Todas as chamadas HTTP (fetch)
│
├── config/             ← Configurações
│   └── constants.js    → URLs, mapeamentos, limites
│
├── core/               ← Núcleo da Aplicação
│   ├── state.js        → Estado global (arquivos, transcrição, laudos)
│   ├── drafts.js       → Rascunhos (localStorage)
│   ├── storage.js      → Abstração do localStorage
│   ├── render.js       → Renderização de markdown/loading/erros
│   ├── theme.js        → Tema claro/escuro
│   ├── utils.js        → Funções utilitárias (capitalize, duração, etc)
│   └── ids.js          → Geração de IDs únicos
│
├── ui/                 ← Interface do Usuário
│   ├── modal.js        → Controle de modais, loaders, erros visuais
│   ├── navigation.js   → Navegação entre abas (audio/foto/video/salvos)
│   ├── upload.js       → Drag-and-drop e preview de arquivos
│   ├── toast.js        → Notificações (sucesso/erro/warning)
│   ├── user.js         → Perfil do usuário (avatar, nome)
│   └── loadingButton.js→ Estado de loading nos botões
│
├── services/           ← Lógica de Negócio
│   ├── analise/        → SUB-MÓDULO de análise (abaixo)
│   ├── export.js       → Copiar texto, exportar PDF
│   ├── salvar.js       → Salvar análises no banco
│   └── transcricao.js  → Formatação de transcrição
│
└── features/           ← Funcionalidades Específicas
    └── salvos.js       → Gestão de documentos salvos
```

---

## Fluxo Principal (app.js)

O `app.js` é o **orquestrador**. Ele:

1. **Importa** todos os módulos necessários
2. **Inicializa** ao carregar a página (DOMContentLoaded)
3. **Expõe funções globais** para o HTML (onclick)

### Funções expostas no `window`:
```javascript
// Navegação
window.setMode           → Muda aba (audio/foto/video/salvos)

// Análise
window.processar         → Analisa foto/vídeo
window.gerarTranscricao  → Gera transcrição de áudio
window.gerarRelatorioPericial → Gera laudo a partir da transcrição

// Edição inline
window.toggleEditTranscricao, salvarEdicaoTranscricao, cancelarEdicaoTranscricao
window.toggleEditLaudo, salvarEdicaoLaudo, cancelarEdicaoLaudo
window.toggleEditFoto, salvarEdicaoFoto, cancelarEdicaoFoto
window.toggleEditVideo, salvarEdicaoVideo, cancelarEdicaoVideo

// Outros
window.copiarTexto, exportarPDF, salvarAnalise, salvarTranscricao
window.limparResultado   → Botão "Novo"
```

---

## ⚡ services/analise/ (Sub-módulo)

Este é o coração da aplicação - onde acontece a análise de mídia.

### Estrutura:
```
services/analise/
├── index.js            ← Reexporta tudo (facade)
├── transcricao.js      ← gerarTranscricao(), abrirTranscricao()
├── relatorio.js        ← gerarRelatorioPericial()
├── processarMidia.js   ← processar('foto' | 'video')
└── edicaoInline.js     ← Todas as funções de edição inline
```

### Como funciona cada arquivo:

| Arquivo | Função Principal | O que faz |
|---------|------------------|-----------|
| `transcricao.js` | `gerarTranscricao()` | Envia áudio → API → Exibe resultado |
| `relatorio.js` | `gerarRelatorioPericial()` | Pega transcrição → API → Gera laudo |
| `processarMidia.js` | `processar(tipo)` | Envia foto/vídeo → API → Exibe laudo |
| `edicaoInline.js` | `toggleEdit*()` | Permite editar textos na tela |

---

## core/state.js (Estado Global)

Armazena dados da sessão atual:

```javascript
// Arquivos selecionados
getFile('audio') / setFile('audio', file)
getFile('foto')  / setFile('foto', file)
getFile('video') / setFile('video', file)

// Transcrição atual
getTranscricao() / setTranscricao(texto)
getTranscricaoValidada() / setTranscricaoValidada(bool)

// Laudos gerados
getLaudo('audio') / setLaudo('audio', markdown)
getLaudo('foto')  / setLaudo('foto', markdown)
getLaudo('video') / setLaudo('video', markdown)
```

---

## api/sinistroApi.js (Chamadas HTTP)

Centraliza TODAS as chamadas ao backend:

```javascript
gerarTranscricaoAPI(file)           → POST /api/transcrever
gerarLaudoTecnicoAPI(transcricao)   → POST /api/gerar-laudo
enviarParaAnalise(file, tipo)       → POST /api/analisar/foto ou /api/analisar/video
```

---

## ui/ (Interface)

| Arquivo | Responsabilidade |
|---------|------------------|
| `modal.js` | Abrir/fechar modais, HTML de loader/erro |
| `navigation.js` | Trocar abas, callback quando abre "Salvos" |
| `upload.js` | Drag-and-drop, preview de arquivo |
| `toast.js` | Notificações coloridas (sucesso/erro) |
| `user.js` | Iniciais do usuário no avatar |

---

## 📝 Fluxo: Gerar Transcrição

```
1. Usuário clica "GERAR TRANSCRIÇÃO"
   ↓
2. app.js → window.gerarTranscricao()
   ↓
3. services/analise/transcricao.js → gerarTranscricao()
   ↓
4. api/sinistroApi.js → gerarTranscricaoAPI(file)
   ↓
5. Backend /api/transcrever
   ↓
6. Resposta → setTranscricao(texto) [core/state.js]
   ↓
7. Exibe na tela + toast.success()
```

---

## Fluxo: Gerar Laudo Pericial

```
1. Usuário clica "GERAR LAUDO PERICIAL"
   ↓
2. app.js → window.gerarRelatorioPericial()
   ↓
3. services/analise/relatorio.js → gerarRelatorioPericial()
   ↓
4. api/sinistroApi.js → gerarLaudoTecnicoAPI(transcricao, duracao..)
   ↓
5. Backend /api/gerar-laudo
   ↓
6. Resposta → setLaudo('audio', markdown) [core/state.js]
   ↓
7. Exibe na tela + toast.success()
```

---

## Como adicionar nova funcionalidade

### Exemplo: Nova função de análise

1. Crie o arquivo em `services/analise/novaFuncao.js`
2. Exporte a função: `export function minhaNovaFuncao() { ... }`
3. Adicione no `services/analise/index.js`:
   ```javascript
   export { minhaNovaFuncao } from './novaFuncao.js';
   ```
4. Importe no `app.js`:
   ```javascript
   import { minhaNovaFuncao } from './services/analise/index.js';
   ```
5. Exponha globalmente:
   ```javascript
   window.minhaNovaFuncao = minhaNovaFuncao;
   ```

---

## Vantagens da Modularização

| Antes | Depois |
|-------|--------|
| 1 arquivo gigante (main.js) | Arquivos pequenos e focados |
| Difícil encontrar funções | Organizado por responsabilidade |
| Conflitos ao editar | Cada módulo é independente |
| Tudo no escopo global | Imports/exports controlados |

---

## Arquivos Importantes

| Se você quer... | Edite... |
|-----------------|----------|
| Mudar URLs da API | `config/constants.js` |
| Alterar lógica de transcrição | `services/analise/transcricao.js` |
| Alterar lógica de laudo | `services/analise/relatorio.js` |
| Mudar visual de loading | `ui/modal.js` |
| Adicionar nova aba | `ui/navigation.js` |
| Mudar estado global | `core/state.js` |

