using System.Configuration;
using System.Data;
using System.Windows;

namespace KfuPet_Tool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            const string mutexName = "KfuPet-Tool_SingleInstance";

            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("KfuPet-Tool 已经在运行中，不能同时打开多个工具。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }

}
