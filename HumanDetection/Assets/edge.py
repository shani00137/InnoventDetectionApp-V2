from flask import Flask, request, jsonify
import cv2
import numpy as np
import onnxruntime as ort

app = Flask(__name__)

# Load ONNX model
session = ort.InferenceSession("yolov5.onnx")
input_name = session.get_inputs()[0].name

# Preprocess image for YOLOv5
def preprocess(img):
    img_resized = cv2.resize(img, (640, 640))
    img_rgb = cv2.cvtColor(img_resized, cv2.COLOR_BGR2RGB)
    img_norm = img_rgb / 255.0
    img_transposed = np.transpose(img_norm, (2, 0, 1))
    return np.expand_dims(img_transposed.astype(np.float32), axis=0)

# Simple NMS (optional improvement later)
def get_best_box(predictions):
    # assuming [x, y, w, h, conf, class]
    pred = predictions[0]
    pred = pred[pred[:, 4] > 0.5]

    if len(pred) == 0:
        return None

    # take highest confidence
    return pred[np.argmax(pred[:, 4])]

# Angle detection using OpenCV
def get_angle(crop):
    gray = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
    edges = cv2.Canny(gray, 50, 150)

    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    if not contours:
        return None

    cnt = max(contours, key=cv2.contourArea)
    rect = cv2.minAreaRect(cnt)
    angle = rect[-1]

    # normalize
    if angle < -45:
        angle = 90 + angle

    return angle

@app.route("/detect", methods=["POST"])
def detect():
    file = request.files["image"]
    img = cv2.imdecode(np.frombuffer(file.read(), np.uint8), cv2.IMREAD_COLOR)

    h, w = img.shape[:2]

    # YOLO inference
    input_tensor = preprocess(img)
    outputs = session.run(None, {input_name: input_tensor})

    box = get_best_box(outputs)

    if box is None:
        return jsonify({"error": "No pallet detected"})

    # Convert to original scale
    x, y, bw, bh = box[:4]

    x1 = int((x - bw/2) * w / 640)
    y1 = int((y - bh/2) * h / 640)
    x2 = int((x + bw/2) * w / 640)
    y2 = int((y + bh/2) * h / 640)

    crop = img[y1:y2, x1:x2]

    angle = get_angle(crop)

    if angle is None:
        return jsonify({"error": "Angle detection failed"})

    # Alignment logic
    aligned = abs(angle) <= 5 or abs(angle - 90) <= 5

    if aligned:
        rotation = "none"
    elif angle > 0:
        rotation = "rotate_left"
    else:
        rotation = "rotate_right"

    return jsonify({
        "angle": float(angle),
        "aligned": aligned,
        "rotation": rotation
    })

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000)