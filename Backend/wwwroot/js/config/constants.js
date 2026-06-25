/**
 * Configurações e constantes globais da aplicação
 */

// Backend serve o frontend, então a origem é sempre a mesma
const isLiveServer = window.location.port === '5500';
export const BASE_URL = isLiveServer ? 'http://localhost:5252' : window.location.origin;

// URLs da API
export const API_URL = `${BASE_URL}/api/analisar`;
export const API_TRANSCREVER = `${BASE_URL}/api/transcrever`;
export const API_SALVAR = `${BASE_URL}/api/salvar`;
export const API_ANALISES = `${BASE_URL}/api/analises`;

// Mapeamentos de tipos
export const TIPO_NOMES = {
    audio: 'Oitiva',
    foto: 'Vistoria',
    video: 'Vídeo'
};

export const TIPO_ICONS = {
    'Oitiva': 'mic',
    'Vistoria': 'camera',
    'Vídeo': 'video',
    'Transcrição': 'scroll-text'
};

export const TIPO_CLASSES = {
    'Oitiva': 'tipo-oitiva',
    'Vistoria': 'tipo-vistoria',
    'Vídeo': 'tipo-video',
    'Transcrição': 'tipo-transcricao'
};

// Mapeamento de tipos para modos do backend
export const MODO_BACKEND = {
    audio: 'oitiva',
    foto: 'vistoria',
    video: 'video'
};

// Títulos para exportação PDF
export const TITULOS_PDF = {
    audio: 'Laudo Pericial - Oitiva de Sinistro',
    foto: 'Laudo Técnico - Vistoria de Imagem',
    video: 'Laudo de Vídeo - Análise de Sinistro',
    transcricao: 'Transcrição Completa - Oitiva de Sinistro'
};

// Limites de tamanho de arquivo (em bytes)
export const FILE_SIZE_LIMITS = {
    AUDIO_INLINE_MAX: 15 * 1024 * 1024,       // 15 MB - limite recomendado para processamento rápido
    AUDIO_ABSOLUTE_MAX: 500 * 1024 * 1024,    // 500 MB - limite máximo do sistema
    WARNING_THRESHOLD: 15 * 1024 * 1024       // Mostrar aviso acima deste tamanho
};

// Mensagens de aviso de tamanho
export const SIZE_WARNING_MESSAGES = {
    LARGE_FILE: '⚠️ Arquivo grande detectado ({size}MB). O processamento pode levar mais tempo.',
    TOO_LARGE: '❌ Arquivo muito grande ({size}MB). O limite máximo é 500MB.'
};

// Opções do html2pdf
export const HTML2PDF_OPTIONS = {
    margin: [10, 15, 10, 15],
    image: { type: 'jpeg', quality: 1 },
    html2canvas: { scale: 2, useCORS: true, backgroundColor: '#ffffff' },
    jsPDF: { unit: 'mm', format: 'a4', orientation: 'portrait' }
};
