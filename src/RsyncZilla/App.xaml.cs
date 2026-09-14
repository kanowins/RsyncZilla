using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RsyncZilla
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
                MessageBox.Show($"Error fatal en RsyncZilla:\n{args.Exception.Message}\n\nDetalles:\n{args.Exception}", "Error en RsyncZilla", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                var crashFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}\nInner: {ex?.InnerException}\n\n";
                File.AppendAllText(crashFile, text);
            }
            catch { }
        }
    }
}
