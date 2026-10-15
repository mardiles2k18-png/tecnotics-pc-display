using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;
using Turing.Render;
using Turing.Sensors;

namespace Turing.Themes;

/// <summary>Convierte un <see cref="Theme"/> en un <see cref="Renderer"/> listo para dibujar.</summary>
public static partial class ThemeLoader
{
    [GeneratedRegex(@"\{([a-z0-9_.]+)(?::([^}]*))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    public static Renderer CreateRenderer(Theme theme)
    {
        var widgets = new List<Widget>();
        foreach (var e in theme.Elements)
        {
            var bounds = new SKRectI(e.X, e.Y, e.X + e.W, e.Y + e.H);
            switch (e.Type.ToLowerInvariant())
            {
                case "text":
                    var template = e.Text ?? "";
                    var colorSensor = e.ColorSensor ?? FirstSensor(template);
                    Func<SensorSnapshot, SKColor?>? colorOf = e.ColorRules is { Count: > 0 } rules && colorSensor is not null
                        ? s => RuleColor(rules, s.Get(colorSensor))
                        : null;
                    widgets.Add(new TextWidget(bounds, (s, now) => Format(template, s, now), e.Size, ParseColor(e.Color),
                        ParseAlign(e.Align), e.Bold, colorOf));
                    break;
                case "bar":
                    widgets.Add(new BarWidget(bounds, e.Sensor ?? "", e.Max <= 0 ? 100 : e.Max, ParseColor(e.Color),
                        ParseColor(e.BackColor ?? "#26334F"), e.Rounded, LoadBitmap(theme, e.FrontImage), LoadBitmap(theme, e.BackImage)));
                    break;
                case "arc":
                    widgets.Add(new ArcWidget(new SKRect(e.X, e.Y, e.X + e.W, e.Y + e.H), e.Sensor ?? "", e.Max <= 0 ? 100 : e.Max,
                        ParseColor(e.Color), e.BackColor is null ? null : ParseColor(e.BackColor), e.Thickness, e.Start));
                    break;
            }
        }
        return new Renderer(theme.Width, theme.Height, c => DrawStatic(c, theme), widgets);
    }

    /// <summary>Dibuja el fondo y los elementos fijos (panel, label, image).</summary>
    private static void DrawStatic(SKCanvas c, Theme theme)
    {
        var bg = theme.Background;
        using (var paint = new SKPaint())
        {
            paint.Color = ParseColor(bg.Color);
            if (bg.GradientTo is not null)
                paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, theme.Height),
                    [ParseColor(bg.Color), ParseColor(bg.GradientTo)], SKShaderTileMode.Clamp);
            c.DrawRect(0, 0, theme.Width, theme.Height, paint);
        }
        if (bg.Image is not null)
            DrawImage(c, theme, bg.Image, new SKRect(0, 0, theme.Width, theme.Height));

        foreach (var e in theme.Elements)
        {
            var r = new SKRect(e.X, e.Y, e.X + e.W, e.Y + e.H);
            switch (e.Type.ToLowerInvariant())
            {
                case "panel":
                {
                    using var fill = new SKPaint { Color = ParseColor(e.Color), IsAntialias = true };
                    c.DrawRoundRect(r, e.Radius, e.Radius, fill);
                    if (e.BorderColor is not null)
                    {
                        using var edge = new SKPaint { Color = ParseColor(e.BorderColor), IsAntialias = true, IsStroke = true, StrokeWidth = 1 };
                        c.DrawRoundRect(r, e.Radius, e.Radius, edge);
                    }
                    break;
                }
                case "label":
                {
                    using var font = Fonts.Create(e.Size, e.Bold);
                    using var paint = new SKPaint { Color = ParseColor(e.Color), IsAntialias = true };
                    var align = ParseAlign(e.Align);
                    float x = align switch { SKTextAlign.Center => r.MidX, SKTextAlign.Right => r.Right, _ => r.Left };
                    var m = font.Metrics;
                    c.DrawText(e.Text ?? "", x, r.MidY - (m.Ascent + m.Descent) / 2, align, font, paint);
                    break;
                }
                case "image" when e.Image is not null:
                    DrawImage(c, theme, e.Image, r);
                    break;
            }
        }
    }

    private static SKBitmap? LoadBitmap(Theme theme, string? file)
    {
        if (file is null)
            return null;
        var path = theme.Folder is null ? file : Path.Combine(theme.Folder, file);
        return File.Exists(path) ? SKBitmap.Decode(path) : null;
    }

    private static void DrawImage(SKCanvas c, Theme theme, string file, SKRect dest)
    {
        var path = theme.Folder is null ? file : Path.Combine(theme.Folder, file);
        using var bmp = File.Exists(path) ? SKBitmap.Decode(path) : null;
        if (bmp is null)
            return;
        using var image = SKImage.FromBitmap(bmp);
        c.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    /// <summary>Sustituye {sensor:formato} por su valor. Valores sin leer se muestran como "--".</summary>
    public static string Format(string template, SensorSnapshot s, DateTime now) =>
        Placeholder().Replace(template, m =>
        {
            var id = m.Groups[1].Value.ToLowerInvariant();
            var fmt = m.Groups[2].Success ? m.Groups[2].Value : null;
            switch (id)
            {
                case "time":
                    return now.ToString(fmt ?? "HH:mm", CultureInfo.CurrentCulture);
                case "date":
                    return now.ToString(fmt ?? "d", CultureInfo.CurrentCulture);
                case "name.cpu":
                case "name.gpu":
                    return s.Names.GetValueOrDefault(id[5..], "");
                case "model.cpu":
                case "model.gpu":
                    return ShortName(s.Names.GetValueOrDefault(id[6..], ""));
            }
            return s.Get(id) is float v ? v.ToString(fmt ?? "0", CultureInfo.CurrentCulture) : "--";
        });

    private static string? FirstSensor(string template)
    {
        foreach (Match m in Placeholder().Matches(template))
        {
            var id = m.Groups[1].Value.ToLowerInvariant();
            if (id is not ("time" or "date") && !id.StartsWith("name.") && !id.StartsWith("model."))
                return id;
        }
        return null;
    }

    private static SKColor? RuleColor(List<ColorRule> rules, float? value)
    {
        if (value is not float v)
            return null;
        foreach (var r in rules)
            if (r.Max is null || v < r.Max)
                return ParseColor(r.Color);
        return null;
    }

    /// <summary>Quita la marca para que el modelo quepa: "NVIDIA GeForce RTX 5060 Ti" -> "RTX 5060 Ti".</summary>
    public static string ShortName(string name)
    {
        foreach (var prefix in new[] { "NVIDIA GeForce ", "NVIDIA ", "AMD Radeon ", "AMD ", "Intel(R) Core(TM) ", "Intel " })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name[prefix.Length..];
        return name;
    }

    public static SKColor ParseColor(string? hex) =>
        hex is not null && SKColor.TryParse(hex, out var c) ? c : SKColors.White;

    private static SKTextAlign ParseAlign(string? align) => align?.ToLowerInvariant() switch
    {
        "center" => SKTextAlign.Center,
        "right" => SKTextAlign.Right,
        _ => SKTextAlign.Left,
    };
}
