using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yolov5Net.Scorer.Models.Abstract;

namespace Yolov5Net.Scorer.Models;

public record YoloCustomModel() : YoloModel(
    640, // width
    640, // height
    3,   // channels
    7,   // classes: Number of custom classes
    new[] { 8, 16, 32 }, // strides
    new[] // anchors
    {
        new[] { new[] { 10, 13 }, new[] { 16, 30 }, new[] { 33, 23 } },
        new[] { new[] { 30, 61 }, new[] { 62, 45 }, new[] { 59, 119 } },
        new[] { new[] { 116, 90 }, new[] { 156, 198 }, new[] { 373, 326 } }
    },
    new[] { 80, 40, 20 }, // outputs
    0.20f, // confidence
    0.25f, // mulConfidence
    0.45f, // overlap
    new[] { "output0" }, // outputNames
   new()
    {
        new(0, "box"),
        new(1, "pallet")

    },
    true // useDetect
);
