/**
 * Gerenciamento de Tema (Dark/Light Mode)
 */

export function initTheme() {
    const btn = document.getElementById('themeToggle');
    if (!btn) {
        console.warn("⚠️ Botão de tema não encontrado");
        return;
    }

    // Carrega tema salvo
    const saved = localStorage.getItem('theme');
    if (saved === 'dark') {
        document.body.setAttribute('data-theme', 'dark');
    }
    updateThemeIcon();

    // Event listener para toggle
    btn.addEventListener('click', toggleTheme);
    
    console.log("🎨 Tema inicializado:", saved || 'light');
}

export function toggleTheme() {
    const isDark = document.body.getAttribute('data-theme') === 'dark';
    
    if (isDark) {
        document.body.removeAttribute('data-theme');
        localStorage.setItem('theme', 'light');
    } else {
        document.body.setAttribute('data-theme', 'dark');
        localStorage.setItem('theme', 'dark');
    }
    
    updateThemeIcon();
    console.log("🌙 Tema alterado:", isDark ? 'claro' : 'escuro');
}

export function updateThemeIcon() {
    const isDark = document.body.getAttribute('data-theme') === 'dark';
    const icon = document.getElementById('themeIcon');
    
    if (icon) {
        // Altera o ícone: se está dark, mostra Sol (para voltar ao claro). Se está light, mostra Lua.
        icon.setAttribute('data-lucide', isDark ? 'sun' : 'moon');
        if (typeof lucide !== 'undefined') lucide.createIcons();
    }
}

export function isDarkTheme() {
    return document.body.getAttribute('data-theme') === 'dark';
}
