using System;
using System.Threading;
using RsyncZilla;
using Xunit;

namespace RsyncZilla.Tests
{
    public class MainWindowTests
    {
        [Fact]
        public void MainWindow_ShouldInstantiateWithoutExceptions()
        {
            Exception? thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var app = System.Windows.Application.Current ?? new System.Windows.Application();
                    var window = new MainWindow();
                    window.Loaded += async (s, e) =>
                    {
                        await System.Threading.Tasks.Task.Delay(500);
                        window.Close();
                        System.Windows.Threading.Dispatcher.ExitAllFrames();
                    };
                    window.Show();
                    System.Windows.Threading.Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);

            if (thrown != null)
            {
                throw new Exception($"Fallo al instanciar MainWindow: {thrown.GetType().Name}: {thrown.Message}\n{thrown.StackTrace}", thrown);
            }
        }
        [Fact]
        public void ConnectionManagerDialog_ShouldInstantiateWithoutExceptions()
        {
            Exception? thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var app = System.Windows.Application.Current ?? new System.Windows.Application();
                    var service = new RsyncZilla.Services.ConnectionManagerService();
                    var dialog = new RsyncZilla.Views.ConnectionManagerDialog(service);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);

            if (thrown != null)
            {
                throw new Exception($"Fallo al instanciar ConnectionManagerDialog: {thrown.GetType().Name}: {thrown.Message}\n{thrown.StackTrace}", thrown);
            }
        }
    }
}
