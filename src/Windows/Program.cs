using System.Diagnostics;

namespace TopoCursor;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // TopoCursor.exe --restore  → restores the system cursors and exits (useful if the process was killed)
        if (args.Contains("--restore", StringComparer.OrdinalIgnoreCase))
        {
            CursorHider.Restore();
            return 0;
        }

        // TopoCursor.exe --quit  → asks the running instance to close cleanly
        if (args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(TrayApp.QuitEventName);
                ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { /* no running instance */ }
            return 0;
        }

        // Intermediate process: launches the watchdog and exits immediately. That way the watchdog is "orphaned"
        // and isn't part of the process tree that `dotnet run`/Ctrl+C/the debugger kill along with the app.
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

        // Watchdog process: waits for the main instance to die and restores the cursors,
        // even if it was killed abruptly (Task Manager, debugger stop, Ctrl+C…).
        int wd = Array.FindIndex(args, a => a.Equals("--watchdog", StringComparison.OrdinalIgnoreCase));
        if (wd >= 0 && wd + 1 < args.Length && int.TryParse(args[wd + 1], out var pid))
        {
            try { Process.GetProcessById(pid).WaitForExit(); } catch { /* already exited */ }
            CursorHider.Restore();
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\TopoCursor.SingleInstance", out bool first);
        if (!first) return 0;

        // Clean up any state left over from a previous session
        CursorHider.Restore();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => CursorHider.Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => CursorHider.Restore();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            CursorHider.Restore();
            MessageBox.Show(e.Exception.ToString(), "TopoCursor – error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            // not essential
        }
    }
}
