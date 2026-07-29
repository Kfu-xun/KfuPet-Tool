using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KfuPet.Ipc.Client;
using KfuPet_Tool.Helpers;
using KfuPet_Tool.Models;

namespace KfuPet_Tool.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private SkeletonPipeClient _pipeClient;

        [ObservableProperty]
        private ObservableCollection<BoneInfo> _rootBones = new();

        [ObservableProperty]
        private BoneInfo? _selectedBone;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _statusMessage = "未连接";

        [ObservableProperty]
        private ObservableCollection<string> _availablePipes = new();

        [ObservableProperty]
        private string? _selectedPipe;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private double _canvasWidth = 400;

        [ObservableProperty]
        private double _canvasHeight = 400;

        /// <summary>
        /// 日志输出集合，绑定到 UI 下方的日志面板。
        /// </summary>
        public ObservableCollection<string> LogMessages { get; } = new();

        public event EventHandler? PreviewUpdated;

        public MainViewModel()
        {
            _pipeClient = new SkeletonPipeClient();
            ScanPipes();
        }

        partial void OnIsConnectedChanged(bool value)
        {
            PreviewUpdated?.Invoke(this, EventArgs.Empty);
        }

        partial void OnRootBonesChanged(ObservableCollection<BoneInfo> value)
        {
            PreviewUpdated?.Invoke(this, EventArgs.Empty);
        }

        partial void OnSelectedPipeChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _pipeClient = new SkeletonPipeClient(value);
                StatusMessage = $"已选择管道：{value}";
            }
        }

        private void RaisePreviewUpdated()
        {
            PreviewUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 写入日志输出（带时间戳），同时保留到 Fire and forget 的最大 500 条。
        /// </summary>
        private void Log(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Insert(0, entry);
                while (LogMessages.Count > 500)
                    LogMessages.RemoveAt(LogMessages.Count - 1);
            });
        }

        [RelayCommand]
        private void ClearLog()
        {
            LogMessages.Clear();
            Log("日志已清除");
        }

        [RelayCommand]
        private void SaveLog()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"KfuPet_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllLines(dialog.FileName, LogMessages);
                    Log("日志已保存到: " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    Log("日志保存失败: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 处理操作异常：检测连接丢失并自动断开。
        /// </summary>
        /// <returns>true 表示连接已丢失并已自动断开</returns>
        private bool HandleOperationError(Exception ex, string operation)
        {
            Log($"{operation}失败：{ex.Message}");

            if (IsConnected)
            {
                DisconnectInternal("检测到连接丢失，已自动断开");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 手动断开连接（API 返回 false 时调用）。
        /// </summary>
        private void HandleDisconnect(string reason)
        {
            Log($"{reason}，连接已断开");
            DisconnectInternal("连接已断开（KfuPet 可能已关闭）");
        }

        private void DisconnectInternal(string statusMessage)
        {
            IsConnected = false;
            StatusMessage = statusMessage;
            RootBones.Clear();
            SelectedBone = null;
        }

        /// <summary>
        /// 校验数值是否为有限值（非 Infinity / NaN），避免超大输入导致 JSON 序列化失败。
        /// </summary>
        private bool IsFinite(double value, string fieldName)
        {
            if (double.IsInfinity(value) || double.IsNaN(value))
            {
                Log($"{fieldName} 值无效：{value}，请输入合理范围的数字");
                MessageBox.Show($"{fieldName} 值无效（{value}），请输入合理范围的数字。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        [RelayCommand]
        private void ScanPipes()
        {
            var pipes = PipeDiscoveryService.DiscoverKfuPetPipes();
            int processCount = PipeDiscoveryService.CountKfuPetProcesses();

            AvailablePipes.Clear();
            foreach (var pipe in pipes)
            {
                AvailablePipes.Add(pipe);
            }

            if (pipes.Count == 0)
            {
                SelectedPipe = null;
                StatusMessage = "未发现 KfuPet 管道，请确认 KfuPet 已运行";
                Log("扫描管道：未发现任何 KfuPet 管道");
            }
            else if (pipes.Count == 1)
            {
                SelectedPipe = pipes[0];
                if (processCount > 1)
                {
                    StatusMessage = $"已识别管道：{pipes[0]}（警告：检测到 {processCount} 个 KfuPet 进程，命令可能随机分配到不同实例，建议只保留一个）";
                    Log($"扫描管道：发现 {pipes[0]}，但检测到 {processCount} 个 KfuPet 进程（可能串扰）");
                }
                else
                {
                    StatusMessage = $"已自动识别管道：{pipes[0]}";
                    Log($"扫描管道：自动选择 {pipes[0]}");
                }
            }
            else
            {
                SelectedPipe = null;
                StatusMessage = $"发现 {pipes.Count} 个管道，请手动选择";
                Log($"扫描管道：发现 {pipes.Count} 个管道（{string.Join(", ", pipes)}），请手动选择");
            }
        }

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (IsLoading) return;

            if (string.IsNullOrEmpty(SelectedPipe))
            {
                StatusMessage = "请先选择一个管道";
                return;
            }

            IsLoading = true;
            try
            {
                var boneIds = await Task.Run(() => _pipeClient.GetBoneIds());
                if (boneIds.Count > 0)
                {
                    IsConnected = true;
                    StatusMessage = $"已连接：{SelectedPipe}";
                    Log($"已连接：{SelectedPipe}，获取到 {boneIds.Count} 个骨骼");
                    await LoadBoneTreeAsync();
                }
                else
                {
                    StatusMessage = "连接失败：未获取到骨骼数据";
                    Log("连接失败：服务端返回 0 个骨骼");
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"连接失败：{ex.Message}";
                Log($"连接失败：{ex.Message}");
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
            IsConnected = false;
            StatusMessage = "已断开";
            Log("已断开连接");
            RootBones.Clear();
            SelectedBone = null;
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (!IsConnected) return;
            Log("刷新骨骼树...");
            await LoadBoneTreeAsync();
        }

        private async Task LoadBoneTreeAsync()
        {
            try
            {
                var boneIds = await Task.Run(() => _pipeClient.GetBoneIds());
                var boneDict = new Dictionary<string, BoneInfo>();

                foreach (var id in boneIds)
                {
                    var bone = new BoneInfo { BoneId = id };
                    boneDict[id] = bone;
                }

                foreach (var bone in boneDict.Values)
                {
                    bone.BoneName = await Task.Run(() => _pipeClient.GetBoneName(bone.BoneId) ?? bone.BoneId);
                    bone.ParentBoneId = await Task.Run(() => _pipeClient.GetParentBoneId(bone.BoneId));

                    var pos = await Task.Run(() => _pipeClient.GetPosition(bone.BoneId));
                    if (pos.HasValue)
                    {
                        bone.PositionX = pos.Value.X;
                        bone.PositionY = pos.Value.Y;
                    }

                    bone.Rotation = await Task.Run(() => _pipeClient.GetRotation(bone.BoneId)) ?? 0;

                    var scale = await Task.Run(() => _pipeClient.GetScale(bone.BoneId));
                    if (scale.HasValue)
                    {
                        bone.ScaleX = scale.Value.X;
                        bone.ScaleY = scale.Value.Y;
                    }

                    bone.IsActive = await Task.Run(() => _pipeClient.IsActive(bone.BoneId)) ?? true;

                    var worldPos = await Task.Run(() => _pipeClient.GetWorldPosition(bone.BoneId));
                    if (worldPos.HasValue)
                    {
                        bone.WorldX = worldPos.Value.X;
                        bone.WorldY = worldPos.Value.Y;
                    }
                }

                RootBones.Clear();
                foreach (var bone in boneDict.Values)
                {
                    if (string.IsNullOrEmpty(bone.ParentBoneId))
                    {
                        RootBones.Add(bone);
                    }
                    else if (boneDict.TryGetValue(bone.ParentBoneId, out var parent))
                    {
                        parent.Children.Add(bone);
                    }
                }
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载骨骼树失败：{ex.Message}";
                Log($"加载骨骼树失败：{ex.Message}");
                MessageBox.Show($"加载骨骼树失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task SetPositionAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            if (!IsFinite(SelectedBone.PositionX, "位置 X") || !IsFinite(SelectedBone.PositionY, "位置 Y")) return;

            try
            {
                var ok = await Task.Run(() => _pipeClient.SetPosition(SelectedBone.BoneId, SelectedBone.PositionX, SelectedBone.PositionY));
                if (!ok)
                {
                    HandleDisconnect("设置位置");
                    return;
                }
                await UpdateWorldPositionAsync(SelectedBone);
                Log($"已设置 {SelectedBone.BoneName} 位置为 ({SelectedBone.PositionX:F1}, {SelectedBone.PositionY:F1})");
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                HandleOperationError(ex, $"设置 {SelectedBone.BoneName} 位置");
            }
        }

        [RelayCommand]
        private async Task SetRotationAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            if (!IsFinite(SelectedBone.Rotation, "旋转角度")) return;

            try
            {
                var boneId = SelectedBone.BoneId;
                var targetDegrees = SelectedBone.Rotation;
                var ok = await Task.Run(() => _pipeClient.SetRotation(boneId, targetDegrees));
                if (!ok)
                {
                    HandleDisconnect("设置旋转");
                    return;
                }

                var actualDegrees = await Task.Run(() => _pipeClient.GetRotation(boneId));
                if (actualDegrees.HasValue && Math.Abs(actualDegrees.Value - targetDegrees) > 0.01)
                {
                    Log($"警告：设置 {SelectedBone.BoneName} 旋转为 {targetDegrees}°，但服务端返回 {actualDegrees.Value}°（可能是 KfuPet 服务端问题）");
                }
                else
                {
                    Log($"已设置 {SelectedBone.BoneName} 旋转为 {targetDegrees}°");
                }

                await RefreshAllWorldPositionsAsync();
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                HandleOperationError(ex, $"设置 {SelectedBone.BoneName} 旋转");
            }
        }

        [RelayCommand]
        private async Task SetScaleAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            if (!IsFinite(SelectedBone.ScaleX, "缩放 X") || !IsFinite(SelectedBone.ScaleY, "缩放 Y")) return;

            try
            {
                var ok = await Task.Run(() => _pipeClient.SetScale(SelectedBone.BoneId, SelectedBone.ScaleX, SelectedBone.ScaleY));
                if (!ok)
                {
                    HandleDisconnect("设置缩放");
                    return;
                }
                Log($"已设置 {SelectedBone.BoneName} 缩放为 ({SelectedBone.ScaleX:F2}, {SelectedBone.ScaleY:F2})");
                await RefreshAllWorldPositionsAsync();
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                HandleOperationError(ex, $"设置 {SelectedBone.BoneName} 缩放");
            }
        }

        [RelayCommand]
        private async Task SetActiveAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            try
            {
                await Task.Run(() => _pipeClient.SetActive(SelectedBone.BoneId, SelectedBone.IsActive));
                Log($"已{(SelectedBone.IsActive ? "激活" : "隐藏")}骨骼 {SelectedBone.BoneName}");
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                HandleOperationError(ex, $"设置 {SelectedBone.BoneName} 激活状态");
            }
        }

        [RelayCommand]
        private async Task ResetBoneAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            try
            {
                var boneId = SelectedBone.BoneId;
                await Task.Run(() => _pipeClient.ResetBone(boneId));

                await RefreshBoneAsync(SelectedBone);
                await RefreshAllWorldPositionsAsync();
                RaisePreviewUpdated();
                Log($"已恢复骨骼 {SelectedBone.BoneName} 到默认状态");
            }
            catch (Exception ex)
            {
                HandleOperationError(ex, $"恢复 {SelectedBone.BoneName}");
            }
        }

        [RelayCommand]
        private async Task ResetAllAsync()
        {
            if (!IsConnected) return;

            var result = MessageBox.Show("确定要恢复所有骨骼到默认状态吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await Task.Run(() => _pipeClient.ResetAll());

                foreach (var bone in GetAllBones())
                {
                    await RefreshBoneAsync(bone);
                }

                await RefreshAllWorldPositionsAsync();
                RaisePreviewUpdated();
                Log("已恢复所有骨骼到默认状态");
            }
            catch (Exception ex)
            {
                HandleOperationError(ex, "恢复所有骨骼");
            }
        }

        private async Task RefreshBoneAsync(BoneInfo bone)
        {
            var pos = await Task.Run(() => _pipeClient.GetPosition(bone.BoneId));
            if (pos.HasValue)
            {
                bone.PositionX = pos.Value.X;
                bone.PositionY = pos.Value.Y;
            }

            bone.Rotation = await Task.Run(() => _pipeClient.GetRotation(bone.BoneId)) ?? 0;

            var scale = await Task.Run(() => _pipeClient.GetScale(bone.BoneId));
            if (scale.HasValue)
            {
                bone.ScaleX = scale.Value.X;
                bone.ScaleY = scale.Value.Y;
            }

            bone.IsActive = await Task.Run(() => _pipeClient.IsActive(bone.BoneId)) ?? true;
        }

        private async Task UpdateWorldPositionAsync(BoneInfo bone)
        {
            var worldPos = await Task.Run(() => _pipeClient.GetWorldPosition(bone.BoneId));
            if (worldPos.HasValue)
            {
                bone.WorldX = worldPos.Value.X;
                bone.WorldY = worldPos.Value.Y;
            }

            foreach (var child in bone.Children)
            {
                await UpdateWorldPositionAsync(child);
            }
        }

        private async Task RefreshAllWorldPositionsAsync()
        {
            try
            {
                foreach (var bone in RootBones)
                {
                    await UpdateWorldPositionAsync(bone);
                }
            }
            catch (Exception ex)
            {
                Log($"刷新世界坐标失败：{ex.Message}");
                if (IsConnected)
                {
                    DisconnectInternal("连接已断开（KfuPet 可能已关闭）");
                    Log("检测到连接丢失，已自动断开");
                }
            }
        }

        public IEnumerable<BoneInfo> GetAllBones()
        {
            foreach (var bone in RootBones)
            {
                yield return bone;
                foreach (var child in GetAllBones(bone))
                {
                    yield return child;
                }
            }
        }

        private IEnumerable<BoneInfo> GetAllBones(BoneInfo parent)
        {
            foreach (var child in parent.Children)
            {
                yield return child;
                foreach (var grandChild in GetAllBones(child))
                {
                    yield return grandChild;
                }
            }
        }

    }
}
