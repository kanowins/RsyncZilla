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

        [Fact]
        public void ConnectCredentialsDialog_ShouldInstantiateWithoutExceptions()
        {
            Exception? thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var app = System.Windows.Application.Current ?? new System.Windows.Application();
                    var dialog = new RsyncZilla.Views.ConnectCredentialsDialog("example.com", "testuser", 22, "My Test Site");
                    Assert.Equal("example.com", dialog.Host);
                    Assert.Equal(22, dialog.Port);
                    Assert.Equal("testuser", dialog.Username);
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
                throw new Exception($"Fallo al instanciar ConnectCredentialsDialog: {thrown.GetType().Name}: {thrown.Message}\n{thrown.StackTrace}", thrown);
            }
        }

        [Fact]
        public void SiteEditDialog_ShouldInstantiateWithoutExceptions()
        {
            Exception? thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var app = System.Windows.Application.Current ?? new System.Windows.Application();
                    var dialog = new RsyncZilla.Views.SiteEditDialog();
                    Assert.NotNull(dialog);
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
                throw new Exception($"Fallo al instanciar SiteEditDialog: {thrown.GetType().Name}: {thrown.Message}\n{thrown.StackTrace}", thrown);
            }
        }

        [Fact]
        public void LocalFileService_ThisPC_ShouldReturnAvailableDrives()
        {
            var service = new RsyncZilla.Services.LocalFileService();
            var (items, error) = service.GetDirectoryContents("This PC");

            Assert.Null(error);
            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.True(item.IsDrive));
            Assert.Contains(items, item => item.FullPath.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void LocalFileService_DriveRoot_ShouldHaveThisPCParent()
        {
            var service = new RsyncZilla.Services.LocalFileService();
            var (items, error) = service.GetDirectoryContents(@"C:\");

            Assert.Null(error);
            Assert.NotEmpty(items);
            var parent = items.FirstOrDefault(i => i.IsParent);
            Assert.NotNull(parent);
            Assert.Equal("..", parent.Name);
            Assert.Equal("This PC", parent.FullPath);
        }

        [Fact]
        public void MainViewModel_FooterInfo_ShouldContainVersionAndRsync()
        {
            var vm = new RsyncZilla.ViewModels.MainViewModel();
            Assert.Contains("v1.0.0", vm.FooterInfo);
            Assert.Contains("rsync 3.3.0", vm.FooterInfo);
            Assert.Contains("SSH.NET", vm.FooterInfo);
        }
    }
}
