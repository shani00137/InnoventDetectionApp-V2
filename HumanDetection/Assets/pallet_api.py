from flask import Flask, request, jsonify
from ultralytics import YOLO
from PIL import Image, ImageDraw, ImageFont
import os
import uuid

app = Flask(__name__)

# Load model
MODEL_PATH = "best.pt"
model = YOLO(MODEL_PATH)

CLASS_NAMES = model.names

TEMP_FOLDER = "temp"
os.makedirs(TEMP_FOLDER, exist_ok=True)


@app.route('/')
def home():
    return "Pallet Alignment API Running ✅"


@app.route('/predict', methods=['POST'])
def predict():
    if 'image' not in request.files:
        return jsonify({"error": "No image provided"}), 400

    image_file = request.files['image']

    try:
        image = Image.open(image_file.stream).convert('RGB')
    except Exception as e:
        return jsonify({"error": str(e)}), 400

    # Run model
    results = model.predict(image, conf=0.4, iou=0.5, verbose=False)

    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default()

    predictions = []

    # Image center
    img_w, img_h = image.size
    img_cx = img_w / 2

    # Alignment threshold
    THRESHOLD = 50

    # Draw center line (yellow)
    draw.line([(img_cx, 0), (img_cx, img_h)], fill="yellow", width=2)

    # Draw alignment zone (blue)
    zone_left = img_cx - THRESHOLD
    zone_right = img_cx + THRESHOLD
    draw.rectangle([zone_left, 0, zone_right, img_h], outline="blue", width=2)

    for r in results:
        if r.boxes is None:
            continue

        for box in r.boxes:
            cls = int(box.cls[0])
            class_name = CLASS_NAMES[cls]

            # Only pallet
            if class_name != "pallet":
                continue

            conf = float(box.conf[0])
            x1, y1, x2, y2 = box.xyxy[0].tolist()

            # Pallet center X
            cx = (x1 + x2) / 2

            # LEFT / RIGHT decision
            if cx < img_cx - THRESHOLD:
                instruction = "➡ Move Right"
            elif cx > img_cx + THRESHOLD:
                instruction = "⬅ Move Left"
            else:
                instruction = "✅ Aligned"

            predictions.append({
                "confidence": round(conf, 3),
                "center_x": round(cx, 2),
                "image_center_x": round(img_cx, 2),
                "instruction": instruction
            })

            # Draw pallet box (green)
            draw.rectangle([x1, y1, x2, y2], outline="green", width=4)

            # Draw instruction text
            draw.text((x1, y1 - 20), instruction, fill="green")

    # Save image
    filename = f"{uuid.uuid4().hex}.jpg"
    filepath = os.path.join(TEMP_FOLDER, filename)
    image.save(filepath)

    return jsonify({
        "total_pallets": len(predictions),
        "pallets": predictions,
        "result_image": filepath
    })


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5001, debug=True)