using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XTSPrimeMoverProject.Services;

namespace XTSPrimeMoverProject
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly string CrashLogPath =
            Path.Combine(AppContext.BaseDirectory, "crash.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ErrorHandlingService.Instance.ReportException(
                ErrorCategory.Unhandled,
                "UI Thread",
                e.Exception,
                wasRecovered: true);

            WriteCrashLog("DispatcherUnhandledException", e.Exception);

            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will attempt to continue. Check crash.log for details.",
                "XTS Prime Mover – Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                ErrorHandlingService.Instance.ReportException(
                    ErrorCategory.Unhandled,
                    "AppDomain",
                    ex,
                    wasRecovered: !e.IsTerminating);

                WriteCrashLog("AppDomainUnhandledException", ex);
            }

            if (e.IsTerminating)
            {
                MessageBox.Show(
                    $"A fatal error occurred and the application must close.\n\n{ex?.Message ?? "Unknown error"}\n\nSee crash.log for details.",
                    "XTS Prime Mover – Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            ErrorHandlingService.Instance.ReportException(
                ErrorCategory.Unhandled,
                "TaskScheduler",
                e.Exception,
                wasRecovered: true);

            WriteCrashLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void WriteCrashLog(string handler, Exception ex)
        {
            try
            {
                string entry = $"[{DateTime.UtcNow:O}] [{handler}] {ex}\n---\n";
                File.AppendAllText(CrashLogPath, entry);
            }
            catch
            {
                // Last-resort: cannot write crash log — swallow to avoid cascading failure.
            }
        }
    }
}
