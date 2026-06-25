import urllib.request
import json
import uuid
import sys
import os

url = "https://eastus2.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2025-10-15"
key = "6Ws98iJ3hijXQ5OtcZbIEJLKklcqZMJ2G1iIjTBSVY73aI3GRyElJQQJ99CBACHYHv6XJ3w3AAAAACOGPSvn"
boundary = uuid.uuid4().hex
audio_path = r"Backend\wwwroot\uploads\audio\11d88381-9859-44f3-b33d-10cde7809cce_audio_completo_merged.wav"

if not os.path.exists(audio_path):
    print(f"File {audio_path} not found!")
    sys.exit(1)

with open(audio_path, "rb") as f:
    wav_data = f.read()[:5000000] # Take first 5MB just to be safe and fast

definition = '{"locales":["pt-BR"],"profanityFilterMode":"None","diarization":{"enabled":true,"maxSpeakers":2}}'

body = (
    f"--{boundary}\r\n"
    f'Content-Disposition: form-data; name="audio"; filename="audio.wav"\r\n'
    f"Content-Type: audio/wav\r\n\r\n"
).encode('utf-8') + wav_data + (
    f"\r\n--{boundary}\r\n"
    f'Content-Disposition: form-data; name="definition"\r\n'
    f"Content-Type: application/json\r\n\r\n"
    f"{definition}\r\n"
    f"--{boundary}--\r\n"
).encode('utf-8')

req = urllib.request.Request(url, data=body, headers={
    'Ocp-Apim-Subscription-Key': key,
    'Content-Type': f'multipart/form-data; boundary={boundary}'
}, method='POST')

try:
    with urllib.request.urlopen(req) as response:
        with open("payload_output.json", "w", encoding='utf-8') as out:
            out.write(response.read().decode('utf-8'))
        print("Success")
except urllib.error.HTTPError as e:
    print(e.read().decode('utf-8'))
