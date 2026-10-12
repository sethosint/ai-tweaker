using System.Security.Principal;
using AITweaker.App.Platform;

namespace AITweaker.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Global\AITweaker.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("AI Tweaker уже запущен.\nAI Tweaker is already running.", "AI Tweaker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("AI Tweaker работает только с правами администратора.\nRun AI Tweaker as administrator.", "AI Tweaker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.ThreadException += (_, e) => AppLog.Error("app", e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("app", e.ExceptionObject.ToString() ?? "unhandled");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("app", e.Exception.Message);
            e.SetObserved();
        };

        AppLog.Info("app", $"AI Tweaker started on {Environment.OSVersion} as {Environment.UserName}");
        Application.Run(new MainForm());
    }
}
