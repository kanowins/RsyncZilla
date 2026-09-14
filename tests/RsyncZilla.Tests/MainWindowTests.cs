using System;
using System.IO;
using System.Linq;
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
                    Assert.Contains(vm.AppVersion, vm.FooterInfo);
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
        [Fact]
        public void LocalFileService_RenameItem_ShouldRenameFileAndDirectorySuccessfully()
        {
            var service = new RsyncZilla.Services.LocalFileService();
            var tempDir = Path.Combine(Path.GetTempPath(), "RsyncZillaTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Test file rename
                var file1 = Path.Combine(tempDir, "old_file.txt");
                File.WriteAllText(file1, "Hello World");
                service.RenameItem(file1, "new_file.txt", false);
                var renamedFile = Path.Combine(tempDir, "new_file.txt");
                Assert.False(File.Exists(file1));
                Assert.True(File.Exists(renamedFile));
                Assert.Equal("Hello World", File.ReadAllText(renamedFile));

                // Test folder rename
                var folder1 = Path.Combine(tempDir, "old_folder");
                Directory.CreateDirectory(folder1);
                File.WriteAllText(Path.Combine(folder1, "inner.txt"), "Inner");
                service.RenameItem(folder1, "new_folder", true);
                var renamedFolder = Path.Combine(tempDir, "new_folder");
                Assert.False(Directory.Exists(folder1));
                Assert.True(Directory.Exists(renamedFolder));
                Assert.True(File.Exists(Path.Combine(renamedFolder, "inner.txt")));

                // Test robust delete
                service.DeleteItem(renamedFolder, true);
                Assert.False(Directory.Exists(renamedFolder));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
