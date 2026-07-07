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
from werkzeug.datastructures.mixins import V

app = Flask(__name__)

# -----------------------------
# CONFIG
# -----------------------------
MAX_SIZE = 1024
DATE_REGEX = r'''(?xi)
    \b
    (?:
        # FORMAT 1: DD.MM.YYYY  DD/MM/YYYY  DD-MM-YYYY  DD MM YYYY
        (?:0?[1-9]|[12]\d|3[01])
        [.\-/\s]
        (?:0?[1-9]|1[0-2])
        [.\-/\s]
        (?:19|20)\d{2}

        |
        # FORMAT 2: YYYY.MM.DD  YYYY/MM/DD  YYYY-MM-DD
        (?:19|20)\d{2}
        [.\-/]
        (?:0?[1-9]|1[0-2])
        [.\-/]
        (?:0?[1-9]|[12]\d|3[01])

        |
        # FORMAT 3: DD.MM.YY  DD/MM/YY  DD-MM-YY
        (?:0?[1-9]|[12]\d|3[01])
        [.\-/]
        (?:0?[1-9]|1[0-2])
        [.\-/]
        \d{2}

        |
        # FORMAT 4: MM/DD/YYYY  MM-DD-YYYY  (US style)
        (?:0?[1-9]|1[0-2])
        [/\-]
        (?:0?[1-9]|[12]\d|3[01])
        [/\-]
        (?:19|20)\d{2}

        |
        # FORMAT 5: DD MMM YYYY  e.g. 15 JAN 2024  15-JAN-2024
        (?:0?[1-9]|[12]\d|3[01])
        [\s.\-/]?
        (?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)
        [\s.\-/]?
        (?:19|20)\d{2}

        |
        # FORMAT 6: MMM DD YYYY  e.g. JAN 15 2024
        (?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)
        [\s.\-/]?
        (?:0?[1-9]|[12]\d|3[01])
        [\s.\-/]?
        (?:19|20)\d{2}

        |
        # FORMAT 7: YYYY MMM DD  e.g. 2024 JAN 15
        (?:19|20)\d{2}
        [\s.\-/]?
        (?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)
        [\s.\-/]?
        (?:0?[1-9]|[12]\d|3[01])

        |
        # FORMAT 8: DDMMYYYY compact (no separator)
        (?:0[1-9]|[12]\d|3[01])
        (?:0[1-9]|1[0-2])
        (?:19|20)\d{2}

        |
        # FORMAT 9: YYYYMMDD ISO compact (no separator)
        (?:19|20)\d{2}
        (?:0[1-9]|1[0-2])
        (?:0[1-9]|[12]\d|3[01])

        |
        # FORMAT 10: MM/YY  MM.YY  MM-YY  (expiry shorthand on labels)
        (?:0?[1-9]|1[0-2])
        [.\-/]
        \d{2}

        |
        # FORMAT 11: MM/YYYY  MM.YYYY  (longer expiry shorthand)
        (?:0?[1-9]|1[0-2])
        [.\-/]
        (?:19|20)\d{2}
    )
    \b
'''

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

# ✅ SAFE GPU WARM-UP
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
    if len(img.shape) == 2:
        return cv2.cvtColor(img, cv2.COLOR_GRAY2RGB)
    return img


def extract_barcodes(img_rgb):
    barcodes = []
    for obj in decode(img_rgb):
        barcodes.append(obj.data.decode("utf-8"))
    return barcodes


def extract_possible_barcodes(texts):
    barcodes = []

    for t in texts:
        cleaned = re.sub(r'\D', '', t)

        if len(cleaned) >= 6:
            barcodes.append(cleaned)

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

    # -----------------------------
    # BARCODE DETECTION
    # -----------------------------

    # Barcodes from image
    barcodes_image = extract_barcodes(img_np)

    # Barcodes from OCR text
    barcodes_text = extract_possible_barcodes(texts)

    # Combine both
    barcodes = list(set(barcodes_image + barcodes_text))

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

    app.run(host="0.0.0.0", port=5000, debug=False, use_reloader=False)