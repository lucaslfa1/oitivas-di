import requests
import wave
import struct
import math
import os

def create_sine_wave(filename, duration=1.0, freq=440.0):
    sample_rate = 44100
    n_samples = int(sample_rate * duration)
    
    with wave.open(filename, 'w') as obj:
        obj.setnchannels(1) # mono
        obj.setsampwidth(2) # 2 bytes (16 bit)
        obj.setframerate(sample_rate)
        
        for i in range(n_samples):
            value = int(32767.0*0.5*math.sin(2.0*math.pi*freq*i/sample_rate))
            data = struct.pack('<h', value)
            obj.writeframesraw(data)

def test_python_endpoint():
    print("\n--- Testing Python Endpoint (Direct) ---")
    url = "http://localhost:8000/process/merge-audio"
    
    files = [
        ('files', ('part1.wav', open('part1.wav', 'rb'), 'audio/wav')),
        ('files', ('part2.wav', open('part2.wav', 'rb'), 'audio/wav')),
        ('files', ('part3.wav', open('part3.wav', 'rb'), 'audio/wav'))
    ]
    
    try:
        response = requests.post(url, files=files)
        if response.status_code == 200:
            print("SUCCESS! Output saved to merged_python.json")
            with open("merged_python.json", "w") as f:
                f.write(response.text)
        else:
            print(f"FAILED: {response.status_code} - {response.text}")
    except Exception as e:
        print(f"ERROR: {e}")
        print("Is the Python server running on port 8000?")

def test_csharp_endpoint():
    print("\n--- Testing C# Endpoint (API) ---")
    url = "http://localhost:5252/api/tools/merge-audio"
    
    # Note: C# accepts 'files' as key due to IFormFileCollection binding default handling or just multiple parts
    # We used [FromForm] IFormFileCollection which binds to request.Form.Files
    files = [
        ('files', ('part1.wav', open('part1.wav', 'rb'), 'audio/wav')),
        ('files', ('part2.wav', open('part2.wav', 'rb'), 'audio/wav')),
        ('files', ('part3.wav', open('part3.wav', 'rb'), 'audio/wav'))
    ]
    
    try:
        response = requests.post(url, files=files)
        if response.status_code == 200:
            print("SUCCESS! Output saved to merged_csharp.wav")
            with open("merged_csharp.wav", "wb") as f:
                f.write(response.content)
            print(f"Received {len(response.content)} bytes")
        else:
            print(f"FAILED: {response.status_code} - {response.text}")
    except Exception as e:
        print(f"ERROR: {e}")
        print("Is the C# server running on port 5252?")

if __name__ == "__main__":
    print("Generating dummy audio files...")
    create_sine_wave("part1.wav", duration=1.0, freq=440) # A4
    create_sine_wave("part2.wav", duration=1.0, freq=554) # C#5
    create_sine_wave("part3.wav", duration=1.0, freq=659) # E5
    
    test_python_endpoint()
    test_csharp_endpoint()
