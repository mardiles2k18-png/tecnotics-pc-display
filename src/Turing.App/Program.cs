using Turing.Engine;

namespace Turing.App;

internal static class Program
{
    /// <summary>Se pone a true para volver a abrir el programa al salir (p. ej. tras instalar el driver).</summary>
    public static bool RestartRequested;

    [STAThread]
    private static void Main()
    {
        Run();
        if (RestartRequested)
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
    }

    private static void Run()
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
