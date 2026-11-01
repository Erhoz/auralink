"""Activate only AuraLink's own class from a native Python host (no CLR preloaded).

No ASUS class, hardware enumeration, effect call or registry mutation is performed.
"""
import ctypes
import json
import uuid
from ctypes import wintypes


class GUID(ctypes.Structure):
    _fields_ = [('Data1', ctypes.c_uint32), ('Data2', ctypes.c_uint16),
                ('Data3', ctypes.c_uint16), ('Data4', ctypes.c_ubyte * 8)]

    @classmethod
    def parse(cls, value):
        return cls.from_buffer_copy(uuid.UUID(value).bytes_le)


def main():
    ole = ctypes.WinDLL('ole32')
    ole.CoInitializeEx.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
    ole.CoInitializeEx.restype = ctypes.c_long
    ole.CoCreateInstance.argtypes = [ctypes.POINTER(GUID), ctypes.c_void_p, ctypes.c_uint32,
                                   ctypes.POINTER(GUID), ctypes.POINTER(ctypes.c_void_p)]
    ole.CoCreateInstance.restype = ctypes.c_long
    initialized = ole.CoInitializeEx(None, 0)
    if initialized < 0:
        raise OSError(f'CoInitializeEx: {initialized & 0xFFFFFFFF:#x}')
    results = []
    try:
        clsid = GUID.parse('9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6')
        for iid_text in ('00000000-0000-0000-C000-000000000046',
                         'F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9'):
            iid = GUID.parse(iid_text)
            pointer = ctypes.c_void_p()
            hr = ole.CoCreateInstance(ctypes.byref(clsid), None, 1, ctypes.byref(iid), ctypes.byref(pointer))
            results.append(dict(iid=iid_text, hresult=f'0x{hr & 0xFFFFFFFF:08X}', pointerReturned=bool(pointer.value)))
            if pointer.value:
                table = ctypes.cast(pointer, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
                release = ctypes.WINFUNCTYPE(ctypes.c_uint32, ctypes.c_void_p)(table[2])
                release(pointer)
    finally:
        ole.CoUninitialize()
    print(json.dumps(dict(processBits=ctypes.sizeof(ctypes.c_void_p)*8, clrPreloadedByClient=False,
                          activations=results, hardwareOutput=False)))


if __name__ == '__main__':
    main()
