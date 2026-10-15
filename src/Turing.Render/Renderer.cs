using SkiaSharp;
using Turing.Device;
using Turing.Sensors;

namespace Turing.Render;

/// <summary>Una zona de la pantalla lista para enviar.</summary>
public sealed record DirtyRegion(int X, int Y, int Width, int Height, byte[] Rgb565);

/// <summary>
/// Dibuja un tema en un lienzo del tamaño de la pantalla y devuelve solo las zonas que cambiaron.
/// </summary>
public sealed class Renderer : IDisposable
{
    private readonly SKBitmap _background;
    private readonly SKBitmap _frame;
    private readonly IReadOnlyList<Widget> _widgets;
    private readonly Dictionary<Widget, string> _lastKeys = new();

    public Renderer(int width, int height, Action<SKCanvas> drawBackground, IReadOnlyList<Widget> widgets)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _background = new SKBitmap(info);
        using (var c = new SKCanvas(_background))
        {
            c.Clear(SKColors.Black);
            drawBackground(c);
        }
        _frame = new SKBitmap(info);
        _background.CopyTo(_frame);
        _widgets = widgets;
    }

    public int Width => _frame.Width;
    public int Height => _frame.Height;

    /// <summary>
    /// Dibuja el estado actual. Con <paramref name="full"/> devuelve la pantalla entera
    /// (al arrancar o reconectar); si no, una zona por cada widget que cambió.
    /// </summary>
    public List<DirtyRegion> Render(SensorSnapshot s, DateTime now, bool full)
    {
        var changed = new List<Widget>();
        using (var canvas = new SKCanvas(_frame))
        {
            foreach (var w in _widgets)
            {
                var key = w.Key(s, now);
                if (!full && _lastKeys.TryGetValue(w, out var last) && last == key)
                    continue;
                _lastKeys[w] = key;
                changed.Add(w);

                canvas.Save();
                canvas.ClipRect(w.Bounds);
                canvas.DrawBitmap(_background, 0, 0); // recortado por el clip
                w.Draw(canvas, s, now);
                canvas.Restore();
            }
        }

        if (full)
            return [Extract(new SKRectI(0, 0, Width, Height))];
        return changed.Select(w => Extract(w.Bounds)).ToList();
    }

    /// <summary>Guarda el fotograma actual como PNG (para depurar).</summary>
    public void SavePng(string path)
    {
        using var data = _frame.Encode(SKEncodedImageFormat.Png, 100);
        using var f = File.Create(path);
        data.SaveTo(f);
    }

    private DirtyRegion Extract(SKRectI r)
    {
        r.Intersect(new SKRectI(0, 0, Width, Height));
        var src = _frame.GetPixelSpan();
        var dst = new byte[r.Width * r.Height * 2];
        int stride = _frame.RowBytes;
        for (int row = 0; row < r.Height; row++)
        {
            var line = src.Slice((r.Top + row) * stride + r.Left * 4, r.Width * 4);
            Rgb565.FromBgra32(line, dst.AsSpan(row * r.Width * 2, r.Width * 2));
        }
        return new DirtyRegion(r.Left, r.Top, r.Width, r.Height, dst);
    }

    public void Dispose()
    {
        _background.Dispose();
        _frame.Dispose();
    }
}
