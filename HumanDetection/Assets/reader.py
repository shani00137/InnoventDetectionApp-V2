import sys
import easyocr
import warnings
import contextlib
import io

# Suppress all UserWarnings
warnings.filterwarnings("ignore", category=UserWarning)

def run_ocr(image_path):
    try:
        # Suppress stdout/stderr during EasyOCR initialization
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            reader = easyocr.Reader(['en'], gpu=False)

        results = reader.readtext(image_path)
        detected_text = " ".join([text for (_, text, _) in results])
        print(detected_text)  # C# can capture this output
    except Exception as e:
        print(f"Error during OCR: {e}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Error: No image path provided")
    else:
        image_path = sys.argv[1]
        run_ocr(image_path)
