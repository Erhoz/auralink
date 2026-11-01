"""Standalone Chroma output experiment; not connected to the Aura HAL.

Default: inventory and GET version only. --apply maintains a static color through
the shared mousepad endpoint, then DELETEs the session in finally. No SDK DLLs,
installers, drivers or services are loaded/changed by this program.
"""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import time
from urllib.parse import urlsplit
from urllib.request import Request, build_opener, ProxyHandler, HTTPRedirectHandler


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError('HTTP redirects are not accepted for the local SDK')


def local_uri(value):
    uri = urlsplit(value)
    if (uri.scheme != 'http' or uri.hostname not in ('localhost', '127.0.0.1', '::1')
            or uri.username or uri.password or uri.fragment or uri.query
            or uri.port is None or not 1024 <= uri.port <= 65535):
        raise ValueError('Expected a local HTTP SDK URI with an explicit port')
    return value.rstrip('/')


class ChromaRestOutput:
    def __init__(self, endpoint='http://localhost:54235/razer/chromasdk'):
        self.endpoint = local_uri(endpoint)
        self.session = None
        self.opener = build_opener(ProxyHandler({}), NoRedirect())

    def request(self, method, uri, body=None):
        data = None if body is None else json.dumps(body).encode('utf-8')
        req = Request(local_uri(uri), data=data, method=method,
                      headers={'Content-Type': 'application/json'})
        with self.opener.open(req, timeout=2) as response:
            raw = response.read(65537)
        if len(raw) > 65536:
            raise ValueError('SDK response exceeds the diagnostic limit')
        result = json.loads(raw)
        if not isinstance(result, dict):
            raise ValueError('SDK response must be an object')
        if result.get('result', 0) != 0:
            raise RuntimeError(f'Chroma result: {result["result"]}')
        if method in ('PUT', 'DELETE') and not uri.endswith('/heartbeat') and result.get('result') != 0:
            raise ValueError('SDK did not confirm success')
        return result

    def version(self):
        result = self.request('GET', self.endpoint)
        if 'version' not in result:
            raise ValueError('Local endpoint did not report a Chroma version')
        return result

    def open(self):
        if self.session is not None:
            raise RuntimeError('Session already initialized')
        result = self.request('POST', self.endpoint, {
            'title': 'AuraLink Base Station diagnostic',
            'description': 'Unofficial bounded static color test; no Aura control',
            'author': {'name': 'AuraLink', 'contact': 'https://github.com/Erhoz/auralink'},
            'device_supported': ['mousepad'], 'category': 'application'})
        self.session = local_uri(result['uri'])

    def set_rgb(self, red, green, blue):
        if self.session is None:
            raise RuntimeError('No SDK session')
        if any(type(channel) is not int or not 0 <= channel <= 255
               for channel in (red, green, blue)):
            raise ValueError('RGB channels must be bytes')
        # Chroma COLORREF/BGR integer: 0x00BBGGRR. Aura raw layout is separate.
        return self.request('PUT', self.session + '/mousepad', {
            'effect': 'CHROMA_STATIC', 'param': {'color': red | green << 8 | blue << 16}})

    def close(self):
        if self.session is not None:
            self.request('DELETE', self.session)
            self.session = None

    def heartbeat(self):
        if self.session is None:
            raise RuntimeError('No SDK session')
        return self.request('PUT', self.session + '/heartbeat')


def snapshot():
    script = r"""
    $ErrorActionPreference='Stop'
    [Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
    $devices=@(Get-PnpDevice -PresentOnly | Where-Object InstanceId -match '^USB\\VID_1532&PID_[0-9A-F]{4}\\' | Select-Object FriendlyName,InstanceId,Status)
    $services=@(Get-CimInstance Win32_Service | Where-Object Name -match 'Razer|Chroma|Synapse|RzSDK' | Select-Object Name,State)
    [pscustomobject]@{Devices=$devices;Services=$services;LightingStateReadable=$false} | ConvertTo-Json -Depth 5
    """
    result = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', script],
                            capture_output=True, timeout=30, check=True)
    return json.loads(result.stdout.decode('utf-8-sig', errors='replace'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--color', default='800000', help='RRGGBB (default: dim red)')
    parser.add_argument('--seconds', type=float, default=3)
    parser.add_argument('--before-note', default=None, help='Optional user observation of the lighting before the test')
    parser.add_argument('--stop-file', type=Path, help='End the active test when this file is created')
    parser.add_argument('--output', type=Path, default=Path('artifacts/razer-stage/chroma-test.json'))
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9a-fA-F]{6}', args.color) or not 0 < args.seconds <= 300:
        parser.error('Use six hex RGB digits and a duration greater than 0 and at most 300 seconds')
    report = dict(recordedUtc=datetime.now(timezone.utc).isoformat(), mode='apply' if args.apply else 'probe',
                  route='mousepad (shared category; not addressed by USB VID/PID)',
                  dependencyAvailable=False,
                  outputAttempted=False, apiAccepted=False, hardwareValidated=False,
                  sessionInitialized=False,
                  releaseAttempted=False, releaseConfirmed=False, error=None)
    report['observedBefore'] = args.before_note
    client = ChromaRestOutput()
    exit_code = 0
    try:
        report['before'] = snapshot()
        devices = report['before']['Devices']
        if args.apply and (len(devices) != 1 or devices[0]['Status'] != 'OK'
                           or 'VID_1532&PID_0F08\\' not in devices[0]['InstanceId'].upper()):
            raise RuntimeError('Apply requires the Base Station to be the only present Razer USB parent device')
        report['sdk'] = client.version()
        report['dependencyAvailable'] = True
        if args.apply:
            client.open()
            report['sessionInitialized'] = True
            report['outputAttempted'] = True
            rgb = tuple(int(args.color[i:i+2], 16) for i in (0, 2, 4))
            client.set_rgb(*rgb)
            report['colorWriteCount'] = 1
            report['apiAccepted'] = True
            print(json.dumps({'status': 'active', 'colorRgb': args.color,
                              'maxSeconds': args.seconds}), flush=True)
            deadline = time.monotonic() + args.seconds
            heartbeat_at = time.monotonic() + 1
            report['heartbeats'] = 0
            report['stopReason'] = 'duration'
            while time.monotonic() < deadline:
                if args.stop_file and args.stop_file.exists():
                    report['stopReason'] = 'stop-file'
                    break
                if time.monotonic() >= heartbeat_at:
                    client.heartbeat()
                    report['heartbeats'] += 1
                    # The real SDK grants lighting access asynchronously after
                    # Init. An initial successful PUT can precede that grant.
                    # Reassert only the requested static color, once per second.
                    client.set_rgb(*rgb)
                    report['colorWriteCount'] += 1
                    heartbeat_at = time.monotonic() + 1
                time.sleep(0.1)
    except (Exception, KeyboardInterrupt) as exc:
        report['error'] = f'{type(exc).__name__}: {exc}'
        exit_code = 2
    finally:
        if client.session is not None:
            report['releaseAttempted'] = True
            try:
                client.close()
                report['releaseConfirmed'] = True
            except Exception as exc:
                report['releaseError'] = str(exc)
                exit_code = 2
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps(report, indent=2))
    return exit_code


if __name__ == '__main__':
    raise SystemExit(main())
