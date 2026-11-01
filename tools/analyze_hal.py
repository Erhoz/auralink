"""Static x64 MSVC RTTI/vtable and x86/x64 instruction inspection; never loads the target.

Optional analysis dependencies: pefile==2024.8.26, capstone==5.0.9.
Install them under artifacts/analysis-deps, not into the application runtime.
"""
import argparse
from pathlib import Path
import re
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'artifacts/analysis-deps'))
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_MODE_32
from capstone.x86 import X86_OP_MEM, X86_REG_RIP


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path)
    parser.add_argument('--class-name', default='Corsair(Hal|Device)')
    parser.add_argument('--function', type=lambda s: int(s, 0))
    parser.add_argument('--bytes', type=int, default=256)
    args = parser.parse_args()
    pe = pefile.PE(str(args.path))
    is_x64 = pe.FILE_HEADER.Machine == 0x8664
    if pe.FILE_HEADER.Machine not in (0x8664, 0x14c):
        parser.error('Only x86/x64 PE images are supported')
    if not is_x64 and args.function is None:
        parser.error('RTTI inspection is x64 only; x86 requires --function')
    raw = args.path.read_bytes()
    base = pe.OPTIONAL_HEADER.ImageBase
    imports = {entry.address: entry.name.decode() if entry.name else str(entry.ordinal)
               for dll in getattr(pe, 'DIRECTORY_ENTRY_IMPORT', []) for entry in dll.imports}
    md = Cs(CS_ARCH_X86, CS_MODE_64 if is_x64 else CS_MODE_32)
    md.detail = True

    def disassemble(rva, length):
        for ins in md.disasm(pe.get_data(rva, length), base + rva):
            notes = []
            for op in ins.operands:
                if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                    target = ins.address + ins.size + op.mem.disp
                    if target in imports:
                        notes.append(imports[target])
                    elif base <= target < base + pe.OPTIONAL_HEADER.SizeOfImage:
                        data = pe.get_data(target - base, 160)
                        for encoding, pattern in [('utf-16le', rb'(?:[ -~]\x00){3,}'), ('ascii', rb'[ -~]{4,}')]:
                            match = re.match(pattern, data)
                            if match:
                                notes.append(repr(match.group().decode(encoding)))
                                break
            print(f'{ins.address-base:08x} {ins.mnemonic:8} {ins.op_str:50} {"; ".join(notes)}')

    if args.function is not None:
        disassemble(args.function, args.bytes)
        return
    for match in re.finditer(rb'\.\?A[UV][^\x00]+', raw):
        name = match.group().decode('ascii', errors='replace')
        if not re.search(args.class_name, name):
            continue
        type_rva = pe.get_rva_from_offset(match.start() - 16)
        for reference in re.finditer(re.escape(struct.pack('<I', type_rva)), raw):
            col_offset = reference.start() - 12
            if col_offset < 0:
                continue
            signature, offset, ctor, _, _, self_rva = struct.unpack_from('<IIIIII', raw, col_offset)
            if signature != 1 or pe.get_rva_from_offset(col_offset) != self_rva:
                continue
            for vptr in re.finditer(re.escape(struct.pack('<Q', base + self_rva)), raw):
                table = vptr.start() + 8
                print(f'\n{name} subobject={offset} vtableRVA={pe.get_rva_from_offset(table):x}')
                for i in range(9):
                    address, = struct.unpack_from('<Q', raw, table + 8 * i)
                    if not base <= address < base + pe.OPTIONAL_HEADER.SizeOfImage:
                        break
                    print(f'  slot {i}: RVA {address-base:x}')
                    disassemble(address - base, 40)


if __name__ == '__main__':
    main()
