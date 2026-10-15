using Turing.Engine;

namespace Turing.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Una sola instancia: dos programas no pueden usar el mismo puerto.
        using var mutex = new Mutex(true, @"Global\TuringMonitorApp", out bool first);
        if (!first)
        {
            MessageBox.Show("Turing Monitor ya se está ejecutando. Búscalo en la bandeja, junto al reloj.",
                "Turing Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.ThreadException += (_, e) => Log.Write($"Error en la interfaz: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Error no controlado: {e.ExceptionObject}");

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
