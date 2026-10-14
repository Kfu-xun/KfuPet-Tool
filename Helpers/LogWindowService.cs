using System.Windows;

namespace KfuPet_Tool.Helpers
{
    /// <summary>
    /// 统一管理“服务端日志”窗口的唯一实例，供启动器与主程序共用，
    /// 保证任何入口打开的都是同一个日志窗口（已打开时仅激活）。
    /// </summary>
    public static class LogWindowService
    {
        private static LogWindow? _instance;

        public static void Show()
        {
            if (_instance == null)
            {
                _instance = new LogWindow();
                _instance.Closed += (_, _) => _instance = null;
            }

            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            _instance.Show();
            _instance.Activate();
        }
    }
}
