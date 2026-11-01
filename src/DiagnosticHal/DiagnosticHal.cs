using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using AuraLink.Core;

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: ComVisible(false)]

namespace AuraLink.DiagnosticHal
{
    internal static class Status
    {
        public const int Ok = 0;
        public const int InvalidArgument = unchecked((int)0x80070057);
        public const int Bounds = unchecked((int)0x8000000B);
        public const int Failure = unchecked((int)0x80004005);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DiagnosticDevice : IAacLedDevice
    {
        private readonly IEffectSink sink;
        public DiagnosticDevice(IEffectSink sink) { this.sink = sink; }
        public int GetCapability(out string capability)
        {
            capability = null;
            try
            {
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AuraLink.Capability.xml"))
                using (var reader = new StreamReader(resource)) capability = reader.ReadToEnd();
                sink.Record("GetCapability", new { returned = true });
                return Status.Ok;
            }
            catch { capability = null; return Status.Failure; }
        }
        public int SetEffect(uint effectId, IntPtr colors, uint numberOfColors)
        {
            if (numberOfColors > 256 || (numberOfColors > 0 && colors == IntPtr.Zero)) return Status.InvalidArgument;
            try
            {
                uint[] values = new uint[numberOfColors];
                for (int i=0; i<values.Length; i++) values[i] = unchecked((uint)Marshal.ReadInt32(colors, i*4));
                sink.Record("SetEffect", new { effectId = effectId, colors = values });
                return Status.Ok;
            }
            catch { return Status.Failure; }
        }
        public int Synchronize(uint effectId, ulong milliseconds)
        {
            try { sink.Record("Synchronize", new { effectId = effectId, milliseconds = milliseconds }); return Status.Ok; }
            catch { return Status.Failure; }
        }
    }

    [ComVisible(true), Guid("9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6"),
     ProgId("AuraLink.DiagnosticHal.1"), ClassInterface(ClassInterfaceType.None),
     ComDefaultInterface(typeof(IAacLedDeviceHal))]
    public sealed class DiagnosticHal : IAacLedDeviceHal
    {
        private readonly IEffectSink sink;
        private readonly DiagnosticDevice device;
        public DiagnosticHal()
        {
            sink = new JsonLineSink(Path.Combine(Path.GetDirectoryName(typeof(DiagnosticHal).Assembly.Location), "logs"));
            device = new DiagnosticDevice(sink);
            sink.Record("Activated", new { classId = typeof(DiagnosticHal).GUID.ToString(), diagnostic = true });
        }
        public int Enumerate(IntPtr devices, ref uint count)
        {
            uint capacity = count;
            count = 1;
            try
            {
                sink.Record("Enumerate", new { capacity = capacity, countOnly = devices == IntPtr.Zero });
                if (devices == IntPtr.Zero) return Status.Ok;
                if (capacity < 1) return Status.Bounds;
                // The recipient owns this reference; do not release it here.
                IntPtr pointer = Marshal.GetComInterfaceForObject(device, typeof(IAacLedDevice));
                try { Marshal.WriteIntPtr(devices, pointer); }
                catch { Marshal.Release(pointer); throw; }
                return Status.Ok;
            }
            catch { return Status.Failure; }
        }
        public int Enumerate2(IntPtr devices, ref uint count)
        {
            uint capacity = count;
            count = 1;
            IntPtr array = IntPtr.Zero;
            try
            {
                sink.Record("Enumerate2", new { capacity = capacity, countOnly = capacity == 0 });
                if (capacity == 0) return Status.Ok;
                if (devices == IntPtr.Zero) return Status.InvalidArgument;
                array = SafeArrayCreateVector(13, 0, 1); // VT_UNKNOWN
                if (array == IntPtr.Zero) return unchecked((int)0x8007000E);
                int index = 0;
                IntPtr pointer = Marshal.GetComInterfaceForObject(device, typeof(IAacLedDevice));
                try { Marshal.ThrowExceptionForHR(SafeArrayPutElement(array, ref index, pointer)); }
                finally { Marshal.Release(pointer); }
                VariantInit(devices);
                Marshal.WriteInt16(devices, unchecked((short)0x200D)); // VT_ARRAY | VT_UNKNOWN
                Marshal.WriteIntPtr(devices, 8, array);
                array = IntPtr.Zero; // Recipient owns SAFEARRAY via VARIANT.
                return Status.Ok;
            }
            catch { return Status.Failure; }
            finally { if (array != IntPtr.Zero) SafeArrayDestroy(array); }
        }
        [DllImport("oleaut32.dll")] private static extern IntPtr SafeArrayCreateVector(ushort vt, int lower, uint count);
        [DllImport("oleaut32.dll")] private static extern int SafeArrayPutElement(IntPtr array, ref int index, IntPtr value);
        [DllImport("oleaut32.dll")] private static extern int SafeArrayDestroy(IntPtr array);
        [DllImport("oleaut32.dll")] private static extern void VariantInit(IntPtr variant);
    }
}
