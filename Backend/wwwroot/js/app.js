/**
 * Sentinel - Main Entry Point
 * Orquestrador principal da aplicação
 * 
 * Este arquivo importa e inicializa todos os módulos,
 * além de expor funções globais necessárias para o HTML.
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

  function openSidebar() {
    sidebar.classList.add('open');
    sidebarOverlay.classList.add('active');
    document.body.style.overflow = 'hidden'; // Previne scroll do body
  }

  function closeSidebar() {
    sidebar.classList.remove('open');
    sidebarOverlay.classList.remove('active');
    document.body.style.overflow = '';
  }

  if (hamburgerBtn) {
    hamburgerBtn.addEventListener('click', () => {
      if (sidebar.classList.contains('open')) {
        closeSidebar();
      } else {
        openSidebar();
      }
    });
  }

  if (sidebarOverlay) {
    sidebarOverlay.addEventListener('click', closeSidebar);
  }

  // Fecha sidebar ao clicar em item do menu (mobile)
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

// Navegação
window.setMode = setMode;
window.switchAudioTab = switchAudioTab;
window.downloadMergedAudio = downloadMergedAudio;
window.uploadMergedAudio = uploadMergedAudio;

// Upload
window.clearFile = (type) => {
  clearFile(type);
};

// Análise
window.processar = processar;
window.gerarTranscricao = gerarTranscricao;
window.abrirTranscricao = abrirTranscricao;
window.gerarRelatorioPericial = gerarRelatorioPericial;

// Edição Inline - Transcrição
window.toggleEditTranscricao = toggleEditTranscricao;
window.salvarEdicaoTranscricao = salvarEdicaoTranscricao;
window.cancelarEdicaoTranscricao = cancelarEdicaoTranscricao;

// Edição Inline - Laudo
window.toggleEditLaudo = toggleEditLaudo;
window.salvarEdicaoLaudo = salvarEdicaoLaudo;
window.cancelarEdicaoLaudo = cancelarEdicaoLaudo;

// Edição Inline - Foto
window.toggleEditFoto = toggleEditFoto;
window.salvarEdicaoFoto = salvarEdicaoFoto;
window.cancelarEdicaoFoto = cancelarEdicaoFoto;

// Edição Inline - Vídeo
window.toggleEditVideo = toggleEditVideo;
window.salvarEdicaoVideo = salvarEdicaoVideo;
window.cancelarEdicaoVideo = cancelarEdicaoVideo;

// Modal
window.fecharTranscricao = fecharModalTranscricao;
window.fecharModal = fecharModalOverlay;

// Exportação
window.copiarTexto = copiarTexto;
window.exportarPDF = exportarPDF;
window.exportarWord = exportarWord;

// Salvamento
window.salvarAnalise = salvarAnalise;
window.salvarTranscricao = salvarTranscricao;

// Arquivos Salvos - Listagem
window.carregarSalvos = carregarSalvos;
window.filtrarSalvos = filtrarSalvos;
window.visualizarSalvo = visualizarSalvo;
window.exportarSalvo = exportarSalvo;
window.exportarSalvoWord = exportarSalvoWord;
window.excluirSalvo = excluirSalvo;

import { seekTo } from './ui/player.js';

// Player Sync
window.seekTo = seekTo;

// Maximizar laudo num modal grande (mais espaço para leitura)
window.maximizarLaudo = (tipo) => {
  const md = getLaudo(tipo);
  if (!md) { toast.warning('Nenhum laudo para exibir ainda.'); return; }
  const html = (typeof marked !== 'undefined') ? marked.parse(md) : `<pre>${md}</pre>`;
  abrirModalTranscricao(linkifyTimestamps(html), '<i data-lucide="maximize-2"></i> Laudo Pericial');
};

// Arquivos Salvos - Visualização Inline e Edição
window.toggleEditSalvo = toggleEditSalvo;
window.salvarEdicaoSalvo = salvarEdicaoSalvo;
window.cancelarEdicaoSalvo = cancelarEdicaoSalvo;
window.fecharVisualizacaoSalvo = fecharVisualizacaoSalvo;
window.copiarTextoSalvo = copiarTextoSalvo;
window.exportarSalvoAtual = exportarSalvoAtual;
window.exportarSalvoAtualWord = exportarSalvoAtualWord;

// Limpar resultado / Novo
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

      const outId = type === 'audio' ? 'laudoView' : `output${type === 'foto' ? 'Foto' : 'Video'}`;
      const out = document.getElementById(outId);
      if (out) {
        out.innerHTML = type === 'audio'
          ? ''
          : `<div class="empty-state"><i data-lucide="${type === 'foto' ? 'image' : 'clapperboard'}"></i><p>Aguardando ${type === 'foto' ? 'imagem' : 'vídeo'}.</p></div>`;
      }

      // Esconde actions e edição
      const actionsId = type === 'audio' ? null : `actions${type === 'foto' ? 'Foto' : 'Video'}`;
      if (actionsId) {
        const actions = document.getElementById(actionsId);
        if (actions) actions.classList.add('hidden');
      }

      if (type === 'audio') {
        const laudoContainer = document.getElementById('laudoContainer');
        if (laudoContainer) laudoContainer.classList.add('hidden');

        const btnEdit = document.getElementById('btnEditLaudo');
        if (btnEdit) btnEdit.classList.add('hidden');

        // Esconde botões de ação do laudo
        const actionsLaudo = document.getElementById('actionsLaudo');
        if (actionsLaudo) actionsLaudo.classList.add('hidden');
      } else {
        const btnEdit = document.getElementById(`btnEdit${type === 'foto' ? 'Foto' : 'Video'}`);
        if (btnEdit) btnEdit.classList.add('hidden');
      }

      refreshIcons();
      return;
    }

    toast.warning('Tipo inválido para limpar.');
  } catch (e) {
    console.error('Erro ao limpar resultado:', e);
    toast.error('Falha ao limpar resultado.');
  }
};
