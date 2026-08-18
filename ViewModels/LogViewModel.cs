using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KfuPet.Ipc.Client;

namespace KfuPet_Tool.ViewModels
{
    /// <summary>
    /// 服务端日志窗口的视图模型，独立于主窗口的骨骼连接，
    /// 通过 LogPipeClient 单独订阅 KfuPet.Log 管道。
    /// </summary>
    public partial class LogViewModel : ObservableObject
    {
        private const int MaxLogCount = 2000;

        private LogPipeClient? _logClient;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = "未连接";

        public ObservableCollection<string> LogMessages { get; } = new();

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (IsLoading || IsConnected) return;

            IsLoading = true;
            StatusMessage = "正在连接...";
            try
            {
                var client = new LogPipeClient();
                client.LogReceived += OnLogReceived;
                client.Disconnected += OnDisconnected;

                await client.ConnectAsync();

                _logClient = client;
                IsConnected = true;
                StatusMessage = "已连接";
                AppendSystemLog("已连接到服务端日志");
            }
            catch (TimeoutException)
            {
                StatusMessage = "连接超时：KfuPet 日志服务未响应";
                AppendSystemLog("连接超时：KfuPet 日志服务未响应");
                MessageBox.Show("连接 KfuPet.Log 超时。\n请确认 KfuPet 已启动且日志服务已开启。",
                    "连接超时", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException)
            {
                StatusMessage = "连接被拒绝：日志管道访问权限不足";
                AppendSystemLog("连接被拒绝：日志管道访问权限不足");
                MessageBox.Show("连接 KfuPet.Log 被拒绝。\n请确认没有其他程序占用该日志管道。",
                    "连接被拒绝", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusMessage = $"连接失败：{ex.Message}";
                AppendSystemLog($"连接失败：{ex.Message}");
                MessageBox.Show($"连接失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            if (_logClient == null) return;

            _logClient.Dispose();
            _logClient = null;
            IsConnected = false;
            StatusMessage = "已断开";
            AppendSystemLog("已断开连接");
        }

        [RelayCommand]
        private void Clear()
        {
            LogMessages.Clear();
        }

        [RelayCommand]
        private void Save()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"KfuPet_ServerLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllLines(dialog.FileName, LogMessages);
                    AppendSystemLog("日志已保存到: " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"日志保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 窗口关闭时释放底层管道连接。
        /// </summary>
        public void Shutdown()
        {
            if (_logClient == null) return;

            _logClient.Dispose();
            _logClient = null;
            IsConnected = false;
        }

        private void OnLogReceived(object? sender, LogMessage message)
        {
            var entry = $"[{message.Timestamp.ToLocalTime():HH:mm:ss}] [{message.Level}] {message.Message}";
            Append(entry);
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (IsConnected)
                {
                    IsConnected = false;
                    StatusMessage = "连接已断开";
                    AppendSystemLog("服务端日志连接已断开");
                }
            });
        }

        private void AppendSystemLog(string message)
        {
            Append($"[{DateTime.Now:HH:mm:ss}] [系统] {message}");
        }

        private void Append(string entry)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Insert(0, entry);
                while (LogMessages.Count > MaxLogCount)
                    LogMessages.RemoveAt(LogMessages.Count - 1);
            });
        }
    }
}
