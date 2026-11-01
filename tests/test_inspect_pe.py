import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('inspect_pe', Path(__file__).parents[1] / 'tools/inspect_pe.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture():
    b = bytearray(0x400)
    b[:2] = b'MZ'
    struct.pack_into('<I', b, 0x3C, 0x80)
    b[0x80:0x84] = b'PE\0\0'
    struct.pack_into('<HH', b, 0x84, 0x8664, 1)
    struct.pack_into('<H', b, 0x94, 0xF0)
    struct.pack_into('<H', b, 0x98, 0x20B)
    struct.pack_into('<I', b, 0x98 + 60, 0x200)
    struct.pack_into('<II', b, 0x98 + 112, 0x1000, 40)
    struct.pack_into('<IIII', b, 0x188 + 8, 0x200, 0x1000, 0x200, 0x200)
    struct.pack_into('<I', b, 0x200 + 24, 1)
    struct.pack_into('<I', b, 0x200 + 32, 0x1040)
    struct.pack_into('<I', b, 0x240, 0x1050)
    b[0x250:0x262] = b'DllGetClassObject\0'
    return b


class PEInspectionTests(unittest.TestCase):
    def inspect(self, data):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fixture.dll'
            path.write_bytes(data)
            return module.inspect(path)

    def test_named_export_uses_address_of_names_not_functions(self):
        result = self.inspect(fixture())
        self.assertEqual(result['exports'], ['DllGetClassObject'])
        self.assertEqual(result['machine'], '8664')

    def test_truncated_and_non_pe_fail(self):
        for data in (b'', b'MZ', b'not executable', fixture()[:0x210]):
            with self.subTest(length=len(data)), self.assertRaises(ValueError):
                self.inspect(data)

    def test_unmapped_export_pointer_fails(self):
        data = fixture()
        struct.pack_into('<I', data, 0x240, 0x7FFFFFFF)
        with self.assertRaises(ValueError):
            self.inspect(data)


if __name__ == '__main__':
    unittest.main()
