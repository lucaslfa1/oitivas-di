/**
 * Fluxo: transcrição (áudio)
 *
 * Módulo responsável pelo passo de TRANSCRIÇÃO do pipeline forense de sinistros:
 * recebe o arquivo de áudio selecionado pelo usuário, dispara a transcrição via
 * backend (Azure Speech) e renderiza o resultado na UI. Também coordena efeitos
 * colaterais de UX:
 *  - progresso em tempo real via SignalR (duas barras: transcrição "simples" e
 *    barra de "merge", usada quando há junção de múltiplos trechos/abas);
 *  - persistência de rascunho (draft) da transcrição;
 *  - extração automática de "insights" (dados estruturados) em background.
 *
 * Convenções importantes deste arquivo:
 *  - Toda interação com o DOM é feita por id, defensivamente (sempre checando se
 *    o elemento existe antes de usar), pois a presença dos elementos depende da
 *    aba/tela ativa.
 *  - O estado canônico da transcrição vive em `core/state.js`; este módulo apenas
 *    o atualiza (setTranscricao / setTranscricaoValidada) e o lê (getTranscricao).
 *  - Imports dinâmicos (`await import(...)`) são usados para quebrar dependências
 *    circulares entre os módulos de análise e a camada de API.
 */

import { gerarTranscricaoAPI } from '../../api/sinistroApi.js?v=3';
import { initSignalR } from '../../services/signalrService.js';
import { getFile, setTranscricao, setTranscricaoValidada, getTranscricao } from '../../core/state.js';
import { refreshIcons } from '../../core/utils.js';
import { formatarTranscricao } from '../transcricao.js';
import { getLoaderHTML, getErrorHTML } from '../../ui/modal.js';
import { toast } from '../../ui/toast.js';
import { saveDraft } from '../../core/drafts.js';

/**
 * @deprecated CÓDIGO MORTO. Esta é uma cópia duplicada da geração de laudo
 * pericial que NÃO é mais utilizada a partir deste módulo. A fonte canônica e
 * mantida é `analise/relatorio.js` (a renderização final delega para
 * `renderizarLaudoComDados` daquele módulo). Mantida aqui apenas por
 * compatibilidade histórica; não documentar comportamento como referência nem
 * adicionar novas dependências a esta função — utilize `analise/relatorio.js`.
 *
 * Comportamento (apenas para fins históricos): lê a transcrição do estado, exige
 * que ela exista, e dispara a API de geração de laudo (`gerarLaudoAPI`), renderizando
 * o resultado via `renderizarLaudoComDados` de `./relatorio.js`. Coordena estado de
 * UI (botão "GERAR LAUDO", loader, scroll, botões de edição/ações).
 *
 * Efeitos colaterais: muta o DOM (innerHTML/disabled/classes), faz scroll suave até
 * o laudo, exibe toasts e dispara imports dinâmicos para quebrar dependência circular.
 *
 * @returns {Promise<void>} Resolve quando o fluxo termina (sucesso ou falha tratada).
 *   Retorna cedo (sem efeitos de rede) quando não há transcrição no estado.
 * @throws {never} Erros de rede/render são capturados internamente e exibidos via toast;
 *   a Promise não rejeita.
 */
export async function gerarRelatorioPericial() {
  const transcricao = getTranscricao();
  if (!transcricao) {
    toast.warning('Gere a transcrição primeiro.');
    return;
  }

  const btn = document.getElementById('btnGerarRelatorio');
  const originalText = btn?.innerHTML ?? '';
  const laudoView = document.getElementById('laudoView');
  const laudoContainer = document.getElementById('laudoContainer');

  try {
    if (btn) {
      btn.disabled = true;
      btn.innerHTML = '<i data-lucide="loader-2" class="spin"></i> GERANDO LAUDO...';
    }

    if (laudoView) laudoView.innerHTML = getLoaderHTML();
    if (laudoContainer) laudoContainer.classList.remove('hidden');

    // Rola até o laudo
    laudoContainer.scrollIntoView({ behavior: 'smooth' });

    // Import dinâmico para evitar dependência circular
    const { gerarLaudoAPI } = await import('../../api/sinistroApi.js?v=3');

    // Payload simplificado sem tipoOperacao
    const data = await gerarLaudoAPI({
      transcricao: transcricao,
      duracao: document.getElementById('timeTotal')?.innerText || "Não informada",
      contexto: "" // Contexto manual se houver campo futuro
    });

    const { renderizarLaudoComDados } = await import('./relatorio.js');
    renderizarLaudoComDados(data.laudo);

    const btnEdit = document.getElementById('btnEditLaudo');
    if (btnEdit) btnEdit.classList.remove('hidden');

    const actionsLaudo = document.getElementById('actionsLaudo');
    if (actionsLaudo) actionsLaudo.classList.remove('hidden');

    refreshIcons();
    toast.success('Laudo gerado com sucesso!');

  } catch (err) {
    console.error('Erro ao gerar laudo:', err);
    if (laudoView) laudoView.innerHTML = getErrorHTML(err.message);
    toast.error('Falha ao gerar laudo: ' + err.message);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.innerHTML = originalText;
    }
    refreshIcons();
  }
}

/**
 * Orquestra o passo de TRANSCRIÇÃO de áudio do pipeline forense.
 *
 * Fluxo (o COMO):
 *  1. Recupera o arquivo de áudio do estado (`getFile('audio')`); aborta com aviso
 *     se nenhum foi selecionado.
 *  2. Prepara a UI: desabilita o botão, troca seu rótulo por um loader animado,
 *     oculta o empty-state e exibe o container/loader da transcrição.
 *  3. Abre a conexão SignalR ANTES de chamar a API para obter o `connectionId`. Esse
 *     id é repassado ao backend para que ele empurre eventos de progresso de volta a
 *     ESTA aba via `updateProgress` (push em tempo real, e não polling).
 *  4. Chama `gerarTranscricaoAPI(file, connectionId)`, persiste o texto no estado
 *     (`setTranscricao`) e o marca como validado (`setTranscricaoValidada(true)`).
 *  5. Salva um rascunho (draft) defensivamente — falha de draft não interrompe o fluxo.
 *  6. Renderiza a transcrição formatada e revela botões de edição/ações.
 *  7. Dispara, em BACKGROUND (sem `await`, via `.then`), a extração automática de
 *     insights (`extrairDadosAPI`), montando cards apenas para campos preenchidos.
 *
 * Sobre as DUAS barras de progresso (o PORQUÊ): `updateProgress` atualiza
 * simultaneamente a barra "simples" (progress*) e a barra de "merge"
 * (progress*Merge), usada quando há junção de múltiplos trechos/abas. A barra de
 * merge só é exibida se a aba `tab-merge` estiver visível; ambas são tratadas pelo
 * mesmo callback para manter os dois indicadores sincronizados.
 *
 * Imports dinâmicos (`await import(...)`) são usados para quebrar dependências
 * circulares com a camada de API.
 *
 * Efeitos colaterais: muta extensivamente o DOM (por id, sempre checando existência),
 * abre conexão SignalR, faz I/O de rede (transcrição + insights), grava draft em
 * storage, exibe toasts e loga no console. No `finally`, reabilita o botão e agenda
 * a ocultação das barras de progresso após 3000 ms (3 s) — atraso proposital para que
 * o usuário enxergue o 100% concluído antes de a barra sumir.
 *
 * @returns {Promise<void>} Resolve quando o fluxo principal termina (sucesso ou falha
 *   tratada). A extração de insights roda em background e pode concluir DEPOIS desta
 *   Promise. Retorna cedo (sem rede) se não houver arquivo de áudio.
 * @throws {never} Erros da transcrição são capturados no `catch` e exibidos via toast;
 *   a Promise não rejeita. Erros de draft e de insights são engolidos separadamente.
 */
export async function gerarTranscricao() {
  const file = getFile('audio');
  if (!file) {
    toast.warning('Selecione um arquivo de áudio primeiro.');
    return;
  }

  const btn = document.getElementById('btnGerarTranscricao');
  const transcricaoContainer = document.getElementById('transcricaoContainer');
  const transcricaoView = document.getElementById('transcricaoView');
  const originalText = btn?.innerHTML ?? '';

  try {
    if (btn) {
      btn.disabled = true;
      btn.innerHTML = '<i data-lucide="loader-2" class="spin"></i> TRANSCREVENDO...';
    }
    refreshIcons();

    const emptyState = document.getElementById('audioEmptyState');
    if (emptyState) emptyState.classList.add('hidden');
    if (transcricaoContainer) transcricaoContainer.classList.remove('hidden');

    if (transcricaoView) transcricaoView.innerHTML = getLoaderHTML();

    const progressContainer = document.getElementById('progressContainer');
    const progressContainerMerge = document.getElementById('progressContainerMerge');

    // Atualiza ambas as barras (simples e robusto)
    /**
     * Callback de progresso invocado pelo SignalR a cada evento empurrado pelo backend.
     * Atualiza, de uma só vez, as DUAS barras (simples e de merge) para mantê-las
     * sincronizadas, independentemente de qual esteja visível no momento.
     *
     * @param {string} _msg - Mensagem de status enviada pelo backend. Atualmente
     *   ignorada (o status textual é zerado via string vazia); mantida na assinatura
     *   por compatibilidade com o contrato de eventos do SignalR.
     * @param {number} pct - Percentual de conclusão (0–100) aplicado ao texto e à
     *   largura CSS de ambas as barras.
     * @returns {void}
     */
    const updateProgress = (_msg, pct) => {
      const els = [
        { status: 'progressStatus', percent: 'progressPercent', bar: 'progressBar' },
        { status: 'progressStatusMerge', percent: 'progressPercentMerge', bar: 'progressBarMerge' }
      ];

      els.forEach(el => {
        const s = document.getElementById(el.status);
        const p = document.getElementById(el.percent);
        const b = document.getElementById(el.bar);
        if (s) s.innerText = '';
        if (p) p.innerText = `${pct}%`;
        if (b) b.style.width = `${pct}%`;
      });
    };

    if (progressContainer) progressContainer.classList.remove('hidden');
    const tabMerge = document.getElementById('tab-merge');
    if (progressContainerMerge && tabMerge && !tabMerge.classList.contains('hidden')) {
      progressContainerMerge.classList.remove('hidden');
    }

    updateProgress("", 0);

    // Conectar ao SignalR
    const connectionId = await initSignalR(updateProgress);

    const data = await gerarTranscricaoAPI(file, connectionId);

    setTranscricao(data.transcricao);
    setTranscricaoValidada(true);

    try {
      const fileName = file?.name || 'N/A';
      saveDraft('transcricao', data.transcricao, { arquivo: fileName, titulo: 'Transcrição (rascunho)' });
    } catch (e) {
      console.warn('Falha ao salvar draft de transcrição:', e);
    }

    const htmlFormatado = formatarTranscricao(data.transcricao);
    if (transcricaoView) transcricaoView.innerHTML = htmlFormatado;

    const btnEdit = document.getElementById('btnEditTranscricao');
    if (btnEdit) btnEdit.classList.remove('hidden');

    // Mostrar botões de ação após gerar transcrição
    const actionsTranscricao = document.getElementById('actionsTranscricao');
    if (actionsTranscricao) actionsTranscricao.classList.remove('hidden');

    refreshIcons();

    // --- EXTRAÇÃO AUTOMÁTICA DE DADOS (Background) ---
    try {
      const insightsContainer = document.getElementById('insightsContainer');
      const insightsContent = document.getElementById('insightsContent');

      if (insightsContainer && insightsContent) {
        // Mostra loading discreto ou apenas inicia
        console.log("🔍 Iniciando extração de dados em background...");

        // Chama API
        import('../../api/sinistroApi.js?v=3').then(async ({ extrairDadosAPI }) => {
          const dados = await extrairDadosAPI(data.transcricao);

          if (dados && Object.keys(dados).length > 0) {
            insightsContent.innerHTML = ''; // Limpa

            for (const [key, value] of Object.entries(dados)) {
              if (value && value !== 'null' && value !== 'Não mencionado') {
                const label = key.replace(/_/g, ' ');
                const div = document.createElement('div');
                div.className = 'insight-item';
                div.innerHTML = `
                                <span class="insight-label">${label}</span>
                                <span class="insight-value">${value}</span>
                            `;
                insightsContent.appendChild(div);
              }
            }

            // Só mostra se tiver dados
            if (insightsContent.children.length > 0) {
              insightsContainer.classList.remove('hidden');
              refreshIcons();
              toast.success("Insights extraídos com sucesso!");
            }
          }
        });
      }
    } catch (e) {
      console.warn("Erro na extração automática:", e);
    }

  } catch (err) {
    console.error('Erro na transcrição:', err);
    if (transcricaoView) transcricaoView.innerHTML = getErrorHTML(err.message);
    toast.error('Falha ao gerar transcrição: ' + err.message);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.innerHTML = originalText;
    }

    if (progressContainer) {
      setTimeout(() => progressContainer.classList.add('hidden'), 3000);
    }
    const progressContainerMerge = document.getElementById('progressContainerMerge');
    if (progressContainerMerge) {
      setTimeout(() => progressContainerMerge.classList.add('hidden'), 3000);
    }
    refreshIcons();
  }
}

/**
 * Traz a transcrição já gerada para a área visível da tela.
 *
 * Não gera nada: apenas verifica se existe transcrição no estado e, em caso
 * afirmativo, faz scroll suave até o container correspondente. Serve como atalho de
 * navegação para o usuário reencontrar o resultado após rolar a página.
 *
 * Efeitos colaterais: rola a viewport (scrollIntoView suave) e/ou exibe um toast
 * informativo quando não há transcrição disponível.
 *
 * @returns {void} Retorna cedo (apenas com toast) se não houver transcrição no estado.
 */
export function abrirTranscricao() {
  const transcricao = getTranscricao();
  if (!transcricao) {
    toast.info('Nenhuma transcrição disponível. Clique em "Gerar Transcrição" primeiro.');
    return;
  }

  const transcricaoContainer = document.getElementById('transcricaoContainer');
  if (transcricaoContainer) {
    transcricaoContainer.scrollIntoView({ behavior: 'smooth' });
  }
}
