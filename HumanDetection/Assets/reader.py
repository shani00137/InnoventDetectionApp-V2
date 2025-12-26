import sys
import os
import json
import easyocr
import cv2

# Initialize OCR once
reader = easyocr.Reader(['en','ar'])

def preprocess_image(path):
    img = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
    img = cv2.resize(img, (0,0), fx=0.5, fy=0.5)  # downscale for speed
    return imga

def process_folder(folder):
    results = {}
    for file in os.listdir(folder):
        if file.lower().endswith(('.png','.jpg','.jpeg')):
            path = os.path.join(folder, file)
            img = preprocess_image(path)
            text = " ".join(reader.readtext(img, detail=0))
            results[file] = text
    return results

# Persistent loop
while True:
    line = sys.stdin.readline()
    if not line:
        break
    folder_path = line.strip()
    if os.path.exists(folder_path):
        res = process_folder(folder_path)
        print(json.dumps(res, ensure_ascii=False))
        sys.stdout.flush()
