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

# ✅ SAFE GPU WARM-UP
dummy = np.zeros((100, 100, 3), dtype=np.uint8)
ocr.ocr(dummy)

# -----------------------------
# HELPERS
# -----------------------------
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
    try:
        for obj in decode(img_rgb):
            barcodes.append(obj.data.decode("utf-8", errors="ignore"))
    except Exception:
        pass  # barcode decoding is skipped when it fails — never a hard error
    return barcodes


def extract_dates(text):
    """Extracts dates from OCR text without missing any plausible date."""
    if not text:
        return []

    # OCR often reads date separators inconsistently (· | \ _ , ;).
    # Normalize them so dates with unusual separators are still found.
    normalized = re.sub(r"[·|\\_,;]", ".", text)
    normalized = re.sub(r"\s+", " ", normalized)

    found = [m for m in re.findall(DATE_REGEX, normalized) if m]

    # Broad fallback: catch compact/atypical numeric dates the primary regex
    # might skip, e.g. 5-10-2024 written with unusual spacing.
    for m in re.finditer(r"\b(\d{1,4})[-./](\d{1,2})[-./](\d{1,4})\b", normalized):
        raw = m.group(0)
        if raw in found:
            continue
        if likely_date(*m.groups()):
            found.append(raw)

    # De-duplicate while preserving order
    seen = set()
    unique = []
    for d in found:
        if d not in seen:
            seen.add(d)
            unique.append(d)

    return unique


def likely_date(a, b, c):
    """Checks whether a day/month/year triple looks like a real date."""
    parts = (a, b, c)
    short = [p for p in parts if len(p) in (1, 2)]
    long = [p for p in parts if len(p) == 4]

    # Case 1: YYYY-MM-DD or D-M-YYYY (one 4-digit year + two day/month fields)
    if len(long) == 1 and len(short) == 2:
        year = int(long[0])
        if not (1900 <= year <= 2100):
            return False
        if not all(1 <= int(p) <= 31 for p in short):
            return False
        return any(1 <= int(p) <= 12 for p in short)

    # Case 2: DD-MM-YY with a 2-digit year (all three fields are short)
    if len(short) == 3:
        if not all(1 <= int(p) <= 31 for p in short):
            return False
        return any(1 <= int(p) <= 12 for p in short) and any(int(p) >= 5 for p in short)

    return False


def _box_to_list(box):
    """Converts a PaddleOCR polygon to a JSON-safe list."""
    if box is None:
        return None
    try:
        return [[round(float(x), 1), round(float(y), 1)] for x, y in box]
    except Exception:
        return None


def process_image_np(img_np):
    start = time.time()

    # Ensure RGB
    img_np = mono8_to_rgb(img_np)

    # Resize for speed
    img_np = resize_image_np(img_np)

    # OCR
    result = ocr.ocr(img_np)

    # Collect text + per-line confidence + the raw PaddleOCR result (boxes too)
    texts = []
    details = []
    raw_result = []
    for item in result:
        if isinstance(item, dict):
            rec_texts = item.get("rec_texts", []) or []
            rec_scores = item.get("rec_scores", []) or []
            rec_boxes = (item.get("rec_boxes") or
                         item.get("dt_polys") or
                         item.get("det_polys") or [])
            texts.extend(rec_texts)
            for i, t in enumerate(rec_texts):
                conf = None
                if i < len(rec_scores):
                    try:
                        conf = round(float(rec_scores[i]), 4)
                    except Exception:
                        conf = None
                box = _box_to_list(rec_boxes[i]) if i < len(rec_boxes) else None
                details.append({"text": t, "confidence": conf})
                raw_result.append({"box": box, "text": t, "confidence": conf})
        elif isinstance(item, list):
            for entry in item:
                try:
                    box, (text, confidence) = entry
                    texts.append(text)
                    conf = round(float(confidence), 4)
                    details.append({"text": text, "confidence": conf})
                    raw_result.append({"box": _box_to_list(box), "text": text, "confidence": conf})
                except Exception:
                    pass

    # Dates (deduplicated across lines so nothing is missed twice or dropped)
    dates = []
    seen_dates = set()
    for t in texts:
        for d in extract_dates(t):
            if d not in seen_dates:
                seen_dates.add(d)
                dates.append(d)

    # -----------------------------
    # BARCODE DETECTION
    # -----------------------------

    # Only REAL decoded barcodes are returned. The human-readable text printed
    # under a barcode is OCR text, not a barcode — it is never counted here.
    # If the barcode is missing or not readable, the list is simply empty (skip).
    barcodes = list(set(extract_barcodes(img_np)))

    elapsed = round(time.time() - start, 3)

    return {
        "text_count": len(texts),
        "raw_text": texts,
        "details": details,
        "raw_result": raw_result,
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