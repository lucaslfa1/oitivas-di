from datetime import timedelta
from fastapi import APIRouter, Depends, HTTPException, status
from fastapi.security import OAuth2PasswordRequestForm
from core.security import verify_password, create_access_token, get_password_hash
from core.config import get_settings
from pydantic import BaseModel

router = APIRouter()
settings = get_settings()

# Simulação de Banco de Dados (Em produção, usar DB real)
# Senhas hashadas para: admin123, Guerr@2026, Je@n2026, Analista@2026
FAKE_USERS_DB = {
    "admin": {
        "username": "admin",
        "full_name": "Administrador",
        "hashed_password": get_password_hash("admin123")
    },
    "Guerra": {
        "username": "Guerra",
        "full_name": "Guerra",
        "hashed_password": get_password_hash("Guerr@2026")
    },
    "Jean": {
        "username": "Jean",
        "full_name": "Jean",
        "hashed_password": get_password_hash("Je@n2026")
    },
    "Analista": {
        "username": "Analista",
        "full_name": "Analista",
        "hashed_password": get_password_hash("Analista@2026")
    },
    "Daniele": {
        "username": "Daniele",
        "full_name": "Daniele",
        "hashed_password": get_password_hash("Gestão2026")
    }
}

class Token(BaseModel):
    access_token: str
    token_type: str
    username: str

@router.post("/login", response_model=Token)
async def login_for_access_token(form_data: OAuth2PasswordRequestForm = Depends()):
    user = FAKE_USERS_DB.get(form_data.username)
    if not user or not verify_password(form_data.password, user["hashed_password"]):
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
