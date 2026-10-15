using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turing.Themes;

/// <summary>
/// Un tema: un archivo theme.json dentro de su propia carpeta, junto a sus imágenes.
/// Coordenadas en píxeles de la pantalla (320x480 en vertical, 480x320 en horizontal).
/// </summary>
public sealed class Theme
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    /// <summary>"portrait" (vertical) o "landscape" (horizontal).</summary>
    public string Orientation { get; set; } = "portrait";
    public ThemeBackground Background { get; set; } = new();
    public List<ThemeElement> Elements { get; set; } = [];

    [JsonIgnore]
    public bool IsLandscape => Orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public int Width => IsLandscape ? 480 : 320;

    [JsonIgnore]
    public int Height => IsLandscape ? 320 : 480;

    /// <summary>Carpeta del tema (para resolver rutas de imágenes). No se guarda.</summary>
    [JsonIgnore]
    public string? Folder { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Theme Parse(string json) =>
        JsonSerializer.Deserialize<Theme>(json, Json) ?? throw new InvalidDataException("El tema está vacío.");

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public sealed class ThemeBackground
{
    /// <summary>Color de fondo "#RRGGBB".</summary>
    public string Color { get; set; } = "#000000";
    /// <summary>Si se indica, degradado vertical de <see cref="Color"/> a este color.</summary>
    public string? GradientTo { get; set; }
    /// <summary>Imagen de fondo (ruta relativa a la carpeta del tema), escalada a la pantalla.</summary>
    public string? Image { get; set; }
}

/// <summary>
/// Un elemento del tema. Según <see cref="Type"/>:
/// <list type="bullet">
/// <item>"panel": rectángulo fijo con esquinas redondeadas (color, borderColor, radius).</item>
/// <item>"label": texto fijo (text, size, color, align, bold).</item>
/// <item>"image": imagen fija (image).</item>
/// <item>"text": texto con valores, p. ej. "{cpu.load:0}%" o "{time:HH:mm}" (además colorRules).</item>
/// <item>"bar": barra de un sensor (sensor, max, color, backColor).</item>
/// </list>
/// </summary>
public sealed class ThemeElement
{
    public string Type { get; set; } = "text";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    public string? Text { get; set; }
    public float Size { get; set; } = 16;
    public string Color { get; set; } = "#FFFFFF";
    /// <summary>left, center o right.</summary>
    public string Align { get; set; } = "left";
    public bool Bold { get; set; }

    public string? Sensor { get; set; }
    public float Max { get; set; } = 100;
    public string? BackColor { get; set; }

    public string? BorderColor { get; set; }
    public float Radius { get; set; }

    public string? Image { get; set; }

    /// <summary>Colores según el valor: el primero cuyo "max" sea mayor que el valor (o sin max) gana.</summary>
    public List<ColorRule>? ColorRules { get; set; }
    /// <summary>Sensor que decide colorRules; por defecto el primero del texto.</summary>
    public string? ColorSensor { get; set; }
}

public sealed class ColorRule
{
    public float? Max { get; set; }
    public string Color { get; set; } = "#FFFFFF";
}
