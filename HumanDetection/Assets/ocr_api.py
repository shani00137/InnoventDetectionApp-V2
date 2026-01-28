import io
import time
import warnings
import contextlib

from flask import Flask, request, jsonify
from PIL import Image
import easyocr
import numpy as np
import cv2

warnings.filterwarnings("ignore", category=UserWarning)

app = Flask(__name__)

# Initialize EasyOCR (English + Arabic, GPU enabled if available)
with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
    reader = easyocr.Reader(['en'], gpu=True)

SUPPORTED_EXTENSIONS = (".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp")


# ---------------- Preprocess image for OCR ----------------
def preprocess(img_bytes: bytes) -> np.ndarray:
    img = Image.open(io.BytesIO(img_bytes)).convert("L")
    img = np.array(img)

    # Upscale small text
    img = cv2.resize(img, None, fx=2, fy=2, interpolation=cv2.INTER_CUBIC)

    # Remove noise and enhance text
    img = cv2.adaptiveThreshold(
        img,
        255,
        cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
        cv2.THRESH_BINARY,
        31,
        11
    )

    # Sharpen
    kernel = np.array([[0, -1, 0], [-1, 5, -1], [0, -1, 0]])
    img = cv2.filter2D(img, -1, kernel)

    # Convert back to RGB (EasyOCR expects 3 channels)
    img = cv2.cvtColor(img, cv2.COLOR_GRAY2RGB)
    return img


# ---------------- OCR Endpoint ----------------
@app.route("/ocr", methods=["POST"])
def ocr_endpoint():
    start_time = time.time()
    files = request.files.getlist("images")

    if not files or all(f.filename == "" for f in files):
        return jsonify({"error": "No images uploaded. Use key 'images'"}), 400

    if len(files) > 10:
        return jsonify({"error": "Maximum 10 images allowed"}), 400

    response = {
        "images_processed": 0,
        "ocr_texts": [],
        "processing_time_sec": 0
    }

    for file in files:
        if not file or not file.filename.lower().endswith(SUPPORTED_EXTENSIONS):
            response["ocr_texts"].append("")
            continue

        img_bytes = file.read()
        if not img_bytes:
            response["ocr_texts"].append("")
            continue

        try:
            processed_img = preprocess(img_bytes)
            results = reader.readtext(
                processed_img,
                paragraph=False,
                text_threshold=0.3,
                low_text=0.2
            )
            detected_text = " ".join([t for (_, t, conf) in results if conf > 0.5])
        except Exception:
            detected_text = ""

        response["ocr_texts"].append(detected_text)
        response["images_processed"] += 1

    response["processing_time_sec"] = round(time.time() - start_time, 2)
    return jsonify(response)


# ---------------- Run Flask ----------------
if __name__ == "__main__":
    app.run(host="0.0.0.0", port=9000, debug=False, threaded=True)
