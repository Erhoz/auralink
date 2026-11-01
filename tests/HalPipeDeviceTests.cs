using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AuraLink.Core;
using AuraLink.DiagnosticHal;

internal static class HalPipeDeviceTests
{
    private sealed class UnwritableSink : IEffectSink
    {
        public int Calls;
        public void Record(string operation,object data) { Calls++; throw new IOException("Injected unavailable diagnostic disk"); }
    }
    private static int Main()
    {
        try
        {
            string name="AuraLink.DeviceTest."+Guid.NewGuid().ToString("N");
            using(var publisher=new FramePipePublisher(name))
            using(var client=new FramePipeClient(name,Process.GetCurrentProcess().Id,1000))
            {
                var broken=new UnwritableSink();
                var device=new DiagnosticDevice(new BestEffortSink(broken),publisher);
                string capability;
                if(device.GetCapability(out capability)!=0 || String.IsNullOrEmpty(capability)) throw new Exception("Optional diagnostic disk blocked capabilities");
                IntPtr colors=Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(colors,unchecked((int)0xff0000ff));
                    if(device.SetEffect(0,colors,1)!=0 || device.Synchronize(0,100)!=0) throw new Exception("Device rejected frame or sync");
                }
                finally { Marshal.FreeHGlobal(colors); }
                NativeFrame frame=client.Poll();
                if(frame==null || frame.Color!=0xff0000ff || broken.Calls!=1) throw new Exception("Frame path depended on diagnostic IO");
            }
            Console.WriteLine("{\"passed\":true,\"processBits\":"+(IntPtr.Size*8)+",\"unwritableDiagnosticsTest\":true,\"realPipe\":true,\"hardwareAccess\":false}");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 2; }
    }
}
