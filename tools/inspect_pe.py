"""Read PE exports and relevant strings without loading or executing the image.

Standard library only. This is a bounded inspection utility, not a PE validator.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct


def inspect(path):
    data = Path(path).read_bytes()

    def unpack(fmt, offset):
        if offset < 0 or offset + struct.calcsize(fmt) > len(data):
            raise ValueError('PE field outside file')
        return struct.unpack_from(fmt, data, offset)

    if data[:2] != b'MZ':
        raise ValueError('Missing MZ signature')
    pe, = unpack('<I', 0x3C)
    if data[pe:pe + 4] != b'PE\0\0':
        raise ValueError('Missing PE signature')
    machine, sections = unpack('<HH', pe + 4)
    optional_size, = unpack('<H', pe + 20)
    optional = pe + 24
    magic, = unpack('<H', optional)
    if magic not in (0x10B, 0x20B):
        raise ValueError('Unsupported optional header')
    directory = optional + (112 if magic == 0x20B else 96)
    if directory + 8 > optional + optional_size:
        raise ValueError('Missing export directory')
    export_rva, export_size = unpack('<II', directory)
    section_table = optional + optional_size
    headers_size, = unpack('<I', optional + 60)

    def rva_offset(rva):
        if 0 <= rva < min(headers_size, len(data)):
            return rva
        for i in range(sections):
            _, va, raw_size, raw = unpack('<IIII', section_table + i * 40 + 8)
            if va <= rva < va + raw_size:
                result = raw + rva - va
                if result < len(data):
                    return result
        raise ValueError(f'Unmapped RVA {rva:#x}')

    def cstring(rva):
        offset = rva_offset(rva)
        end = data.find(b'\0', offset, min(offset + 4096, len(data)))
        if end < 0:
            raise ValueError('Unterminated export name')
        return data[offset:end].decode('ascii')

    exports = []
    if export_rva and export_size:
        entry = rva_offset(export_rva)
        count, = unpack('<I', entry + 24)
        names_rva, = unpack('<I', entry + 32)
        if count > len(data) // 4:
            raise ValueError('Impossible export count')
        for i in range(count):
            name_rva, = unpack('<I', rva_offset(names_rva + i * 4))
            exports.append(cstring(name_rva))
    strings = [s.decode('ascii') for s in re.findall(rb'[ -~]{8,}', data)]
    relevant = sorted(set(s for s in strings if re.search(r'IAac|IAsus|CDCS|Corsair.*dll|Aura.*pdb', s, re.I)))
    return dict(path=str(path), sha256=hashlib.sha256(data).hexdigest(),
                machine=f'{machine:04x}', exports=exports, relevant_strings=relevant)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path)
    args = parser.parse_args()
    print(json.dumps(inspect(args.path), indent=2, ensure_ascii=False))
