from flask import Flask, request, jsonify
from paddleocr import PaddleOCR
from pyzbar.pyzbar import decode
import cv2
import numpy as np
import tempfile
import os
import re

app = Flask(__name__)

# -------------------------------
# Initialize OCR ONCE
# -------------------------------
ocr = PaddleOCR(
    use_gpu=False,            # set True if GPU is available
    lang="en",
    use_angle_cls=True,
    show_log=False
)

# -------------------------------
# Regex
# -------------------------------
DATE_REGEX = r"\b\d{2}[./-]\d{2}[./-]\d{4}\b"
BARCODE_REGEX = r"\b\d{8,14}\b"


# -------------------------------
# Helpers
# -------------------------------
def extract_dates(ocr_items):
    dates = []
    for item in ocr_items:
        matches = re.findall(DATE_REGEX, item["text"])
        for m in matches:
            dates.append({
                "value": m,
                "confidence": item["confidence"]
            })
    return dates


def extract_barcodes_from_ocr(ocr_items):
    barcodes = []
    for item in ocr_items:
        text = item["text"].replace(" ", "")
        if re.fullmatch(BARCODE_REGEX, text):
            barcodes.append({
                "value": text,
                "confidence": item["confidence"],
                "source": "ocr"
            })
    return barcodes


def extract_barcodes_from_image(image_path):
    barcodes = []
    img = cv2.imread(image_path)

    for b in decode(img):
        barcodes.append({
            "value": b.data.decode("utf-8"),
            "type": b.type,
            "source": "pyzbar"
        })
    return barcodes


def run_paddle_ocr(image_path):
    result = ocr.ocr(image_path, cls=True)

    if not result or not result[0]:
        return []

    items = []
    for line in result[0]:
        items.append({
            "text": line[1][0],
            "confidence": float(line[1][1]),
            "box": line[0]
        })

    return items


# -------------------------------
# API
# -------------------------------
@app.route("/ocr", methods=["POST"])
def ocr_api():
    if "image" not in request.files:
        return jsonify({"error": "image file missing"}), 400

    image_file = request.files["image"]

    with tempfile.NamedTemporaryFile(delete=False, suffix=".jpg") as tmp:
        image_path = tmp.name
        image_file.save(image_path)

    try:
        ocr_items = run_paddle_ocr(image_path)

        dates = extract_dates(ocr_items)
        barcodes = extract_barcodes_from_ocr(ocr_items)
        barcodes += extract_barcodes_from_image(image_path)

        return jsonify({
            "text_count": len(ocr_items),
            "dates": dates,
            "date_count": len(dates),
            "barcodes": barcodes,
            "barcode_count": len(barcodes),
            "raw_text": ocr_items
        })

    finally:
        os.remove(image_path)


# -------------------------------
# Run
# -------------------------------
if __name__ == "__main__":
    app.run(host="0.0.0.0", port=9000, debug=False)
