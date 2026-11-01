"""Static x86 PUSH/MOV-immediate candidates referencing selected PE strings.

Does not execute the image. Byte matches may be data or overlapping instructions;
use the candidate RVAs for manual control-flow inspection, not as proof alone.
"""
import argparse
import json
from pathlib import Path
import re
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'artifacts/analysis-deps'))
import pefile


def inspect(path, pattern):
    raw = path.read_bytes()
    pe = pefile.PE(data=raw)
    if pe.FILE_HEADER.Machine != 0x14c:
        raise ValueError('This scanner is for x86 PE images only')
    base = pe.OPTIONAL_HEADER.ImageBase
    results = []
    for encoding, regex in [('ascii', rb'[ -~]{4,}'), ('utf-16le', rb'(?:[ -~]\x00){4,}')]:
        for match in re.finditer(regex, raw):
            value = match.group().decode(encoding)
            if not re.search(pattern, value, re.I):
                continue
            address = base + pe.get_rva_from_offset(match.start())
            candidates = []
            for ref in re.finditer(re.escape(struct.pack('<I', address)), raw):
                offset = ref.start() - 1
                if offset < 0 or raw[offset] not in (0x68, *range(0xb8, 0xc0)):
                    continue
                rva = pe.get_rva_from_offset(offset)
                section = pe.get_section_by_rva(rva)
                if section and section.Characteristics & 0x20000000:
                    candidates.append(dict(rva=hex(rva), opcode=hex(raw[offset])))
            results.append(dict(string=value, rva=hex(address-base), candidates=candidates))
    return dict(path=str(path), targetExecuted=False, candidatesOnly=True, strings=results)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path)
    parser.add_argument('--pattern', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(inspect(args.path, args.pattern), indent=2), encoding='utf-8')
