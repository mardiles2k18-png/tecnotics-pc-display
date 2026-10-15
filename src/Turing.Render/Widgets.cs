using SkiaSharp;
using Turing.Sensors;

namespace Turing.Render;

/// <summary>Un elemento del tema que ocupa un rectángulo y se redibuja cuando cambia su "clave".</summary>
public abstract class Widget
{
    protected Widget(SKRectI bounds) => Bounds = bounds;

    public SKRectI Bounds { get; }

    /// <summary>Representación de lo que se va a dibujar; si no cambia, no se vuelve a enviar.</summary>
    public abstract string Key(SensorSnapshot s, DateTime now);

    public abstract void Draw(SKCanvas canvas, SensorSnapshot s, DateTime now);
}

/// <summary>Texto calculado a partir de los sensores (o la hora).</summary>
public sealed class TextWidget(
    SKRectI bounds,
    Func<SensorSnapshot, DateTime, string> text,
    float size,
    SKColor color,
    SKTextAlign align = SKTextAlign.Left,
    bool bold = false,
    Func<SensorSnapshot, SKColor?>? colorOf = null) : Widget(bounds)
{
    public override string Key(SensorSnapshot s, DateTime now) => text(s, now) + (colorOf?.Invoke(s)?.ToString() ?? "");

    public override void Draw(SKCanvas canvas, SensorSnapshot s, DateTime now)
    {
        using var font = Fonts.Create(size, bold);
        using var paint = new SKPaint { Color = colorOf?.Invoke(s) ?? color, IsAntialias = true };
        float x = align switch
        {
            SKTextAlign.Center => Bounds.MidX,
            SKTextAlign.Right => Bounds.Right,
            _ => Bounds.Left,
        };
        // Centrar verticalmente usando las métricas de la fuente.
        var m = font.Metrics;
        float y = Bounds.MidY - (m.Ascent + m.Descent) / 2;
        canvas.DrawText(text(s, now), x, y, align, font, paint);
    }
}

/// <summary>Barra horizontal de 0 a <paramref name="max"/>.</summary>
public sealed class BarWidget(SKRectI bounds, string sensor, float max, SKColor front, SKColor back) : Widget(bounds)
{
    private int FilledWidth(SensorSnapshot s)
    {
        float v = s.Get(sensor) ?? 0;
        return (int)Math.Round(Math.Clamp(v / max, 0, 1) * Bounds.Width);
    }

    public override string Key(SensorSnapshot s, DateTime now) => FilledWidth(s).ToString();

    public override void Draw(SKCanvas canvas, SensorSnapshot s, DateTime now)
    {
        float r = Bounds.Height / 2f;
        using var backPaint = new SKPaint { Color = back, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Bottom), r, r, backPaint);
        int w = FilledWidth(s);
        if (w <= 0)
            return;
        using var frontPaint = new SKPaint { Color = front, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(Bounds.Left, Bounds.Top, Bounds.Left + Math.Max(w, Bounds.Height), Bounds.Bottom), r, r, frontPaint);
    }
}

public static class Fonts
{
    // Inter va incluida en la app para que los temas se vean igual en cualquier PC.
    private static readonly SKTypeface Regular = Load("Fonts.Inter-Regular.otf");
    private static readonly SKTypeface Bold = Load("Fonts.Inter-Bold.otf");

    private static SKTypeface Load(string resource)
    {
        using var stream = typeof(Fonts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Falta la fuente {resource}.");
        return SKTypeface.FromStream(stream) ?? SKTypeface.Default;
    }

    public static SKFont Create(float size, bool bold) => new(bold ? Bold : Regular, size) { Edging = SKFontEdging.Antialias };
}
