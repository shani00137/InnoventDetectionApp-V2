import sys
from doctr.io import DocumentFile
from doctr.models import ocr_predictor

def run_ocr(image_path):
    # Load pretrained model
    model = ocr_predictor(pretrained=True)

    # Load image
    doc = DocumentFile.from_images(image_path)

    # Run OCR
    result = model(doc)

    # Extract plain text
    plain_text = []
    for page in result.export()["pages"]:
        for block in page["blocks"]:
            for line in block["lines"]:
                words = [w["value"] for w in line["words"]]
                if words:
                    plain_text.append(" ".join(words))

    return "\n".join(plain_text)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Please provide image path")
    else:
        image_path = sys.argv[1]
        text = run_ocr(image_path)
        print(text)
