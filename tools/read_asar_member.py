"""Read one packed text member of an Electron ASAR without running its code."""
import argparse
import json
from pathlib import Path
import struct


def read_member(path, member):
    with path.open('rb') as stream:
        prefix = stream.read(16)
        if len(prefix) != 16:
            raise ValueError('Truncated ASAR header')
        marker, header_size, payload_size, json_size = struct.unpack('<IIII', prefix)
        if marker != 4 or not 0 < json_size <= payload_size <= header_size <= 16 * 1024 * 1024:
            raise ValueError('Invalid ASAR header')
        header = json.loads(stream.read(json_size))
        node = header
        for part in member.split('/'):
            if part in ('', '.', '..'):
                raise ValueError('Use a literal member path')
            node = node['files'][part]
        if node.get('unpacked') or 'link' in node or 'files' in node:
            raise ValueError('Only packed regular members are supported')
        offset, size = int(node['offset']), int(node['size'])
        start = 8 + header_size + offset
        if offset < 0 or not 0 <= size <= 8 * 1024 * 1024 or start + size > path.stat().st_size:
            raise ValueError('Member is outside the archive or exceeds text limit')
        stream.seek(start)
        return stream.read(size).decode('utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('member')
    args = parser.parse_args()
    print(read_member(args.archive, args.member))
