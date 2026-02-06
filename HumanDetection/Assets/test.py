from flask import Flask, request, jsonify
import os
import tempfile
import time
import numpy as np
import cv2
import re

import paddle
from paddleocr import PaddleOCR
from pyzbar.pyzbar import decode

app = Flask(__name__)

# -----------------------------
# CONFIG
# -----------------------------
MAX_SIZE = 1024
DATE_REGEX = r'\b\d{2}[./-]\d{2}[./-]\d{4}\b'

# -----------------------------
# OCR INIT (GPU AUTO)
# -----------------------------
ocr = PaddleOCR(
    lang="en",
    use_textline_orientation=True
)

# -----------------------------
# GPU SETTINGS
# -----------------------------
paddle.set_device("gpu:0")

# ✅ SAFE GPU WARM-UP (NumPy, NOT PIL)
dummy = np.zeros((100, 100, 3), dtype=np.uint8)
ocr.ocr(dummy)

# -----------------------------
# HELPERS
# -----------------------------
def extract_dates(text):
    return re.findall(DATE_REGEX, text)


def resize_image_np(img):
    h, w = img.shape[:2]
    max_dim = max(h, w)
    if max_dim > MAX_SIZE:
        scale = MAX_SIZE / max_dim
        img = cv2.resize(img, (int(w * scale), int(h * scale)))
    return img


def mono8_to_rgb(img):
    """
    Convert MONO8 (H,W) → RGB (H,W,3)
    """
    if len(img.shape) == 2:
        return cv2.cvtColor(img, cv2.COLOR_GRAY2RGB)
    return img


def extract_barcodes(img_rgb):
    barcodes = []
    for obj in decode(img_rgb):
        barcodes.append(obj.data.decode("utf-8"))
    return barcodes


def process_image_np(img_np):
    start = time.time()

    # Ensure RGB
    img_np = mono8_to_rgb(img_np)

    # Resize for speed
    img_np = resize_image_np(img_np)

    # OCR
    result = ocr.ocr(img_np)

    # Collect text
    texts = []
    for item in result:
        if isinstance(item, dict):
            texts.extend(item.get("rec_texts", []))
        elif isinstance(item, list):
            for entry in item:
                try:
                    _, (text, _) = entry
                    texts.append(text)
                except Exception:
                    pass

    # Dates
    dates = []
    for t in texts:
        dates.extend(extract_dates(t))

    # Barcodes
    barcodes = extract_barcodes(img_np)

    elapsed = round(time.time() - start, 3)

    return {
        "text_count": len(texts),
        "raw_text": texts,
        "dates": dates,
        "date_count": len(dates),
        "barcodes": barcodes,
        "barcode_count": len(barcodes),
        "processing_time_sec": elapsed
    }

# -----------------------------
# FLASK ENDPOINT
# -----------------------------
@app.route("/ocr", methods=["POST"])
def ocr_endpoint():
    if "images" not in request.files:
        return jsonify({"error": "No images part in request"}), 400

    files = request.files.getlist("images")
    results = {}

    total_start = time.time()

    for file in files:
        suffix = os.path.splitext(file.filename)[1].lower()
        if suffix not in [".jpg", ".jpeg", ".png", ".bmp"]:
            results[file.filename] = {"error": "Unsupported file type"}
            continue

        with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
            file.save(tmp.name)
            path = tmp.name

        try:
            # Read as MONO or COLOR
            img = cv2.imread(path, cv2.IMREAD_UNCHANGED)
            if img is None:
                results[file.filename] = {"error": "Could not read image"}
                continue

            results[file.filename] = process_image_np(img)
        finally:
            os.remove(path)

    total_elapsed = time.time() - total_start

    return jsonify({
        "results": results,
        "total_images": len(files),
        "total_time_sec": round(total_elapsed, 3),
        "total_time_min": round(total_elapsed / 60, 3)
    })

# -----------------------------
# RUN
# -----------------------------
if __name__ == "__main__":
    print("CUDA available:", paddle.is_compiled_with_cuda())
    print("GPU count:", paddle.device.cuda.device_count())
    app.run(host="0.0.0.0", port=5000, debug=True)