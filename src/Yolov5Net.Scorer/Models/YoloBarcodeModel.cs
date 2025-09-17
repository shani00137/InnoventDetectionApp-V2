using Yolov5Net.Scorer.Models.Abstract;

public record YoloBarcodeModel() : YoloModel(
    640,      // Width (matches ONNX input)
    640,      // Height (matches ONNX input)
    3,        // Channels (RGB)
    6,        // Dimensions (4 box coords + 1 conf + 1 class) - CRITICAL FIX!
    new[] { 8, 16, 32 },  // Strides

    // Anchors (keep your current values)
    new[]
    {
        new[] { new[] { 10, 13 }, new[] { 16, 30 }, new[] { 33, 23 } },
        new[] { new[] { 30, 61 }, new[] { 62, 45 }, new[] { 59, 119 } },
        new[] { new[] {116, 90 }, new[] {156,198 }, new[] { 373,326 } }
    },

    // Shapes for P5 model
    new[] { 80, 40, 20 },

    // Confidence thresholds (adjusted for barcode detection)
    0.30f,    // Confidence (higher for precise barcode reading)
    0.50f,    // Overlap (increased to reduce duplicate detections) 
    0.30f,    // MulConfidence

    // Output name (matches your ONNX)
    new[] { "output0" },

     // Single class
     new()
         {
            new(0, "barcode")
         },

    true      // Use detach
);