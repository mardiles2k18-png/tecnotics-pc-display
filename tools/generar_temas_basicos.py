# Genera los temas "Básico vertical" y "Básico horizontal" (src/Turing.Themes/Builtin) con el diseño de la fase 2.
# Uso: python3 tools/generar_temas_basicos.py
import json, os

MUTED = "#8A9BB8"
TEMP_RULES = [{"max": 60, "color": "#76D66F"}, {"max": 80, "color": "#FFC14D"}, {"color": "#FF5C5C"}]


def panel(l, t, r, b, title, accent):
    return [
        {"type": "panel", "x": l, "y": t, "w": r - l, "h": b - t, "color": "#18233A", "borderColor": "#2A3A5C", "radius": 10},
        {"type": "label", "x": l + 14, "y": t + 12, "w": 60, "h": 22, "text": title, "size": 18, "bold": True, "color": accent},
    ]


def device_card(l, t, r, kind, accent, extra):
    mid = (l + r) // 2
    return panel(l, t, r, t + 175, kind.upper(), accent) + [
        {"type": "label", "x": l + 14, "y": t + 44, "w": 80, "h": 16, "text": "Uso", "size": 12, "color": MUTED},
        {"type": "label", "x": r - 114, "y": t + 44, "w": 100, "h": 16, "text": "Temperatura", "size": 12, "color": MUTED, "align": "right"},
        {"type": "text", "x": l + 62, "y": t + 10, "w": r - 10 - (l + 62), "h": 24, "text": "{model.%s}" % kind, "size": 13, "color": MUTED},
        {"type": "text", "x": l + 10, "y": t + 60, "w": mid - (l + 10), "h": 50, "text": "{%s.load:0}%%" % kind, "size": 40, "bold": True},
        {"type": "text", "x": mid, "y": t + 60, "w": r - 10 - mid, "h": 50, "text": "{%s.temp:0}°C" % kind, "size": 40, "bold": True,
         "align": "right", "colorRules": TEMP_RULES},
        {"type": "bar", "x": l + 14, "y": t + 128, "w": r - l - 28, "h": 16, "sensor": "%s.load" % kind, "color": accent, "backColor": "#26334F"},
        {"type": "text", "x": l + 10, "y": t + 150, "w": r - l - 20, "h": 22, "text": extra, "size": 13, "color": MUTED},
    ]


def ram_card(l, t, r):
    return panel(l, t, r, t + 62, "RAM", "#C08BFF") + [
        {"type": "text", "x": l + 70, "y": t + 8, "w": r - 14 - (l + 70), "h": 26, "size": 16, "align": "right",
         "text": "{ram.used:0.0} / {ram.total:0.0} GB  ·  {ram.load:0}%"},
        {"type": "bar", "x": l + 14, "y": t + 40, "w": r - l - 28, "h": 12, "sensor": "ram.load", "color": "#C08BFF", "backColor": "#26334F"},
    ]


def theme(name, landscape):
    w = 480 if landscape else 320
    cpu = (10, 40, 235) if landscape else (10, 40, 310)
    gpu = (245, 40, 470) if landscape else (10, 225, 310)
    ram = (10, 235, 470) if landscape else (10, 408, 310)
    return {
        "formatVersion": 1,
        "name": name,
        "orientation": "landscape" if landscape else "portrait",
        "background": {"color": "#0B1220", "gradientTo": "#101A2E"},
        "elements":
            [{"type": "text", "x": 0, "y": 4, "w": w, "h": 30, "text": "{time:HH:mm}", "size": 22, "bold": True, "align": "center"}]
            + device_card(cpu[0], cpu[1], cpu[2], "cpu", "#3DA9FC", "{cpu.clock.ghz:0.0} GHz  ·  {cpu.power:0} W")
            + device_card(gpu[0], gpu[1], gpu[2], "gpu", "#76D66F", "VRAM {gpu.mem.used.gb:0.0}/{gpu.mem.total.gb:0} GB  ·  {gpu.power:0} W")
            + ram_card(ram[0], ram[1], ram[2]),
    }


out = os.path.join(os.path.dirname(__file__), "..", "src", "Turing.Themes", "Builtin")
for name, landscape, file in [("Básico vertical", False, "basico-vertical.json"), ("Básico horizontal", True, "basico-horizontal.json")]:
    with open(os.path.join(out, file), "w", encoding="utf-8") as f:
        json.dump(theme(name, landscape), f, ensure_ascii=False, indent=2)
    print("escrito", file)
