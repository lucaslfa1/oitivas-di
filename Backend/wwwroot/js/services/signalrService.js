import { BASE_URL } from '../config/constants.js';

let connection = null;
let connectionId = null;
let connectionStartPromise = null;
const progressListeners = new Set();
const membersMessageListeners = new Set();

export async function initSignalR(onProgress) {
    if (typeof onProgress === 'function') {
        progressListeners.add(onProgress);
    }

    const conn = await ensureConnection();
    return conn ? connectionId : null;
}

export function getConnectionId() {
    return connectionId;
}

export function subscribeMembersMessages(onMessage) {
    if (typeof onMessage !== 'function') {
        return () => {};
    }

    membersMessageListeners.add(onMessage);
    return () => membersMessageListeners.delete(onMessage);
}

export async function connectMembersChat() {
    const conn = await ensureConnection();
    return Boolean(conn);
}

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

function isConnected() {
    return Boolean(connection && connection.state === signalR.HubConnectionState.Connected);
}

function notifyProgress(message, percent) {
    progressListeners.forEach(listener => {
        try {
            listener(message, percent);
        } catch (err) {
            console.warn('SignalR progress listener failed:', err);
        }
    });
}

function notifyMembersMessage(username, message, sentAtUtc) {
    membersMessageListeners.forEach(listener => {
        try {
            listener({ username, message, sentAtUtc });
        } catch (err) {
            console.warn('SignalR members listener failed:', err);
        }
    });
}

function ensureSignalRLoaded() {
    // Verifica se a lib signalR foi carregada
    if (typeof signalR === 'undefined') {
        console.error('SignalR library not loaded!');
        return false;
    }
    return true;
}

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
