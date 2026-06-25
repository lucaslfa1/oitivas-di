# Sistema de Autenticação e Controle de Acesso — Sentinel

## Objetivo

Implementar sistema de login/senha com **níveis hierárquicos de acesso** para controlar funcionalidades e dados visíveis por cada tipo de usuário.

---

## 1. Hierarquia de Acesso (Roles)

Definição dos níveis proposta:

| Nível | Role | Descrição | Permissões |
|:-----:|------|-----------|------------|
| 1 | `Operador` | Usuário base | Transcrever áudio, visualizar próprios laudos |
| 2 | `Analista` | Peritos/Analistas | Tudo do Operador + Analisar imagens/vídeos + Editar laudos |
| 3 | `Supervisor` | Gestão operacional | Tudo do Analista + Ver laudos de todos + Dashboard de métricas |
| 4 | `Admin` | Administrador | Acesso total + Gerenciar usuários + Configurações |

> [!IMPORTANT]
> Precisamos definir se haverá subdivisões (ex: Analista Jr/Sr) ou se esses 4 níveis são suficientes.

---

## 2. Modelo de Dados

### Tabela `Users`

```sql
CREATE TABLE Users (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Username        TEXT NOT NULL UNIQUE,
    Email           TEXT NOT NULL UNIQUE,
    PasswordHash    TEXT NOT NULL,
    Role            TEXT NOT NULL DEFAULT 'Operador',
    FullName        TEXT,
    Department      TEXT,
    IsActive        INTEGER DEFAULT 1,
    CreatedAt       TEXT DEFAULT CURRENT_TIMESTAMP,
    LastLoginAt     TEXT,
    CreatedBy       INTEGER REFERENCES Users(Id)
);
```

### Tabela `AuditLog` (Opcional, recomendado)

```sql
CREATE TABLE AuditLog (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER REFERENCES Users(Id),
    Action      TEXT NOT NULL,       -- 'LOGIN', 'TRANSCRIBE', 'GENERATE_REPORT', etc.
    EntityType  TEXT,                -- 'Audio', 'Laudo', 'User'
    EntityId    INTEGER,
    Details     TEXT,                -- JSON com detalhes
    IpAddress   TEXT,
    CreatedAt   TEXT DEFAULT CURRENT_TIMESTAMP
);
```

---

## 3. Opções de Implementação

### Opção A: JWT + ASP.NET Identity (Recomendado)

```
┌─────────────────────────────────────────────────────────────┐
│                      FLUXO DE AUTENTICAÇÃO                   │
├─────────────────────────────────────────────────────────────┤
│  1. Login (POST /api/auth/login)                            │
│     └─▶ Valida credenciais                                  │
│     └─▶ Gera JWT Token (expira em 8h)                       │
│     └─▶ Retorna { token, user, expiresAt }                  │
│                                                              │
│  2. Requisições Autenticadas                                │
│     └─▶ Header: Authorization: Bearer {token}               │
│     └─▶ Middleware valida token                             │
│     └─▶ Middleware verifica Role permitida                  │
│                                                              │
│  3. Refresh Token (opcional)                                │
│     └─▶ Token expirando? Solicita novo                      │
└─────────────────────────────────────────────────────────────┘
```

**Prós:**
- Stateless (sem sessão no servidor)
- Funciona bem com SPA (frontend JS)
- Padrão da indústria
- Fácil integração com Cloud Run

**Contras:**
- Token pode ser roubado se não usar HTTPS
- Logout requer invalidação no cliente

### Opção B: Cookie-based Session

**Prós:**
- Mais simples de implementar
- Logout real (invalida sessão no servidor)

**Contras:**
- Requer estado no servidor
- Menos adequado para APIs

> [!TIP]
> **Recomendação:** Opção A (JWT) é mais adequada para a arquitetura atual do Sentinel.

---

## 4. Endpoints de Autenticação

| Método | Endpoint | Descrição | Acesso |
|--------|----------|-----------|--------|
| POST | `/api/auth/login` | Login com username/senha | Público |
| POST | `/api/auth/logout` | Invalidar token (cliente) | Autenticado |
| GET | `/api/auth/me` | Dados do usuário logado | Autenticado |
| POST | `/api/auth/change-password` | Alterar própria senha | Autenticado |
| POST | `/api/users` | Criar usuário | Admin |
| GET | `/api/users` | Listar usuários | Admin/Supervisor |
| PUT | `/api/users/{id}` | Editar usuário | Admin |
| DELETE | `/api/users/{id}` | Desativar usuário | Admin |

---

## 5. Proteção de Endpoints Existentes

### Mapeamento de Roles por Endpoint

| Endpoint | Operador | Analista | Supervisor | Admin |
|----------|:--------:|:--------:|:----------:|:-----:|
| `POST /api/transcrever` | ✅ | ✅ | ✅ | ✅ |
| `POST /api/analisar/imagem` | ❌ | ✅ | ✅ | ✅ |
| `POST /api/analisar/video` | ❌ | ✅ | ✅ | ✅ |
| `POST /api/analisar/laudo` | ✅ | ✅ | ✅ | ✅ |
| `GET /api/analises` | Próprias | Próprias | Todas | Todas |
| `GET /api/health` | ❌ | ❌ | ✅ | ✅ |
| `GET /api/users` | ❌ | ❌ | ✅ (readonly) | ✅ |

---

## 6. Mudanças no Frontend

### Novas Telas Necessárias

1. **Tela de Login** (`login.html` ou modal)
   - Campo usuário/email
   - Campo senha
   - Botão "Entrar"
   - Link "Esqueci minha senha" (fase 2)

2. **Cabeçalho com Usuário Logado**
   - Mostrar nome do usuário
   - Dropdown com "Meu Perfil", "Alterar Senha", "Sair"

3. **Página de Gerenciamento de Usuários** (Admin)
   - Tabela de usuários
   - Criar/Editar/Desativar

### Fluxo de Autenticação no JS

```javascript
// Ao fazer login
const response = await fetch('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password })
});
const { token, user } = await response.json();
localStorage.setItem('auth_token', token);
localStorage.setItem('user', JSON.stringify(user));

// Em todas as requisições
fetch('/api/transcrever', {
    headers: {
        'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
    }
});
```

---

## 7. Considerações de Segurança

| Aspecto | Implementação |
|---------|---------------|
| **Senhas** | Hash com BCrypt ou Argon2 (nunca texto puro) |
| **HTTPS** | Obrigatório (Cloud Run já fornece) |
| **Token Expiration** | 8 horas (ou configurável) |
| **Rate Limiting** | Limitar tentativas de login (ex: 5/min) |
| **Audit Log** | Registrar logins, ações sensíveis |
| **Password Policy** | Mínimo 8 caracteres, complexidade |

---

## 8. Fases de Implementação Sugeridas

### Fase 1 — MVP de Autenticação
- [ ] Modelo de dados (Users)
- [ ] Endpoint de login/logout
- [ ] JWT token generation
- [ ] Middleware de autorização
- [ ] Tela de login básica
- [ ] Proteção dos endpoints existentes

### Fase 2 — Gestão de Usuários
- [ ] CRUD de usuários (Admin)
- [ ] Página de gerenciamento
- [ ] Alterar própria senha
- [ ] Desativação de usuários

### Fase 3 — Recursos Avançados
- [ ] Audit Log
- [ ] "Esqueci minha senha" (email)
- [ ] Sessões múltiplas / força logout
- [ ] Dashboard de métricas por usuário

---

## 9. Perguntas para Definição

Antes de implementar, precisamos definir:

1. **Hierarquia:** Os 4 níveis propostos (Operador, Analista, Supervisor, Admin) são suficientes?

2. **Criação de usuários:** Apenas Admin cria, ou Supervisor também pode criar Operadores?

3. **Departamentos:** Usuários serão agrupados por departamento/filial?

4. **Primeiro usuário:** Como criar o Admin inicial? (seed no banco)

5. **Dados existentes:** Os laudos já salvos pertencem a quem? (migração)

6. **Expiração de sessão:** 8 horas é adequado para o fluxo de trabalho?

7. **Multi-tenant:** No futuro, haverá múltiplas empresas usando o sistema?

---

## 10. Estimativa de Esforço

| Fase | Complexidade | Tempo Estimado |
|------|:------------:|:--------------:|
| Fase 1 (MVP) | Média | 2-3 dias |
| Fase 2 (Gestão) | Média | 1-2 dias |
| Fase 3 (Avançado) | Alta | 3-5 dias |

---

> [!NOTE]
> Este documento é apenas um planejamento. Nenhuma alteração de código será feita até aprovação.

