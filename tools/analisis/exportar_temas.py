"""Exporta los temas .data del software original (BinaryFormatter .NET) a JSON + PNG.
Uso: python3 exportar_temas.py <carpeta_config> <carpeta_salida>"""
import sys, os, re, glob, json, base64
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nrbf

def clean(k):
    k = re.sub(r'.*<(.*)>k__BackingField', r'\1', k)
    return k.split('+')[-1]

def conv(v, out_prefix, path):
    if isinstance(v, nrbf.Obj):
        if v.cls.startswith('System.Collections.Generic.List'):
            return [conv(x, out_prefix, f'{path}[{i}]') for i, x in enumerate(v['_items'][:v['_size']]) if x is not None]
        if v.cls == 'System.Drawing.Bitmap':
            data = v['Data']; ext = 'png' if data[:4] == b'\x89PNG' else 'jpg'
            fn = f'{out_prefix}.' + f'{path}.{ext}'.replace('/', '_')
            open(fn, 'wb').write(data); return {'imagen': os.path.basename(fn)}
        if v.cls == 'System.Drawing.Color':
            return {'argb': v['value'], 'knownColor': v['knownColor'], 'state': v['state']}
        return {clean(k): conv(x, out_prefix, path + '.' + clean(k)) for k, x in v.items()}
    if isinstance(v, (bytes, bytearray)):
        return {'bytes': len(v), 'b64': base64.b64encode(v).decode()}
    if isinstance(v, list):
        return [conv(x, out_prefix, f'{path}[{i}]') for i, x in enumerate(v)]
    return v

if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]; os.makedirs(dst, exist_ok=True)
    for p in sorted(glob.glob(os.path.join(src, '*.data'))):
        name = os.path.basename(p)[:-5]
        try: root = nrbf.load(p)
        except Exception as e: print('ERROR', name, e); continue
        j = conv(root, os.path.join(dst, name), 'tema')
        j['_clase'] = root.cls
        json.dump(j, open(os.path.join(dst, name + '.json'), 'w'), ensure_ascii=False, indent=1)
        print('ok', name)
