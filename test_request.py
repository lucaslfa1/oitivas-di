import requests

url = "http://localhost:8000/process/audio"
files = {'file': ('test.txt', b'dummy content', 'audio/mpeg')}

try:
    response = requests.post(url, files=files)
    print(f"Status: {response.status_code}")
    print(f"Response: {response.text}")
except Exception as e:
    print(f"Error: {e}")
