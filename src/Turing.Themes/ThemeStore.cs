namespace Turing.Themes;

/// <summary>
/// Carpeta de temas: cada subcarpeta con un theme.json es un tema. La primera vez se copian los temas incluidos.
/// </summary>
public sealed class ThemeStore(string folder)
{
    public const string FileName = "theme.json";

    /// <summary>Tema que se usa la primera vez que se abre la app (con la marca TECNOTICS).</summary>
    public const string DefaultTheme = "TECNOTICS horizontal";

    public string Folder { get; } = folder;

    /// <summary>Copia los temas incluidos en la app que todavía no existan en la carpeta.</summary>
    public void InstallBuiltins()
    {
        Directory.CreateDirectory(Folder);
        var asm = typeof(ThemeStore).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(r => r.StartsWith("Builtin.") && r.EndsWith(".json")))
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            var theme = Theme.Parse(new StreamReader(stream).ReadToEnd());
            var dir = Path.Combine(Folder, theme.Name);
            var file = Path.Combine(dir, FileName);
            if (File.Exists(file))
                continue;
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, theme.ToJson());
        }

        // Temas en carpeta: recursos "Builtin/<carpeta>/<archivo>" (theme.json + imágenes).
        var folders = asm.GetManifestResourceNames()
            .Where(r => r.StartsWith("Builtin/"))
            .Select(r => (Res: r, Parts: r["Builtin/".Length..].Replace('\\', '/').Split('/', 2)))
            .Where(x => x.Parts.Length == 2)
            .GroupBy(x => x.Parts[0]);
        foreach (var group in folders)
        {
            var themeRes = group.FirstOrDefault(x => x.Parts[1] == FileName).Res;
            if (themeRes is null)
                continue;
            using var stream = asm.GetManifestResourceStream(themeRes)!;
            var theme = Theme.Parse(new StreamReader(stream).ReadToEnd());
            var dir = Path.Combine(Folder, theme.Name);
            if (File.Exists(Path.Combine(dir, FileName)))
                continue;
            Directory.CreateDirectory(dir);
            foreach (var (res, parts) in group)
            {
                using var src = asm.GetManifestResourceStream(res)!;
                using var dst = File.Create(Path.Combine(dir, parts[1]));
                src.CopyTo(dst);
            }
        }
    }

    /// <summary>Nombres de los temas disponibles (nombre de la carpeta), ordenados.</summary>
    public IReadOnlyList<string> List() =>
        Directory.Exists(Folder)
            ? Directory.GetDirectories(Folder)
                .Where(d => File.Exists(Path.Combine(d, FileName)))
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : [];

    public Theme Load(string name)
    {
        var dir = Path.Combine(Folder, name);
        var theme = Theme.Parse(File.ReadAllText(Path.Combine(dir, FileName)));
        theme.Folder = dir;
        if (string.IsNullOrWhiteSpace(theme.Name))
            theme.Name = name;
        return theme;
    }
}
