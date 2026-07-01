/**
 * Sentinel - Main Entry Point
 * Orquestrador principal da aplicação (sistema de análise forense de sinistros).
 *
 * Responsabilidades deste módulo:
 *  - Importar todos os submódulos (core, ui, services, features) que compõem o front-end.
 *  - Disparar a inicialização da aplicação no evento DOMContentLoaded (ícones, tema,
 *    usuário, uploads, merge de áudio, modal e callbacks de navegação).
 *  - Expor em `window.*` as funções chamadas diretamente por handlers `onclick` no HTML.
 *    Como o app usa ES Modules (escopo isolado por padrão), o HTML inline só enxerga
 *    o que for explicitamente atribuído a `window`. Por isso este arquivo funciona como
 *    a "ponte" entre o HTML estático e a lógica modularizada.
 *
 * Observação de arquitetura: a análise de mídia (áudio/foto/vídeo/transcrição) é
 * processada exclusivamente via Azure (Azure-only). Não há mais integração com
 * Gemini/Vertex/Central; qualquer resíduo desses nomes deve ser tratado como obsoleto.
 *
 * Efeito colateral: a simples importação deste arquivo registra o listener de
 * DOMContentLoaded e popula o objeto global `window`, então ele deve ser carregado
 * uma única vez como ponto de entrada da página.
 */

// ============================================
// IMPORTS
// ============================================

// Core
import { initTheme } from './core/theme.js';
import { refreshIcons, linkifyTimestamps } from './core/utils.js';
import { clearDrafts } from './core/drafts.js';
import { setLaudo, setTranscricao, setTranscricaoValidada, getLaudo } from './core/state.js';

// UI
import { initUploads, clearFile } from './ui/upload.js';
import { setMode, setOnSalvosOpen } from './ui/navigation.js';
import { initModal, fecharModalTranscricao, fecharModalOverlay, abrirModalTranscricao } from './ui/modal.js';
import { initUser } from './ui/user.js';
import { toast } from './ui/toast.js';

// Services
import {
  processar,
  gerarTranscricao,
  abrirTranscricao,
  gerarRelatorioPericial,
  toggleEditTranscricao,
  salvarEdicaoTranscricao,
  cancelarEdicaoTranscricao,
  toggleEditLaudo,
  salvarEdicaoLaudo,
  cancelarEdicaoLaudo,
  toggleEditFoto,
  salvarEdicaoFoto,
  cancelarEdicaoFoto,
  toggleEditVideo,
  salvarEdicaoVideo,
  cancelarEdicaoVideo
} from './services/analise/index.js?v=6';
import { copiarTexto, exportarPDF, exportarWord } from './services/export.js';
import { salvarAnalise, salvarTranscricao } from './services/salvar.js';

// Features
import { carregarSalvos,
  filtrarSalvos,
  visualizarSalvo,
  exportarSalvo,
  exportarSalvoWord,
  excluirSalvo,
  toggleEditSalvo,
  salvarEdicaoSalvo,
  cancelarEdicaoSalvo,
  fecharVisualizacaoSalvo,
  copiarTextoSalvo,
  exportarSalvoAtual,
  exportarSalvoAtualWord
} from './features/salvos.js';
import { initMerge, switchAudioTab, downloadMergedAudio, uploadMergedAudio } from './features/merge.js';


// ============================================
// INICIALIZAÇÃO
// ============================================

console.log("✅ App.js (ES6 Modules) carregado!");

/**
 * Bootstrap da aplicação.
 *
 * Ouve o DOMContentLoaded para garantir que todos os elementos referenciados
 * por `document.getElementById` já existam no DOM antes de configurá-los.
 * A ordem das inicializações importa: ícones e tema primeiro (afetam toda a
 * UI), depois usuário/uploads/merge/modal (que dependem de elementos prontos)
 * e, por fim, o registro do callback de navegação e os bindings da sidebar mobile.
 *
 * @listens Document#DOMContentLoaded
 * @returns {void}
 */
document.addEventListener('DOMContentLoaded', () => {
  console.log("🚀 DOM carregado, inicializando...");

  // Inicializa ícones Lucide
  refreshIcons();
  console.log("✅ Lucide icons carregados");

  // Inicializa tema (dark/light)
  initTheme();

  // Inicializa usuário
  initUser();

  // Configura uploads para cada tipo
  initUploads();

  // Inicializa Merge de Áudio
  initMerge();

  // Inicializa modal
  initModal();

  // Registra callback para quando aba salvos for aberta
  setOnSalvosOpen(carregarSalvos);

  // ============================================
  // MENU HAMBURGER (Mobile Sidebar Toggle)
  // ============================================
  const hamburgerBtn = document.getElementById('hamburgerBtn');
  const sidebar = document.getElementById('sidebar');
  const sidebarOverlay = document.getElementById('sidebarOverlay');

  /**
   * Abre a sidebar de navegação no layout mobile.
   *
   * Como funciona:
   *  - Adiciona a classe `open` à sidebar (o CSS faz o slide-in da gaveta).
   *  - Ativa o overlay escuro (`active`) por trás da sidebar para foco visual.
   *  - Trava o scroll do `body` (`overflow: hidden`) enquanto a gaveta está
   *    aberta, evitando que o conteúdo de fundo role junto com a navegação.
   *
   * Closure: depende das variáveis `sidebar` e `sidebarOverlay` capturadas no
   * escopo do DOMContentLoaded.
   *
   * @returns {void}
   */
  function openSidebar() {
    sidebar.classList.add('open');
    sidebarOverlay.classList.add('active');
    document.body.style.overflow = 'hidden'; // Previne scroll do body
  }

  /**
   * Fecha a sidebar de navegação no layout mobile.
   *
   * Reverte exatamente o que `openSidebar` aplica: remove `open` da sidebar,
   * desativa o overlay e restaura o scroll do `body` (limpa o `overflow` inline
   * voltando ao valor herdado do CSS).
   *
   * @returns {void}
   */
  function closeSidebar() {
    sidebar.classList.remove('open');
    sidebarOverlay.classList.remove('active');
    document.body.style.overflow = '';
  }

  // Botão hamburger atua como toggle: se a gaveta já está aberta, fecha; senão, abre.
  // O `if (hamburgerBtn)` evita erro quando o botão não existe (ex.: layout desktop).
  if (hamburgerBtn) {
    hamburgerBtn.addEventListener('click', () => {
      if (sidebar.classList.contains('open')) {
        closeSidebar();
      } else {
        openSidebar();
      }
    });
  }

  // Clicar no overlay (área escura fora da gaveta) fecha a sidebar — padrão UX de drawer.
  if (sidebarOverlay) {
    sidebarOverlay.addEventListener('click', closeSidebar);
  }

  // Fecha sidebar ao clicar em item do menu (mobile)
  // O breakpoint 700px define o limite "mobile": acima dele a sidebar é fixa e
  // não deve fechar ao navegar; em telas <= 700px a gaveta se fecha após a escolha.
  document.querySelectorAll('.menu-item').forEach(item => {
    item.addEventListener('click', () => {
      if (window.innerWidth <= 700) {
        closeSidebar();
      }
    });
  });

  console.log("✅ Sentinel inicializado com sucesso!");
});

// ============================================
// EXPOSIÇÃO GLOBAL (para onclick no HTML)
// ============================================
//
// Contrato geral: cada `window.<nome>` abaixo é a API pública consumida pelos
// atributos `onclick`/`onchange` do HTML estático. Salvo indicação contrária,
// são re-exportações diretas das funções dos módulos (mesma assinatura e mesmo
// comportamento); a atribuição a `window` apenas as torna acessíveis ao escopo
// global do HTML, que não enxerga o escopo de módulo ES6.
// Mantenha o nome exposto idêntico ao usado no HTML — renomear quebra os handlers.

// Navegação
// setMode(mode): troca a aba/modo ativo da aplicação (transcrição, áudio, foto, vídeo, salvos...).
// switchAudioTab(tab): alterna entre as sub-abas da feature de merge de áudio.
// downloadMergedAudio()/uploadMergedAudio(): baixam/enviam o áudio resultante do merge.
window.setMode = setMode;
window.switchAudioTab = switchAudioTab;
window.downloadMergedAudio = downloadMergedAudio;
window.uploadMergedAudio = uploadMergedAudio;

// Upload
// Wrapper fino sobre `clearFile`: o HTML chama `clearFile(type)` e este adaptador
// apenas repassa o argumento. Existe como arrow function (em vez de re-export direto)
// para padronizar a forma de chamada no HTML e isolar a assinatura caso a interna mude.
/**
 * Remove o arquivo selecionado para um determinado tipo de upload.
 * @param {('transcricao'|'audio'|'foto'|'video')} type - Tipo de upload a ser limpo.
 * @returns {void}
 */
window.clearFile = (type) => {
  clearFile(type);
};

// Análise
// processar(): dispara o pipeline de análise forense (Azure-only) do tipo ativo.
// gerarTranscricao(): gera a transcrição do áudio/vídeo carregado.
// abrirTranscricao(): abre a transcrição já existente para visualização.
// gerarRelatorioPericial(): produz o laudo pericial a partir do material analisado.
window.processar = processar;
window.gerarTranscricao = gerarTranscricao;
window.abrirTranscricao = abrirTranscricao;
window.gerarRelatorioPericial = gerarRelatorioPericial;

// Edição Inline - Transcrição
// Trio padrão de edição inline (mesmo contrato repetido para laudo/foto/vídeo):
//  - toggleEdit*(): entra/sai do modo de edição do bloco correspondente.
//  - salvarEdicao*(): persiste o texto editado no estado e re-renderiza.
//  - cancelarEdicao*(): descarta as alterações e volta ao conteúdo original.
window.toggleEditTranscricao = toggleEditTranscricao;
window.salvarEdicaoTranscricao = salvarEdicaoTranscricao;
window.cancelarEdicaoTranscricao = cancelarEdicaoTranscricao;

// Edição Inline - Laudo (mesmo contrato toggle/salvar/cancelar, aplicado ao laudo pericial)
window.toggleEditLaudo = toggleEditLaudo;
window.salvarEdicaoLaudo = salvarEdicaoLaudo;
window.cancelarEdicaoLaudo = cancelarEdicaoLaudo;

// Edição Inline - Foto (mesmo contrato, aplicado à análise de imagem)
window.toggleEditFoto = toggleEditFoto;
window.salvarEdicaoFoto = salvarEdicaoFoto;
window.cancelarEdicaoFoto = cancelarEdicaoFoto;

// Edição Inline - Vídeo (mesmo contrato, aplicado à análise de vídeo)
window.toggleEditVideo = toggleEditVideo;
window.salvarEdicaoVideo = salvarEdicaoVideo;
window.cancelarEdicaoVideo = cancelarEdicaoVideo;

// Modal
// Atenção ao mapeamento de nomes (intencional, casa com o HTML):
//  - window.fecharTranscricao -> fecharModalTranscricao (fecha o modal de transcrição/laudo).
//  - window.fecharModal       -> fecharModalOverlay (fecha o overlay genérico de modal).
window.fecharTranscricao = fecharModalTranscricao;
window.fecharModal = fecharModalOverlay;

// Exportação
// copiarTexto(): copia o conteúdo ativo para a área de transferência.
// exportarPDF()/exportarWord(): geram o arquivo do resultado nos formatos PDF/DOCX.
window.copiarTexto = copiarTexto;
window.exportarPDF = exportarPDF;
window.exportarWord = exportarWord;

// Salvamento
// salvarAnalise(): persiste o laudo/análise atual no backend.
// salvarTranscricao(): persiste a transcrição atual no backend.
window.salvarAnalise = salvarAnalise;
window.salvarTranscricao = salvarTranscricao;

// Arquivos Salvos - Listagem
// carregarSalvos(): busca e renderiza a lista de análises salvas.
// filtrarSalvos(): filtra a lista exibida conforme o termo/critério de busca.
// visualizarSalvo(id): abre um item salvo para visualização inline.
// exportarSalvo(id)/exportarSalvoWord(id): exportam o item salvo em PDF/DOCX.
// excluirSalvo(id): remove o item salvo (efeito colateral: chamada ao backend).
window.carregarSalvos = carregarSalvos;
window.filtrarSalvos = filtrarSalvos;
window.visualizarSalvo = visualizarSalvo;
window.exportarSalvo = exportarSalvo;
window.exportarSalvoWord = exportarSalvoWord;
window.excluirSalvo = excluirSalvo;

import { seekTo } from './ui/player.js';

// Player Sync
// seekTo(seconds): posiciona o player de áudio/vídeo no timestamp informado.
// Usado pelos timestamps clicáveis da linha do tempo (sincroniza texto e mídia).
window.seekTo = seekTo;

// Maximizar laudo num modal grande (mais espaço para leitura)
/**
 * Abre o laudo pericial de um tipo em um modal maximizado para leitura confortável.
 *
 * Fluxo:
 *  1. Recupera o markdown do laudo do estado via `getLaudo(tipo)`.
 *  2. Se não houver laudo gerado ainda, avisa o usuário (toast) e aborta — evita
 *     abrir um modal vazio.
 *  3. Renderiza o markdown para HTML. Usa a lib global `marked` quando disponível;
 *     o fallback `<pre>${md}</pre>` garante que, mesmo sem a lib carregada, o texto
 *     bruto ainda seja exibido (degradação graciosa) em vez de quebrar.
 *  4. Aplica `linkifyTimestamps` para transformar timestamps do texto em links
 *     clicáveis (que acionam `seekTo` no player) e abre o modal de transcrição
 *     reutilizado, com título/ícone "maximize-2" do Lucide.
 *
 * @param {('audio'|'foto'|'video')} tipo - Identifica de qual análise pegar o laudo no estado.
 * @returns {void}
 * @sideeffect Exibe um toast de aviso quando não há laudo, ou abre um modal na UI.
 */
window.maximizarLaudo = (tipo) => {
  const md = getLaudo(tipo);
  if (!md) { toast.warning('Nenhum laudo para exibir ainda.'); return; }
  const html = (typeof marked !== 'undefined') ? marked.parse(md) : `<pre>${md}</pre>`;
  abrirModalTranscricao(linkifyTimestamps(html), '<i data-lucide="maximize-2"></i> Laudo Pericial');
};

// Arquivos Salvos - Visualização Inline e Edição
// Mesma família de operações da listagem, porém atuando sobre o item salvo aberto
// na visualização inline (o "atual"):
//  - toggleEditSalvo/salvarEdicaoSalvo/cancelarEdicaoSalvo: edição inline do item aberto.
//  - fecharVisualizacaoSalvo(): fecha o painel de visualização inline.
//  - copiarTextoSalvo(): copia o texto do item aberto.
//  - exportarSalvoAtual()/exportarSalvoAtualWord(): exportam o item aberto em PDF/DOCX.
window.toggleEditSalvo = toggleEditSalvo;
window.salvarEdicaoSalvo = salvarEdicaoSalvo;
window.cancelarEdicaoSalvo = cancelarEdicaoSalvo;
window.fecharVisualizacaoSalvo = fecharVisualizacaoSalvo;
window.copiarTextoSalvo = copiarTextoSalvo;
window.exportarSalvoAtual = exportarSalvoAtual;
window.exportarSalvoAtualWord = exportarSalvoAtualWord;

// Limpar resultado / Novo
/**
 * Reseta a UI e o estado do resultado de uma análise, voltando-a ao estado "vazio".
 *
 * Acionada pelo botão "Novo"/limpar de cada aba. A lógica é ramificada por `type`
 * porque cada tipo de análise tem IDs de elementos e empty-states próprios, mas
 * todos seguem o mesmo roteiro: (1) apagar o draft persistido para que a navegação
 * não restaure automaticamente o conteúdo limpo; (2) zerar o estado em memória;
 * (3) esconder containers/botões de ação/edição; (4) reexibir o placeholder vazio;
 * (5) `refreshIcons()` para re-renderizar os ícones Lucide injetados via innerHTML.
 *
 * Detalhes por ramo:
 *  - 'transcricao': limpa transcrição e flag de validação, esvazia a view, esconde
 *    também o `laudoContainer` (o laudo deriva da transcrição; reiniciá-la invalida o laudo)
 *    e reexibe o empty-state do áudio.
 *  - 'audio' | 'foto' | 'video': zera o laudo daquele tipo. O ID de saída e o conteúdo
 *    do empty-state são montados dinamicamente a partir do `type`:
 *      • 'audio' usa `laudoView` e fica com innerHTML vazio (o empty-state é tratado
 *        à parte, escondendo `laudoContainer`/`btnEditLaudo`/`actionsLaudo`).
 *      • 'foto'/'video' usam `outputFoto`/`outputVideo` e recebem um empty-state com
 *        ícone ('image' para foto, 'clapperboard' para vídeo) e mensagem correspondente.
 *    O ternário `type === 'foto' ? 'Foto' : 'Video'` capitaliza o sufixo para casar
 *    com a convenção de IDs do HTML (ex.: `actionsFoto`, `btnEditVideo`).
 *
 * Robustez: cada acesso ao DOM é guardado por `if (elemento)` para não quebrar caso
 * um id não exista no layout atual; o `try/catch` externo captura qualquer falha e
 * informa o usuário via toast em vez de deixar a exceção escapar para o console silenciosamente.
 *
 * @param {('transcricao'|'audio'|'foto'|'video')} type - Qual resultado limpar. Valores
 *   fora desse conjunto disparam um toast de aviso ("Tipo inválido para limpar.").
 * @returns {void}
 * @sideeffect Apaga drafts persistidos, muta o estado global (transcrição/laudo),
 *   manipula o DOM (innerHTML e classes `hidden`) e pode exibir toasts.
 */
window.limparResultado = (type) => {
  try {
    // Remove drafts para não restaurar automaticamente ao navegar
    if (type === 'transcricao') {
      clearDrafts('transcricao');
      setTranscricao('');
      setTranscricaoValidada(false);

      const transView = document.getElementById('transcricaoView');
      const transContainer = document.getElementById('transcricaoContainer');
      const laudoContainer = document.getElementById('laudoContainer');
      const emptyState = document.getElementById('audioEmptyState');

      if (transView) transView.innerHTML = '';
      if (transContainer) transContainer.classList.add('hidden');
      if (laudoContainer) laudoContainer.classList.add('hidden'); // ao reiniciar transcrição, esconde laudo
      if (emptyState) emptyState.classList.remove('hidden');

      const btnEdit = document.getElementById('btnEditTranscricao');
      if (btnEdit) btnEdit.classList.add('hidden');

      // Esconde botões de ação
      const actionsTranscricao = document.getElementById('actionsTranscricao');
      if (actionsTranscricao) actionsTranscricao.classList.add('hidden');

      refreshIcons();
      return;
    }

    if (type === 'audio' || type === 'foto' || type === 'video') {
      clearDrafts(type);
      setLaudo(type, '');

      // Monta o id do contêiner de saída conforme o tipo: 'audio' -> laudoView;
      // 'foto'/'video' -> outputFoto/outputVideo (sufixo capitalizado p/ casar com o HTML).
      const outId = type === 'audio' ? 'laudoView' : `output${type === 'foto' ? 'Foto' : 'Video'}`;
      const out = document.getElementById(outId);
      if (out) {
        // Áudio fica simplesmente vazio; foto/vídeo recebem um empty-state com
        // ícone e texto específicos (Lucide: 'image' p/ foto, 'clapperboard' p/ vídeo).
        out.innerHTML = type === 'audio'
          ? ''
          : `<div class="empty-state"><i data-lucide="${type === 'foto' ? 'image' : 'clapperboard'}"></i><p>Aguardando ${type === 'foto' ? 'imagem' : 'vídeo'}.</p></div>`;
      }

      // Esconde actions e edição
      // Áudio não tem barra de actions própria aqui (tratada no bloco específico abaixo),
      // por isso actionsId fica null e o bloco é pulado.
      const actionsId = type === 'audio' ? null : `actions${type === 'foto' ? 'Foto' : 'Video'}`;
      if (actionsId) {
        const actions = document.getElementById(actionsId);
        if (actions) actions.classList.add('hidden');
      }

      if (type === 'audio') {
        // Áudio: além do laudoView, esconde o contêiner do laudo e seus controles
        // (botão de editar e barra de ações do laudo), já que o resultado foi zerado.
        const laudoContainer = document.getElementById('laudoContainer');
        if (laudoContainer) laudoContainer.classList.add('hidden');

        const btnEdit = document.getElementById('btnEditLaudo');
        if (btnEdit) btnEdit.classList.add('hidden');

        // Esconde botões de ação do laudo
        const actionsLaudo = document.getElementById('actionsLaudo');
        if (actionsLaudo) actionsLaudo.classList.add('hidden');
      } else {
        // Foto/vídeo: esconde apenas o botão de editar específico (btnEditFoto/btnEditVideo).
        const btnEdit = document.getElementById(`btnEdit${type === 'foto' ? 'Foto' : 'Video'}`);
        if (btnEdit) btnEdit.classList.add('hidden');
      }

      refreshIcons();
      return;
    }

    // Nenhum ramo correspondeu: tipo desconhecido, apenas avisa o usuário.
    toast.warning('Tipo inválido para limpar.');
  } catch (e) {
    console.error('Erro ao limpar resultado:', e);
    toast.error('Falha ao limpar resultado.');
  }
};
