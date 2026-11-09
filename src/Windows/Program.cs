using System.Diagnostics;

namespace TopoCursor;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // TopoCursor.exe --restore  → rimette i cursori di sistema ed esce (utile se il processo è stato ucciso)
        if (args.Contains("--restore", StringComparer.OrdinalIgnoreCase))
        {
            CursorHider.Restore();
            return 0;
        }

        // TopoCursor.exe --quit  → chiede all'istanza in esecuzione di chiudersi in modo pulito
        if (args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(TrayApp.QuitEventName);
                ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { /* nessuna istanza attiva */ }
            return 0;
        }

        // Processo intermedio: lancia il guardiano ed esce subito. Così il guardiano resta "orfano"
        // e non fa parte dell'albero di processi che `dotnet run`/Ctrl+C/il debugger uccidono insieme all'app.
        int sp = Array.FindIndex(args, a => a.Equals("--spawn-watchdog", StringComparison.OrdinalIgnoreCase));
        if (sp >= 0 && sp + 1 < args.Length)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--watchdog {args[sp + 1]}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch { }
            return 0;
        }

        // Processo "guardiano": aspetta la morte dell'istanza principale e ripristina i cursori,
        // anche se quella è stata terminata brutalmente (Task Manager, stop del debugger, Ctrl+C…).
        int wd = Array.FindIndex(args, a => a.Equals("--watchdog", StringComparison.OrdinalIgnoreCase));
        if (wd >= 0 && wd + 1 < args.Length && int.TryParse(args[wd + 1], out var pid))
        {
            try { Process.GetProcessById(pid).WaitForExit(); } catch { /* già terminato */ }
            CursorHider.Restore();
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\TopoCursor.SingleInstance", out bool first);
        if (!first) return 0;

        // Pulisce un eventuale stato lasciato da una sessione precedente
        CursorHider.Restore();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => CursorHider.Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => CursorHider.Restore();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            CursorHider.Restore();
            MessageBox.Show(e.Exception.ToString(), "TopoCursor – errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        };

        StartWatchdog();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            ConfigStore.Init();
            using var app = new TrayApp();
            Application.Run(app);
        }
        finally
        {
            CursorHider.Restore();
        }
        return 0;
    }

    static void StartWatchdog()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo(exe, $"--spawn-watchdog {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch
        {
            // non fondamentale
        }
    }
}
