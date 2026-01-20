from flask import Flask, request, jsonify
from paddleocr import PaddleOCR
from PIL import Image
import numpy as np
import io
import re
import cv2
import time

app = Flask(__name__)

# Initialize PaddleOCR (GPU auto-detected)
ocr = PaddleOCR(use_textline_orientation=True, lang='en')

DATE_PATTERNS = [
    r'\b\d{2}[/-]\d{2}[/-]\d{4}\b',   # 12/01/2025
    r'\b\d{4}[/-]\d{2}[/-]\d{2}\b',   # 2025-01-12
    r'\b\d{2}[/-]\d{2}[/-]\d{2}\b',   # 12/01/25
    r'\b\d{1,2}\s?(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s?\d{2,4}\b'
]

def extract_dates(text):
    found = []
    for pattern in DATE_PATTERNS:
        found.extend(re.findall(pattern, text, re.IGNORECASE))
    return list(set(found))

def preprocess_image(img_bytes):
    img = np.array(Image.open(io.BytesIO(img_bytes)).convert("RGB"))
    img_gray = cv2.cvtColor(img, cv2.COLOR_RGB2GRAY)
    img_gray = cv2.resize(img_gray, None, fx=2, fy=2, interpolation=cv2.INTER_CUBIC)
    _, img_thresh = cv2.threshold(img_gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    kernel = np.array([[0, -1, 0], [-1, 5, -1], [0, -1, 0]])
    img_sharp = cv2.filter2D(img_thresh, -1, kernel)
    return img_sharp

@app.route("/ocr", methods=["POST"])
def ocr_api():
    start_time = time.time()
    files = request.files.getlist("images")
    if not files:
        return jsonify({"success": False, "error": "No images uploaded"}), 400

    global_text = []
    all_dates = []

    for file in files:
        img_bytes = file.read()
        if not img_bytes:
            continue

        img_preprocessed = preprocess_image(img_bytes)
        result = ocr.predict(img_preprocessed)

        image_text = []
        for line in result[0]:
            text = line[1][0]
            text = re.sub(r'[\u200b\u202f]', '', text)
            image_text.append(text)
            all_dates.extend(extract_dates(text))

        global_text.append(" ".join(image_text))

    response = {
        "success": True,
        "images_processed": len(files),
        "text": "\n".join(global_text),
        "dates": list(set(all_dates)),
        "processing_time_sec": round(time.time() - start_time, 2)
    }
    return jsonify(response)

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=9000, debug=False, threaded=True)
