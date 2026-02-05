from flask import Flask, request, jsonify
import os
import tempfile
import time
from paddleocr import PaddleOCR
import paddle
from pyzbar.pyzbar import decode
from PIL import Image
import re

app = Flask(__name__)

# -----------------------------
# CONFIG
# -----------------------------
MAX_SIZE = 1024  # Maximum width/height for resizing
DATE_REGEX = r'\b\d{2}[./-]\d{2}[./-]\d{4}\b'  # DD/MM/YYYY or DD.MM.YYYY

# -----------------------------
# INITIALIZE OCR
# -----------------------------
ocr = PaddleOCR(use_textline_orientation=True, lang='en')

# -----------------------------
# GPU SETTINGS
# -----------------------------
paddle.set_device("gpu:0")
# Warm up GPU
ocr.predict([Image.new("RGB", (100, 100))])

# -----------------------------
# HELPER FUNCTIONS
# -----------------------------
def extract_dates(text):
    return re.findall(DATE_REGEX, text)

def extract_barcodes(image_path):
    barcodes = []
    with Image.open(image_path) as img:
        decoded_objects = decode(img)
        for obj in decoded_objects:
            barcodes.append(obj.data.decode('utf-8'))
    return barcodes

def resize_image(image_path):
    with Image.open(image_path) as img:
        w, h = img.size
        max_dim = max(w, h)
        if max_dim > MAX_SIZE:
            scale = MAX_SIZE / max_dim
            new_w = int(w * scale)
            new_h = int(h * scale)
            img_resized = img.resize((new_w, new_h), Image.Resampling.LANCZOS)
            tmp_resized = tempfile.NamedTemporaryFile(delete=False, suffix=".bmp")
            img_resized.save(tmp_resized.name)
            return tmp_resized.name
    return image_path

def process_image(image_path):
    start_time = time.time()
    resized_path = resize_image(image_path)
    temp_created = resized_path != image_path

    try:
        result = ocr.predict(resized_path)
    finally:
        if temp_created:
            try:
                os.remove(resized_path)
            except PermissionError:
                print(f"Could not delete temp file: {resized_path}, still in use")

    # OCR text collection
    all_texts = []
    for item in result:
        if isinstance(item, dict):
            all_texts.extend(item.get('rec_texts', []))
        elif isinstance(item, list):
            for entry in item:
                try:
                    _, (text, _) = entry
                    all_texts.append(text)
                except Exception:
                    pass

    all_dates = []
    for text in all_texts:
        all_dates.extend(extract_dates(text))

    all_barcodes = extract_barcodes(image_path)

    elapsed_time = round(time.time() - start_time, 3)

    return {
        "text_count": len(all_texts),
        "dates": all_dates,
        "date_count": len(all_dates),
        "barcodes": all_barcodes,
        "barcode_count": len(all_barcodes),
        "raw_text": all_texts,
        "processing_time_sec": elapsed_time
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

    start_total_time = time.time()

    for file in files:
        ext = os.path.splitext(file.filename)[1].lower()
        if ext not in [".jpg", ".jpeg", ".png", ".bmp"]:
            results[file.filename] = {"error": f"Unsupported file type: {ext}"}
            continue

        with tempfile.NamedTemporaryFile(delete=False, suffix=ext) as tmp:
            file.save(tmp.name)
            temp_file_path = tmp.name

        try:
            res = process_image(temp_file_path)
            results[file.filename] = res
        finally:
            try:
                os.remove(temp_file_path)
            except PermissionError:
                print(f"Could not delete temp file: {temp_file_path}, still in use")

    total_elapsed = time.time() - start_total_time
    total_time_sec = round(total_elapsed, 3)
    total_time_min = round(total_elapsed / 60, 2)

    return jsonify({
        "results": results,
        "total_images": len(files),
        "total_time_sec": total_time_sec,
        "total_time_min": total_time_min
    })

# -----------------------------
# RUN APP
# -----------------------------
if __name__ == "__main__":
    print("CUDA available:", paddle.is_compiled_with_cuda())
    app.run(host="0.0.0.0", port=5000, debug=True)
