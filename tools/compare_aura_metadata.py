"""Compare saved capability XML and registry snapshots; never activates COM.

The Corsair input must be an existing Read-HalContract JSON result. This tool
does not collect fresh capabilities because vendor initialization has effects.
"""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def capability(xml):
    root = ET.fromstring(xml)
    device = root.find('device')
    if device is None:
        raise ValueError('Missing device element')
    return dict(version=root.findtext('version'), type=int(root.findtext('type')),
                name=device.findtext('name'), id=device.findtext('id'),
                manufacturer=device.findtext('manufacturer'),
                ledCount=int(device.findtext('layout/led_count')),
                width=int(device.findtext('layout/size/width')),
                height=int(device.findtext('layout/size/height')),
                fields=sorted({element.tag for element in root.iter()}),
                effects=[{key: effect.findtext(key) for key in
                          ('name', 'id', 'synchronizable', 'customized_color')}
                         for effect in device.findall('supported_effect/effect')])


def compare(saved_path, own_path, registry_path):
    saved = json.loads(saved_path.read_text(encoding='utf-8-sig'))
    corsair = [capability(xml) for xml in saved['Capabilities']]
    own = capability(own_path.read_text(encoding='utf-8-sig'))
    registry = json.loads(registry_path.read_text(encoding='utf-8-sig'))
    registration = [row for row in registry
                    if row['ClassId'].lower() == '{099b9eeb-5bba-435c-a89e-212feeefa394}']
    return dict(schemaVersion=1, liveComActivated=False, servicesRestarted=False,
                inputs=dict(corsairSaved=str(saved_path), auraLinkSource=str(own_path),
                            registrySnapshot=str(registry_path)),
                corsairCapabilities=corsair, auraLinkCapability=own,
                corsairDiscovery=registration,
                auraLinkFieldsAbsentFromCorsair=sorted(set(own['fields'])-set(corsair[0]['fields'])),
                corsairFieldsAbsentFromAuraLink=sorted(set(corsair[0]['fields'])-set(own['fields'])),
                missingCorsairEffectIds=sorted({e['id'] for e in corsair[0]['effects']} -
                                             {e['id'] for e in own['effects']}),
                nativeIntegrationProven=False)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--corsair-saved', type=Path, required=True)
    parser.add_argument('--auralink-xml', type=Path, required=True)
    parser.add_argument('--registry-snapshot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(compare(args.corsair_saved, args.auralink_xml,
                                              args.registry_snapshot), indent=2), encoding='utf-8')
