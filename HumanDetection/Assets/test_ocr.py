import cv2
import pytesseract
import numpy as np
from PIL import Image
import os

# Set Tesseract path explicitly (adjust if needed)
pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

def test_installation():
    print("Testing OpenCV...")
    print(f"OpenCV version: {cv2.__version__}")
    
    # Create a test image with text
    img = np.ones((100, 400, 3), dtype=np.uint8) * 255
    cv2.putText(img, 'TEST OCR 123', (50, 50), 
                cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 0), 2)
    
    # Save test image
    cv2.imwrite('test_image.jpg', img)
    print("Created test_image.jpg")
    
    # Test Tesseract
    try:
        text = pytesseract.image_to_string(img)
        print(f"Tesseract test output: {text.strip()}")
    except Exception as e:
        print(f"Tesseract error: {e}")
        print("Check Tesseract installation and path!")
    
    # Test basic image operations
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    print(f"Image shape: {gray.shape}")
    
    # Clean up
    if os.path.exists('test_image.jpg'):
        os.remove('test_image.jpg')
    
    return True

def simple_ocr_example(image_path):
    """Simple OCR for text on boxes"""
    if not os.path.exists(image_path):
        print(f"Image {image_path} not found!")
        return
    
    # Read image
    img = cv2.imread(image_path)
    if img is None:
        print(f"Could not read image: {image_path}")
        return
    
    # Convert to grayscale
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    
    # Apply threshold
    _, thresh = cv2.threshold(gray, 150, 255, cv2.THRESH_BINARY_INV)
    
    # Find contours (potential text areas)
    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    
    results = []
    for i, contour in enumerate(contours):
        x, y, w, h = cv2.boundingRect(contour)
        
        # Filter small regions (adjust based on your image)
        if w > 50 and h > 20:
            roi = gray[y:y+h, x:x+w]
            
            # OCR the region
            text = pytesseract.image_to_string(roi, config='--psm 6').strip()
            
            if text:
                results.append({
                    'id': i,
                    'text': text,
                    'area': (x, y, w, h)
                })
                
                # Draw rectangle on original image
                cv2.rectangle(img, (x, y), (x+w, y+h), (0, 255, 0), 2)
                cv2.putText(img, f"{i}: {text[:15]}", (x, y-5), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 255), 1)
    
    # Save result
    if results:
        output_path = 'ocr_result.jpg'
        cv2.imwrite(output_path, img)
        print(f"Results saved to {output_path}")
    
    return results

if __name__ == "__main__":
    print("=== OCR Installation Test ===\n")
    
    # Test basic installation
    test_installation()
    
    print("\n=== Simple OCR Demo ===\n")
    
    # Example usage
    # Replace 'your_image.jpg' with your actual image
    if os.path.exists('your_image.jpg'):
        results = simple_ocr_example('your_image.jpg')
        if results:
            print(f"Found {len(results)} text regions:")
            for r in results:
                print(f"  Region {r['id']}: '{r['text']}'")
    else:
        print("Create an image named 'your_image.jpg' or modify the script")
        print("To test with your own image:")
        print("1. Take a photo of boxes on pallet")
        print("2. Save as 'pallet_image.jpg' in current directory")
        print("3. Run: python pallet_ocr.py")