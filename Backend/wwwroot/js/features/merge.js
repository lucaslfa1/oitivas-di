/**
 * Feature: Merge de Áudio
 * Permite juntar múltiplos arquivos de áudio em um só
 */

import { handleFile } from '../ui/upload.js';
import { toast } from '../ui/toast.js';
import { uploadFileAPI, salvarAnaliseAPI } from '../api/sinistroApi.js';
import { setFile } from '../core/state.js';

let selectedFiles = [];

/**
 * Inicializa a funcionalidade de merge
 */
export function initMerge() {
    const dropZone = document.getElementById('dropZoneMerge');
    const input = document.getElementById('inputMerge');
    const btnMerge = document.getElementById('btnMerge');
    const btnSaveMerge = document.getElementById('btnSaveMerge');

    if (!dropZone || !input) return;

    // Event Listeners para Upload
    dropZone.addEventListener('click', () => input.click());

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
            addFiles(Array.from(e.dataTransfer.files));
        }
    });

    input.addEventListener('change', (e) => {
        if (e.target.files.length > 0) {
            addFiles(Array.from(e.target.files));
            // Limpa o input para permitir selecionar os mesmos arquivos novamente se necessário
            input.value = '';
        }
    });

    // Botão de Merge
    if (btnMerge) {
        btnMerge.addEventListener('click', mergeAudios);
    }

    // Botão de Salvar (Download)
    if (btnSaveMerge) {
        btnSaveMerge.addEventListener('click', downloadMergedAudio);
    }

    console.log("✅ Feature: Merge inicializado");
}

/**
 * Alterna entre abas de áudio (Upload individual vs Merge)
 * @param {string} tab - 'upload' ou 'merge'
 */
export function switchAudioTab(tab) {
    // Remove active de todos
    document.querySelectorAll('.card-tabs .tab-btn').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.tab-content').forEach(content => content.classList.add('hidden'));

    // Ativa o selecionado
    const btn = document.querySelector(`.card-tabs .tab-btn[onclick="switchAudioTab('${tab}')"]`);
    const content = document.getElementById(`tab-${tab}`);

    if (btn) btn.classList.add('active');
    if (content) content.classList.remove('hidden');
}

/**
 * Adiciona arquivos à lista de merge
 * @param {File[]} files 
 */
function addFiles(files) {
    // Filtra áudios e mpeg
    const audioFiles = files.filter(f =>
        f.type.startsWith('audio/') ||
        f.type === 'video/mpeg' ||
        f.type === 'video/mpg' ||
        f.name.toLowerCase().endsWith('.mpeg') ||
        f.name.toLowerCase().endsWith('.mpg')
    );

    if (audioFiles.length === 0) {
        toast.warning("Apenas arquivos de áudio ou MPEG são permitidos.");
        return;
    }

    selectedFiles = [...selectedFiles, ...audioFiles];
    updateList();
}

/**
 * Remove arquivo da lista
 * @param {number} index 
 */
function removeFile(index) {
    selectedFiles.splice(index, 1);
    updateList();
}

/**
 * Atualiza a lista visual de arquivos
 */
function updateList() {
    const listEl = document.getElementById('mergeFileList');
    const btnMerge = document.getElementById('btnMerge');

    if (!listEl) return;

    listEl.innerHTML = '';

    selectedFiles.forEach((file, index) => {
        const li = document.createElement('li');
        li.innerHTML = `
            <span>${file.name} (${(file.size / 1024).toFixed(1)} KB)</span> 
            <button class="rm-btn" title="Remover">✕</button>
        `;

        // Adiciona evento ao botão remover
        li.querySelector('.rm-btn').addEventListener('click', (e) => {
            e.stopPropagation();
            removeFile(index);
        });

        listEl.appendChild(li);
    });

    // Habilita botão se tiver pelo menos 2 arquivos
    if (btnMerge) {
        btnMerge.disabled = selectedFiles.length < 2;
    }
}

/**
 * Executa o merge dos áudios
 */
async function mergeAudios() {
    const btnMerge = document.getElementById('btnMerge');
    const statusEl = document.getElementById('mergeStatus');

    if (selectedFiles.length < 2) return;

    // UI Loading
    btnMerge.disabled = true;
    btnMerge.textContent = "PROCESSANDO...";
    if (statusEl) {
        statusEl.textContent = "Enviando arquivos e unindo...";
        statusEl.className = "status-text";
    }

    const formData = new FormData();
    selectedFiles.forEach(file => {
        formData.append("files", file);
    });

    try {
        const response = await fetch('/api/tools/merge-audio', {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            const blob = await response.blob();

            // Cria um novo arquivo File a partir do Blob
            const mergedFile = new File([blob], "audio_completo_merged.mp3", { type: "audio/mp3" });
            lastMergedFile = mergedFile;

            toast.success("Áudios unidos com sucesso!");

            if (statusEl) {
                statusEl.textContent = "Sucesso! Alternando para análise...";
                statusEl.className = "status-text success";
            }

            // Habilita botão salvar
            const btnSaveMerge = document.getElementById('btnSaveMerge');
            if (btnSaveMerge) {
                btnSaveMerge.classList.remove('hidden');
                btnSaveMerge.disabled = false;
            }

            // Reseta a lista
            selectedFiles = [];
            updateList();

            // Atualiza preview na aba de merge (sem trocar de aba)
            const previewBox = document.getElementById('mergePreviewBox');
            const previewEl = document.getElementById('mergeAudioPreview');
            const nameEl = document.getElementById('mergeAudioName');
            const btnGroup = document.getElementById('btnGroupMergeAction');

            if (nameEl) nameEl.textContent = mergedFile.name;

            const url = URL.createObjectURL(mergedFile);
            if (previewEl) {
                previewEl.src = url;
            }

            if (previewBox) previewBox.classList.remove('hidden');
            if (btnGroup) btnGroup.classList.remove('hidden');

            // Define o arquivo no estado global para que gerarTranscricao() funcione
            setFile('audio', mergedFile);

        } else {
            const err = await response.text();
            console.error("Erro no merge:", err);
            toast.error("Falha ao unir áudios: " + err);

            if (statusEl) {
                statusEl.textContent = "Erro: " + err;
                statusEl.className = "status-text error";
            }
        }
    } catch (error) {
        console.error("Erro de conexão:", error);
        toast.error("Erro de conexão ao servidor.");

        if (statusEl) {
            statusEl.textContent = "Erro de conexão.";
            statusEl.className = "status-text error";
        }
    } finally {
        btnMerge.disabled = false;
        btnMerge.textContent = "JUNTAR E ANALISAR";
    }
}

let lastMergedFile = null;

/**
 * Salva o áudio mergeado no banco como uma análise
 */
/**
 * Faz o download do áudio mergeado
 */
export function downloadMergedAudio() {
    if (!lastMergedFile) {
        toast.warning("Nenhum áudio para baixar.");
        return;
    }

    const url = URL.createObjectURL(lastMergedFile);
    const a = document.createElement('a');
    a.href = url;
    a.download = lastMergedFile.name; // Já está como .mp3
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    toast.success("Download iniciado!");
}

/**
 * Salva o áudio mergeado no banco como uma análise (Upload)
 * @deprecated Mantido para uso futuro se adicionarmos botão específico
 */
export async function uploadMergedAudio() {
    if (!lastMergedFile) {
        toast.warning("Nenhum áudio mergeado para salvar.");
        return;
    }

    try {
        toast.info("Fazendo upload do áudio...");

        // 1. Upload do arquivo
        const uploadResult = await uploadFileAPI(lastMergedFile);

        // 2. Salvar metadata da análise
        const dadosAnalise = {
            Tipo: 'Oitiva',
            Arquivo: lastMergedFile.name,
            Conteudo: `**Áudio Mergeado**\n\nArquivo salvo em: [${uploadResult.url}](${uploadResult.url})\n\nEste áudio foi criado a partir da junção de múltiplos arquivos.`,
            Data: new Date().toISOString()
        };

        await salvarAnaliseAPI(dadosAnalise);

        toast.success("Áudio salvo no sistema com sucesso!");

    } catch (error) {
        console.error("Erro ao salvar áudio:", error);
        toast.error("Erro ao salvar áudio.");
    }
}
