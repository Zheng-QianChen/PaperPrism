using System.Runtime.InteropServices;
using EInkSmartInvert.Config;
using EInkSmartInvert.Diagnostics;
using EInkSmartInvert.UI;

namespace EInkSmartInvert;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;

    private static void LogBoot(string msg)
    {
        try
        {
            string logFile = Path.Combine(AppContext.BaseDirectory, "runtime.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] [Boot] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        LogBoot($"Main entered. Args=[{string.Join(", ", args)}], BaseDir={AppContext.BaseDirectory}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogBoot($"UnhandledException: {e.ExceptionObject}");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogBoot($"ThreadException: {e.Exception}");

        try
        {
            return MainCore(args);
        }
        catch (Exception ex)
        {
            LogBoot($"FATAL in Main: {ex}");
            return 1;
        }
    }

    private static int MainCore(string[] args)
    {
        // Ensure Per-Monitor DPI Awareness V2 for 1:1 physical pixel accuracy on high-DPI E-Ink screens
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        // CLI GPU Self-Test mode: EInkSmartInvert.exe --test-render <outputDir>
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--test-render", StringComparison.OrdinalIgnoreCase))
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                string outDir = (i + 1 < args.Length) ? args[i + 1] : Path.Combine(AppContext.BaseDirectory, "test-output");
                return SyntheticSelfTest.Run(outDir);
            }
            if (string.Equals(args[i], "--dump-ui", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Any(a => string.Equals(a, "--en", StringComparison.OrdinalIgnoreCase)))
                {
                    var en = new System.Globalization.CultureInfo("en-US");
                    System.Globalization.CultureInfo.CurrentCulture = en;
                    System.Globalization.CultureInfo.CurrentUICulture = en;
                }
                ApplicationConfiguration.Initialize();
                string outDir = (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    ? args[i + 1]
                    : Path.Combine(AppContext.BaseDirectory, "test-output");
                Directory.CreateDirectory(outDir);
                var uiSettings = new AppSettings { OnlyInvertListedApps = true };
                using var dummyEngine = new Core.OverlayEngine(uiSettings);
                using var sf = new SettingsForm(uiSettings, dummyEngine, _ => { });
                sf.StartPosition = FormStartPosition.Manual;
                sf.Location = new Point(80, 60);
                sf.Show();
                string[] names = { "ui_ch1_prism.png", "ui_ch2_apps.png", "ui_ch3_tuning.png" };
                sf.Bounds = new Rectangle(80, 60, 640, 570);
                sf.ApplyResponsiveLayout();
                for (int ch = 0; ch < 3; ch++)
                {
                    sf.ShowChapter(ch);
                    Application.DoEvents();
                    using var bmp = new Bitmap(sf.Width, sf.Height);
                    sf.DrawToBitmap(bmp, new Rectangle(0, 0, sf.Width, sf.Height));
                    bmp.Save(Path.Combine(outDir, names[ch]), System.Drawing.Imaging.ImageFormat.Png);
                }

                // Also dump a 2.25x High-DPI / High-Res scaled verification render (1440 x 1282)!
                sf.Bounds = new Rectangle(40, 40, 1440, 1282);
                sf.ApplyResponsiveLayout();
                sf.ShowChapter(1);
                Application.DoEvents();
                using (var hiBmp = new Bitmap(sf.Width, sf.Height))
                {
                    sf.DrawToBitmap(hiBmp, new Rectangle(0, 0, sf.Width, sf.Height));
                    hiBmp.Save(Path.Combine(outDir, "ui_hidpi_verify.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                sf.Close();
                return 0;
            }
            if (string.Equals(args[i], "--smoke-overlay", StringComparison.OrdinalIgnoreCase))
            {
                ApplicationConfiguration.Initialize();
                var testSettings = AppSettings.Load();
                using var engine = new Core.OverlayEngine(testSettings);
                using var timer = new System.Windows.Forms.Timer { Interval = 2500 };
                timer.Tick += (_, _) => Application.ExitThread();
                timer.Start();
                engine.Start();
                Application.Run();
                return 0;
            }
        }

        bool openUiOnStart = args.Any(a => string.Equals(a, "--open-ui", StringComparison.OrdinalIgnoreCase)) ||
                             !AppSettings.SettingsFileExists();
        const string ExitSignalName = "Local\\PaperPrism_ExitSignal";
        const string LegacyExitSignalName = "Local\\EInkSmartInvert_ExitSignal";
        const string MutexName = "Local\\PaperPrism_SingleInstance_Mutex";

        // Cleanly signal any legacy EInkSmartInvert instance during upgrade
        try
        {
            using var legacyEv = new EventWaitHandle(false, EventResetMode.AutoReset, LegacyExitSignalName);
            legacyEv.Set();
        }
        catch { }

        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        try
        {
            if (!createdNew)
            {
                LogBoot("Existing instance detected via mutex; signaling exit and terminating stale processes if needed...");
                exitEvent.Set();
                try
                {
                    createdNew = mutex.WaitOne(TimeSpan.FromSeconds(2));
                }
                catch (AbandonedMutexException)
                {
                    createdNew = true;
                }

                if (!createdNew)
                {
                    int myPid = Environment.ProcessId;
                    foreach (var procName in new[] { "PaperPrism", "EInkSmartInvert" })
                    {
                        foreach (var proc in System.Diagnostics.Process.GetProcessesByName(procName))
                        {
                            if (proc.Id != myPid)
                            {
                                try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                            }
                        }
                    }
                    try { createdNew = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
                    catch (AbandonedMutexException) { createdNew = true; }
                }

                if (!createdNew)
                {
                    LogBoot("Could not acquire mutex after cleanup.");
                    return 0;
                }
            }

            bool smokeTray = args.Any(a => string.Equals(a, "--smoke-tray", StringComparison.OrdinalIgnoreCase));
            exitEvent.Reset();
            ApplicationConfiguration.Initialize();
            var settings = AppSettings.Load();
            if (!AppSettings.SettingsFileExists())
                settings.Save();
            using var ctx = new TrayApplicationContext(settings, exitEvent, openUiOnStart);
            if (smokeTray)
            {
                var t = new System.Windows.Forms.Timer { Interval = 2000 };
                t.Tick += (_, _) => { t.Stop(); ctx.ExitThread(); };
                t.Start();
            }
            LogBoot("Entering Application.Run(ctx)...");
            Application.Run(ctx);
            LogBoot("Application.Run(ctx) exited cleanly.");
            return 0;
        }
        finally
        {
            if (createdNew)
                mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
