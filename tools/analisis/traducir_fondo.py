"""Traduce los textos dibujados dentro del fondo de un tema convertido.

Uso: python3 traducir_fondo.py <carpeta_tema> <regiones.json>

regiones.json es una lista de zonas con texto horneado:
  {"r": [x0, y0, x1, y1], "t": "USO", "rot": 0, "align": "left", "bold": true, "size": null, "color": null, "maxw": null}
- r: rectángulo que cubre el texto original (se borra con "inpainting" solo donde hay letras);
- t: texto nuevo en español ("" para solo borrar);
- rot: 90 o -90 para texto vertical; align: left, center o right dentro del rectángulo;
- size: tamaño de letra (por defecto, el que llena la altura); color: "#RRGGBB" (por defecto, el del texto original);
- fill: "#RRGGBB" para borrar rellenando con ese color en vez de reconstruir el fondo;
- maxw: ancho máximo si el texto nuevo es más largo que el original (por defecto, el del rectángulo + 40 %).
Guarda el fondo sin tocar como fondo-original.png la primera vez, para poder repetir el proceso.
"""
import sys, os, json, shutil
import numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont

FONTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'src', 'Turing.Render', 'Fonts')


def text_mask(img, r):
    """Píxeles de letra dentro de r: los que se alejan del color del borde del rectángulo."""
    x0, y0, x1, y1 = r
    crop = img[y0:y1, x0:x1].astype(np.int32)
    border = np.concatenate([crop[0], crop[-1], crop[:, 0], crop[:, -1]])
    bg = np.median(border, axis=0)
    dist = np.sqrt(((crop - bg) ** 2).sum(axis=2))
    m = (dist > 40).astype(np.uint8) * 255
    ys, xs = np.nonzero(m)
    box = (x0 + xs.min(), y0 + ys.min(), x0 + xs.max() + 1, y0 + ys.max() + 1) if len(xs) else tuple(r)
    m = cv2.dilate(m, np.ones((3, 3), np.uint8), iterations=1)
    full = np.zeros(img.shape[:2], np.uint8)
    full[y0:y1, x0:x1] = m
    # Color del texto: mediana de los píxeles más alejados del fondo.
    sel = crop[dist > max(60, np.percentile(dist, 90) * 0.8)]
    color = tuple(int(c) for c in (np.median(sel, axis=0) if len(sel) else 255 - bg))
    flat = tuple(int(v) for v in bg) if border.std(axis=0).max() < 14 else None
    return full, color, box, flat


def draw_text(base, reg, color, box):
    # Se coloca sobre las letras originales (box), no sobre el rectángulo con margen.
    x0, y0, x1, y1 = box
    rot = reg.get('rot', 0)
    w, h = (x1 - x0, y1 - y0) if rot == 0 else (y1 - y0, x1 - x0)
    font_file = os.path.join(FONTS, 'Inter-Bold.otf' if reg.get('bold', True) else 'Inter-Regular.otf')
    size = reg.get('size') or max(6, round(h / 0.72))
    maxw = reg.get('maxw') or int(w * 1.4)
    while True:
        font = ImageFont.truetype(font_file, size)
        bbox = font.getbbox(reg['t'], anchor='ls')
        if bbox[2] - bbox[0] <= maxw or size <= 6:
            break
        size -= 1
    tw = bbox[2] - bbox[0]
    layer = Image.new('RGBA', (max(tw, w) + 8, h + 8), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    align = reg.get('align', 'left')
    tx = {'left': 4, 'center': 4 + (max(tw, w) - tw) / 2, 'right': 4 + max(tw, w) - tw}[align] - bbox[0]
    cap = font.getbbox('H', anchor='ls')
    ty = 4 + (h + (cap[3] - cap[1])) / 2 - cap[3]
    d.text((tx, ty), reg['t'], font=font, fill=color, anchor='ls')
    if rot:
        layer = layer.rotate(rot, expand=True)
    # Colocar: el borde del rectángulo según la alineación (en vertical, centrado en el eje).
    if rot == 0:
        ox = x0 - 4 - ({'left': 0, 'center': (max(tw, w) - w) / 2, 'right': max(tw, w) - w}[align])
        oy = y0 - 4
    else:
        ox = x0 + (x1 - x0) / 2 - layer.width / 2
        oy = y0 + (y1 - y0) / 2 - layer.height / 2
    base.alpha_composite(layer, (int(round(ox)), int(round(oy))))


def main(folder, regions_file):
    src = os.path.join(folder, 'fondo-original.png')
    if not os.path.exists(src):
        shutil.copy(os.path.join(folder, 'fondo.png'), src)
    regions = json.load(open(regions_file, encoding='utf-8'))
    img = cv2.cvtColor(np.array(Image.open(src).convert('RGB')), cv2.COLOR_RGB2BGR)
    mask = np.zeros(img.shape[:2], np.uint8)
    colors, boxes, flats = [], [], []
    for reg in regions:
        m, c, box, flat = text_mask(img, reg['r'])
        if reg.get('fill'):  # color de relleno forzado, "#RRGGBB"
            h = reg['fill'].lstrip('#')
            flat = (int(h[4:6], 16), int(h[2:4], 16), int(h[0:2], 16))
        if flat is not None:
            flats.append((m, flat))  # fondo liso: se rellena con su color
        else:
            mask |= m              # fondo con dibujo: se reconstruye con inpainting
        colors.append(c[::-1])  # BGR -> RGB
        boxes.append(box)
    clean = cv2.inpaint(img, mask, 3, cv2.INPAINT_TELEA) if mask.any() else img.copy()
    for m, flat in flats:
        clean[m > 0] = flat
    base = Image.fromarray(cv2.cvtColor(clean, cv2.COLOR_BGR2RGB)).convert('RGBA')
    for reg, c, box in zip(regions, colors, boxes):
        if reg.get('t'):
            col = reg.get('color') or '#%02X%02X%02X' % c
            draw_text(base, reg, col, box)
    base.convert('RGB').save(os.path.join(folder, 'fondo.png'))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
