/**
 * @file core/state.js
 * @module core/state
 *
 * Estado Global da Aplicação (Sentinel - análise forense de sinistros).
 *
 * Implementa um "store" simples em memória (singleton de módulo): um único
 * objeto {@link state} compartilhado entre todos os módulos que importam este
 * arquivo, mais funções de acesso (getters) e mutação (setters).
 *
 * Por que este padrão:
 * - Como módulos ES são avaliados uma única vez por sessão de página, o objeto
 *   `state` é efetivamente um singleton: qualquer módulo que faça
 *   `import { state } from './core/state.js'` enxerga e muta a MESMA referência.
 * - Não há framework reativo aqui; "reativo" significa apenas estado central
 *   mutável. A UI deve ler via getters após cada operação, não há observers.
 *
 * Convenção de fluxo de uma oitiva/análise:
 *   1. Usuário carrega um arquivo (áudio/foto/vídeo) -> `setFile`.
 *   2. Para áudio, a transcrição é gerada -> `setTranscricao`.
 *   3. Usuário revisa/edita e valida a transcrição -> `setTranscricaoValidada`.
 *   4. Com transcrição validada, o laudo pode ser gerado -> `setLaudo`
 *      (gate verificado por {@link canGenerateLaudo}).
 *
 * Efeito colateral transversal: todos os setters fazem `console.log` para
 * rastreabilidade/depuração no console do navegador.
 */

/**
 * Objeto único e mutável que guarda o estado da sessão atual.
 *
 * Campos:
 * @property {{audio: (File|null), foto: (File|null), video: (File|null)}} currentFiles
 *   Arquivos carregados por tipo de mídia. `null` quando não há arquivo do tipo.
 * @property {(string|null)} transcricaoAtual - Texto da transcrição em uso
 *   (pode estar editado pelo usuário). `null` antes de transcrever.
 * @property {(string|null)} transcricaoOriginal - Cópia imutável da transcrição
 *   como veio do serviço de transcrição (Azure), capturada na 1ª gravação.
 *   Serve de backup para "cancelar edição" e restaurar o texto original.
 * @property {boolean} transcricaoValidada - Flag indicando que o usuário revisou
 *   e aprovou a transcrição. É o gate que libera a geração de laudo.
 * @property {{audio: (string|null), foto: (string|null), video: (string|null)}} laudoAtual
 *   Laudo atual por tipo de mídia (pode estar editado). `null` se ainda não gerado.
 * @property {{audio: (string|null), foto: (string|null), video: (string|null)}} laudoOriginal
 *   Backup do laudo original por tipo, capturado na 1ª gravação, para permitir
 *   descartar edições e voltar à versão gerada.
 * @property {string} filtroSalvos - Filtro ativo na listagem de itens salvos.
 *   Valor inicial `'todos'` (sem filtro).
 */
// Estado reativo dos arquivos carregados
export const state = {
    currentFiles: {
        audio: null,
        foto: null,
        video: null
    },
    transcricaoAtual: null,
    transcricaoOriginal: null,      // Backup antes de edições
    transcricaoValidada: false,     // Flag: usuário revisou a transcrição
    laudoAtual: {
        audio: null,
        foto: null,
        video: null
    },
    laudoOriginal: {                // Backup antes de edições
        audio: null,
        foto: null,
        video: null
    },
    filtroSalvos: 'todos'
};

// Getters

/**
 * Retorna o arquivo atualmente carregado para um dado tipo de mídia.
 *
 * @param {('audio'|'foto'|'video')} type - Tipo de mídia a consultar.
 * @returns {(File|null)} O arquivo carregado para o tipo, ou `null` se não houver.
 */
export const getFile = (type) => state.currentFiles[type];

/**
 * Retorna a transcrição atualmente em uso (pode estar editada pelo usuário).
 *
 * @returns {(string|null)} Texto da transcrição atual, ou `null` antes de transcrever.
 */
export const getTranscricao = () => state.transcricaoAtual;

/**
 * Retorna a cópia da transcrição original (backup capturado na 1ª gravação),
 * usada para restaurar o texto ao cancelar uma edição.
 *
 * @returns {(string|null)} Texto original da transcrição, ou `null` se ainda não houver.
 */
export const getTranscricaoOriginal = () => state.transcricaoOriginal;

/**
 * Indica se a transcrição já foi revisada e aprovada pelo usuário.
 * É o gate consultado por {@link canGenerateLaudo} antes de liberar a geração de laudo.
 *
 * @returns {boolean} `true` se a transcrição foi validada; caso contrário `false`.
 */
export const isTranscricaoValidada = () => state.transcricaoValidada;

/**
 * Retorna o laudo atual (possivelmente editado) para um dado tipo de mídia.
 *
 * @param {('audio'|'foto'|'video')} type - Tipo de mídia a consultar.
 * @returns {(string|null)} Texto do laudo atual do tipo, ou `null` se ainda não gerado.
 */
export const getLaudo = (type) => state.laudoAtual[type];

/**
 * Retorna o laudo original (backup capturado na 1ª gravação) para um dado tipo,
 * usado para descartar edições e voltar à versão gerada.
 *
 * @param {('audio'|'foto'|'video')} type - Tipo de mídia a consultar.
 * @returns {(string|null)} Texto original do laudo do tipo, ou `null` se ainda não houver.
 */
export const getLaudoOriginal = (type) => state.laudoOriginal[type];

/**
 * Retorna o filtro ativo na listagem de itens salvos.
 *
 * @returns {string} Identificador do filtro ativo (ex.: `'todos'` para sem filtro).
 */
export const getFiltroSalvos = () => state.filtroSalvos;

// Setters

/**
 * Define o arquivo carregado para um dado tipo de mídia.
 *
 * Efeitos colaterais:
 * - Muta `state.currentFiles[type]` com o arquivo informado.
 * - Quando `type === 'audio'`: reseta toda a transcrição derivada do áudio
 *   anterior (`transcricaoAtual`, `transcricaoOriginal` e a flag
 *   `transcricaoValidada`), pois um novo áudio invalida a transcrição/validação
 *   prévias — a transcrição precisa ser refeita para o novo arquivo.
 * - Loga no console para rastreabilidade (usa `file?.name`, exibindo `'null'`
 *   quando `file` for nulo/indefinido).
 *
 * @param {('audio'|'foto'|'video')} type - Tipo de mídia sendo definido.
 * @param {(File|null)} file - Arquivo a armazenar (ou `null` para limpar).
 * @returns {void}
 */
export const setFile = (type, file) => {
    state.currentFiles[type] = file;
    // Reset transcrição quando novo arquivo é carregado
    if (type === 'audio') {
        state.transcricaoAtual = null;
        state.transcricaoOriginal = null;
        state.transcricaoValidada = false;
    }
    console.log(`📎 Arquivo atualizado [${type}]:`, file?.name || 'null');
};

/**
 * Atualiza a transcrição atual em uso.
 *
 * Efeitos colaterais:
 * - Na PRIMEIRA chamada (enquanto `transcricaoOriginal` for falsy), grava também
 *   `transcricaoOriginal` como backup da versão recém-gerada. Chamadas seguintes
 *   (edições do usuário) NÃO sobrescrevem esse backup, preservando o texto
 *   original para permitir "cancelar edição" e restaurar.
 * - Sempre muta `transcricaoAtual` com o valor informado.
 * - Loga no console o tamanho do texto (`transcricao?.length`, ou `0` se nulo).
 *
 * Observação: como a condição testa o valor de `transcricaoOriginal`, passar uma
 * string vazia como original na 1ª vez não a marca como gravada (vazio é falsy).
 *
 * @param {(string|null)} transcricao - Novo texto da transcrição.
 * @returns {void}
 */
export const setTranscricao = (transcricao) => {
    // Salva original apenas na primeira vez
    if (!state.transcricaoOriginal) {
        state.transcricaoOriginal = transcricao;
    }
    state.transcricaoAtual = transcricao;
    console.log(`📜 Transcrição atualizada (${transcricao?.length || 0} chars)`);
};

/**
 * Define a flag de validação da transcrição (gate para geração de laudo).
 *
 * Efeitos colaterais:
 * - Muta `transcricaoValidada`. Quando `true`, junto com uma `transcricaoAtual`
 *   não vazia, libera {@link canGenerateLaudo}.
 * - Loga no console o novo valor da flag.
 *
 * @param {boolean} validada - `true` para marcar como revisada/aprovada; `false` para invalidar.
 * @returns {void}
 */
export const setTranscricaoValidada = (validada) => {
    state.transcricaoValidada = validada;
    console.log(`✅ Transcrição validada: ${validada}`);
};

/**
 * Atualiza o laudo atual para um dado tipo de mídia.
 *
 * Efeitos colaterais:
 * - Na PRIMEIRA chamada para o `type` (enquanto `laudoOriginal[type]` for falsy),
 *   grava também `laudoOriginal[type]` como backup da versão recém-gerada.
 *   Edições posteriores NÃO sobrescrevem esse backup, preservando o laudo
 *   original para permitir descartar edições e voltar à versão gerada.
 * - Sempre muta `laudoAtual[type]` com o valor informado.
 * - Loga no console para rastreabilidade.
 *
 * @param {('audio'|'foto'|'video')} type - Tipo de mídia ao qual o laudo pertence.
 * @param {(string|null)} laudo - Novo texto do laudo.
 * @returns {void}
 */
export const setLaudo = (type, laudo) => {
    // Salva original apenas na primeira vez
    if (!state.laudoOriginal[type]) {
        state.laudoOriginal[type] = laudo;
    }
    state.laudoAtual[type] = laudo;
    console.log(`📋 Laudo atualizado [${type}]`);
};

/**
 * Define o filtro ativo da listagem de itens salvos.
 *
 * Efeito colateral: muta `state.filtroSalvos`. (Não loga no console.)
 *
 * @param {string} filtro - Identificador do filtro (ex.: `'todos'` para sem filtro).
 * @returns {void}
 */
export const setFiltroSalvos = (filtro) => {
    state.filtroSalvos = filtro;
};

/**
 * Remove o arquivo de um dado tipo de mídia e limpa o estado derivado dele.
 *
 * Efeitos colaterais:
 * - Zera `state.currentFiles[type]`.
 * - Quando `type === 'audio'`: limpa todo o pipeline derivado do áudio —
 *   transcrição (`transcricaoAtual`, `transcricaoOriginal`), a flag
 *   `transcricaoValidada`, e o laudo de áudio (`laudoAtual.audio` e o backup
 *   `laudoOriginal.audio`) — pois sem o áudio nada disso faz sentido.
 * - Loga no console para rastreabilidade.
 *
 * @param {('audio'|'foto'|'video')} type - Tipo de mídia a remover.
 * @returns {void}
 */
// Limpar arquivo específico
export const clearFile = (type) => {
    state.currentFiles[type] = null;
    if (type === 'audio') {
        state.transcricaoAtual = null;
        state.transcricaoOriginal = null;
        state.transcricaoValidada = false;
        state.laudoAtual.audio = null;
        state.laudoOriginal.audio = null;
    }
    console.log(`🗑️ Arquivo removido [${type}]`);
};

/**
 * Reseta TODO o estado da sessão para os valores iniciais.
 *
 * Efeitos colaterais (reatribui referências, não muta os objetos existentes):
 * - `currentFiles` volta a `{ audio: null, foto: null, video: null }`.
 * - Limpa `transcricaoAtual`, `transcricaoOriginal` e `transcricaoValidada`.
 * - `laudoAtual` e `laudoOriginal` voltam a `{ audio: null, foto: null, video: null }`.
 * - `filtroSalvos` volta ao padrão `'todos'` (sem filtro).
 * - Loga no console para rastreabilidade.
 *
 * Observação: substitui os objetos aninhados por NOVAS referências; qualquer
 * código que tenha guardado `state.currentFiles`/`laudoAtual` etc. passará a ver
 * o objeto antigo. Importar `state` diretamente continua válido (a referência de
 * topo `state` não muda).
 *
 * @returns {void}
 */
// Reset completo
export const resetState = () => {
    state.currentFiles = { audio: null, foto: null, video: null };
    state.transcricaoAtual = null;
    state.transcricaoOriginal = null;
    state.transcricaoValidada = false;
    state.laudoAtual = { audio: null, foto: null, video: null };
    state.laudoOriginal = { audio: null, foto: null, video: null };
    state.filtroSalvos = 'todos';
    console.log('🔄 Estado resetado');
};

/**
 * Indica se há condições para gerar o laudo: precisa existir uma transcrição
 * atual não vazia E ela ter sido validada pelo usuário.
 *
 * Não tem efeitos colaterais (apenas lê o estado).
 *
 * Observação sobre o tipo de retorno: usa `&&` sem coerção booleana, então o
 * resultado segue a semântica do JS — retorna `null`/`''` (falsy) quando não há
 * transcrição, ou o valor de `transcricaoValidada` (boolean) quando há. Use em
 * contexto booleano (ex.: `if (canGenerateLaudo())`).
 *
 * @returns {(boolean|string|null)} Valor truthy quando o laudo pode ser gerado;
 *   falsy (`null`, `''` ou `false`) caso contrário.
 */
// Verificar se pode gerar laudo (transcrição existe e foi validada)
export const canGenerateLaudo = () => {
    return state.transcricaoAtual && state.transcricaoValidada;
};
