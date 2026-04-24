using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.WPF.Properties;
using Virtuagym.CheckIn.WPF.Services;

namespace Virtuagym.CheckIn.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Initialize localization before any window opens
            string lang = Settings.Default.AppLanguage;
            if (string.IsNullOrEmpty(lang)) lang = "en";
            L.Initialize(lang);
            try
            {
                var culture = new CultureInfo(lang);
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
            }
            catch
            {
                // Unbekannte Sprache ΓåÆ System-Default beibehalten
            }

            _mutex = new Mutex(true, Constants.MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                MessageBox.Show(L.T("App_AlreadyRunning"), Constants.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Resolve assemblies from the shared framework that WPF clipboard needs
            AppDomain.CurrentDomain.AssemblyResolve += (s, args) =>
            {
                var name = new AssemblyName(args.Name);
                if (name.Name == "System.Reflection.Metadata")
                {
                    string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                    string path = Path.Combine(runtimeDir, "System.Reflection.Metadata.dll");
                    if (File.Exists(path))
                        return Assembly.LoadFrom(path);
                }
                return null;
            };

            // Globale Exception-Handler registrieren
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Alte Log-Dateien bereinigen
            int deleted = LogCleanupService.CleanupOldLogs(Settings.Default.LogRetentionDays);
            if (deleted > 0)
            {
                try
                {
                    Directory.CreateDirectory(Constants.LogFolder);
                    string logFile = Path.Combine(Constants.LogFolder,
                        DateTime.Now.ToString(Constants.LogDateFormat) + Constants.LogFileSuffix);
                    File.AppendAllText(logFile,
                        $"{DateTime.Now.ToString(Constants.LogTimestampFormat)} # Info: {deleted} alte Log-Datei(en) bereinigt.{Environment.NewLine}");
                }
                catch
                {
                    // Log-Cleanup-Meldung ist nicht kritisch
                }            
            }

            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Silently handle assembly load failures (e.g. clipboard-related) without logging as fatal
            if (e.Exception is System.IO.FileNotFoundException or System.IO.FileLoadException)
            {
                e.Handled = true;
                return;
            }

            LogFatalException("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogFatalException("UnobservedTaskException", e.Exception?.InnerException ?? e.Exception);
            e.SetObserved();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogFatalException("UnhandledException", e.ExceptionObject as Exception);
        }

        private static void LogFatalException(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Constants.LogFolder);
                string logFile = Path.Combine(Constants.LogFolder,
                    DateTime.Now.ToString(Constants.LogDateFormat) + Constants.LogFileSuffix);
                string message = $"{DateTime.Now.ToString(Constants.LogTimestampFormat)} # Error!: [{source}] {ex?.Message}{Environment.NewLine}{ex?.StackTrace}{Environment.NewLine}";
                File.AppendAllText(logFile, message);
            }
            catch
            {
                // Letzter Rettungsversuch ΓÇô hier darf nichts mehr schiefgehen
            }        
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await CheckinHardwareTriggerService.WaitForPendingGateAsync(TimeSpan.FromSeconds(10));
            CheckinHardwareTriggerService.ResetJablotronClient();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
