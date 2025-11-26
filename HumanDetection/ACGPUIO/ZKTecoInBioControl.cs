using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ACGPUIO
{

    public static class InBioDevice
    {
        [DllImport("plcommpro.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr Connect(string parameters);

        [DllImport("plcommpro.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern void Disconnect(IntPtr handle);

        public static IntPtr ConnectToDevice(string ip, int port = 4370, string password = "")
        {
            string connParams = $"protocol=TCP,ipaddress={ip},port={port},timeout=2000,passwd={password}";
            IntPtr handle = Connect(connParams);
            if (handle == IntPtr.Zero)
                throw new Exception("Failed to connect to device. Check IP, port, or password.");
            return handle;
        }

        public static void DisconnectDevice(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
                Disconnect(handle);
        }
    }

}