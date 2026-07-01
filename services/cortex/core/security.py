"""Primitivas de seguranca de autenticacao do Sentinel (analise forense de sinistros).

Este modulo concentra as operacoes de seguranca usadas no fluxo de login da API:

1. Hash e verificacao de senhas (via passlib/bcrypt) para nunca persistir
   credenciais em texto puro.
2. Emissao de tokens de acesso JWT assinados (via python-jose) que autorizam
   as chamadas subsequentes do usuario a API.

Toda a configuracao sensivel (chave de assinatura, algoritmo e tempo de
expiracao do token) e injetada a partir de `core.config.get_settings`, e nao
fica hardcoded aqui. Isso mantem o segredo fora do codigo-fonte e permite que
cada ambiente (dev/homolog/prod) use sua propria chave.
"""

from datetime import datetime, timedelta
from typing import Optional
from jose import jwt
from passlib.context import CryptContext
from core.config import get_settings

# Configuracoes carregadas uma unica vez no import do modulo. `get_settings` e
# tipicamente memoizada (cache), entao `settings` reflete a mesma instancia
# usada pelo resto da aplicacao (mesma secret_key/algorithm/expiracao).
settings = get_settings()

# Contexto de hashing de senhas.
# - schemes=["bcrypt"]: bcrypt e o algoritmo ativo para gerar/validar hashes.
#   Ele ja embute um salt aleatorio por hash e tem custo computacional ajustavel,
#   o que o torna resistente a ataques de forca bruta e de tabela arco-iris.
# - deprecated="auto": marca como "obsoleto" qualquer esquema diferente do(s)
#   listado(s). Na pratica, se no futuro um novo esquema for adicionado a frente
#   de bcrypt, hashes antigos passam a ser sinalizados para re-hash automatico.
pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")

def verify_password(plain_password: str, hashed_password: str) -> bool:
    """Confere se uma senha em texto puro corresponde a um hash armazenado.

    Args:
        plain_password: Senha digitada pelo usuario, ainda em texto puro
            (tipicamente vinda do formulario/payload de login).
        hashed_password: Hash bcrypt previamente persistido no banco para
            aquele usuario (saida de `get_password_hash`).

    Returns:
        True se a senha confere com o hash; False caso contrario.

    Como funciona:
        1. Delega para `pwd_context.verify`, que identifica o esquema do hash
           informado (bcrypt) e extrai o salt embutido no proprio hash.
        2. Recalcula o hash da senha em texto puro usando esse mesmo salt e
           parametros de custo.
        3. Compara o resultado com o hash armazenado de forma resistente a
           timing attacks (comparacao em tempo constante, feita pela passlib).
        Note que nunca e necessario desfazer o hash: bcrypt e unidirecional,
        por isso a verificacao sempre re-hasheia e compara, nunca "descriptografa".
    """
    return pwd_context.verify(plain_password, hashed_password)

def get_password_hash(password: str) -> str:
    """Gera o hash bcrypt de uma senha para armazenamento seguro.

    Args:
        password: Senha em texto puro a ser protegida antes de persistir
            (por exemplo, no cadastro ou na troca de senha do usuario).

    Returns:
        String do hash bcrypt (ja contendo algoritmo, fator de custo e salt
        embutidos), pronta para ser salva no banco.

    Como funciona:
        1. `pwd_context.hash` gera um salt aleatorio novo a cada chamada.
        2. Aplica o bcrypt sobre a senha + salt usando o fator de custo padrao
           do contexto, produzindo um hash que e proposital e computacionalmente
           caro de gerar (defesa contra forca bruta).
        3. Retorna a string final no formato bcrypt (`$2b$<custo>$<salt+hash>`),
           autocontida — `verify_password` consegue validar so com ela, sem
           precisar guardar o salt separadamente.
        Consequencia importante: duas chamadas com a MESMA senha produzem hashes
        DIFERENTES (salts distintos). Portanto nunca compare hashes diretamente;
        use sempre `verify_password`.
    """
    return pwd_context.hash(password)

def create_access_token(data: dict, expires_delta: Optional[timedelta] = None) -> str:
    """Cria um token de acesso JWT assinado com prazo de validade.

    Args:
        data: Dicionario de claims a embutir no payload do token. Por convencao,
            o chamador inclui `{"sub": <identificador do usuario>}` — a claim
            padrao "subject" do JWT, que identifica a quem o token pertence.
        expires_delta: Janela de validade opcional. Se informada, define
            explicitamente quanto tempo o token vale a partir de agora. Se
            omitida (None), usa o tempo padrao de `settings.access_token_expire_minutes`.

    Returns:
        String do JWT codificado (formato `header.payload.assinatura`), pronta
        para ser devolvida ao cliente e enviada de volta no header Authorization.

    Como funciona:
        1. Copia `data` para `to_encode` para nao mutar o dicionario recebido do
           chamador (efeito colateral indesejado).
        2. Calcula o instante de expiracao `expire`:
           - com `expires_delta`: `agora (UTC) + expires_delta`;
           - sem ele: `agora (UTC) + access_token_expire_minutes` (valor de
             configuracao, p.ex. 30 = token expira em 30 minutos).
           Usa `datetime.utcnow()` para fixar o tempo em UTC, evitando ambiguidade
           de fuso ao validar o token depois.
        3. Adiciona a claim reservada "exp" (expiration time) ao payload. O proprio
           python-jose, na decodificacao, rejeita automaticamente tokens cujo "exp"
           ja passou — e o que garante que o token deixe de ser aceito apos o prazo.
        4. Assina e serializa o payload com `jwt.encode`, usando:
           - `settings.secret_key`: a chave secreta de assinatura (HMAC);
           - `settings.algorithm`: o algoritmo de assinatura, tipicamente HS256
             (HMAC-SHA256, simetrico — a mesma secret_key assina e valida).
           A assinatura garante integridade: qualquer alteracao no payload
           invalida o token, pois quem nao tem a `secret_key` nao consegue
           reassinar.
        5. Retorna o JWT resultante.
    """
    to_encode = data.copy()
    if expires_delta:
        expire = datetime.utcnow() + expires_delta
    else:
        expire = datetime.utcnow() + timedelta(minutes=settings.access_token_expire_minutes)

    to_encode.update({"exp": expire})
    encoded_jwt = jwt.encode(to_encode, settings.secret_key, algorithm=settings.algorithm)
    return encoded_jwt
