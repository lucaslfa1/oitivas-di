"""Router de autenticação (API v1) do Sentinel.

Expõe o endpoint de login OAuth2 (fluxo *password grant*) que troca
usuário/senha por um JWT de acesso. É o ponto de entrada de autenticação
do sistema de análise forense de sinistros: todos os demais endpoints
protegidos validam o token emitido aqui.

Como funciona:
    1. O cliente envia `username`/`password` via `application/x-www-form-urlencoded`
       para `POST /login` (formato exigido pelo padrão OAuth2 password grant).
    2. O usuário é resolvido contra `FAKE_USERS_DB` (stub de desenvolvimento) e a
       senha é conferida contra o hash armazenado por `core.security.verify_password`.
    3. Em caso de sucesso, um JWT assinado é emitido por `create_access_token`,
       com expiração definida nas configurações da aplicação (`get_settings`).

Observação de segurança:
    O banco de usuários é um stub em memória pensado apenas para desenvolvimento.
    Nenhuma senha é versionada no código: a credencial do admin de dev vem da
    variável de ambiente `SENTINEL_DEV_ADMIN_PASSWORD`. Em produção, este stub
    deve ser substituído por um repositório de usuários real.
"""

import os
from datetime import timedelta
from fastapi import APIRouter, Depends, HTTPException, status
from fastapi.security import OAuth2PasswordRequestForm
from core.security import verify_password, create_access_token, get_password_hash
from core.config import get_settings
from pydantic import BaseModel

router = APIRouter()
settings = get_settings()

# Stub de autenticação para desenvolvimento (em produção, usar um banco de usuários real).
# ATENÇÃO: nenhuma senha deve ser versionada. A senha do admin de desenvolvimento é lida
# da variável de ambiente SENTINEL_DEV_ADMIN_PASSWORD; se não definida, o login fica inativo.
# Ver docs/PLANO_MELHORIA_HANDOFF.md (Fase 0 / segurança).
_dev_admin_password = os.getenv("SENTINEL_DEV_ADMIN_PASSWORD", "")
FAKE_USERS_DB = {
    "admin": {
        "username": "admin",
        "full_name": "Administrador",
        "hashed_password": get_password_hash(_dev_admin_password) if _dev_admin_password else "",
    },
}

class Token(BaseModel):
    """Modelo de resposta do endpoint de login (corpo do JWT emitido).

    Define o contrato de saída devolvido ao cliente após autenticação
    bem-sucedida. Usado como `response_model` de `login_for_access_token`,
    o que faz o FastAPI validar/serializar a resposta e documentá-la no
    schema OpenAPI.

    Attributes:
        access_token: JWT assinado a ser enviado nas requisições seguintes
            via cabeçalho `Authorization: Bearer <token>`.
        token_type: Tipo do token conforme OAuth2; sempre `"bearer"` aqui.
        username: Nome do usuário autenticado, ecoado por conveniência do
            cliente (evita decodificar o JWT só para exibir quem logou).
    """
    access_token: str
    token_type: str
    username: str

@router.post("/login", response_model=Token)
async def login_for_access_token(form_data: OAuth2PasswordRequestForm = Depends()):
    """Autentica via OAuth2 *password grant* e emite um JWT de acesso.

    Endpoint `POST /login`: recebe credenciais no formato de formulário OAuth2,
    valida usuário/senha contra o stub `FAKE_USERS_DB` e, em caso de sucesso,
    devolve um token de acesso assinado para uso nas demais rotas protegidas.

    Args:
        form_data: Credenciais injetadas pelo FastAPI a partir do corpo
            `application/x-www-form-urlencoded` (campos `username` e `password`),
            conforme exige o padrão OAuth2 password grant.

    Returns:
        dict: Payload compatível com o `response_model` `Token`, contendo
            `access_token` (JWT), `token_type` (`"bearer"`) e `username`.

    Raises:
        HTTPException: 401 (Unauthorized) com cabeçalho `WWW-Authenticate: Bearer`
            quando o usuário não existe, não possui senha cadastrada (stub
            inativo) ou a senha informada não confere.

    Como funciona:
        1. Resolve o usuário em `FAKE_USERS_DB` pelo `username` informado.
        2. Rejeita o login se: (a) o usuário não existe; (b) o hash de senha
           está vazio — caso em que o stub de dev está inativo por falta da env
           `SENTINEL_DEV_ADMIN_PASSWORD`; ou (c) a senha não bate com o hash via
           `verify_password`. As três condições retornam o mesmo 401 genérico
           para não revelar qual delas falhou (evita enumeração de usuários).
        3. Calcula a validade do token: `access_token_expire_minutes` das
           configurações é convertido em `timedelta` (janela de expiração em
           minutos antes do JWT deixar de ser aceito).
        4. Emite o JWT com `create_access_token`, gravando o `username` na claim
           padrão `sub` (subject/identidade do portador) e aplicando a expiração.
        5. Retorna o token, o tipo `"bearer"` e o `username` autenticado.
    """
    user = FAKE_USERS_DB.get(form_data.username)
    if not user or not user["hashed_password"] or not verify_password(form_data.password, user["hashed_password"]):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Usuário ou senha incorretos",
            headers={"WWW-Authenticate": "Bearer"},
        )
    
    access_token_expires = timedelta(minutes=settings.access_token_expire_minutes)
    access_token = create_access_token(
        data={"sub": user["username"]}, expires_delta=access_token_expires
    )
    
    return {
        "access_token": access_token, 
        "token_type": "bearer",
        "username": user["username"]
    }
