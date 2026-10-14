using System.Configuration;
using System.Data;
using System.Windows;
using KfuPet_Tool.Helpers;

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

            // 启动阶段先弹选择窗，此时不能因“最后一个窗口关闭”而退出进程，
            // 因此临时切换为显式关闭，待目标窗口启动完成后再恢复。
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var launcher = new LauncherWindow();
            if (launcher.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            if (launcher.LaunchMainProgram)
            {
                new MainWindow().Show();
            }

            if (launcher.LaunchServerLog)
            {
                LogWindowService.Show();
            }

            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
    }

}
