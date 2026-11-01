using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AuraLink.Adapters.Razer
{
    // Metadata only. No input, output or feature-report transfers are performed.
    public static class HidMetadata
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct InterfaceData
        {
            public int Size;
            public Guid ClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Attributes
        {
            public int Size;
            public ushort VendorId, ProductId, Version;
        }
        public sealed class Collection
        {
            public string Path { get; set; }
            public bool Opened { get; set; }
            public int OpenError { get; set; }
            public int VendorId { get; set; }
            public int ProductId { get; set; }
            public int Version { get; set; }
            public int UsagePage { get; set; }
            public int Usage { get; set; }
            public int InputBytes { get; set; }
            public int OutputBytes { get; set; }
            public int FeatureBytes { get; set; }
            public string Error { get; set; }
        }

        public static List<Collection> ReadBaseStation()
        {
            Guid guid;
            HidD_GetHidGuid(out guid);
            IntPtr info = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (info == new IntPtr(-1)) throw new Win32Exception();
            var result = new List<Collection>();
            try
            {
                for (uint index = 0; index < 4096; index++)
                {
                    var item = new InterfaceData { Size = Marshal.SizeOf(typeof(InterfaceData)) };
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref guid, index, ref item))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 259) return result;
                        throw new Win32Exception(error);
                    }
                    uint bytes;
                    SetupDiGetDeviceInterfaceDetail(info, ref item, IntPtr.Zero, 0, out bytes, IntPtr.Zero);
                    if (bytes < 6 || bytes > 65536) throw new InvalidOperationException("Invalid interface detail length");
                    IntPtr detail = Marshal.AllocHGlobal((int)bytes);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(info, ref item, detail, bytes, out bytes, IntPtr.Zero))
                            throw new Win32Exception();
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (path == null || path.IndexOf("vid_1532&pid_0f08", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var collection = new Collection { Path = path };
                        result.Add(collection);
                        using (var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (handle.IsInvalid) { collection.OpenError = Marshal.GetLastWin32Error(); continue; }
                            collection.Opened = true;
                            var attributes = new Attributes { Size = Marshal.SizeOf(typeof(Attributes)) };
                            if (!HidD_GetAttributes(handle, ref attributes)) { collection.Error = "GetAttributes failed"; continue; }
                            collection.VendorId = attributes.VendorId;
                            collection.ProductId = attributes.ProductId;
                            collection.Version = attributes.Version;
                            if (attributes.VendorId != 0x1532 || attributes.ProductId != 0x0f08)
                                throw new InvalidOperationException("HID identity mismatch");
                            IntPtr preparsed;
                            if (!HidD_GetPreparsedData(handle, out preparsed)) { collection.Error = "GetPreparsedData failed"; continue; }
                            IntPtr caps = Marshal.AllocHGlobal(64); // documented HIDP_CAPS: 32 USHORTs
                            try
                            {
                                int status = HidP_GetCaps(preparsed, caps);
                                if (status != 0x00110000) { collection.Error = "GetCaps NTSTATUS=0x" + status.ToString("X8"); continue; }
                                collection.Usage = (ushort)Marshal.ReadInt16(caps, 0);
                                collection.UsagePage = (ushort)Marshal.ReadInt16(caps, 2);
                                collection.InputBytes = (ushort)Marshal.ReadInt16(caps, 4);
                                collection.OutputBytes = (ushort)Marshal.ReadInt16(caps, 6);
                                collection.FeatureBytes = (ushort)Marshal.ReadInt16(caps, 8);
                            }
                            finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(preparsed); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
                throw new InvalidOperationException("HID enumeration limit exceeded");
            }
            finally { SetupDiDestroyDeviceInfoList(info); }
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
        [DllImport("hid.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, IntPtr caps);
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr info, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info, ref InterfaceData data, IntPtr detail, uint bytes, out uint required, IntPtr device);
        [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    }
}
