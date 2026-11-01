using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AuraLink.ContractProbe
{
    // Only the common, statically confirmed first method is callable here.
    // Corsair 2.2.115 has a C++ destructor at slot 4, NOT Enumerate2.
    [ComImport, Guid("F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IHalV1
    {
        [PreserveSig] int Enumerate(IntPtr buffer, ref uint count);
    }
    [ComImport, Guid("61711778-AB59-4026-89E8-7A63422C29C2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceV1
    {
        [PreserveSig] int GetCapability([MarshalAs(UnmanagedType.BStr)] out string capability);
    }
    public sealed class Result
    {
        public int ActivationHResult;
        public int? CountHResult, FillHResult;
        public uint Count;
        public List<string> Capabilities = new List<string>();
    }
    public static class Reader
    {
        [DllImport("ole32.dll", PreserveSig=true)]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IHalV1 hal);
        public static Result Read()
        {
            var clsid = new Guid("099B9EEB-5BBA-435C-A89E-212FEEEFA394");
            var iid = typeof(IHalV1).GUID;
            var result = new Result();
            IHalV1 hal;
            result.ActivationHResult = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out hal);
            if (result.ActivationHResult < 0) return result;
            IntPtr buffer = IntPtr.Zero;
            uint capacity = 0;
            try
            {
                uint count = 0;
                result.CountHResult = hal.Enumerate(IntPtr.Zero, ref count);
                result.Count = count;
                if (result.CountHResult < 0 || count == 0) return result;
                if (count > 256) throw new InvalidOperationException("Unexpected device count");
                capacity = count;
                buffer = Marshal.AllocCoTaskMem(checked((int)capacity * IntPtr.Size));
                for (int i=0; i<capacity; i++) Marshal.WriteIntPtr(buffer, i*IntPtr.Size, IntPtr.Zero);
                result.FillHResult = hal.Enumerate(buffer, ref count);
                result.Count = count;
                if (result.FillHResult < 0) return result;
                if (count > capacity) throw new InvalidOperationException("HAL exceeded capacity");
                for (int i=0; i<count; i++)
                {
                    IntPtr pointer = Marshal.ReadIntPtr(buffer, i*IntPtr.Size);
                    if (pointer == IntPtr.Zero) throw new InvalidOperationException("Null device");
                    object device = Marshal.GetObjectForIUnknown(pointer);
                    try
                    {
                        string xml;
                        Marshal.ThrowExceptionForHR(((IDeviceV1)device).GetCapability(out xml));
                        result.Capabilities.Add(xml);
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                return result;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    for (int i=0; i<capacity; i++)
                    {
                        IntPtr pointer = Marshal.ReadIntPtr(buffer, i*IntPtr.Size);
                        if (pointer != IntPtr.Zero) Marshal.Release(pointer);
                    }
                    Marshal.FreeCoTaskMem(buffer);
                }
                Marshal.ReleaseComObject(hal);
            }
        }
    }
}
