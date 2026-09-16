using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace RsyncZilla
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            EnsureStandardMenuDropAlignment();

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

        public static void EnsureStandardMenuDropAlignment()
        {
            try
            {
                static void ForceLeft()
                {
                    if (SystemParameters.MenuDropAlignment)
                    {
                        var field = typeof(SystemParameters).GetField(
                            "_menuDropAlignment",
                            BindingFlags.NonPublic | BindingFlags.Static);
                        field?.SetValue(null, false);
                    }
                }

                ForceLeft();
                SystemParameters.StaticPropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SystemParameters.MenuDropAlignment))
                    {
                        ForceLeft();
                    }
                };
            }
            catch
            {
                // Non-critical reflection safeguard
            }
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
