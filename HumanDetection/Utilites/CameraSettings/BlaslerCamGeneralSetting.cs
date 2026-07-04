using System;

namespace Utilites.CameraSettings
{
    public enum CameraPosition
    {
        Front,
        Top,
        Left,
        Right,
        Back,
        Unknown
    }

    public static class CameraHelper
    {
        public static CameraPosition GetCameraPosition(string serial)
        {
            return serial switch
            {
                "25261336" => CameraPosition.Front, // Front camera
                "25261337" => CameraPosition.Top,
                "25523223" => CameraPosition.Right,// Top camera
                _ => CameraPosition.Unknown
            };
        }
    }

    public class BlaslerCamGeneralSetting
    {

    }
}