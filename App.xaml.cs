using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Scribo;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    public const string UNIQUE_MUTEX_NAME = "Scribo_SingleInstance_App_Mutex_99182";
    public const string UNIQUE_ACTIVATE_MSG = "SCRIBO_ACTIVATE_INSTANCE_MSG";

    protected override void OnStartup(StartupEventArgs e)
    {
        Logger.Log("App.OnStartup started");

        bool isNew;
        try
        {
            _singleInstanceMutex = new Mutex(true, UNIQUE_MUTEX_NAME, out isNew);
        }
        catch (Exception ex)
        {
            Logger.LogException("Mutex creation", ex);
            isNew = true;
        }

        if (!isNew)
        {
            Logger.Log("Another Scribo instance detected, signaling existing instance to restore.");
            try
            {
                int msg = NativeMethods.RegisterWindowMessage(UNIQUE_ACTIVATE_MSG);
                if (msg != 0)
                {
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("Instance broadcast activation", ex);
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // Global Exception Handlers
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);

        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            Logger.Log("MainWindow displayed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogException("MainWindow initialization", ex);
            MessageBox.Show(
                $"Scribo failed to start:\n\n{ex.Message}\n\nPlease check the log file at:\n{Logger.LogPath}",
                "Scribo Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log($"App.OnExit with code {e.ApplicationExitCode}");
        try
        {
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }
        catch { }

        base.OnExit(e);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Logger.LogException("AppDomain.UnhandledException", ex);
        if (e.IsTerminating)
        {
            Logger.Log("[FATAL] Process is terminating due to unhandled domain exception.");
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.LogException("DispatcherUnhandledException", e.Exception);
        // Prevent application crash for non-fatal UI exceptions
        e.Handled = true;
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.LogException("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
