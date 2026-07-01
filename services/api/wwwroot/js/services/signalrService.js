/**
 * @file signalrService.js
 * @module services/signalrService
 *
 * Servico de comunicacao em tempo real (SignalR) do Sentinel.
 *
 * Centraliza UMA unica conexao WebSocket com o hub `/hubs/analysis` do backend
 * (Azure-only) e a compartilha por todo o frontend. O hub serve a dois fluxos:
 *
 *   1. Progresso de analise forense de sinistros: o backend emite o evento
 *      `ReceiveProgress` (mensagem + percentual) enquanto processa a midia/laudo,
 *      e o frontend repassa isso a todos os listeners registrados.
 *   2. Chat entre membros (sala unica): evento `ReceiveMembersMessage` na entrada
 *      e metodo de hub `SendMembersMessage` no envio.
 *
 * Padrao de projeto adotado:
 *   - Estado de modulo (singleton): a conexao, o id e os conjuntos de listeners
 *     vivem no escopo do modulo, garantindo que todos os consumidores compartilhem
 *     a mesma conexao.
 *   - Pub/sub via Set: listeners sao registrados em conjuntos e notificados em
 *     broadcast; cada `subscribe` retorna uma funcao de cancelamento.
 *   - Guarda contra conexoes concorrentes: `connectionStartPromise` evita que
 *     duas chamadas simultaneas abram dois WebSockets (ver ensureConnection).
 *
 * Dependencia externa: a biblioteca `signalR` (ASP.NET Core SignalR JS client)
 * deve estar carregada globalmente (via <script>) antes do uso; o codigo apenas
 * a consome, nunca a importa.
 */
import { BASE_URL } from '../config/constants.js';

/**
 * Instancia unica de HubConnection (singleton de modulo).
 * `null` enquanto a conexao ainda nao foi construida ou apos uma falha de start
 * (em que e zerada para permitir nova tentativa). @type {object|null}
 */
let connection = null;

/**
 * Id da conexao atribuido pelo servidor SignalR. Necessario para que o backend
 * direcione eventos de progresso a este cliente especifico. Atualizado a cada
 * (re)conexao bem-sucedida. @type {string|null}
 */
let connectionId = null;

/**
 * Promise "em voo" do start da conexao, usada como trava (lock) para evitar
 * conexoes concorrentes. Enquanto nao for `null`, ha um start em andamento e os
 * chamadores reaproveitam esta mesma promise em vez de iniciar outra conexao.
 * Zerada no `finally` do start. @type {Promise<object|null>|null}
 */
let connectionStartPromise = null;

/**
 * Conjunto de callbacks inscritos para receber atualizacoes de progresso da
 * analise. Set evita duplicatas e permite remocao em O(1). @type {Set<Function>}
 */
const progressListeners = new Set();

/**
 * Conjunto de callbacks inscritos para receber mensagens do chat de membros.
 * @type {Set<Function>}
 */
const membersMessageListeners = new Set();

/**
 * Ponto de entrada do servico: inscreve um callback de progresso (opcional) e
 * garante que a conexao com o hub esteja ativa.
 *
 * Como funciona:
 *   1. Se `onProgress` for uma funcao, registra-a no conjunto de listeners de
 *      progresso (validacao defensiva: ignora valores nao-funcao em silencio).
 *   2. Delega a abertura/reuso da conexao a `ensureConnection`, que e idempotente
 *      e segura contra concorrencia.
 *   3. So retorna o id depois que `ensureConnection` resolveu, garantindo que o
 *      `connectionId` ja foi preenchido pelo handshake.
 *
 * @param {(message: string, percent: number) => void} [onProgress] Callback
 *   chamado a cada evento `ReceiveProgress` (mensagem de status e percentual).
 * @returns {Promise<string|null>} O id da conexao se conectada com sucesso, ou
 *   `null` se a conexao falhou ou a lib SignalR nao esta carregada.
 * @sideeffect Adiciona `onProgress` a `progressListeners` e pode abrir o WebSocket.
 */
export async function initSignalR(onProgress) {
    if (typeof onProgress === 'function') {
        progressListeners.add(onProgress);
    }

    const conn = await ensureConnection();
    return conn ? connectionId : null;
}

/**
 * Retorna o id da conexao atual sem disparar nenhuma conexao.
 *
 * Util para anexar o id em requisicoes HTTP, permitindo que o backend correlacione
 * a chamada REST com este cliente WebSocket e envie o progresso de volta.
 *
 * @returns {string|null} Id da conexao, ou `null` se ainda nao conectado.
 */
export function getConnectionId() {
    return connectionId;
}

/**
 * Inscreve um callback para receber mensagens do chat de membros.
 *
 * Segue o padrao "subscribe retorna unsubscribe": o valor retornado e uma funcao
 * que, ao ser chamada, remove o callback do conjunto de listeners. Isso permite
 * que componentes de UI limpem sua inscricao ao desmontar, evitando vazamento de
 * memoria e callbacks orfaos.
 *
 * Validacao defensiva: se `onMessage` nao for funcao, retorna um unsubscribe
 * no-op (que nao faz nada), preservando o contrato de retorno sem registrar nada.
 *
 * @param {(payload: {username: string, message: string, sentAtUtc: string}) => void} onMessage
 *   Callback chamado a cada mensagem recebida, com o remetente, o texto e o
 *   timestamp UTC de envio.
 * @returns {() => void} Funcao de cancelamento (unsubscribe) idempotente.
 * @sideeffect Adiciona `onMessage` a `membersMessageListeners`.
 */
export function subscribeMembersMessages(onMessage) {
    if (typeof onMessage !== 'function') {
        return () => {};
    }

    membersMessageListeners.add(onMessage);
    return () => membersMessageListeners.delete(onMessage);
}

/**
 * Garante que a conexao do chat de membros esteja ativa.
 *
 * E um wrapper de conveniencia sobre `ensureConnection` que normaliza o resultado
 * para booleano: a UI do chat so precisa saber se ha conexao, nao a instancia.
 * Tipicamente chamado ao abrir o painel de chat, antes de habilitar o envio.
 *
 * @returns {Promise<boolean>} `true` se conectado com sucesso, `false` caso
 *   contrario (falha de start ou lib ausente).
 * @sideeffect Pode abrir o WebSocket via `ensureConnection`.
 */
export async function connectMembersChat() {
    const conn = await ensureConnection();
    return Boolean(conn);
}

/**
 * Envia uma mensagem ao chat de membros, invocando o metodo `SendMembersMessage`
 * no hub do servidor.
 *
 * Etapas e por que de cada validacao:
 *   1. Sanitiza as entradas com `String(... || '').trim()`: coage qualquer tipo
 *      para string, trata `null`/`undefined` como vazio e remove espacos das bordas.
 *   2. Rejeita username ou mensagem vazios (apos trim) antes de tocar a rede,
 *      poupando um round-trip que o servidor recusaria de qualquer forma.
 *   3. Garante a conexao via `ensureConnection`; se falhar, lanca erro claro.
 *   4. Verifica explicitamente o estado `Connected`: durante uma reconexao
 *      automatica a instancia existe, mas `invoke` falharia. Aqui detectamos
 *      esse estado intermediario e pedimos ao usuario para tentar de novo.
 *   5. So entao invoca o metodo do hub, que faz o broadcast aos demais membros.
 *
 * @param {string} username Nome de exibicao do remetente.
 * @param {string} message Texto da mensagem.
 * @returns {Promise<void>} Resolve quando o servidor confirma o recebimento da
 *   invocacao.
 * @throws {Error} 'Username and message are required.' se algum campo for vazio.
 * @throws {Error} 'Could not connect to chat.' se a conexao nao pode ser aberta.
 * @throws {Error} 'Chat is reconnecting. Try again in a moment.' se a conexao
 *   estiver em estado transitorio (reconectando).
 * @sideeffect Envia dados pela rede ao hub SignalR.
 */
export async function sendMembersMessage(username, message) {
    const safeUsername = String(username || '').trim();
    const safeMessage = String(message || '').trim();

    if (!safeUsername || !safeMessage) {
        throw new Error('Username and message are required.');
    }

    const conn = await ensureConnection();
    if (!conn) {
        throw new Error('Could not connect to chat.');
    }

    if (conn.state !== signalR.HubConnectionState.Connected) {
        throw new Error('Chat is reconnecting. Try again in a moment.');
    }

    await conn.invoke('SendMembersMessage', safeUsername, safeMessage);
}

/**
 * Indica se ha uma conexao ativa e plenamente estabelecida.
 *
 * Diferente de apenas checar `connection != null`: a instancia pode existir mas
 * estar em estado `Connecting`/`Reconnecting`/`Disconnected`. Aqui exigimos o
 * estado `Connected`, o unico em que `invoke` e seguro. Usada por `ensureConnection`
 * para decidir se pode reaproveitar a conexao existente.
 *
 * @returns {boolean} `true` somente se a conexao existe e esta em `Connected`.
 */
function isConnected() {
    return Boolean(connection && connection.state === signalR.HubConnectionState.Connected);
}

/**
 * Faz broadcast de uma atualizacao de progresso a todos os listeners inscritos.
 *
 * Cada listener e chamado dentro de seu proprio try/catch: o isolamento garante
 * que uma excecao em um listener (ex.: erro de render na UI) nao interrompa o loop
 * nem impeca os demais listeners de receberem o evento. Falhas sao apenas logadas
 * como warning, sem propagar.
 *
 * @param {string} message Texto de status do progresso vindo do hub.
 * @param {number} percent Percentual concluido (0-100).
 * @returns {void}
 * @sideeffect Invoca callbacks externos; pode emitir `console.warn` em falha.
 */
function notifyProgress(message, percent) {
    progressListeners.forEach(listener => {
        try {
            listener(message, percent);
        } catch (err) {
            console.warn('SignalR progress listener failed:', err);
        }
    });
}

/**
 * Faz broadcast de uma mensagem de chat recebida a todos os listeners inscritos.
 *
 * Os tres argumentos posicionais vindos do hub sao empacotados em um unico objeto
 * `{ username, message, sentAtUtc }`, dando aos listeners uma API estavel e
 * autoexplicativa em vez de depender da ordem dos parametros. Mesmo isolamento
 * por try/catch de `notifyProgress`: um listener com erro nao afeta os outros.
 *
 * @param {string} username Remetente da mensagem.
 * @param {string} message Texto da mensagem.
 * @param {string} sentAtUtc Timestamp de envio em UTC.
 * @returns {void}
 * @sideeffect Invoca callbacks externos; pode emitir `console.warn` em falha.
 */
function notifyMembersMessage(username, message, sentAtUtc) {
    membersMessageListeners.forEach(listener => {
        try {
            listener({ username, message, sentAtUtc });
        } catch (err) {
            console.warn('SignalR members listener failed:', err);
        }
    });
}

/**
 * Verifica se a biblioteca global `signalR` foi carregada na pagina.
 *
 * O cliente SignalR e injetado via <script> externo, nao por import de modulo;
 * portanto pode estar ausente se o script falhar ou ainda nao tiver carregado.
 * Testar `typeof signalR === 'undefined'` evita um ReferenceError que travaria
 * todo o servico. Em caso de ausencia, loga um erro e retorna `false` para que os
 * chamadores degradem graciosamente.
 *
 * @returns {boolean} `true` se a lib esta disponivel; `false` caso contrario.
 * @sideeffect Emite `console.error` quando a lib nao foi carregada.
 */
function ensureSignalRLoaded() {
    // Verifica se a lib signalR foi carregada
    if (typeof signalR === 'undefined') {
        console.error('SignalR library not loaded!');
        return false;
    }
    return true;
}

/**
 * Registra todos os handlers de eventos do hub e do ciclo de vida na conexao.
 *
 * Chamada UMA unica vez por instancia de conexao (logo apos `build()`, antes do
 * `start()`), pois os handlers devem estar prontos para nao perder os primeiros
 * eventos do servidor. Conecta duas categorias:
 *
 *   - Eventos de dominio (`conn.on`): `ReceiveProgress` e `ReceiveMembersMessage`,
 *     que apenas repassam para as funcoes de broadcast internas.
 *   - Eventos de ciclo de vida:
 *       * `onreconnected`: apos a reconexao automatica, atualiza o `connectionId`.
 *         O fallback em cascata (`newConnectionId || conn.connectionId ||
 *         connectionId`) cobre os casos em que o novo id nao vem no callback,
 *         preferindo nessa ordem: id do evento, id atual da conexao, id antigo.
 *       * `onclose`: loga um warning apenas quando o fechamento foi por erro
 *         (fechamento limpo nao gera ruido no console).
 *
 * @param {object} conn Instancia de HubConnection ja construida.
 * @returns {void}
 * @sideeffect Anexa handlers a `conn`; pode atualizar `connectionId` e logar.
 */
function wireConnectionHandlers(conn) {
    conn.on('ReceiveProgress', (message, percent) => {
        notifyProgress(message, percent);
    });

    conn.on('ReceiveMembersMessage', (username, message, sentAtUtc) => {
        notifyMembersMessage(username, message, sentAtUtc);
    });

    conn.onreconnected((newConnectionId) => {
        connectionId = newConnectionId || conn.connectionId || connectionId;
        console.log('SignalR reconnected. ID:', connectionId);
    });

    conn.onclose((err) => {
        if (err) {
            console.warn('SignalR disconnected:', err.message || err);
        }
    });
}

/**
 * Obtem a conexao com o hub, criando-a e iniciando-a sob demanda (lazy), e
 * garante que NUNCA existam dois starts/conexoes concorrentes (singleton).
 *
 * Ordem das guardas (importa o curto-circuito):
 *   1. Lib carregada? Se nao, aborta com `null` (nada mais faz sentido).
 *   2. Ja conectado (estado `Connected`)? Reusa a conexao imediatamente.
 *   3. Ha um start em andamento (`connectionStartPromise` != null)? Retorna a
 *      MESMA promise. Este e o coracao da protecao contra concorrencia: se duas
 *      partes do app chamarem `ensureConnection` quase ao mesmo tempo, ambas
 *      aguardam o unico start em voo em vez de abrir um segundo WebSocket.
 *   4. Sem instancia ainda? Constroi via HubConnectionBuilder apontando para
 *      `${BASE_URL}/hubs/analysis`, com `withAutomaticReconnect()` (o cliente
 *      tenta reconectar sozinho apos quedas), e fia os handlers UMA vez.
 *
 * Em seguida inicia a conexao e armazena a promise resultante em
 * `connectionStartPromise` (a trava). A cadeia:
 *   - `then`: captura o `connectionId` definitivo do handshake e resolve com a
 *     conexao.
 *   - `catch`: em falha, loga, ZERA `connection` (para que a proxima chamada
 *     reconstrua do zero em vez de reutilizar uma instancia quebrada) e resolve
 *     com `null` em vez de relancar — chamadores tratam `null` graciosamente.
 *   - `finally`: SEMPRE limpa a trava (`connectionStartPromise = null`), seja
 *     sucesso ou falha, liberando futuras tentativas de conexao.
 *
 * @returns {Promise<object|null>} A HubConnection conectada, ou `null` se a lib
 *   nao esta carregada ou o start falhou.
 * @sideeffect Pode construir a conexao, abrir o WebSocket, mutar `connection`,
 *   `connectionId` e `connectionStartPromise`, e logar no console.
 */
async function ensureConnection() {
    if (!ensureSignalRLoaded()) {
        return null;
    }

    if (isConnected()) {
        return connection;
    }

    if (connectionStartPromise) {
        return connectionStartPromise;
    }

    if (!connection) {
        connection = new signalR.HubConnectionBuilder()
            .withUrl(`${BASE_URL}/hubs/analysis`)
            .withAutomaticReconnect()
            .build();

        wireConnectionHandlers(connection);
    }

    connectionStartPromise = connection.start()
        .then(() => {
            connectionId = connection.connectionId;
            console.log('SignalR connected. ID:', connectionId);
            return connection;
        })
        .catch((err) => {
            console.error('SignalR connection error:', err);
            connection = null;
            return null;
        })
        .finally(() => {
            connectionStartPromise = null;
        });

    return connectionStartPromise;
}
