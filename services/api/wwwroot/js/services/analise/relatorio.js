/**
 * @module services/analise/relatorio
 *
 * Fluxo de geração do LAUDO PERICIAL de áudio a partir da transcrição.
 *
 * Responsabilidades deste módulo:
 *  - Orquestrar a chamada à API de análise (Azure-only) que produz o laudo
 *    técnico em Markdown a partir da transcrição da oitiva/sinistro.
 *  - Persistir o resultado no estado global da aplicação e em um rascunho
 *    (draft) local, de forma que o laudo sobreviva a recarregamentos de página.
 *  - Renderizar o laudo na interface, separando os "dados identificados"
 *    (cabeçalho estruturado) do corpo textual do laudo.
 *
 * Convenção de domínio importante: o Markdown devolvido pela API pode conter o
 * marcador literal "---SEPARADOR_DADOS---". Quando presente, ele divide o
 * documento em duas seções (dados identificados | corpo do laudo). Veja
 * {@link renderizarLaudoComDados} para a regra completa.
 *
 * Todas as dependências de UI são resolvidas via `document.getElementById`,
 * portanto este módulo assume que os elementos do DOM já existem na página.
 */

import { gerarLaudoTecnicoAPI } from '../../api/sinistroApi.js?v=3';
import { getFile, getTranscricao, setLaudo } from '../../core/state.js';
import { refreshIcons, getMediaDuration } from '../../core/utils.js';
import { toast } from '../../ui/toast.js';
import { saveDraft } from '../../core/drafts.js';
import { renderMarkdown, renderErrorMessage, renderLoadingBar } from '../../core/render.js';
import { setLoadingButton } from '../../ui/loadingButton.js';

/**
 * Fonte canônica de geração do laudo pericial de áudio.
 *
 * É o handler chamado ao clicar em "GERAR LAUDO PERICIAL". Conduz todo o
 * fluxo: validação de pré-requisito, feedback visual de carregamento, coleta
 * de metadados (duração da mídia e contexto livre informado pelo perito),
 * chamada à API de análise (Azure-only), persistência do resultado e
 * renderização final. É `async` porque depende de duas operações de I/O:
 * a leitura da duração da mídia e a chamada HTTP à API.
 *
 * Como funciona, passo a passo:
 *  1. Lê a transcrição do estado global. Sem transcrição não há o que analisar,
 *     então emite um aviso e aborta (retorno antecipado).
 *  2. Resolve os elementos de UI envolvidos e guarda o HTML original do botão
 *     para poder restaurá-lo no `finally`.
 *  3. Coloca o botão em estado de carregamento e exibe a barra de progresso no
 *     container do laudo.
 *  4. Tenta obter a duração da mídia; se falhar, mantém o fallback
 *     "Não informada" e apenas registra o aviso (a duração é informativa, não
 *     bloqueia a geração do laudo).
 *  5. Lê o contexto adicional digitado pelo perito (campo `ctxAudio`).
 *  6. Chama `gerarLaudoTecnicoAPI`, recebendo `{ markdown }` com o laudo.
 *  7. Salva o laudo no estado e tenta gravar um draft local (falha de draft é
 *     tolerada — não deve impedir a exibição do laudo já gerado).
 *  8. Renderiza o laudo e revela o botão de edição.
 *
 * Efeitos colaterais:
 *  - Muta o estado global via `setLaudo('audio', ...)`.
 *  - Grava um rascunho local via `saveDraft` (best-effort).
 *  - Manipula o DOM (botões, containers, views) e ícones.
 *  - Emite toasts e loga no console.
 *
 * @async
 * @returns {Promise<void>} Resolve quando o fluxo termina (sucesso ou erro
 *   tratado). Não relança erros: falhas da API são capturadas e exibidas na UI.
 */
export async function gerarRelatorioPericial() {
  // Pré-requisito: a análise é feita sobre a transcrição já existente.
  // Sem ela, avisa o usuário e interrompe sem montar nenhum estado de loading.
  const transcricao = getTranscricao();
  if (!transcricao) {
    toast.warning('Por favor, gere a transcrição primeiro.');
    return;
  }

  // Elementos de UI do fluxo de laudo. `file` é a mídia de áudio carregada
  // (usada apenas para metadados: duração e nome do arquivo no draft).
  const btn = document.getElementById('btnGerarRelatorio');
  const laudoContainer = document.getElementById('laudoContainer');
  const laudoView = document.getElementById('laudoView');
  const file = getFile('audio');
  const contextEl = document.getElementById('ctxAudio');

  // Guarda o conteúdo original do botão para restaurá-lo no `finally`.
  // Fallback com o rótulo padrão caso o botão não exista no DOM.
  const originalText = btn ? btn.innerHTML : 'GERAR LAUDO PERICIAL';

  // Estado de carregamento: troca o conteúdo do botão por um spinner enquanto
  // a análise (operação potencialmente longa) está em andamento.
  setLoadingButton(btn, {
    loading: true,
    loadingHtml: '<i data-lucide="loader-2" class="spin"></i> ANALISANDO...'
  });

  // Revela o container do laudo e exibe a barra de progresso na área de
  // visualização enquanto aguardamos a resposta da API.
  if (laudoContainer) laudoContainer.classList.remove('hidden');
  renderLoadingBar(laudoView);

  try {
    // Duração da mídia: metadado informativo enviado à API. A leitura pode
    // falhar (formato não suportado, mídia ausente), então é isolada em seu
    // próprio try/catch e mantém o fallback "Não informada" sem abortar o fluxo.
    let duracao = 'Não informada';
    if (file) {
      try {
        duracao = await getMediaDuration(file);
      } catch (e) {
        console.warn('Erro ao obter duração:', e);
      }
    }

    // Contexto livre digitado pelo perito (ex.: número do sinistro, observações)
    // que é repassado à API para enriquecer a análise. Vazio se o campo não existir.
    const contexto = contextEl ? contextEl.value : '';

    // Chamada à API de análise (Azure-only). Retorna `{ markdown }` com o laudo
    // já formatado, possivelmente contendo o marcador "---SEPARADOR_DADOS---".
    const data = await gerarLaudoTecnicoAPI(transcricao, duracao, contexto);

    // Persiste o laudo no estado global sob a chave 'audio'.
    setLaudo('audio', data.markdown);

    // Salva um rascunho local para sobreviver a recarregamentos. É best-effort:
    // qualquer falha (ex.: storage cheio) é apenas logada e não interrompe a
    // renderização do laudo já obtido.
    try {
      const fileName = file?.name || 'N/A';
      saveDraft('audio', data.markdown, { arquivo: fileName, titulo: 'Laudo (áudio)' });
    } catch (e) {
      console.warn('Falha ao salvar draft de laudo (áudio):', e);
    }

    // Renderiza o laudo na UI, aplicando a regra de separação dados/corpo.
    renderizarLaudoComDados(data.markdown);

    // Com o laudo gerado, libera o botão de edição (antes oculto).
    const btnEdit = document.getElementById('btnEditLaudo');
    if (btnEdit) btnEdit.classList.remove('hidden');



  } catch (err) {
    // Falha na geração (rede, API, etc.): loga, notifica via toast e exibe a
    // mensagem de erro no lugar do laudo. O erro NÃO é relançado.
    console.error('Erro ao gerar relatório:', err);
    toast.error(`Erro ao gerar relatório: ${err.message}`);
    renderErrorMessage(laudoView, `Falha na análise: ${err.message}`);
  } finally {
    // Sempre restaura o botão ao estado normal, com sucesso ou erro.
    setLoadingButton(btn, { loading: false, defaultHtml: originalText });
  }
}

/**
 * Renderiza o laudo na interface aplicando a regra de domínio de separação
 * entre "dados identificados" e o "corpo do laudo".
 *
 * Regra de domínio do separador:
 *   O Markdown produzido pela API pode conter o marcador literal
 *   "---SEPARADOR_DADOS---". Quando presente, ele particiona o documento em
 *   exatamente duas seções:
 *     - parte[0] = DADOS IDENTIFICADOS — um cabeçalho estruturado (tipicamente
 *       uma tabela: nomes, datas, número do sinistro, etc.) renderizado em um
 *       container próprio destacado no topo.
 *     - parte[1] = CORPO DO LAUDO — o texto analítico/pericial em si.
 *   Quando o marcador NÃO está presente (`partes.length === 1`), todo o
 *   Markdown é tratado como corpo do laudo e a seção de dados fica oculta.
 *   `.trim()` remove o espaçamento residual ao redor do marcador em cada parte.
 *   Observação: usa apenas `partes[0]` e `partes[1]`; se o marcador aparecer
 *   mais de uma vez, as seções extras são ignoradas.
 *
 * Efeitos colaterais: manipula o DOM (mostra/oculta containers, injeta o
 * Markdown renderizado nas views) e reprocessa os ícones (`refreshIcons`).
 *
 * @param {string} markdown - Laudo completo em Markdown, opcionalmente contendo
 *   o marcador "---SEPARADOR_DADOS---" entre os dados identificados e o corpo.
 * @returns {void}
 */
export function renderizarLaudoComDados(markdown) {
  // Separa dados identificados do corpo do laudo usando o marcador de domínio.
  // Sem o marcador, `partes` tem 1 item e `corpoLaudo` permanece o markdown inteiro.
  const partes = markdown.split('---SEPARADOR_DADOS---');
  let dadosIdentificados = '';
  let corpoLaudo = markdown;

  // Há separador: parte[0] são os dados estruturados; parte[1] é o corpo.
  if (partes.length > 1) {
    dadosIdentificados = partes[0].trim();
    corpoLaudo = partes[1].trim();
  }

  // Renderiza a seção de dados identificados (se houver). Quando ausente,
  // o container correspondente é ocultado para não deixar área vazia na tela.
  const dadosContainer = document.getElementById('dadosIdentificadosContainer');
  const dadosView = document.getElementById('dadosIdentificadosView');

  if (dadosIdentificados && dadosView) {
    if (dadosContainer) dadosContainer.classList.remove('hidden');
    renderMarkdown(dadosView, dadosIdentificados);
  } else if (dadosContainer) {
    dadosContainer.classList.add('hidden');
  }

  // Renderiza o corpo do laudo na view principal.
  const laudoView = document.getElementById('laudoView');
  if (laudoView) {
    renderMarkdown(laudoView, corpoLaudo);
  }

  // Revela a barra de ações do laudo (exportar, editar, etc.) e reprocessa os
  // ícones, pois o HTML recém-injetado pode conter novos elementos de ícone.
  const actionsLaudo = document.getElementById('actionsLaudo');
  if (actionsLaudo) {
    actionsLaudo.classList.remove('hidden');
    refreshIcons();
  }
}
