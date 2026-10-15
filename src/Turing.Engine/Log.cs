namespace Turing.Engine;

/// <summary>Registro sencillo en turing.log junto al .exe (se reinicia al pasar de 1 MB).</summary>
public static class Log
{
    private static readonly object Lock = new();
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "turing.log");

    public static void Write(string message)
    {
        lock (Lock)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                    info.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Sin permisos de escritura: no hay registro, pero la app sigue.
            }
        }
    }
}
