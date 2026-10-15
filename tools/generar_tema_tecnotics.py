# Genera los temas "TECNOTICS vertical" y "TECNOTICS horizontal" (src/Turing.Themes/Builtin/tecnotics-*):
# fondo.png con la marca y la decoración (anillos, tarjetas, circuitos) + theme.json con los valores en vivo.
# Uso: python3 tools/generar_tema_tecnotics.py
import json, os, math, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
LOGO = os.path.join(ROOT, "src", "Turing.App", "Assets", "logo.png")
FONT = os.path.join(ROOT, "src", "Turing.Render", "Fonts", "Inter-Bold.otf")
OUT = os.path.join(ROOT, "src", "Turing.Themes", "Builtin")

CYAN, ORANGE = "#6EC6D9", "#F28C1E"
MUTED, DIM = "#7F98A6", "#1C4553"
TEMP_RULES = [{"max": 60, "color": CYAN}, {"max": 80, "color": "#FFC14D"}, {"color": "#FF5C5C"}]
S = 3  # el fondo se dibuja al triple y se reduce, para bordes suaves


def rgba(hex_, a=255):
    h = hex_.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def logo_parts():
    """Chip y palabra TECNOTICS por separado, con el interior blanco del chip transparente (para fondo oscuro)."""
    im = Image.open(LOGO).convert("RGBA")
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a and min(r, g, b) > 225:
                px[x, y] = (0, 0, 0, 0)
    chip, word = im.crop((0, 0, im.width, 441)), im.crop((0, 505, im.width, im.height))
    return chip.crop(chip.getbbox()), word.crop(word.getbbox())


def paste_h(base, img, x, y, h):
    w = round(img.width * h / img.height)
    base.alpha_composite(img.resize((w * S, h * S), Image.LANCZOS), (x * S, y * S))
    return w


class Bg:
    def __init__(self, w, h, seed):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w * S, h * S), rgba("#04070C"))
        self.glow = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.g = ImageDraw.Draw(self.glow)
        self.rnd = random.Random(seed)
        # Degradado vertical suave.
        for y in range(h * S):
            t = y / (h * S)
            c = tuple(int(a + (b - a) * t) for a, b in zip((5, 10, 16), (8, 18, 27)))
            self.d.line([(0, y), (w * S, y)], fill=c + (255,))
        self.circuits()
        self.corners()

    def circuits(self):
        """Pistas de circuito muy tenues, en ángulos de 0 y 45 grados."""
        layer = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        for _ in range(28):
            x, y = self.rnd.randrange(0, self.w), self.rnd.randrange(0, self.h)
            pts = [(x, y)]
            for _ in range(self.rnd.randint(2, 4)):
                dx, dy = self.rnd.choice([(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1)])
                n = self.rnd.randint(12, 45)
                x, y = x + dx * n, y + dy * n
                pts.append((x, y))
            d.line([(px * S, py * S) for px, py in pts], fill=rgba(CYAN, 22), width=S, joint="curve")
            ex, ey = pts[-1]
            d.ellipse([(ex - 2) * S, (ey - 2) * S, (ex + 2) * S, (ey + 2) * S], outline=rgba(CYAN, 35), width=S)
        self.img.alpha_composite(layer)

    def corners(self):
        L = 14
        for x, y, sx, sy in [(4, 4, 1, 1), (self.w - 5, 4, -1, 1), (4, self.h - 5, 1, -1), (self.w - 5, self.h - 5, -1, -1)]:
            self.d.line([(x * S, (y + sy * L) * S), (x * S, y * S), ((x + sx * L) * S, y * S)], fill=rgba(ORANGE), width=2 * S)

    def line(self, pts, color, width=1, glow=0):
        p = [(x * S, y * S) for x, y in pts]
        self.d.line(p, fill=color, width=width * S)
        if glow:
            self.g.line(p, fill=color, width=(width + glow) * S)

    def card(self, l, t, r, b, accent=CYAN):
        """Tarjeta con esquinas cortadas, borde tenue y marcas de color en dos esquinas."""
        c = 9
        poly = [(l + c, t), (r, t), (r, b - c), (r - c, b), (l, b), (l, t + c)]
        p = [(x * S, y * S) for x, y in poly]
        self.d.polygon(p, fill=rgba("#0A1822", 235))
        self.d.line(p + [p[0]], fill=rgba(DIM), width=S)
        self.line([(l, t + c + 12), (l, t + c), (l + c, t), (l + c + 12, t)], rgba(accent), 2, glow=3)
        self.line([(r, b - c - 12), (r, b - c), (r - c, b), (r - c - 12, b)], rgba(accent), 2, glow=3)

    def gauge(self, cx, cy, d, accent):
        """Pista del arco (el arco en vivo se dibuja encima), anillo exterior con marcas y halo."""
        r = d / 2
        self.g.ellipse([(cx - r - 6) * S, (cy - r - 6) * S, (cx + r + 6) * S, (cy + r + 6) * S], outline=rgba(accent, 70), width=8 * S)
        self.d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], outline=rgba("#10252E"), width=10 * S)
        ro = r + 11
        self.d.ellipse([(cx - ro) * S, (cy - ro) * S, (cx + ro) * S, (cy + ro) * S], outline=rgba(DIM), width=S)
        for i in range(60):
            a = math.radians(i * 6 - 90)
            long_ = i % 5 == 0
            r1, r2 = ro + 2, ro + (7 if long_ else 4)
            col = rgba(accent, 200) if long_ else rgba(DIM)
            self.d.line([((cx + r1 * math.cos(a)) * S, (cy + r1 * math.sin(a)) * S),
                         ((cx + r2 * math.cos(a)) * S, (cy + r2 * math.sin(a)) * S)], fill=col, width=S)
        ri = r - 13
        self.d.ellipse([(cx - ri) * S, (cy - ri) * S, (cx + ri) * S, (cy + ri) * S], outline=rgba(DIM), width=S)

    def text(self, x, y, s, size, color, spacing=0):
        font = ImageFont.truetype(FONT, size * S)
        for ch in s:
            self.d.text((x * S, y * S), ch, font=font, fill=color)
            x += font.getlength(ch) / S + spacing

    def save(self, path):
        glow = self.glow.filter(ImageFilter.GaussianBlur(5 * S))
        out = self.img.copy()
        out.alpha_composite(glow)  # halo encima: solo suma un brillo difuso alrededor de las líneas de color
        out.resize((self.w, self.h), Image.LANCZOS).convert("RGB").save(path)


def T(x, y, w, h, text, size, color="#FFFFFF", bold=False, align="left", **kw):
    e = {"type": "text" if "{" in text else "label", "x": x, "y": y, "w": w, "h": h, "text": text, "size": size,
         "color": color, "align": align, "bold": bold}
    e.update(kw)
    return e


def gauge_elements(kind, cx, cy, d, accent, info):
    r = d // 2
    return [
        {"type": "arc", "x": cx - r, "y": cy - r, "w": d, "h": d, "sensor": f"{kind}.load", "max": 100,
         "thickness": 10, "start": -90, "color": accent},
        T(cx - 40, cy - 34, 80, 16, kind.upper(), 13, accent, True, "center"),
        T(cx - 48, cy - 18, 96, 38, f"{{{kind}.load:0}}%", 30, "#FFFFFF", True, "center"),
        T(cx - 40, cy + 18, 80, 18, f"{{{kind}.temp:0}}°C", 14, CYAN, True, "center", colorRules=TEMP_RULES),
        T(cx - r - 14, cy + r + 18, d + 28, 16, info, 11, MUTED, False, "center"),
    ]


def bar_card(bg, l, t, r, title, value, sensor, accent):
    bg.card(l, t, r, t + 54, accent)
    return [
        T(l + 12, t + 8, 70, 18, title, 13, accent, True),
        T(l + 70, t + 8, r - l - 82, 18, value, 12, "#FFFFFF", True, "right"),
        {"type": "bar", "x": l + 12, "y": t + 34, "w": r - l - 24, "h": 8, "sensor": sensor, "max": 100,
         "color": accent, "backColor": "#132A35", "rounded": True},
    ]


def net_card(bg, l, t, r, b, size=17):
    bg.card(l, t, r, b, ORANGE)
    mid = (l + r) // 2
    return [
        T(l + 12, t + 8, 60, 18, "RED", 13, ORANGE, True),
        T(l + 12, t + 30, mid - l - 16, 14, "↑ SUBIDA", 10, MUTED, True),
        T(l + 12, t + 44, mid - l - 16, 24, "{net.up:0.00} MB/s", size, "#FFFFFF", True),
        T(mid + 4, t + 30, r - mid - 16, 14, "↓ BAJADA", 10, MUTED, True),
        T(mid + 4, t + 44, r - mid - 16, 24, "{net.down:0.00} MB/s", size, "#FFFFFF", True),
    ]


def header(bg, chip, word, w, chip_h, word_h, tagline_size):
    paste_h(bg.img, chip, 12, 9, chip_h)
    x = 12 + round(chip.width * chip_h / chip.height) + 10
    ww = paste_h(bg.img, word, x, 12, word_h)
    bg.text(x + 1, 14 + word_h + 4, "MONITOR DE SISTEMA", tagline_size, rgba(MUTED), spacing=1.6)
    y = 9 + chip_h + 8
    bg.line([(10, y), (w - 10, y)], rgba(DIM), 1)
    bg.line([(10, y), (70, y)], rgba(ORANGE), 2, glow=3)
    bg.line([(w - 70, y), (w - 10, y)], rgba(CYAN), 2, glow=3)
    return y, x + ww


def clock(w):
    return [T(w - 112, 8, 100, 30, "{time:HH:mm}", 26, "#FFFFFF", True, "right"),
            T(w - 132, 38, 120, 16, "{date:dd/MM/yyyy}", 11, MUTED, True, "right")]


def build(landscape):
    chip, word = logo_parts()
    W, H = (480, 320) if landscape else (320, 480)
    bg = Bg(W, H, 7 if landscape else 3)
    els = clock(W)
    cpu_info = "{cpu.clock.ghz:0.0} GHz · {cpu.power:0} W"
    gpu_info = "{gpu.mem.used.gb:0.0} GB · {gpu.power:0} W"
    if landscape:
        y, _ = header(bg, chip, word, W, 40, 20, 7)
        d, cy = 108, 150
        for kind, cx, acc, info in [("cpu", 76, CYAN, cpu_info), ("gpu", 212, ORANGE, gpu_info)]:
            bg.gauge(cx, cy, d, acc)
            els += gauge_elements(kind, cx, cy, d, acc, info)
        els += bar_card(bg, 284, 68, 470, "RAM", "{ram.used:0.0} GB · {ram.load:0}%", "ram.load", CYAN)
        els += bar_card(bg, 284, 130, 470, "DISCO", "{disk.used.gb:0} GB · {disk.used:0}%", "disk.used", ORANGE)
        els += net_card(bg, 284, 192, 470, 270, 15)
        els += [T(10, 290, 460, 16, "{model.cpu}  ·  {model.gpu}", 11, MUTED, False, "center")]
        bg.line([(10, 282), (470, 282)], rgba(DIM), 1)
    else:
        y, _ = header(bg, chip, word, W, 44, 18, 6)
        els = [T(W - 92, 8, 80, 28, "{time:HH:mm}", 22, "#FFFFFF", True, "right"),
               T(W - 112, 36, 100, 16, "{date:dd/MM/yyyy}", 10, MUTED, True, "right")]
        d, cy = 108, 146
        for kind, cx, acc, info in [("cpu", 82, CYAN, cpu_info), ("gpu", 238, ORANGE, gpu_info)]:
            bg.gauge(cx, cy, d, acc)
            els += gauge_elements(kind, cx, cy, d, acc, info)
        els += bar_card(bg, 10, 240, 310, "RAM", "{ram.used:0.0}/{ram.total:0} GB · {ram.load:0}%", "ram.load", CYAN)
        els += bar_card(bg, 10, 302, 310, "DISCO", "{disk.used.gb:0}/{disk.total.gb:0} GB · {disk.used:0}%", "disk.used", ORANGE)
        els += net_card(bg, 10, 364, 310, 440)
        els += [T(10, 452, 300, 16, "{model.cpu}", 11, MUTED, False, "center")]
    name = "TECNOTICS horizontal" if landscape else "TECNOTICS vertical"
    folder = os.path.join(OUT, "tecnotics-horizontal" if landscape else "tecnotics-vertical")
    os.makedirs(folder, exist_ok=True)
    bg.save(os.path.join(folder, "fondo.png"))
    theme = {"formatVersion": 1, "name": name, "orientation": "landscape" if landscape else "portrait",
             "background": {"color": "#04070C", "image": "fondo.png"}, "elements": els}
    with open(os.path.join(folder, "theme.json"), "w", encoding="utf-8") as f:
        json.dump(theme, f, ensure_ascii=False, indent=2)
    print("escrito", folder)


build(False)
build(True)
