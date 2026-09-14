using System;
using System.Threading;
using RsyncZilla;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RsyncZilla.Tests
{
    public class MainWindowTests
    {
        [Fact]
        public void UiViewsAndDialogs_ShouldInstantiateWithoutExceptions()
        {
            Exception? thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var app = System.Windows.Application.Current ?? new System.Windows.Application();

                    var window = new MainWindow();
                    Assert.NotNull(window);
                    var vm = window.DataContext as ViewModels.MainViewModel;
                    Assert.NotNull(vm);
                    Assert.Contains("v1.0.0", vm.FooterInfo);
                    Assert.Contains("rsync 3.3.0", vm.FooterInfo);
                    Assert.Contains("SSH.NET", vm.FooterInfo);
                    window.Close();

                    var service = new RsyncZilla.Services.ConnectionManagerService();
                    var cmDialog = new RsyncZilla.Views.ConnectionManagerDialog(service);
                    Assert.NotNull(cmDialog);
                    cmDialog.Close();

                    var credDialog = new RsyncZilla.Views.ConnectCredentialsDialog("example.com", "testuser", 22, "My Test Site");
                    Assert.Equal("example.com", credDialog.Host);
                    Assert.Equal(22, credDialog.Port);
                    Assert.Equal("testuser", credDialog.Username);
                    credDialog.Close();

                    var siteDialog = new RsyncZilla.Views.SiteEditDialog();
                    Assert.NotNull(siteDialog);
                    siteDialog.Close();
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            })
            {
                IsBackground = true
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);

            if (thrown != null)
            {
                throw new Exception($"Fallo al instanciar UI views/dialogs: {thrown.GetType().Name}: {thrown.Message}\n{thrown.StackTrace}", thrown);
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
    }
}
