using System.Diagnostics;
using System.Security;
using System.Text;

namespace Turing.Engine;

/// <summary>
/// Arranque con Windows mediante una tarea programada "al iniciar sesión" con privilegios máximos,
/// igual que el software original. Así no aparece el aviso de administrador en cada arranque.
/// </summary>
public static class AutoStart
{
    public const string TaskName = "Turing Monitor";

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void Enable(string exePath)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Monitor de estado en la pantalla Turing Smart Screen.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT10S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exePath)}</Command><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exePath))}</WorkingDirectory></Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "turing-monitor-task.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            if (RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F") != 0)
                throw new InvalidOperationException("Windows no aceptó la tarea programada.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Disable() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

    private static int RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(15000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
