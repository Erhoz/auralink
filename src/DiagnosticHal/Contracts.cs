using System;
using System.Runtime.InteropServices;

namespace AuraLink.DiagnosticHal
{
    // The first three slots are IUnknown. Pointer parameters remain explicit:
    // Enumerate returns caller-owned AddRef'd interfaces; Enumerate2 returns a
    // VARIANT containing SAFEARRAY(VT_UNKNOWN), to be released with VariantClear.
    [ComVisible(true), Guid("F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAacLedDeviceHal
    {
        [PreserveSig] int Enumerate(IntPtr devices, ref uint count);
        [PreserveSig] int Enumerate2(IntPtr devices, ref uint count);
    }

    [ComVisible(true), Guid("61711778-AB59-4026-89E8-7A63422C29C2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAacLedDevice
    {
        [PreserveSig] int GetCapability([MarshalAs(UnmanagedType.BStr)] out string capability);
        [PreserveSig] int SetEffect(uint effectId, IntPtr colors, uint numberOfColors);
        [PreserveSig] int Synchronize(uint effectId, ulong milliseconds);
    }
}
