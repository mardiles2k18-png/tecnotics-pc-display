"""Convierte los temas del software original (ya exportados a JSON con exportar_temas.py) a nuestro formato.

Uso: python3 convertir_temas.py <carpeta_json> <carpeta_statusbar> <carpeta_salida> [nombre ...]

Por cada tema crea <salida>/<nombre>/ con:
  - fondo.png: imagen de fondo (backImg) con la capa del tema (osdImg) encima;
  - theme.json: textos, barras y arcos en nuestras coordenadas, con las etiquetas traducidas al español;
  - barra-*.png: imágenes de barra del original (config/statusbar), si el tema las usa.
Los textos que vienen dibujados dentro de las imágenes no se traducen aquí (eso se hace a mano por tema).
"""
import sys, os, json, glob, re
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
KNOWN = {int(k): v.split(':')[1] for k, v in json.load(open(os.path.join(HERE, 'knowncolors.json'))).items()}

# Valores del original -> (plantilla de texto, sensor para barras y arcos, máximo)
ITEMS = {
    'CpuLoad': ('{cpu.load:0}', 'cpu.load', 100),
    'GpuLoad': ('{gpu.load:0}', 'gpu.load', 100),
    'RamLoad': ('{ram.load:0}', 'ram.load', 100),
    'HDDLoad': ('{disk.used:0}', 'disk.used', 100),
    'CpuTemp': ('{cpu.temp:0}', 'cpu.temp', 100),
    'GpuTemp': ('{gpu.temp:0}', 'gpu.temp', 100),
    'HDDTemp': ('{disk.temp:0}', 'disk.temp', 100),
    'CpuFreq': ('{cpu.clock.ghz:0.0}', 'cpu.clock', 5000),
    'GpuRam': ('{gpu.mem.used:0}', 'gpu.mem.load', 100),
    'GpuRamLoad': ('{gpu.mem.load:0}', 'gpu.mem.load', 100),
    'RamValue': ('{ram.used.mb:0}', 'ram.load', 100),
    'InternetUp': ('{net.up:0.0}', 'net.up', 10),
    'InternetDown': ('{net.down:0.0}', 'net.down', 10),
    'VulomeLoad': ('{volume:0}', 'volume', 100),
    'Volume': ('{volume:0}', 'volume', 100),
    'CpuFan': ('{cpu.fan:0}', 'cpu.fan', 3000),
    'GpuFan': ('{gpu.fan:0}', 'gpu.fan', 3000),
    'CpuPower': ('{cpu.power:0}', 'cpu.power', 200),
    'Time': ('{time:HH:mm}', None, 0),
    'Date': ('{date:dd/MM/yyyy}', None, 0),
}

# Etiquetas fijas (userDefine) al español. Los nombres de componentes se cambian por los del PC.
LABELS = {
    'cpu': 'CPU', 'gpu': 'GPU', 'ram': 'RAM', 'ghz': 'GHz', 'm': 'MB', 'volume': 'VOLUMEN',
    'cpu temp': 'TEMP. CPU', 'gpu temp': 'TEMP. GPU', 'upload': 'SUBIDA', 'download': 'BAJADA',
    'ssd': 'SSD', 'hdd': 'DISCO', 'hdd/ssd': 'DISCO', 'up   network   down': 'subida    red    bajada',
    'used': 'USADO', 'temp': 'TEMP.', 'network': 'RED', 'cpu name': '{model.cpu}',
}
CPU_NAME = re.compile(r'intel|amd|ryzen|i[3579]|9900k|10700|8700', re.I)
GPU_NAME = re.compile(r'rtx|gtx|radeon|nvidia|geforce|3080|3070|3060|1060', re.I)
RAM_NAME = re.compile(r'^\d+g$|asgard|ddr|\d+g\*\d', re.I)


def color(c, default='#FFFFFF'):
    if not c:
        return default
    if c.get('state') == 2 or c.get('knownColor', 0) == 0:
        a = c.get('argb', 0) & 0xFFFFFFFF
        return '#%08X' % a if a else default
    return '#' + KNOWN.get(c['knownColor'], 'FFFFFFFF')


def label_text(v):
    key = v.strip().lower()
    if key in LABELS:
        return LABELS[key]
    if GPU_NAME.search(v):
        return '{model.gpu}'
    if RAM_NAME.search(v.strip()):
        return '{ram.total:0} GB'
    if CPU_NAME.search(v):
        return '{model.cpu}'
    return v


def font_size(ft):
    return round(ft['H'] * 1.2)


def convert(j, statusbar, out, src_prefix):
    landscape = j['isLanscape']
    W, H = (480, 320) if landscape else (320, 480)
    os.makedirs(out, exist_ok=True)

    # Fondo = backImg + capa osdImg (con transparencia).
    def img(key):
        files = glob.glob(f'{src_prefix}.tema.{key}.*')
        return Image.open(files[0]).convert('RGBA') if files else None
    back, osd = img('backImg'), img('osdWithFontBack' if j.get('useOsdWithFontBack') else 'osdImg')
    base = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    for layer in (back, osd):
        if layer is not None:
            if layer.size != (W, H):
                layer = layer.resize((W, H))
            base = Image.alpha_composite(base, layer)
    base.convert('RGB').save(os.path.join(out, 'fondo.png'))

    els = []
    skipped = []
    for g in j.get('groupList') or []:
        for it in g['itemList']:
            if not it['enabled']:
                continue
            name, ft = it['itemName'], it['fontType']
            x, y = it['posX'] + g.get('baseX', 0), it['posY'] + g.get('baseY', 0)
            size, col = font_size(ft), color(ft['color'], '#000000')
            if name.startswith('userDefine'):
                t = label_text(it['valueStr'])
                if not t.strip():
                    continue
                kind = 'text' if '{' in t else 'label'
                w = min(W - x, max(30, int(len(t) * ft['interval'] * 1.1) + 10))
                els.append({'type': kind, 'x': x, 'y': y, 'w': w, 'h': ft['H'] + 4, 'text': t, 'size': size,
                            'color': col, 'align': 'left', 'bold': False})
                continue
            if name not in ITEMS:
                skipped.append(name)
                continue
            tpl = ITEMS[name][0]
            if name == 'Date':
                w, align = min(W - x, 150), 'left'
            elif name == 'Time':
                w, align = max(5 * ft['interval'] + 8, 60), 'left'
            else:
                n = max(it['strLen'], 2)
                w, align = n * ft['interval'] + 6, 'right'
                x -= 6
            els.append({'type': 'text', 'x': x, 'y': y - 2, 'w': w, 'h': ft['H'] + 4, 'text': tpl, 'size': size,
                        'color': col, 'align': align, 'bold': True})

    # Barras: estilo común del tema (colores o imágenes de config/statusbar).
    styles = sorted(os.path.basename(p) for p in glob.glob(os.path.join(statusbar, '*.bmp')))

    def bar_image(index, prefix):
        if j['barType'] == 1 and index < len(styles):
            fn = f'barra-{prefix}-{index}.png'
            Image.open(os.path.join(statusbar, styles[index])).convert('RGB').save(os.path.join(out, fn))
            return fn
        return None
    for b in j.get('barList') or []:
        if not b['enabled'] or b['itemName'] not in ITEMS:
            if b['enabled']:
                skipped.append('barra ' + b['itemName'])
            continue
        _, sensor, mx = ITEMS[b['itemName']]
        e = {'type': 'bar', 'x': b['posX'], 'y': b['posY'], 'w': b['length'], 'h': b['height'] or 13, 'sensor': sensor,
             'max': mx, 'rounded': False, 'color': color(j['barFrontColor']), 'backColor': color(j['barBackColor'])}
        if j['barType'] == 1:
            front, back = bar_image(j['barFrontIndex'], 'llena'), bar_image(j['barBackIndex'], 'fondo')
            if front:
                e['frontImage'] = front
            if back:
                e['backImage'] = back
            elif j['barBackIndex'] == len(styles):  # "sin fondo"
                e['backColor'] = '#00000000'
        els.append(e)

    # Arcos: círculo de diámetro archR; 100 % = vuelta completa desde startPer * 3,6 grados.
    for a in j.get('archList') or []:
        if not a['enabled'] or a['itemName'] not in ITEMS:
            if a['enabled']:
                skipped.append('arco ' + a['itemName'])
            continue
        _, sensor, mx = ITEMS[a['itemName']]
        x, y = a['posX'] + 30 - a['offsetX'], a['posY'] + 30 - a['offsetY']
        els.append({'type': 'arc', 'x': x, 'y': y, 'w': a['archR'], 'h': a['archR'], 'sensor': sensor, 'max': mx,
                    'thickness': a['archWidth'], 'start': a['startPer'] * 3.6,
                    'color': color(a['frontArchColor']), 'backColor': color(a['backArchColor'], '#00000000')})

    # Primero barras y arcos, luego textos, para que los números queden encima.
    order = {'bar': 0, 'arc': 0, 'label': 1, 'text': 2}
    els.sort(key=lambda e: order[e['type']])
    theme = {'formatVersion': 1, 'name': os.path.basename(out), 'orientation': 'landscape' if landscape else 'portrait',
             'background': {'color': '#000000', 'image': 'fondo.png'}, 'elements': els}
    json.dump(theme, open(os.path.join(out, 'theme.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    return skipped


if __name__ == '__main__':
    src, statusbar, dst = sys.argv[1:4]
    names = sys.argv[4:] or [os.path.basename(p)[:-5] for p in sorted(glob.glob(os.path.join(src, '*.json')))]
    for name in names:
        j = json.load(open(os.path.join(src, name + '.json')))
        if j.get('_clase') != 'usb_hid.ThemeConfig':
            continue
        skipped = convert(j, statusbar, os.path.join(dst, name), os.path.join(src, name))
        print('ok', name, ('(sin convertir: ' + ', '.join(sorted(set(skipped))) + ')') if skipped else '')
