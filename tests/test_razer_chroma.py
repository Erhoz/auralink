"""Protocol tests against a local fake server. No Razer SDK or hardware used."""
import importlib.util
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
from pathlib import Path
import threading
import unittest

spec = importlib.util.spec_from_file_location('razer_probe', Path(__file__).parents[1] / 'tools/test_razer_chroma.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ProtocolTests(unittest.TestCase):
    def setUp(self):
        self.requests = []
        self.fail_effect = False
        self.fail_close = False
        self.remote_session = False
        fixture = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def handle_request(self):
                body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
                fixture.requests.append((self.command, self.path, json.loads(body) if body else None))
                result = {'result': 0}
                if self.command == 'GET':
                    result = {'version': 'fake-test-only'}
                elif self.command == 'POST':
                    host = 'example.com' if fixture.remote_session else '127.0.0.1'
                    result = {'uri': f'http://{host}:{self.server.server_port}/chromasdk'}
                elif self.command == 'PUT' and fixture.fail_effect:
                    result = {'result': 87}
                elif self.command == 'DELETE' and fixture.fail_close:
                    result = {'result': 5}
                encoded = json.dumps(result).encode()
                self.send_response(200)
                self.send_header('Content-Length', str(len(encoded)))
                self.end_headers()
                self.wfile.write(encoded)

            do_GET = do_POST = do_PUT = do_DELETE = handle_request

        self.server = HTTPServer(('127.0.0.1', 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.client = module.ChromaRestOutput(f'http://127.0.0.1:{self.server.server_port}/razer/chromasdk')

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def test_probe_does_not_create_session_or_send_color(self):
        self.assertEqual(self.client.version()['version'], 'fake-test-only')
        self.assertEqual([r[0] for r in self.requests], ['GET'])

    def test_rgb_mapping_scope_and_release(self):
        self.client.open()
        try:
            self.client.set_rgb(0x12, 0x34, 0x56)
        finally:
            self.client.close()
        self.assertEqual(self.requests[0][2]['device_supported'], ['mousepad'])
        self.assertEqual(self.requests[1][2]['param']['color'], 0x563412)
        self.assertEqual(self.requests[-1][:2], ('DELETE', '/chromasdk'))
        self.assertIsNone(self.client.session)

    def test_release_after_effect_failure(self):
        self.fail_effect = True
        self.client.open()
        with self.assertRaisesRegex(RuntimeError, '87'):
            try:
                self.client.set_rgb(128, 0, 0)
            finally:
                self.client.close()
        self.assertEqual(self.requests[-1][0], 'DELETE')

    def test_release_error_is_visible_and_not_marked_closed(self):
        self.fail_close = True
        self.client.open()
        with self.assertRaisesRegex(RuntimeError, '5'):
            self.client.close()
        self.assertIsNotNone(self.client.session)

    def test_remote_session_is_rejected_before_effect(self):
        self.remote_session = True
        with self.assertRaisesRegex(ValueError, 'local HTTP'):
            self.client.open()
        self.assertEqual([r[0] for r in self.requests], ['POST'])


if __name__ == '__main__':
    unittest.main()
