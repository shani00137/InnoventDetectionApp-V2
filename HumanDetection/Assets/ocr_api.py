import subprocess
import sys

# Auto-install packages if missing
required_packages = [
    "Flask",
    "easyocr",
    "torch",
    "torchvision",
    "torchaudio",
    "pyzbar",
    "Pillow"
]
for package in required_packages:
    try:
        __import__(package if package != "Pillow" else "PIL")
    except ImportError:
        subprocess.check_call([sys.executable, "-m", "pip", "install", package])

import io
import time
import warnings
import contextlib
from flask import Flask, request, jsonify
import easyocr
from pyzbar.pyzbar import decode
from PIL import Image

warnings.filterwarnings("ignore", category=UserWarning)

app = Flask(__name__)

# Initialize EasyOCR (English + Arabic, GPU enabled if available)
with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
    reader = easyocr.Reader(['en', 'ar'], gpu=True)

SUPPORTED_EXTENSIONS = (".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp")


@app.route("/ocr", methods=["POST"])
def ocr_endpoint():
    start_time = time.time()
    files = request.files.getlist("images")

    if not files or all(f.filename == '' for f in files):
        return jsonify({"error": "No valid images uploaded. Use key 'images'"}), 400

    if len(files) > 10:
        return jsonify({"error": "Maximum 10 images allowed"}), 400

    output = {
        "images_processed": 0,
        "ocr_texts": [],
        "barcodes": [],
        "processing_time_sec": 0
    }

    for file in files:
        if not file or not file.filename:
            continue

        img_bytes = file.read()
        if not img_bytes:
            continue

        # Barcode decoding
        try:
            img = Image.open(io.BytesIO(img_bytes))
            barcodes = decode(img)
            barcode_data = [b.data.decode('utf-8', errors='ignore') for b in barcodes]
        except Exception:
            barcode_data = []

        output["barcodes"].append(barcode_data)

        # OCR using EasyOCR (English + Arabic)
        try:
            results = reader.readtext(img_bytes, paragraph=True)
            detected_text = " ".join([text for (_, text) in results])
        except Exception:
            detected_text = ""

        output["ocr_texts"].append(detected_text)
        output["images_processed"] += 1

    output["processing_time_sec"] = round(time.time() - start_time, 2)
    return jsonify(output)


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=9000, debug=False, threaded=True)
