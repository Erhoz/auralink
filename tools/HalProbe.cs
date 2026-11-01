using System;
using System.Runtime.InteropServices;

namespace AuraLink.Probe
{
    [ComImport, Guid("F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ILedDeviceHal
    {
        // Marker for QueryInterface only. The ASUS TLB is insufficient evidence
        // for the method ABI of Corsair's independent implementation.
    }
    public sealed class HalResult
    {
        public string ClassId;
        public int ActivationHResult;
        public bool EnumerationAttempted = false;
        public bool SupportsHalInterface;
    }
    public static class HalProbe
    {
        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out ILedDeviceHal result);
        public static HalResult Read(string classId)
        {
            var result = new HalResult { ClassId = classId };
            var clsid = new Guid(classId);
            var iid = typeof(ILedDeviceHal).GUID;
            ILedDeviceHal hal;
            result.ActivationHResult = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out hal);
            result.SupportsHalInterface = result.ActivationHResult >= 0 && hal != null;
            if (hal != null) Marshal.ReleaseComObject(hal);
            return result;
        }
    }
}
