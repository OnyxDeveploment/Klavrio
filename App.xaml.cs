using System.Windows;
using System.Windows.Threading;

namespace Klavrio;

public partial class App : System.Windows.Application
{
    private Mutex? singleInstance;
    private bool sessionStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool startedWithWindows = e.Args.Any(argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
        singleInstance = new Mutex(true, @"Local\Klavrio.Desktop.7F80B9CE", out bool firstInstance);
        if (!firstInstance)
        {
            if (!startedWithWindows)
                System.Windows.MessageBox.Show("Klavrio is already running. Check the notification area near the clock.",
                    "Klavrio", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLogger.Record("AppDomain unhandled", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()), true);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLogger.Record("Unobserved task", args.Exception);
            args.SetObserved();
        };

        try
        {
            CrashLogger.StartSession();
            sessionStarted = true;
            ShortcutManager.EnsureUserLaunchShortcuts();
            WinApi.ValidateInterop();
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            Report(exception, "Startup");
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (sessionStarted) CrashLogger.EndSession();
        singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception, "WPF dispatcher");
        e.Handled = true;
    }

    private static void Report(Exception exception, string source)
    {
        CrashLogger.Record(source, exception, true);
        try
        {
            System.Windows.MessageBox.Show($"Klavrio encountered an error.\n\nA crash report was saved in:\n{CrashLogger.DirectoryPath}\n\n{exception.Message}",
                "Klavrio error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }
}
