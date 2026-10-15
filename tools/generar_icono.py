import sys
from PIL import Image, ImageDraw
src = Image.open(sys.argv[1]).convert("RGBA")
chip = src.crop((0, 0, src.width, 441))
chip = chip.crop(chip.getbbox())
w, h = chip.size
# Rellenar de blanco lo que queda dentro del chip (el logo tiene el interior transparente).
mask = Image.new("L", (w + 2, h + 2), 0)
alpha = chip.split()[3]
pad = Image.new("L", (w + 2, h + 2), 0); pad.paste(alpha, (1, 1))
binary = pad.point(lambda a: 255 if a > 0 else 0)
ImageDraw.floodfill(binary, (0, 0), 128)
outside = binary.crop((1, 1, w + 1, h + 1)).point(lambda v: 255 if v == 128 else 0)
white = Image.new("RGBA", (w, h), (255, 255, 255, 255))
white.putalpha(outside.point(lambda v: 0 if v else 255))
filled = Image.alpha_composite(white, chip)
side = max(w, h)
sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
sq.paste(filled, ((side - w) // 2, (side - h) // 2))
sq.save("chip.png")
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
sq.resize((256, 256), Image.LANCZOS).save("tecnotics.ico", sizes=[(s, s) for s in sizes])
src.save("logo.png")
for s in (16, 32, 48): sq.resize((s, s), Image.LANCZOS).resize((s*8, s*8), Image.NEAREST).save(f"prev{s}.png")
print(w, h)
