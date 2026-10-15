using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Turing.Engine;

namespace Turing.App;

/// <summary>
/// Driver PawnIO, que LibreHardwareMonitor necesita para leer temperatura, consumo y frecuencia de la CPU.
/// No se incluye en el programa: se descarga del sitio oficial (pawnio.eu, publicado en GitHub por su autor),
/// se comprueba su firma digital y se instala sin preguntas con "-install -silent".
/// </summary>
internal static class SensorDriver
{
    private const string SetupUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    /// <summary>Versión instalada, o null si no está (misma comprobación que hace LibreHardwareMonitor).</summary>
    public static Version? InstalledVersion()
    {
        foreach (var view in new[] { RegistryView.Default, RegistryView.Registry64 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hklm.OpenSubKey(UninstallKey);
            if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out var v))
                return v;
        }
        return null;
    }

    public static bool IsInstalled => InstalledVersion() is not null;

    /// <summary>
    /// Pregunta, descarga e instala con una ventanita de progreso. Devuelve true si quedó instalado.
    /// </summary>
    public static bool InstallInteractive(IWin32Window? owner)
    {
        using var form = new Form
        {
            Text = "Turing Monitor",
            Icon = Branding.AppIcon(new Size(32, 32)),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(380, 90),
            Font = new Font("Segoe UI", 9f),
            ShowInTaskbar = true,
        };
        var label = new Label { Text = "Descargando el driver de sensores (PawnIO)...", AutoSize = false, Bounds = new Rectangle(16, 14, 348, 22) };
        var bar = new ProgressBar { Style = ProgressBarStyle.Marquee, Bounds = new Rectangle(16, 44, 348, 18) };
        form.Controls.Add(label);
        form.Controls.Add(bar);

        string? error = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var path = await Download();
                label.Text = "Instalando el driver de sensores...";
                if (!VerifySignature(path))
                    throw new InvalidOperationException("el instalador descargado no tiene una firma digital válida.");
                await Task.Run(() => RunSetup(path));
                TryDelete(path);
                if (!IsInstalled)
                    throw new InvalidOperationException("el instalador terminó pero el driver no aparece instalado.");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write($"Instalación de PawnIO: {ex}");
            }
            form.Close();
        };
        form.ShowDialog(owner);

        if (error is null)
        {
            Log.Write($"PawnIO {InstalledVersion()} instalado.");
            return true;
        }

        MessageBox.Show(owner,
            $"No pude instalar el driver de sensores: {error}\n\nPuedes instalarlo a mano desde https://pawnio.eu " +
            "y volver a abrir Turing Monitor. Mientras tanto la app funciona, pero sin temperatura ni consumo de CPU.",
            "Turing Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private static async Task<string> Download()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PawnIO_setup_{Guid.NewGuid():N}.exe");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TuringMonitor");
        using var response = await http.GetAsync(SetupUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var file = File.Create(path))
            await response.Content.CopyToAsync(file);
        return path;
    }

    private static void RunSetup(string path)
    {
        using var p = Process.Start(new ProcessStartInfo(path, "-install -silent") { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("no se pudo iniciar el instalador.");
        if (!p.WaitForExit(TimeSpan.FromMinutes(3)))
            throw new TimeoutException("el instalador tardó demasiado.");
        Log.Write($"PawnIO_setup terminó con código {p.ExitCode}.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // --- Comprobación de la firma Authenticode con WinVerifyTrust ---

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;          // 2 = sin interfaz
        public uint fdwRevocationChecks; // 0 = ninguna
        public uint dwUnionChoice;       // 1 = archivo
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

    private static bool VerifySignature(string path)
    {
        var fileInfo = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = path };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,
                dwUnionChoice = 1,
                pFile = pFile,
            };
            return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }
}
