import requests

url = 'http://localhost:8051/login'
data = {'username': 'Analista', 'password': 'Analista@2026'}

try:
    response = requests.post(url, data=data, allow_redirects=False)
    print(f"Status Code: {response.status_code}")
    print(f"Headers: {response.headers}")
    if response.status_code == 302 and '/' in response.headers.get('Location', ''):
        print("✅ Login Successful! Redirected to /")
    else:
        print("❌ Login Failed or Unexpected Response")
        print(response.text)
except Exception as e:
    print(f"❌ Error: {e}")
