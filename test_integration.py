import requests
import json
import time

BASE_URL = "http://localhost:5252/api"

def test_full_flow():
    print("--- INICIANDO TESTE DE INTEGRAÇÃO DO SISTEMA ---")
    
    # 1. Testar Transcrição (Fast STT ou Whisper)
    print("\n1. Testando endpoint de Transcrição...")
    try:
        with open("part1.wav", "rb") as f:
            files = {"Arquivo": ("part1.wav", f, "audio/wav")}
            start_time = time.time()
            response = requests.post(f"{BASE_URL}/transcrever", files=files)
            end_time = time.time()
            
            if response.status_code == 200:
                transcricao = response.json().get("transcricao", "")
                print(f"SUCESSO: Transcrição concluída em {end_time - start_time:.2f}s")
                print(f"Texto: {transcricao[:200]}...")
            else:
                print(f"FALHA: Status {response.status_code}")
                print(response.text)
                return
    except Exception as e:
        print(f"ERRO no teste de transcrição: {str(e)}")
        return

    # 2. Testar Auditoria (GPT-4o)
    print("\n2. Testando endpoint de Auditoria...")
    try:
        payload = {"Transcricao": transcricao}
        start_time = time.time()
        response = requests.post(f"{BASE_URL}/auditar", json=payload)
        end_time = time.time()
        
        if response.status_code == 200:
            analise = response.json().get("analise", "")
            print(f"SUCESSO: Auditoria concluída em {end_time - start_time:.2f}s")
            print(f"Resultado: {analise[:500]}...")
        else:
            print(f"FALHA: Status {response.status_code}")
            print(response.text)
    except Exception as e:
        print(f"ERRO no teste de auditoria: {str(e)}")

if __name__ == "__main__":
    test_full_flow()
