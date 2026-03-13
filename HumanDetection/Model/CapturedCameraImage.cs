using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media.Imaging;
using Utilites.CameraSettings;

namespace Model
{
    public class CapturedCameraImage
    {
        public CameraPosition Position { get; set; }  // Front, Top, Left, Right
        public BitmapImage Image { get; set; }        // Captured image
    }
}
