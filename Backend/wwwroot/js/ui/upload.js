/**
 * Upload e Manipulação de Arquivos
 */

import { capitalize, refreshIcons } from '../core/utils.js';
import { setFile, clearFile as clearFileState } from '../core/state.js';

/**
 * Configura upload para um tipo específico
 * @param {string} type - Tipo: audio, foto, video
 */
export function setupUpload(type) {
    const dropZone = document.getElementById(`dropZone${capitalize(type)}`);
    const input = document.getElementById(`input${capitalize(type)}`);

    if (!dropZone || !input) {
        console.error(`❌ Elementos não encontrados para: ${type}`);
        return;
    }

    // Clique na zona de drop
    dropZone.addEventListener('click', () => {
        console.log(`📁 Abrindo seletor para: ${type}`);
        input.click();
    });

    // Drag and drop
    dropZone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropZone.classList.add('drag-over');
    });

    dropZone.addEventListener('dragleave', () => {
        dropZone.classList.remove('drag-over');
    });

    dropZone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropZone.classList.remove('drag-over');
        if (e.dataTransfer.files.length > 0) {
            handleFile(e.dataTransfer.files[0], type);
        }
    });

    // Input change
    input.addEventListener('change', (e) => {
        if (e.target.files.length > 0) {
            handleFile(e.target.files[0], type);
        }
    });

    console.log(`✅ Upload configurado para: ${type}`);
}

/**
 * Manipula arquivo selecionado
 * @param {File} file - Arquivo selecionado
 * @param {string} type - Tipo: audio, foto, video
 */
export function handleFile(file, type) {
    setFile(type, file);

    const dropZone = document.getElementById(`dropZone${capitalize(type)}`);
    const previewBox = document.getElementById(`${type}PreviewBox`);
    const previewEl = document.getElementById(`${type}Preview`);
    const nameEl = document.getElementById(`${type}Name`);

    // Atualiza nome do arquivo
    if (nameEl) nameEl.textContent = file.name;

    // Revogar URL anterior para evitar memory leak
    if (previewEl && previewEl.src && previewEl.src.startsWith('blob:')) {
        URL.revokeObjectURL(previewEl.src);
    }

    // Cria URL para preview
    const url = URL.createObjectURL(file);
    if (previewEl) previewEl.src = url;

    // Se for áudio, inicia Waveform
    if (type === 'audio') {
        import('./waveform.js').then(module => {
            module.initWaveform(url);
        });
    }

    // Mostra preview, esconde dropzone
    if (dropZone) dropZone.classList.add('hidden');
    if (previewBox) previewBox.classList.remove('hidden');

    console.log(`📎 Arquivo carregado [${type}]:`, file.name);
}

/**
 * Limpa arquivo de um tipo
 * @param {string} type - Tipo: audio, foto, video
 */
export function clearFile(type) {
    clearFileState(type);

    const dropZone = document.getElementById(`dropZone${capitalize(type)}`);
    const previewBox = document.getElementById(`${type}PreviewBox`);
    const previewEl = document.getElementById(`${type}Preview`);
    const input = document.getElementById(`input${capitalize(type)}`);

    if (previewEl) previewEl.src = '';
    if (previewBox) previewBox.classList.add('hidden');
    if (dropZone) dropZone.classList.remove('hidden');
    if (input) input.value = '';

    if (type === 'audio') {
        import('./waveform.js').then(module => {
            module.destroyWaveform();
        });
    }

    if (type === 'merge') {
        const previewBox = document.getElementById('mergePreviewBox');
        const previewEl = document.getElementById('mergeAudioPreview');
        const btnGroup = document.getElementById('btnGroupMergeAction');

        if (previewEl) previewEl.src = '';
        if (previewBox) previewBox.classList.add('hidden');
        if (btnGroup) btnGroup.classList.add('hidden');
        return;
    }

    console.log(`🗑️ Arquivo removido [${type}]`);
}

/**
 * Inicializa todos os uploads
 */
export function initUploads() {
    setupUpload('audio');
    setupUpload('foto');
    setupUpload('video');
    console.log("✅ Uploads inicializados");
}
