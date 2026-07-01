/**
 * Inicialização do Usuário
 */

/**
 * Inicializa informações do usuário
 */
export function initUser() {
    const nome = localStorage.getItem('sentinel-user') || 'Operador';
    const role = localStorage.getItem('sentinel-role') || 'Membro';
    const iniciais = nome
        .split(' ')
        .filter(Boolean)
        .slice(0, 2)
        .map(p => p[0].toUpperCase())
        .join('');

    const avatarEl = document.getElementById('userAvatar');
    const nameEl = document.getElementById('userNameDisplay');
    const roleEl = document.getElementById('userRole');

    if (avatarEl) avatarEl.textContent = iniciais || 'SB';
    if (nameEl) nameEl.textContent = nome;
    if (roleEl) roleEl.textContent = role;

    console.log(`?? Usuário: ${nome}`);
}
