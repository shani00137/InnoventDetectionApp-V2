using System;
using zkemkeeper;

namespace ACGPUIO
{
    public class AccessController
    {
        //private CZKEMClass axCZKEM;

        //public bool Connect(string ip, int port)
        //{
        //    axCZKEM = new CZKEMClass();
        //    var response = axCZKEM.Connect_Net(ip, port);
        //    if (axCZKEM.Connect_Net(ip, port))
        //    {
        //        Console.WriteLine("✅ Connected to ZKTeco inBio device.");

        //        // Example: trigger light connected to relay 1 for 2 seconds
        //        TriggerLight(1, 2000);
        //        return true;
        //    }
        //    else
        //    {
        //        Console.WriteLine("❌ Connection failed.");
        //        return false;
        //    }
        //}

        //private void TriggerLight(int outputPort, int durationMs)
        //{
        //    try
        //    {
        //        // Depending on the SDK version, one of these works:

        //        // OPTION 1 (most common for inBio)
        //        axCZKEM.SetWorkCode(1, outputPort);
        //        // Parameters: (machineNumber, index, state, address, delay)

        //        // OPTION 2 (if ControlDevice not supported)
        //        // axCZKEM.SetDeviceWorkCode(1, outputPort, 1);

        //        Console.WriteLine($"💡 Light on port {outputPort} triggered for {durationMs}ms");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"⚠ Error triggering light: {ex.Message}");
        //    }
        //}

        //public void Disconnect()
        //{
        //    if (axCZKEM != null)
        //    {
        //        axCZKEM.Disconnect();
        //        Console.WriteLine("🔌 Disconnected from device.");
        //    }
        //}
    }
}
