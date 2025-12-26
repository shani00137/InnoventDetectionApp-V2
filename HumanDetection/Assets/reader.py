import sys
import os
import json
from doctr.io import DocumentFile
from doctr.models import ocr_predictor

# Arabic + English friendly:
# - det_arch: "db_resnet50" is a solid general detector
# - reco_arch: "sar_resnet31" supports Arabic well (also works for many Latin cases)
MODEL = ocr_predictor(
    det_arch="db_resnet50",
    reco_arch="sar_resnet31",
    pretrained=True
)

def ocr_single_image(image_path: str) -> str:
    doc = DocumentFile.from_images(image_path)
    result = MODEL(doc)

    lines_out = []
    exported = result.export()

    for page in exported.get("pages", []):
        for block in page.get("blocks", []):
            for line in block.get("lines", []):
                words = [w.get("value", "") for w in line.get("words", [])]
                words = [w for w in words if w]
                if words:
                    lines_out.append(" ".join(words))

    return "\n".join(lines_out).strip()

def ocr_folder(folder_path: str) -> dict:
    exts = (".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp")
    results = {}

    if not os.path.isdir(folder_path):
        return {"__error__": f"Folder not found: {folder_path}"}

    files = sorted(
        f for f in os.listdir(folder_path)
        if f.lower().endswith(exts)
    )

    for fname in files:
        fpath = os.path.join(folder_path, fname)
        try:
            results[fname] = ocr_single_image(fpath)
        except Exception as e:
            results[fname] = f"__error__: {str(e)}"

    return results

def main():
    for line in sys.stdin:
        folder = line.strip().strip('"')
        if not folder:
            continue

        try:
            out = ocr_folder(folder)
        except Exception as e:
            out = {"__error__": str(e)}

        sys.stdout.write(json.dumps(out, ensure_ascii=False) + "\n")
        sys.stdout.flush()

if __name__ == "__main__":
    main()
