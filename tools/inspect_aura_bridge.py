"""Read-only PE string references, bounded to x64 runtime-function entries.

Never imports/executes the target. Requires the optional analysis dependencies
in artifacts/analysis-deps (see analysis-requirements.txt).
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'artifacts/analysis-deps'))
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import X86_OP_MEM, X86_REG_RIP


def inspect(path, pattern, context):
    raw = path.read_bytes()
    pe = pefile.PE(data=raw)
    if pe.FILE_HEADER.Machine != 0x8664:
        raise ValueError('Only x64 PE images are supported')
    base = pe.OPTIONAL_HEADER.ImageBase
    strings = {}
    for encoding, regex in [('ascii', rb'[ -~]{4,}'), ('utf-16le', rb'(?:[ -~]\x00){4,}')]:
        for match in re.finditer(regex, raw):
            value = match.group().decode(encoding)
            if re.search(pattern, value, re.I):
                strings[base + pe.get_rva_from_offset(match.start())] = value
    entries = sorted({(entry.struct.BeginAddress, entry.struct.EndAddress)
                      for entry in getattr(pe, 'DIRECTORY_ENTRY_EXCEPTION', [])})
    starts = [start for start, _ in entries]
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    references = []
    for start, end in entries:
        instructions = list(md.disasm(pe.get_data(start, end - start), base + start))
        for i, ins in enumerate(instructions):
            targets = [ins.address + ins.size + op.mem.disp for op in ins.operands
                       if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP]
            for target in targets:
                if target not in strings:
                    continue
                references.append(dict(string=strings[target], stringRva=hex(target-base),
                    instructionRva=hex(ins.address-base), functionStart=hex(start), functionEnd=hex(end),
                    context=[f'{x.address-base:08x} {x.mnemonic} {x.op_str}'
                             for x in instructions[max(0, i-context):i+context+1]]))
    return dict(path=str(path), sha256=hashlib.sha256(raw).hexdigest(),
                machine='x64', targetExecuted=False, runtimeFunctionCount=len(starts),
                matchingStrings=[dict(rva=hex(k-base), value=v) for k, v in strings.items()],
                references=references)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path)
    parser.add_argument('--pattern', default=r'GetDeviceListFromLightingService|GetDeviceStatusNew|GSkillDram|MousePad|RogAudioDevices_Headset|UnknownDevice|DeviceListSize')
    parser.add_argument('--context', type=int, default=8)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(inspect(args.path, args.pattern, args.context), indent=2), encoding='utf-8')
