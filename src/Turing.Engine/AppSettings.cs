using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turing.Engine;

/// <summary>Ajustes guardados en turing.json junto al .exe.</summary>
public sealed class AppSettings
{
    /// <summary>Nombre del tema activo (carpeta dentro de "temas").</summary>
    public string? Tema { get; set; }
    public int Brillo { get; set; } = 70;
    /// <summary>Gira la pantalla 180° (para montarla al revés).</summary>
    public bool Invertida { get; set; }
    /// <summary>Puerto fijo (p. ej. "COM6"); vacío para detectarlo solo.</summary>
    public string? Puerto { get; set; }
    /// <summary>False para dejar la pantalla apagada.</summary>
    public bool Encendida { get; set; } = true;
    /// <summary>True cuando ya se preguntó si arrancar con Windows.</summary>
    public bool ArranquePreguntado { get; set; }

    /// <summary>Solo para migrar el turing.json de la fase 2 ("vertical", "horizontal-invertida"...).</summary>
    public string? Orientacion { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string BaseFolder => AppContext.BaseDirectory;
    public static string FilePath => Path.Combine(BaseFolder, "turing.json");
    public static string ThemesFolder => Path.Combine(BaseFolder, "temas");

    public static AppSettings Load()
    {
        AppSettings s;
        try
        {
            s = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Write($"No pude leer {FilePath}: {ex.Message}");
            s = new AppSettings();
        }

        // Migración desde la fase 2.
        if (s.Tema is null && s.Orientacion is not null)
        {
            s.Tema = s.Orientacion.StartsWith("horizontal") ? "Básico horizontal" : "Básico vertical";
            s.Invertida = s.Orientacion.EndsWith("invertida");
        }
        s.Orientacion = null;
        return s;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            Log.Write($"No pude guardar {FilePath}: {ex.Message}");
        }
    }
}
