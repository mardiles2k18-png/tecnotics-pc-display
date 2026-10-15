# Pantalla Turing rev. A simulada: lee los bytes guardados con --puerto archivo:salida.bin y guarda lo dibujado como PNG.
# Uso: python3 simulador.py salida.png salida.bin
import sys
from PIL import Image
out = sys.argv[1]
src = open(sys.argv[2], "rb").read()
W, H = 320, 480
fb = Image.new("RGB", (W, H))
buf = bytearray()
log = []
def need(n):
    global src
    if len(buf) < n and src:
        buf.extend(src); src = b""
    return len(buf) >= n
def take(n):
    d = bytes(buf[:n]); del buf[:n]; return d
idle_seen = False
while True:
    if not need(6):
        break
    b = take(6)
    x0 = (b[0] << 2) | (b[1] >> 6); y0 = ((b[1] & 63) << 4) | (b[2] >> 4)
    x1 = ((b[2] & 15) << 6) | (b[3] >> 2); y1 = ((b[3] & 3) << 8) | b[4]; cmd = b[5]
    if cmd == 197:
        w, h = x1 - x0 + 1, y1 - y0 + 1
        if not need(w * h * 2): log.append("bitmap truncado"); break
        px = take(w * h * 2)
        img = Image.new("RGB", (w, h))
        img.putdata([(((c >> 11) & 31) * 255 // 31, ((c >> 5) & 63) * 255 // 63, (c & 31) * 255 // 31)
                     for c in (px[i] | (px[i+1] << 8) for i in range(0, len(px), 2))])
        fb.paste(img, (x0, y0)); log.append(f"BITMAP {x0},{y0} {w}x{h}")
    elif cmd == 121:
        need(10); e = take(10)
        o = e[0] - 100; W2, H2 = (e[1] << 8) | e[2], (e[3] << 8) | e[4]
        log.append(f"ORIENTACION {o} {W2}x{H2}")
        if (W2, H2) != fb.size: fb = Image.new("RGB", (W2, H2))
    elif cmd == 110: log.append(f"BRILLO nivel={x0}")
    elif cmd in (108, 109): log.append("APAGAR" if cmd == 108 else "ENCENDER")
    elif cmd == 69: log.append("HELLO")
    else: log.append(f"ORDEN {cmd} {x0},{y0},{x1},{y1}")
fb.save(out)
from collections import Counter
print("\n".join(f"{n}x {k}" for k, n in Counter(log).items()))
