using SkiaSharp;
using Turing.Device;
using Turing.Render;

namespace Turing.Engine;

/// <summary>
/// Prueba visual: 8 recuadros con números que cambian, cada uno enviado de una forma distinta.
/// Al final todos muestran el mismo número; una foto de la pantalla dice qué formas funcionan.
/// </summary>
public static class Diagnostics
{
    private record Way(string Name, int MaxBytes, int PauseMs, int CellW = 0, int CellH = 0);

    private static readonly Way[] Ways =
    [
        new("zona entera", 0, 0),
        new("entera + pausa 20 ms", 0, 20),
        new("tiras de 4 KB", 4096, 0),
        new("tiras de 2 KB", 2048, 0),
        new("tiras de 1 KB", 1024, 0),
        new("tiras de 1 fila", 1, 0),
        new("celdas de 20x25", 0, 0, 20, 25),
        new("tiras 1 KB + pausa 5 ms", 1024, 5),
    ];

    public static void Run(TuringRevA device, Orientation orientation)
    {
        device.ScreenOn();
        device.SetOrientation(orientation);
        int w = device.Width, h = device.Height;
        bool landscape = w > h;
        int cols = landscape ? 3 : 2, boxW = landscape ? 150 : 150, boxH = 50, labelH = 18;
        int gapX = (w - cols * boxW) / (cols + 1);
        var boxes = new List<SKRectI>();
        for (int i = 0; i < Ways.Length; i++)
        {
            int c = i % cols, r = i / cols;
            int x = gapX + c * (boxW + gapX), y = 34 + r * (boxH + labelH + 12) + labelH;
            boxes.Add(new SKRectI(x, y, x + boxW, y + boxH));
        }

        // Fondo con títulos: se envía como pantalla completa, que sabemos que funciona.
        using var frame = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(frame))
        {
            c.Clear(new SKColor(0x10, 0x18, 0x28));
            using var title = Fonts.Create(15, true);
            using var small = Fonts.Create(11, false);
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var muted = new SKPaint { Color = new SKColor(0x9A, 0xAB, 0xC8), IsAntialias = true };
            c.DrawText($"Diagnóstico {(landscape ? "horizontal" : "vertical")}", w / 2f, 22, SKTextAlign.Center, title, white);
            for (int i = 0; i < Ways.Length; i++)
                c.DrawText($"{i + 1}. {Ways[i].Name}", boxes[i].Left, boxes[i].Top - 5, SKTextAlign.Left, small, muted);
        }
        Send(device, frame, new SKRectI(0, 0, w, h), new Way("", 0, 0));

        // 15 rondas de números cambiantes y al final "1234" en todos.
        var rnd = new Random(1);
        for (int round = 0; round <= 15; round++)
        {
            string text = round == 15 ? "1234" : rnd.Next(1000, 9999).ToString();
            for (int i = 0; i < Ways.Length; i++)
            {
                DrawBox(frame, boxes[i], text);
                Send(device, frame, boxes[i], Ways[i]);
            }
        }
    }

    private static void DrawBox(SKBitmap frame, SKRectI box, string text)
    {
        using var c = new SKCanvas(frame);
        c.ClipRect(box);
        c.Clear(new SKColor(0x22, 0x30, 0x4C));
        using var font = Fonts.Create(38, true);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var m = font.Metrics;
        c.DrawText(text, box.MidX, box.MidY - (m.Ascent + m.Descent) / 2, SKTextAlign.Center, font, paint);
    }

    private static void Send(TuringRevA device, SKBitmap frame, SKRectI r, Way way)
    {
        device.MaxBytesPerCommand = way.MaxBytes;
        device.PauseAfterBitmapMs = way.PauseMs;
        int cw = way.CellW > 0 ? way.CellW : r.Width, ch = way.CellH > 0 ? way.CellH : r.Height;
        for (int y = r.Top; y < r.Bottom; y += ch)
            for (int x = r.Left; x < r.Right; x += cw)
            {
                var cell = new SKRectI(x, y, Math.Min(x + cw, r.Right), Math.Min(y + ch, r.Bottom));
                device.DrawRgb565(cell.Left, cell.Top, cell.Width, cell.Height, Extract(frame, cell));
            }
    }

    private static byte[] Extract(SKBitmap frame, SKRectI r)
    {
        var src = frame.GetPixelSpan();
        var dst = new byte[r.Width * r.Height * 2];
        for (int row = 0; row < r.Height; row++)
            Rgb565.FromBgra32(src.Slice((r.Top + row) * frame.RowBytes + r.Left * 4, r.Width * 4), dst.AsSpan(row * r.Width * 2, r.Width * 2));
        return dst;
    }
}
