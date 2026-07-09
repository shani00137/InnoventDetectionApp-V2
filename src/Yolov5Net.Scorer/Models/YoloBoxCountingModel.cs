using System.Collections.Generic;
using Yolov5Net.Scorer.Models.Abstract;

namespace Yolov5Net.Scorer.Models;

public record YoloBoxCountingModel() : YoloModel(
    640,
    640,
    3,
    8,
    new[] { 8, 16, 32 },
    new[]
    {
        new[] { new[] { 10, 13 }, new[] { 16, 30 }, new[] { 33, 23 } },
        new[] { new[] { 30, 61 }, new[] { 62, 45 }, new[] { 59, 119 } },
        new[] { new[] { 116, 90 }, new[] { 156, 198 }, new[] { 373, 326 } }
    },
    new[] { 80, 40, 20 },
    0.20f,
    0.25f,
    0.45f,
    new[] { "output0" },
    new()
    {
        new(0, "12"),
        new(1, "box"),
        new(2, "pallet")
    },
    true
);
