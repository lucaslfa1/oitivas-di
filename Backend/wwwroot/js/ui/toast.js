/**
 * Sistema de Notificações Toast Profissional
 * Notificações flutuantes elegantes para feedback do usuário
 */

// Container dos toasts
let toastContainer = null;

// Configurações padrão
const TOAST_CONFIG = {
    duration: 5000,        // 5 segundos
    position: 'top-right', // top-right, top-left, bottom-right, bottom-left
    maxToasts: 5,          // Máximo de toasts simultâneos
    gap: 12                // Espaço entre toasts
};

// Tipos de toast com ícones e cores
const TOAST_TYPES = {
    success: {
        icon: 'check-circle',
        title: 'Sucesso',
        className: 'toast-success'
    },
    error: {
        icon: 'x-circle',
        title: 'Erro',
        className: 'toast-error'
    },
    warning: {
        icon: 'alert-triangle',
        title: 'Atenção',
        className: 'toast-warning'
    },
    info: {
        icon: 'info',
        title: 'Informação',
        className: 'toast-info'
    }
};

/**
 * Inicializa o container de toasts
 */
function initToastContainer() {
    if (toastContainer) return;

    toastContainer = document.createElement('div');
    toastContainer.id = 'toast-container';
    toastContainer.className = `toast-container ${TOAST_CONFIG.position}`;
    document.body.appendChild(toastContainer);

    // Injeta estilos se ainda não existirem
    if (!document.getElementById('toast-styles')) {
        injectStyles();
    }
}

/**
 * Injeta estilos CSS do Toast
 */
function injectStyles() {
    const styles = document.createElement('style');
    styles.id = 'toast-styles';
    styles.textContent = `
        .toast-container {
            position: fixed;
            z-index: 10000;
            display: flex;
            flex-direction: column;
            gap: ${TOAST_CONFIG.gap}px;
            pointer-events: none;
            max-width: 420px;
            width: calc(100% - 32px);
        }
        
        .toast-container.top-right {
            top: 20px;
            right: 16px;
        }
        
        .toast-container.top-left {
            top: 20px;
            left: 16px;
        }
        
        .toast-container.bottom-right {
            bottom: 20px;
            right: 16px;
        }
        
        .toast-container.bottom-left {
            bottom: 20px;
            left: 16px;
        }
        
        .toast {
            display: flex;
            align-items: flex-start;
            gap: 12px;
            padding: 16px;
            border-radius: 12px;
            background: var(--card-bg, #ffffff);
            box-shadow: 0 10px 40px rgba(0, 0, 0, 0.15), 
                        0 4px 12px rgba(0, 0, 0, 0.1);
            pointer-events: auto;
            transform: translateX(120%);
            opacity: 0;
            transition: all 0.4s cubic-bezier(0.16, 1, 0.3, 1);
            position: relative;
            overflow: hidden;
            border: 1px solid var(--border-color, #e5e7eb);
        }
        
        .toast-container.top-left .toast,
        .toast-container.bottom-left .toast {
            transform: translateX(-120%);
        }
        
        .toast.show {
            transform: translateX(0);
            opacity: 1;
        }
        
        .toast.hiding {
            transform: translateX(120%);
            opacity: 0;
        }
        
        .toast-container.top-left .toast.hiding,
        .toast-container.bottom-left .toast.hiding {
            transform: translateX(-120%);
        }
        
        .toast-icon {
            flex-shrink: 0;
            width: 24px;
            height: 24px;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 50%;
            padding: 4px;
        }
        
        .toast-icon svg {
            width: 18px;
            height: 18px;
        }
        
        .toast-content {
            flex: 1;
            min-width: 0;
        }
        
        .toast-title {
            font-weight: 600;
            font-size: 14px;
            color: var(--text-primary, #1f2937);
            margin-bottom: 4px;
        }
        
        .toast-message {
            font-size: 13px;
            color: var(--text-secondary, #6b7280);
            line-height: 1.4;
            word-wrap: break-word;
        }
        
        .toast-close {
            flex-shrink: 0;
            background: none;
            border: none;
            cursor: pointer;
            padding: 4px;
            border-radius: 6px;
            color: var(--text-secondary, #9ca3af);
            transition: all 0.2s ease;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        
        .toast-close:hover {
            background: var(--hover-bg, #f3f4f6);
            color: var(--text-primary, #374151);
        }
        
        .toast-close svg {
            width: 16px;
            height: 16px;
        }
        
        .toast-progress {
            position: absolute;
            bottom: 0;
            left: 0;
            height: 3px;
            background: currentColor;
            opacity: 0.3;
            border-radius: 0 0 0 12px;
            transition: width linear;
        }
        
        /* Tipos de Toast */
        .toast-success .toast-icon {
            background: rgba(16, 185, 129, 0.1);
            color: #10b981;
        }
        
        .toast-success .toast-progress {
            background: #10b981;
        }
        
        .toast-error .toast-icon {
            background: rgba(239, 68, 68, 0.1);
            color: #ef4444;
        }
        
        .toast-error .toast-progress {
            background: #ef4444;
        }
        
        .toast-warning .toast-icon {
            background: rgba(245, 158, 11, 0.1);
            color: #f59e0b;
        }
        
        .toast-warning .toast-progress {
            background: #f59e0b;
        }
        
        .toast-info .toast-icon {
            background: rgba(59, 130, 246, 0.1);
            color: #3b82f6;
        }
        
        .toast-info .toast-progress {
            background: #3b82f6;
        }
        
        /* Dark mode support */
        [data-theme="dark"] .toast {
            background: var(--card-bg, #1f2937);
            border-color: var(--border-color, #374151);
            box-shadow: 0 10px 40px rgba(0, 0, 0, 0.4), 
                        0 4px 12px rgba(0, 0, 0, 0.3);
        }
        
        /* Animação de shake para erros críticos */
        @keyframes toast-shake {
            0%, 100% { transform: translateX(0); }
            10%, 30%, 50%, 70%, 90% { transform: translateX(-4px); }
            20%, 40%, 60%, 80% { transform: translateX(4px); }
        }
        
        .toast.shake {
            animation: toast-shake 0.5s ease-in-out;
        }
        
        /* Responsivo */
        @media (max-width: 480px) {
            .toast-container {
                left: 16px !important;
                right: 16px !important;
                width: auto;
                max-width: none;
            }
            
            .toast {
                transform: translateY(-20px);
            }
            
            .toast.show {
                transform: translateY(0);
            }
            
            .toast.hiding {
                transform: translateY(-20px);
            }
        }
    `;
    document.head.appendChild(styles);
}

/**
 * Cria e exibe um toast
 * @param {string} type - Tipo: 'success', 'error', 'warning', 'info'
 * @param {string} message - Mensagem do toast
 * @param {Object} options - Opções adicionais
 * @returns {HTMLElement} Elemento do toast
 */
export function showToast(type, message, options = {}) {
    initToastContainer();

    const config = TOAST_TYPES[type] || TOAST_TYPES.info;
    const duration = options.duration ?? TOAST_CONFIG.duration;
    const title = options.title ?? config.title;
    const shake = options.shake ?? (type === 'error');

    // Limita número de toasts
    const existingToasts = toastContainer.querySelectorAll('.toast');
    if (existingToasts.length >= TOAST_CONFIG.maxToasts) {
        const oldest = existingToasts[0];
        removeToast(oldest);
    }

    // Cria elemento do toast
    const toast = document.createElement('div');
    toast.className = `toast ${config.className}`;
    toast.innerHTML = `
        <div class="toast-icon">
            <i data-lucide="${config.icon}"></i>
        </div>
        <div class="toast-content">
            <div class="toast-title">${title}</div>
            <div class="toast-message">${message}</div>
        </div>
        <button class="toast-close" aria-label="Fechar">
            <i data-lucide="x"></i>
        </button>
        ${duration > 0 ? '<div class="toast-progress"></div>' : ''}
    `;

    // Adiciona ao container
    toastContainer.appendChild(toast);

    // Inicializa ícones Lucide
    if (window.lucide) {
        window.lucide.createIcons();
    }

    // Animação de entrada
    requestAnimationFrame(() => {
        toast.classList.add('show');
        if (shake) {
            setTimeout(() => toast.classList.add('shake'), 100);
        }
    });

    // Progress bar
    if (duration > 0) {
        const progressBar = toast.querySelector('.toast-progress');
        if (progressBar) {
            progressBar.style.width = '100%';
            progressBar.style.transitionDuration = `${duration}ms`;
            requestAnimationFrame(() => {
                progressBar.style.width = '0%';
            });
        }
    }

    // Botão de fechar
    const closeBtn = toast.querySelector('.toast-close');
    closeBtn.addEventListener('click', () => removeToast(toast));

    // Auto-dismiss
    if (duration > 0) {
        setTimeout(() => removeToast(toast), duration);
    }

    return toast;
}

/**
 * Remove um toast com animação
 * @param {HTMLElement} toast - Elemento do toast
 */
function removeToast(toast) {
    if (!toast || toast.classList.contains('hiding')) return;

    toast.classList.add('hiding');
    toast.classList.remove('show');

    setTimeout(() => {
        toast.remove();
        // Remove container se vazio
        if (toastContainer && toastContainer.children.length === 0) {
            // Mantém o container para próximos toasts
        }
    }, 400);
}

/**
 * Atalhos para tipos específicos
 */
export const toast = {
    success: (message, options) => showToast('success', message, options),
    error: (message, options) => showToast('error', message, options),
    warning: (message, options) => showToast('warning', message, options),
    info: (message, options) => showToast('info', message, options),
    
    /**
     * Toast de promise - mostra loading, depois sucesso/erro
     * @param {Promise} promise - Promise a ser monitorada
     * @param {Object} messages - Mensagens para cada estado
     */
    promise: async (promise, messages = {}) => {
        const loadingToast = showToast('info', messages.loading || 'Processando...', { 
            duration: 0,
            title: 'Aguarde'
        });
        
        try {
            const result = await promise;
            removeToast(loadingToast);
            showToast('success', messages.success || 'Operação concluída!');
            return result;
        } catch (error) {
            removeToast(loadingToast);
            showToast('error', messages.error || error.message || 'Ocorreu um erro');
            throw error;
        }
    }
};

// Expõe globalmente para uso em onclick do HTML
window.toast = toast;
window.showToast = showToast;

export default toast;
